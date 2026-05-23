using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Flatline.Database;
using Flatline.Http;
using Flatline.Logging;
using Flatline.Models;

namespace Flatline.Routes
{
    public class LoginRequest
    {
        public string Username = "";
        public string Password = "";
    }

    /* Per-IP login attempt tracker for the rate limiter. Cleared on success. */
    public class LoginAttemptTracker
    {
        public int ConsecutiveFailures;
        public DateTime LockedUntilUtc;
    }

    public static class AuthRoutes
    {
        private const string SessionCookieName = "flatline_session";
        private const int SessionLifetimeDays = 30;

        /* After this many consecutive failed logins from one IP, the IP is
         * locked out for LoginLockoutSeconds. Counter resets on success. */
        private const int LoginFailuresBeforeLockout = 5;
        private const int LoginLockoutSeconds = 60;

        /* Used by the rate-limiter and by the constant-time bcrypt path. The
         * dummy hash is generated once per process so the unknown-user branch
         * spends the same ~100ms in BCrypt.Verify as the known-user branch. */
        private static Dictionary<string, LoginAttemptTracker> s_LoginAttempts = new Dictionary<string, LoginAttemptTracker>();
        private static readonly object s_LoginAttemptsLock = new object();
        private static readonly string s_DummyPasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N"));

        public static void HandleLogin(FlatlineHttpContext context)
        {
            LoginRequest loginRequest = HttpRequestReader.ReadBodyAsJson<LoginRequest>(context);
            if (loginRequest == null || string.IsNullOrWhiteSpace(loginRequest.Username) || string.IsNullOrWhiteSpace(loginRequest.Password))
            {
                HttpResponseWriter.WriteJson(context, 400, new { error = "Username and password are required." });
                return;
            }

            string remoteIp = context.RemoteIpAddress;
            if (string.IsNullOrEmpty(remoteIp))
            {
                remoteIp = "unknown";
            }

            if (IsLockedOut(remoteIp))
            {
                Log.Warning("Login rate-limited for " + remoteIp + " (username=" + loginRequest.Username + ").");
                HttpResponseWriter.WriteJson(context, 429, new { error = "Too many failed login attempts. Try again later." });
                return;
            }

            SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
            try
            {
                SqliteCommand selectCommand = connection.CreateCommand();
                selectCommand.CommandText = "SELECT id, username, password_hash, display_name, is_admin, created_at FROM users WHERE username = $username;";
                selectCommand.Parameters.AddWithValue("$username", loginRequest.Username);

                SqliteDataReader reader = selectCommand.ExecuteReader();
                bool userFound = reader.Read();
                long userId = 0;
                string username = "";
                string passwordHash = s_DummyPasswordHash;
                string displayName = "";
                bool isAdmin = false;
                string createdAt = "";
                if (userFound)
                {
                    userId = reader.GetInt64(0);
                    username = reader.GetString(1);
                    passwordHash = reader.GetString(2);
                    displayName = reader.GetString(3);
                    isAdmin = reader.GetInt64(4) != 0;
                    createdAt = reader.GetString(5);
                }
                reader.Close();

                /* Always run BCrypt.Verify, even when the user is unknown, so the
                 * 'unknown user' and 'wrong password' branches take the same wall
                 * time. Closes the timing oracle that previously let an attacker
                 * enumerate which usernames existed. */
                bool passwordValid = BCrypt.Net.BCrypt.Verify(loginRequest.Password, passwordHash);
                if (!userFound || !passwordValid)
                {
                    RecordLoginFailure(remoteIp);
                    Log.Warning("Login failed for username='" + loginRequest.Username + "' from " + remoteIp + ".");
                    HttpResponseWriter.WriteJson(context, 401, new { error = "Invalid username or password." });
                    return;
                }

                RecordLoginSuccess(remoteIp);

                string sessionToken = GenerateSessionToken();
                SqliteCommand insertCommand = connection.CreateCommand();
                insertCommand.CommandText = "INSERT INTO sessions (token, user_id, created_at) VALUES ($token, $user_id, $created_at);";
                insertCommand.Parameters.AddWithValue("$token", sessionToken);
                insertCommand.Parameters.AddWithValue("$user_id", userId);
                insertCommand.Parameters.AddWithValue("$created_at", DateTime.UtcNow.ToString("o"));
                insertCommand.ExecuteNonQuery();

                HttpRequestReader.SetCookie(context, SessionCookieName, sessionToken, DateTime.UtcNow.AddDays(SessionLifetimeDays), true, "/");

                User user = new User();
                user.Id = userId;
                user.Username = username;
                user.DisplayName = displayName;
                user.IsAdmin = isAdmin;
                user.CreatedAt = createdAt;
                HttpResponseWriter.WriteJson(context, 200, user);
            }
            finally
            {
                connection.Close();
            }
        }

