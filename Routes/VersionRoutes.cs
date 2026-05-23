using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Flatline.Database;
using Flatline.Http;
using Flatline.Models;

namespace Flatline.Routes
{
    public class VersionCreateRequest
    {
        public string Name = "";
    }

    public class VersionUpdateRequest
    {
        public string Name = "";
    }

    public static class VersionRoutes
    {
        public static void HandleListVersions(FlatlineHttpContext context, long projectId)
        {
            User currentUser = AuthRoutes.GetCurrentUser(context);
            if (currentUser == null)
            {
                HttpResponseWriter.WriteJson(context, 401, new { error = "Not authenticated." });
                return;
            }

            List<ProjectVersion> versionList = new List<ProjectVersion>();
            SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
            try
            {
                SqliteCommand selectCommand = connection.CreateCommand();
                selectCommand.CommandText = "SELECT id, project_id, name, created_at FROM versions WHERE project_id = $project_id ORDER BY name ASC;";
                selectCommand.Parameters.AddWithValue("$project_id", projectId);
                SqliteDataReader reader = selectCommand.ExecuteReader();
                const int maxRows = 100000;
                for (int rowIndex = 0; rowIndex < maxRows; rowIndex++)
                {
                    if (!reader.Read())
                    {
                        break;
                    }
                    ProjectVersion version = new ProjectVersion();
                    version.Id = reader.GetInt64(0);
                    version.ProjectId = reader.GetInt64(1);
                    version.Name = reader.GetString(2);
                    version.CreatedAt = reader.GetString(3);
                    versionList.Add(version);
                }
                reader.Close();
            }
            finally
            {
                connection.Close();
            }

            HttpResponseWriter.WriteJson(context, 200, versionList);
        }

        public static void HandleCreateVersion(FlatlineHttpContext context, long projectId)
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

            VersionCreateRequest createRequest = HttpRequestReader.ReadBodyAsJson<VersionCreateRequest>(context);
            if (createRequest == null || string.IsNullOrWhiteSpace(createRequest.Name))
            {
                HttpResponseWriter.WriteJson(context, 400, new { error = "Name is required." });
                return;
            }

            SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
            try
            {
                SqliteCommand projectExistsCommand = connection.CreateCommand();
                projectExistsCommand.CommandText = "SELECT id FROM projects WHERE id = $id;";
                projectExistsCommand.Parameters.AddWithValue("$id", projectId);
                object projectExistsResult = projectExistsCommand.ExecuteScalar();
                if (projectExistsResult == null)
                {
                    HttpResponseWriter.WriteJson(context, 404, new { error = "Project not found." });
                    return;
                }

                SqliteCommand duplicateCommand = connection.CreateCommand();
                duplicateCommand.CommandText = "SELECT id FROM versions WHERE project_id = $project_id AND name = $name;";
                duplicateCommand.Parameters.AddWithValue("$project_id", projectId);
                duplicateCommand.Parameters.AddWithValue("$name", createRequest.Name);
                object duplicateResult = duplicateCommand.ExecuteScalar();
                if (duplicateResult != null)
                {
                    HttpResponseWriter.WriteJson(context, 409, new { error = "Version name already exists in this project." });
                    return;
                }

                string nowIso = DateTime.UtcNow.ToString("o");
                SqliteCommand insertCommand = connection.CreateCommand();
                insertCommand.CommandText = "INSERT INTO versions (project_id, name, created_at) VALUES ($project_id, $name, $created_at); "
                    + "SELECT last_insert_rowid();";
                insertCommand.Parameters.AddWithValue("$project_id", projectId);
                insertCommand.Parameters.AddWithValue("$name", createRequest.Name);
                insertCommand.Parameters.AddWithValue("$created_at", nowIso);
                object scalarResult = insertCommand.ExecuteScalar();
                long newVersionId = Convert.ToInt64(scalarResult);

                ProjectVersion version = new ProjectVersion();
                version.Id = newVersionId;
                version.ProjectId = projectId;
                version.Name = createRequest.Name;
                version.CreatedAt = nowIso;
                HttpResponseWriter.WriteJson(context, 200, version);
            }
            finally
            {
                connection.Close();
            }
        }

