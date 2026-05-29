using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Flatline.Database;
using Flatline.Http;
using Flatline.Models;

namespace Flatline.Routes
{
    public class ProjectCreateRequest
    {
        public string Name = "";
        public string Prefix = "";
    }

    public class ProjectUpdateRequest
    {
        public string Name = "";
        public string Prefix = "";
    }

    public static class ProjectRoutes
    {
        public static void HandleListProjects(FlatlineHttpContext context)
        {
            User currentUser = AuthRoutes.GetCurrentUser(context);
            if (currentUser == null)
            {
                HttpResponseWriter.WriteJson(context, 401, new { error = "Not authenticated." });
                return;
            }
            ListProjects(context);
        }

        public static void ListProjects(FlatlineHttpContext context)
        {
            List<Project> projectList = new List<Project>();
            SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
            try
            {
                SqliteCommand selectCommand = connection.CreateCommand();
                selectCommand.CommandText = "SELECT p.id, p.name, p.prefix, p.created_at, "
                    + "(SELECT COUNT(*) FROM versions v WHERE v.project_id = p.id) AS version_count "
                    + "FROM projects p ORDER BY p.name ASC;";
                SqliteDataReader reader = selectCommand.ExecuteReader();
                const int maxRows = 100000;
                for (int rowIndex = 0; rowIndex < maxRows; rowIndex++)
                {
                    if (!reader.Read())
                    {
                        break;
                    }
                    Project project = new Project();
                    project.Id = reader.GetInt64(0);
                    project.Name = reader.GetString(1);
                    project.Prefix = reader.GetString(2);
                    project.CreatedAt = reader.GetString(3);
                    project.VersionCount = reader.GetInt32(4);
                    projectList.Add(project);
                }
                reader.Close();
            }
            finally
            {
                connection.Close();
            }

            HttpResponseWriter.WriteJson(context, 200, projectList);
        }

        public static void HandleCreateProject(FlatlineHttpContext context)
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

            ProjectCreateRequest createRequest = HttpRequestReader.ReadBodyAsJson<ProjectCreateRequest>(context);
            if (createRequest == null || string.IsNullOrWhiteSpace(createRequest.Name))
            {
                HttpResponseWriter.WriteJson(context, 400, new { error = "Name is required." });
                return;
            }
            string normalizedPrefix = NormalizeProjectPrefix(createRequest.Prefix);
            if (normalizedPrefix.Length == 0)
            {
                HttpResponseWriter.WriteJson(context, 400, new { error = "Prefix must be exactly 3 letters." });
                return;
            }

            SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
            try
            {
                SqliteCommand duplicateCommand = connection.CreateCommand();
                duplicateCommand.CommandText = "SELECT id FROM projects WHERE name = $name;";
                duplicateCommand.Parameters.AddWithValue("$name", createRequest.Name);
                object duplicateResult = duplicateCommand.ExecuteScalar();
                if (duplicateResult != null)
                {
                    HttpResponseWriter.WriteJson(context, 409, new { error = "Project name already exists." });
                    return;
                }

                SqliteCommand prefixDuplicateCommand = connection.CreateCommand();
                prefixDuplicateCommand.CommandText = "SELECT id FROM projects WHERE prefix = $prefix;";
                prefixDuplicateCommand.Parameters.AddWithValue("$prefix", normalizedPrefix);
                object prefixDuplicateResult = prefixDuplicateCommand.ExecuteScalar();
                if (prefixDuplicateResult != null)
                {
                    HttpResponseWriter.WriteJson(context, 409, new { error = "Project prefix already exists." });
                    return;
                }

                string nowIso = DateTime.UtcNow.ToString("o");
                SqliteCommand insertCommand = connection.CreateCommand();
                insertCommand.CommandText = "INSERT INTO projects (name, prefix, created_at) VALUES ($name, $prefix, $created_at); "
                    + "SELECT last_insert_rowid();";
                insertCommand.Parameters.AddWithValue("$name", createRequest.Name);
                insertCommand.Parameters.AddWithValue("$prefix", normalizedPrefix);
                insertCommand.Parameters.AddWithValue("$created_at", nowIso);
                object scalarResult = insertCommand.ExecuteScalar();
                long newProjectId = Convert.ToInt64(scalarResult);

                Project project = new Project();
                project.Id = newProjectId;
                project.Name = createRequest.Name;
                project.Prefix = normalizedPrefix;
                project.CreatedAt = nowIso;
                project.VersionCount = 0;
                HttpResponseWriter.WriteJson(context, 200, project);
            }
            finally
            {
                connection.Close();
            }
        }

        public static void HandleUpdateProject(FlatlineHttpContext context, long id)
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

            ProjectUpdateRequest updateRequest = HttpRequestReader.ReadBodyAsJson<ProjectUpdateRequest>(context);
            if (updateRequest == null || string.IsNullOrWhiteSpace(updateRequest.Name))
            {
                HttpResponseWriter.WriteJson(context, 400, new { error = "Name is required." });
                return;
            }
            string normalizedPrefix = NormalizeProjectPrefix(updateRequest.Prefix);
            if (normalizedPrefix.Length == 0)
            {
                HttpResponseWriter.WriteJson(context, 400, new { error = "Prefix must be exactly 3 letters." });
                return;
            }

            SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
            try
            {
                SqliteCommand existsCommand = connection.CreateCommand();
                existsCommand.CommandText = "SELECT id FROM projects WHERE id = $id;";
                existsCommand.Parameters.AddWithValue("$id", id);
                object existsResult = existsCommand.ExecuteScalar();
                if (existsResult == null)
                {
                    HttpResponseWriter.WriteJson(context, 404, new { error = "Project not found." });
                    return;
                }

                SqliteCommand duplicateCommand = connection.CreateCommand();
                duplicateCommand.CommandText = "SELECT id FROM projects WHERE name = $name AND id != $id;";
                duplicateCommand.Parameters.AddWithValue("$name", updateRequest.Name);
                duplicateCommand.Parameters.AddWithValue("$id", id);
                object duplicateResult = duplicateCommand.ExecuteScalar();
                if (duplicateResult != null)
                {
                    HttpResponseWriter.WriteJson(context, 409, new { error = "Project name already exists." });
                    return;
                }

                SqliteCommand prefixDuplicateCommand = connection.CreateCommand();
                prefixDuplicateCommand.CommandText = "SELECT id FROM projects WHERE prefix = $prefix AND id != $id;";
                prefixDuplicateCommand.Parameters.AddWithValue("$prefix", normalizedPrefix);
                prefixDuplicateCommand.Parameters.AddWithValue("$id", id);
                object prefixDuplicateResult = prefixDuplicateCommand.ExecuteScalar();
                if (prefixDuplicateResult != null)
                {
                    HttpResponseWriter.WriteJson(context, 409, new { error = "Project prefix already exists." });
                    return;
                }

                SqliteCommand updateCommand = connection.CreateCommand();
                updateCommand.CommandText = "UPDATE projects SET name = $name, prefix = $prefix WHERE id = $id;";
                updateCommand.Parameters.AddWithValue("$name", updateRequest.Name);
                updateCommand.Parameters.AddWithValue("$prefix", normalizedPrefix);
                updateCommand.Parameters.AddWithValue("$id", id);
                updateCommand.ExecuteNonQuery();

                SqliteCommand selectCommand = connection.CreateCommand();
                selectCommand.CommandText = "SELECT p.id, p.name, p.prefix, p.created_at, "
                    + "(SELECT COUNT(*) FROM versions v WHERE v.project_id = p.id) AS version_count "
                    + "FROM projects p WHERE p.id = $id;";
                selectCommand.Parameters.AddWithValue("$id", id);
                SqliteDataReader reader = selectCommand.ExecuteReader();
                reader.Read();
                Project project = new Project();
                project.Id = reader.GetInt64(0);
                project.Name = reader.GetString(1);
                project.Prefix = reader.GetString(2);
                project.CreatedAt = reader.GetString(3);
                project.VersionCount = reader.GetInt32(4);
                reader.Close();
                HttpResponseWriter.WriteJson(context, 200, project);
            }
            finally
            {
                connection.Close();
            }
        }

        public static void HandleDeleteProject(FlatlineHttpContext context, long id)
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
                bugRefCommand.CommandText = "SELECT COUNT(*) FROM bugs WHERE project_id = $id;";
                bugRefCommand.Parameters.AddWithValue("$id", id);
                object countResult = bugRefCommand.ExecuteScalar();
                long bugCount = 0;
                if (countResult != null)
                {
                    bugCount = Convert.ToInt64(countResult);
                }
                if (bugCount > 0)
                {
                    HttpResponseWriter.WriteJson(context, 409, new { error = "Cannot delete project that has bugs assigned to it." });
                    return;
                }

                SqliteCommand deleteCommand = connection.CreateCommand();
                deleteCommand.CommandText = "DELETE FROM projects WHERE id = $id;";
                deleteCommand.Parameters.AddWithValue("$id", id);
                int rowsAffected = deleteCommand.ExecuteNonQuery();
                if (rowsAffected == 0)
                {
                    HttpResponseWriter.WriteJson(context, 404, new { error = "Project not found." });
                    return;
                }
            }
            finally
            {
                connection.Close();
            }

            HttpResponseWriter.WriteJson(context, 200, new { ok = true });
        }

        /* Returns the uppercased prefix when the input is exactly three ASCII
         * letters, or "" when it is not valid. Prefixes are stored uppercase so
         * equality comparisons (uniqueness checks) are effectively
         * case-insensitive. */
        private static string NormalizeProjectPrefix(string rawPrefix)
        {
            if (rawPrefix == null)
            {
                return "";
            }
            string trimmedPrefix = rawPrefix.Trim();
            if (trimmedPrefix.Length != 3)
            {
                return "";
            }
            for (int characterIndex = 0; characterIndex < 3; characterIndex++)
            {
                char currentCharacter = trimmedPrefix[characterIndex];
                bool isLetter = (currentCharacter >= 'A' && currentCharacter <= 'Z') || (currentCharacter >= 'a' && currentCharacter <= 'z');
                if (!isLetter)
                {
                    return "";
                }
            }
            return trimmedPrefix.ToUpperInvariant();
        }
    }
}