        private static bool IsLockedOut(string remoteIp)
        {
            lock (s_LoginAttemptsLock)
            {
                LoginAttemptTracker tracker;
                if (!s_LoginAttempts.TryGetValue(remoteIp, out tracker))
                {
                    return false;
                }
                return tracker.LockedUntilUtc > DateTime.UtcNow;
            }
        }

        private static void RecordLoginFailure(string remoteIp)
        {
            lock (s_LoginAttemptsLock)
            {
                LoginAttemptTracker tracker;
                if (!s_LoginAttempts.TryGetValue(remoteIp, out tracker))
                {
                    tracker = new LoginAttemptTracker();
                    s_LoginAttempts[remoteIp] = tracker;
                }
                tracker.ConsecutiveFailures = tracker.ConsecutiveFailures + 1;
                if (tracker.ConsecutiveFailures >= LoginFailuresBeforeLockout)
                {
                    tracker.LockedUntilUtc = DateTime.UtcNow.AddSeconds(LoginLockoutSeconds);
                    tracker.ConsecutiveFailures = 0;
                    Log.Warning("Locking out " + remoteIp + " for " + LoginLockoutSeconds + "s after repeated login failures.");
                }
            }
        }

        private static void RecordLoginSuccess(string remoteIp)
        {
            lock (s_LoginAttemptsLock)
            {
                s_LoginAttempts.Remove(remoteIp);
            }
        }

        public static void HandleLogout(FlatlineHttpContext context)
        {
            string sessionToken = HttpRequestReader.GetCookieValue(context, SessionCookieName);
            if (!string.IsNullOrEmpty(sessionToken))
            {
                SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
                try
                {
                    SqliteCommand deleteCommand = connection.CreateCommand();
                    deleteCommand.CommandText = "DELETE FROM sessions WHERE token = $token;";
                    deleteCommand.Parameters.AddWithValue("$token", sessionToken);
                    deleteCommand.ExecuteNonQuery();
                }
                finally
                {
                    connection.Close();
                }
            }
            HttpRequestReader.DeleteCookie(context, SessionCookieName, "/");
            HttpResponseWriter.WriteJson(context, 200, new { ok = true });
        }

        public static void HandleGetSession(FlatlineHttpContext context)
        {
            User user = GetCurrentUser(context);
            if (user == null)
            {
                HttpResponseWriter.WriteJson(context, 401, new { error = "Not authenticated." });
                return;
            }
            HttpResponseWriter.WriteJson(context, 200, user);
        }

        public static User GetCurrentUser(FlatlineHttpContext context)
        {
            string sessionToken = HttpRequestReader.GetCookieValue(context, SessionCookieName);
            if (string.IsNullOrEmpty(sessionToken))
            {
                return null;
            }

            string expiryThresholdIso = DateTime.UtcNow.AddDays(-SessionLifetimeDays).ToString("o");

            SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
            try
            {
                /* Opportunistically prune any session rows that are now older than
                 * the lifetime. Cheap on a small sessions table, and means we
                 * don't need a separate sweep job. */
                SqliteCommand pruneCommand = connection.CreateCommand();
                pruneCommand.CommandText = "DELETE FROM sessions WHERE created_at < $expiry_threshold;";
                pruneCommand.Parameters.AddWithValue("$expiry_threshold", expiryThresholdIso);
                pruneCommand.ExecuteNonQuery();

                SqliteCommand selectCommand = connection.CreateCommand();
                selectCommand.CommandText = "SELECT u.id, u.username, u.display_name, u.is_admin, u.created_at "
                    + "FROM sessions s INNER JOIN users u ON u.id = s.user_id "
                    + "WHERE s.token = $token AND s.created_at >= $expiry_threshold;";
                selectCommand.Parameters.AddWithValue("$token", sessionToken);
                selectCommand.Parameters.AddWithValue("$expiry_threshold", expiryThresholdIso);

                SqliteDataReader reader = selectCommand.ExecuteReader();
                if (!reader.Read())
                {
                    reader.Close();
                    return null;
                }

                User user = new User();
                user.Id = reader.GetInt64(0);
                user.Username = reader.GetString(1);
                user.DisplayName = reader.GetString(2);
                user.IsAdmin = reader.GetInt64(3) != 0;
                user.CreatedAt = reader.GetString(4);
                reader.Close();
                return user;
            }
            finally
            {
                connection.Close();
            }
        }

        private static string GenerateSessionToken()
        {
            byte[] tokenBytes = new byte[32];
            RandomNumberGenerator.Fill(tokenBytes);
            return Convert.ToBase64String(tokenBytes).Replace("+", "-").Replace("/", "_").Replace("=", "");
        }
    }
}
