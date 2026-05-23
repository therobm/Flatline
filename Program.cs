using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using Microsoft.Data.Sqlite;
using Flatline.Database;
using Flatline.Http;
using Flatline.Logging;

namespace Flatline
{
    public class Program
    {
        private static ManualResetEvent ShutdownEvent = new ManualResetEvent(false);

        public static void Main(string[] args)
        {
            string configPath = Path.Combine(AppContext.BaseDirectory, "flatline.json");
            FlatlineConfig config = FlatlineConfig.LoadOrDefault(configPath);
            IPAddress bindAddress = config.ResolveBindAddress();

            Migrations.RunMigrations();
            SeedInitialAdminIfNeeded();

            X509Certificate2 serverCertificate = CertificateProvider.EnsureServerCertificate();

            FlatlineHttpServer server = new FlatlineHttpServer();
            server.Start(bindAddress, config.HttpPort, config.HttpsPort, serverCertificate);

            Console.CancelKeyPress += OnCancelKeyPress;
            ShutdownEvent.WaitOne();

            Log.Info("Stopping server.");
            server.StopAndWait();
            CertificateProvider.RemoveFromCurrentUserStore(serverCertificate);
            serverCertificate.Dispose();
            Log.Info("Server stopped.");
        }

        private static void OnCancelKeyPress(object sender, ConsoleCancelEventArgs eventArgs)
        {
            eventArgs.Cancel = true;
            ShutdownEvent.Set();
        }

        private static string GenerateInitialPassword()
        {
            /* 18 random bytes -> 24-char base64 (no padding). Plenty of entropy
             * for an admin bootstrap password, short enough to type once. */
            byte[] randomBytes = new byte[18];
            RandomNumberGenerator.Fill(randomBytes);
            return Convert.ToBase64String(randomBytes).Replace("+", "-").Replace("/", "_").Replace("=", "");
        }

        private static void SeedInitialAdminIfNeeded()
        {
            SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
            try
            {
                SqliteCommand countCommand = connection.CreateCommand();
                countCommand.CommandText = "SELECT COUNT(*) FROM users;";
                object countResult = countCommand.ExecuteScalar();
                long userCount = 0;
                if (countResult != null)
                {
                    userCount = Convert.ToInt64(countResult);
                }
                if (userCount > 0)
                {
                    return;
                }

                string defaultUsername = "admin";
                string defaultDisplayName = "Administrator";
                string generatedPassword = GenerateInitialPassword();
                string passwordHash = BCrypt.Net.BCrypt.HashPassword(generatedPassword);
                string nowIso = DateTime.UtcNow.ToString("o");

                SqliteCommand insertCommand = connection.CreateCommand();
                insertCommand.CommandText = "INSERT INTO users (username, password_hash, display_name, is_admin, created_at) "
                    + "VALUES ($username, $password_hash, $display_name, 1, $created_at);";
                insertCommand.Parameters.AddWithValue("$username", defaultUsername);
                insertCommand.Parameters.AddWithValue("$password_hash", passwordHash);
                insertCommand.Parameters.AddWithValue("$display_name", defaultDisplayName);
                insertCommand.Parameters.AddWithValue("$created_at", nowIso);
                insertCommand.ExecuteNonQuery();

                /* The initial admin password is printed directly to stdout and is
                 * deliberately NOT routed through Log.* — otherwise the daily-rolling
                 * log file would retain it forever, even after the admin changes the
                 * password. The operator must capture it from the console on first run. */
                Console.WriteLine("------------------------------------------------------------");
                Console.WriteLine("Flatline: created initial admin user.");
                Console.WriteLine("  Username: " + defaultUsername);
                Console.WriteLine("  Password: " + generatedPassword);
                Console.WriteLine("Save this password now — it is not stored anywhere else and");
                Console.WriteLine("will not appear in the log file. Change it after first login.");
                Console.WriteLine("------------------------------------------------------------");
                Log.Info("Created initial admin user '" + defaultUsername + "'. Password was printed to stdout once and is not in the log.");
            }
            finally
            {
                connection.Close();
            }
        }
    }
}
