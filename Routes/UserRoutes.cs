using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Flatline.Database;
using Flatline.Http;
using Flatline.Models;

namespace Flatline.Routes
{
    public class UserCreateRequest
    {
        public string Username = "";
        public string Password = "";
        public string DisplayName = "";
        public bool IsAdmin;
    }

    public class UserUpdateRequest
    {
        public string DisplayName;
        public string Password;
        public bool IsAdmin;
        public bool UpdateIsAdmin;
    }

    public static class UserRoutes
    {
        public static void HandleListUsers(FlatlineHttpContext context)
        {
            User currentUser = AuthRoutes.GetCurrentUser(context);
            if (currentUser == null)
            {
                HttpResponseWriter.WriteJson(context, 401, new { error = "Not authenticated." });
                return;
            }

            List<User> userList = new List<User>();
            SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
            try
            {
                SqliteCommand selectCommand = connection.CreateCommand();
                selectCommand.CommandText = "SELECT id, username, display_name, is_admin, created_at FROM users ORDER BY username ASC;";
                SqliteDataReader reader = selectCommand.ExecuteReader();
                for (bool hasRow = reader.Read(); hasRow; hasRow = reader.Read())
                {
                    User user = new User();
                    user.Id = reader.GetInt64(0);
                    user.Username = reader.GetString(1);
                    user.DisplayName = reader.GetString(2);
                    user.IsAdmin = reader.GetInt64(3) != 0;
                    user.CreatedAt = reader.GetString(4);
                    userList.Add(user);
                }
                reader.Close();
            }
            finally
            {
                connection.Close();
            }

            HttpResponseWriter.WriteJson(context, 200, userList);
        }

        public static void HandleCreateUser(FlatlineHttpContext context)
        {
            User currentUser = AuthRoutes.GetCurrentUser(context);
            if (currentUser == null)
            {
                HttpResponseWriter.WriteJson(context, 401, new { error = "Not authenticated." });
                return;
            }
            if (!currentUser.IsAdmin)
            {
                HttpResponseWriter.WriteJson(context, 403, new { error = "Admin privileges required." });
                return;
            }

            UserCreateRequest createRequest = HttpRequestReader.ReadBodyAsJson<UserCreateRequest>(context);
            if (createRequest == null)
            {
                HttpResponseWriter.WriteJson(context, 400, new { error = "Body is required." });
                return;
            }
            if (string.IsNullOrWhiteSpace(createRequest.Username))
            {
                HttpResponseWriter.WriteJson(context, 400, new { error = "Username is required." });
                return;
            }
            if (string.IsNullOrWhiteSpace(createRequest.Password))
            {
                HttpResponseWriter.WriteJson(context, 400, new { error = "Password is required." });
                return;
            }
            string displayName = createRequest.DisplayName;
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = createRequest.Username;
            }

            SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
            try
            {
                SqliteCommand duplicateCommand = connection.CreateCommand();
                duplicateCommand.CommandText = "SELECT id FROM users WHERE username = $username;";
                duplicateCommand.Parameters.AddWithValue("$username", createRequest.Username);
                object duplicateResult = duplicateCommand.ExecuteScalar();
                if (duplicateResult != null)
                {
                    HttpResponseWriter.WriteJson(context, 409, new { error = "Username already exists." });
                    return;
                }

                string passwordHash = BCrypt.Net.BCrypt.HashPassword(createRequest.Password);
                string nowIso = DateTime.UtcNow.ToString("o");

                int adminFlag = 0;
                if (createRequest.IsAdmin)
                {
                    adminFlag = 1;
                }

                SqliteCommand insertCommand = connection.CreateCommand();
                insertCommand.CommandText = "INSERT INTO users (username, password_hash, display_name, is_admin, created_at) "
                    + "VALUES ($username, $password_hash, $display_name, $is_admin, $created_at); "
                    + "SELECT last_insert_rowid();";
                insertCommand.Parameters.AddWithValue("$username", createRequest.Username);
                insertCommand.Parameters.AddWithValue("$password_hash", passwordHash);
                insertCommand.Parameters.AddWithValue("$display_name", displayName);
                insertCommand.Parameters.AddWithValue("$is_admin", adminFlag);
                insertCommand.Parameters.AddWithValue("$created_at", nowIso);

                object scalarResult = insertCommand.ExecuteScalar();
                long newUserId = Convert.ToInt64(scalarResult);

                User createdUser = new User();
                createdUser.Id = newUserId;
                createdUser.Username = createRequest.Username;
                createdUser.DisplayName = displayName;
                createdUser.IsAdmin = createRequest.IsAdmin;
                createdUser.CreatedAt = nowIso;
                HttpResponseWriter.WriteJson(context, 200, createdUser);
            }
            finally
            {
                connection.Close();
            }
        }