        public static void HandleUpdateVersion(FlatlineHttpContext context, long projectId, long versionId)
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

            VersionUpdateRequest updateRequest = HttpRequestReader.ReadBodyAsJson<VersionUpdateRequest>(context);
            if (updateRequest == null || string.IsNullOrWhiteSpace(updateRequest.Name))
            {
                HttpResponseWriter.WriteJson(context, 400, new { error = "Name is required." });
                return;
            }

            SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
            try
            {
                SqliteCommand existsCommand = connection.CreateCommand();
                existsCommand.CommandText = "SELECT id FROM versions WHERE id = $id AND project_id = $project_id;";
                existsCommand.Parameters.AddWithValue("$id", versionId);
                existsCommand.Parameters.AddWithValue("$project_id", projectId);
                object existsResult = existsCommand.ExecuteScalar();
                if (existsResult == null)
                {
                    HttpResponseWriter.WriteJson(context, 404, new { error = "Version not found." });
                    return;
                }

                SqliteCommand duplicateCommand = connection.CreateCommand();
                duplicateCommand.CommandText = "SELECT id FROM versions WHERE project_id = $project_id AND name = $name AND id != $id;";
                duplicateCommand.Parameters.AddWithValue("$project_id", projectId);
                duplicateCommand.Parameters.AddWithValue("$name", updateRequest.Name);
                duplicateCommand.Parameters.AddWithValue("$id", versionId);
                object duplicateResult = duplicateCommand.ExecuteScalar();
                if (duplicateResult != null)
                {
                    HttpResponseWriter.WriteJson(context, 409, new { error = "Version name already exists in this project." });
                    return;
                }

                SqliteCommand updateCommand = connection.CreateCommand();
                updateCommand.CommandText = "UPDATE versions SET name = $name WHERE id = $id;";
                updateCommand.Parameters.AddWithValue("$name", updateRequest.Name);
                updateCommand.Parameters.AddWithValue("$id", versionId);
                updateCommand.ExecuteNonQuery();

                SqliteCommand selectCommand = connection.CreateCommand();
                selectCommand.CommandText = "SELECT id, project_id, name, created_at FROM versions WHERE id = $id;";
                selectCommand.Parameters.AddWithValue("$id", versionId);
                SqliteDataReader reader = selectCommand.ExecuteReader();
                reader.Read();
                ProjectVersion version = new ProjectVersion();
                version.Id = reader.GetInt64(0);
                version.ProjectId = reader.GetInt64(1);
                version.Name = reader.GetString(2);
                version.CreatedAt = reader.GetString(3);
                reader.Close();
                HttpResponseWriter.WriteJson(context, 200, version);
            }
            finally
            {
                connection.Close();
            }
        }

        public static void HandleDeleteVersion(FlatlineHttpContext context, long projectId, long versionId)
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
                SqliteCommand bugRefCommand = connection.CreateCommand();
                bugRefCommand.CommandText = "SELECT COUNT(*) FROM bugs WHERE found_in_version_id = $id OR fixed_in_version_id = $id;";
                bugRefCommand.Parameters.AddWithValue("$id", versionId);
                object countResult = bugRefCommand.ExecuteScalar();
                long bugCount = 0;
                if (countResult != null)
                {
                    bugCount = Convert.ToInt64(countResult);
                }
                if (bugCount > 0)
                {
                    HttpResponseWriter.WriteJson(context, 409, new { error = "Cannot delete version that is referenced by bugs." });
                    return;
                }

                SqliteCommand deleteCommand = connection.CreateCommand();
                deleteCommand.CommandText = "DELETE FROM versions WHERE id = $id AND project_id = $project_id;";
                deleteCommand.Parameters.AddWithValue("$id", versionId);
                deleteCommand.Parameters.AddWithValue("$project_id", projectId);
                int rowsAffected = deleteCommand.ExecuteNonQuery();
                if (rowsAffected == 0)
                {
                    HttpResponseWriter.WriteJson(context, 404, new { error = "Version not found." });
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
