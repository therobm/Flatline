using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Flatline.Database;
using Flatline.Http;
using Flatline.Models;

namespace Flatline.Routes
{
    public class ApiKeyCreateRequest
    {
        public long UserId;
        public string Name = "";
    }

    public class ApiKeyCreateResponse
    {
        public long Id;
        public long UserId;
        public string UserDisplayName = "";
        public string UserUsername = "";
        public string Name = "";
        public string KeyPrefix = "";
        public string CreatedAt = "";
        public string Key = "";
    }

    public static class ApiKeyRoutes
    {
        private const string ApiKeyHeaderName = "X-API-Key";
        private const string RawKeyPrefix = "flk_";
        private const int RawKeyByteCount = 24;
        private const int DisplayedPrefixLength = 12;

        public static void HandleListApiKeys(FlatlineHttpContext context)
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

            List<ApiKey> apiKeyList = new List<ApiKey>();
            SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
            try
            {
                SqliteCommand selectCommand = connection.CreateCommand();
                selectCommand.CommandText = "SELECT ak.id, ak.user_id, ak.name, ak.key_prefix, ak.created_at, ak.last_used_at, "
                    + "u.username, u.display_name "
                    + "FROM api_keys ak "
                    + "INNER JOIN users u ON u.id = ak.user_id "
                    + "ORDER BY ak.created_at DESC;";
                SqliteDataReader reader = selectCommand.ExecuteReader();
                const int maxRows = 100000;
                for (int rowIndex = 0; rowIndex < maxRows; rowIndex++)
                {
                    if (!reader.Read())
                    {
                        break;
                    }
                    ApiKey apiKey = new ApiKey();
                    apiKey.Id = reader.GetInt64(0);
                    apiKey.UserId = reader.GetInt64(1);
                    apiKey.Name = reader.GetString(2);
                    apiKey.KeyPrefix = reader.GetString(3);
                    apiKey.CreatedAt = reader.GetString(4);
                    if (reader.IsDBNull(5))
                    {
                        apiKey.LastUsedAt = "";
                    }
                    else
                    {
                        apiKey.LastUsedAt = reader.GetString(5);
                    }
                    apiKey.UserUsername = reader.GetString(6);
                    apiKey.UserDisplayName = reader.GetString(7);
                    apiKeyList.Add(apiKey);
                }
                reader.Close();
            }
            finally
            {
                connection.Close();
            }

            HttpResponseWriter.WriteJson(context, 200, apiKeyList);
        }

        public static void HandleCreateApiKey(FlatlineHttpContext context)
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

            ApiKeyCreateRequest createRequest = HttpRequestReader.ReadBodyAsJson<ApiKeyCreateRequest>(context);
            if (createRequest == null)
            {
                HttpResponseWriter.WriteJson(context, 400, new { error = "Body is required." });
                return;
            }
            if (createRequest.UserId <= 0)
            {
                HttpResponseWriter.WriteJson(context, 400, new { error = "UserId is required." });
                return;
            }
            if (string.IsNullOrWhiteSpace(createRequest.Name))
            {
                HttpResponseWriter.WriteJson(context, 400, new { error = "Name is required." });
                return;
            }

            string rawKey = GenerateRawApiKey();
            string keyHash = HashApiKey(rawKey);
            string keyPrefix = rawKey.Substring(0, DisplayedPrefixLength);

            SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
            try
            {
                SqliteCommand userCommand = connection.CreateCommand();
                userCommand.CommandText = "SELECT username, display_name FROM users WHERE id = $id;";
                userCommand.Parameters.AddWithValue("$id", createRequest.UserId);
                SqliteDataReader userReader = userCommand.ExecuteReader();
                if (!userReader.Read())
                {
                    userReader.Close();
                    HttpResponseWriter.WriteJson(context, 400, new { error = "User not found." });
                    return;
                }
                string username = userReader.GetString(0);
                string displayName = userReader.GetString(1);
                userReader.Close();

                string nowIso = DateTime.UtcNow.ToString("o");
                SqliteCommand insertCommand = connection.CreateCommand();
                insertCommand.CommandText = "INSERT INTO api_keys (user_id, name, key_hash, key_prefix, created_at) "
                    + "VALUES ($user_id, $name, $key_hash, $key_prefix, $created_at); "
                    + "SELECT last_insert_rowid();";
                insertCommand.Parameters.AddWithValue("$user_id", createRequest.UserId);
                insertCommand.Parameters.AddWithValue("$name", createRequest.Name);
                insertCommand.Parameters.AddWithValue("$key_hash", keyHash);
                insertCommand.Parameters.AddWithValue("$key_prefix", keyPrefix);
                insertCommand.Parameters.AddWithValue("$created_at", nowIso);
                object scalarResult = insertCommand.ExecuteScalar();
                long newId = Convert.ToInt64(scalarResult);

                ApiKeyCreateResponse response = new ApiKeyCreateResponse();
                response.Id = newId;
                response.UserId = createRequest.UserId;
                response.UserDisplayName = displayName;
                response.UserUsername = username;
                response.Name = createRequest.Name;
                response.KeyPrefix = keyPrefix;
                response.CreatedAt = nowIso;
                response.Key = rawKey;
                HttpResponseWriter.WriteJson(context, 200, response);
            }
            finally
            {
                connection.Close();
            }
        }

        public static void HandleDeleteApiKey(FlatlineHttpContext context, long id)
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

            SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
            try
            {
                SqliteCommand deleteCommand = connection.CreateCommand();
                deleteCommand.CommandText = "DELETE FROM api_keys WHERE id = $id;";
                deleteCommand.Parameters.AddWithValue("$id", id);
                int rowsAffected = deleteCommand.ExecuteNonQuery();
                if (rowsAffected == 0)
                {
                    HttpResponseWriter.WriteJson(context, 404, new { error = "API key not found." });
                    return;
                }
            }
            finally
            {
                connection.Close();
            }

            HttpResponseWriter.WriteJson(context, 200, new { ok = true });
        }

        public static User GetUserFromApiKey(FlatlineHttpContext context)
        {
            string rawKey = HttpRequestReader.GetHeaderValue(context, ApiKeyHeaderName);
            if (string.IsNullOrEmpty(rawKey))
            {
                return null;
            }
            if (!rawKey.StartsWith(RawKeyPrefix))
            {
                return null;
            }
            string keyHash = HashApiKey(rawKey);

            SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
            try
            {
                SqliteCommand selectCommand = connection.CreateCommand();
                selectCommand.CommandText = "SELECT ak.id, u.id, u.username, u.display_name, u.is_admin, u.created_at "
                    + "FROM api_keys ak "
                    + "INNER JOIN users u ON u.id = ak.user_id "
                    + "WHERE ak.key_hash = $key_hash;";
                selectCommand.Parameters.AddWithValue("$key_hash", keyHash);
                SqliteDataReader reader = selectCommand.ExecuteReader();
                if (!reader.Read())
                {
                    reader.Close();
                    return null;
                }
                long apiKeyId = reader.GetInt64(0);
                User user = new User();
                user.Id = reader.GetInt64(1);
                user.Username = reader.GetString(2);
                user.DisplayName = reader.GetString(3);
                user.IsAdmin = reader.GetInt64(4) != 0;
                user.CreatedAt = reader.GetString(5);
                reader.Close();

                SqliteCommand touchCommand = connection.CreateCommand();
                touchCommand.CommandText = "UPDATE api_keys SET last_used_at = $now WHERE id = $id;";
                touchCommand.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
                touchCommand.Parameters.AddWithValue("$id", apiKeyId);
                touchCommand.ExecuteNonQuery();

                return user;
            }
            finally
            {
                connection.Close();
            }
        }

        private static string GenerateRawApiKey()
        {
            byte[] keyBytes = new byte[RawKeyByteCount];
            RandomNumberGenerator.Fill(keyBytes);
            string encoded = Convert.ToBase64String(keyBytes).Replace("+", "-").Replace("/", "_").Replace("=", "");
            return RawKeyPrefix + encoded;
        }

        private static string HashApiKey(string rawKey)
        {
            byte[] inputBytes = Encoding.UTF8.GetBytes(rawKey);
            byte[] hashBytes = SHA256.HashData(inputBytes);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
    }
}