        public static void HandleUpdateUser(FlatlineHttpContext context, long id)
        {
            User currentUser = AuthRoutes.GetCurrentUser(context);
            if (currentUser == null)
            {
                HttpResponseWriter.WriteJson(context, 401, new { error = "Not authenticated." });
                return;
            }

            bool isSelfUpdate = currentUser.Id == id;
            bool isAdmin = currentUser.IsAdmin;
            if (!isSelfUpdate && !isAdmin)
            {
                HttpResponseWriter.WriteJson(context, 403, new { error = "Not authorized." });
                return;
            }

            UserUpdateRequest updateRequest = HttpRequestReader.ReadBodyAsJson<UserUpdateRequest>(context);
            if (updateRequest == null)
            {
                HttpResponseWriter.WriteJson(context, 400, new { error = "Body is required." });
                return;
            }

            if (updateRequest.UpdateIsAdmin && !isAdmin)
            {
                HttpResponseWriter.WriteJson(context, 403, new { error = "Only admins can change admin flag." });
                return;
            }

            SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
            try
            {
                SqliteCommand existsCommand = connection.CreateCommand();
                existsCommand.CommandText = "SELECT id FROM users WHERE id = $id;";
                existsCommand.Parameters.AddWithValue("$id", id);
                object existsResult = existsCommand.ExecuteScalar();
                if (existsResult == null)
                {
                    HttpResponseWriter.WriteJson(context, 404, new { error = "User not found." });
                    return;
                }

                List<string> setClauses = new List<string>();
                SqliteCommand updateCommand = connection.CreateCommand();

                if (!string.IsNullOrWhiteSpace(updateRequest.DisplayName))
                {
                    setClauses.Add("display_name = $display_name");
                    updateCommand.Parameters.AddWithValue("$display_name", updateRequest.DisplayName);
                }
                if (!string.IsNullOrEmpty(updateRequest.Password))
                {
                    string passwordHash = BCrypt.Net.BCrypt.HashPassword(updateRequest.Password);
                    setClauses.Add("password_hash = $password_hash");
                    updateCommand.Parameters.AddWithValue("$password_hash", passwordHash);
                }
                if (updateRequest.UpdateIsAdmin)
                {
                    int adminFlag = 0;
                    if (updateRequest.IsAdmin)
                    {
                        adminFlag = 1;
                    }
                    setClauses.Add("is_admin = $is_admin");
                    updateCommand.Parameters.AddWithValue("$is_admin", adminFlag);
                }

                if (setClauses.Count == 0)
                {
                    HttpResponseWriter.WriteJson(context, 400, new { error = "No fields to update." });
                    return;
                }

                updateCommand.Parameters.AddWithValue("$id", id);
                string setSql = string.Join(", ", setClauses);
                updateCommand.CommandText = "UPDATE users SET " + setSql + " WHERE id = $id;";
                updateCommand.ExecuteNonQuery();

                SqliteCommand selectCommand = connection.CreateCommand();
                selectCommand.CommandText = "SELECT id, username, display_name, is_admin, created_at FROM users WHERE id = $id;";
                selectCommand.Parameters.AddWithValue("$id", id);
                SqliteDataReader reader = selectCommand.ExecuteReader();
                reader.Read();
                User updatedUser = new User();
                updatedUser.Id = reader.GetInt64(0);
                updatedUser.Username = reader.GetString(1);
                updatedUser.DisplayName = reader.GetString(2);
                updatedUser.IsAdmin = reader.GetInt64(3) != 0;
                updatedUser.CreatedAt = reader.GetString(4);
                reader.Close();
                HttpResponseWriter.WriteJson(context, 200, updatedUser);
            }
            finally
            {
                connection.Close();
            }
        }

        public static void HandleDeleteUser(FlatlineHttpContext context, long id)
        {
            User currentUser = AuthRoutes.GetCurrentUser(context);
            if (currentUser == null)
            {
                HttpResponseWriter.WriteJson(context, 401, new { error = "Not authenticated." });
                return;
            }
            if (!currentUser.IsAdmin)
            {
                HttpResponseWriter.WriteJson(context, 403, new { error = "Admin privileges required." });
                return;
            }
            if (currentUser.Id == id)
            {
                HttpResponseWriter.WriteJson(context, 400, new { error = "Cannot delete yourself." });
                return;
            }

            SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
            try
            {
                SqliteCommand deleteCommand = connection.CreateCommand();
                deleteCommand.CommandText = "DELETE FROM users WHERE id = $id;";
                deleteCommand.Parameters.AddWithValue("$id", id);
                int rowsAffected = deleteCommand.ExecuteNonQuery();
                if (rowsAffected == 0)
                {
                    HttpResponseWriter.WriteJson(context, 404, new { error = "User not found." });
                    return;
                }
            }
            finally
            {
                connection.Close();
            }

            HttpResponseWriter.WriteJson(context, 200, new { ok = true });
        }
    }
}
