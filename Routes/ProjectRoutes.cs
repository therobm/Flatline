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
    }

    public class ProjectUpdateRequest
    {
        public string Name = "";
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

            List<Project> projectList = new List<Project>();
            SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
            try
            {
                SqliteCommand selectCommand = connection.CreateCommand();
                selectCommand.CommandText = "SELECT p.id, p.name, p.created_at, "
                    + "(SELECT COUNT(*) FROM versions v WHERE v.project_id = p.id) AS version_count "
                    + "FROM projects p ORDER BY p.name ASC;";
                SqliteDataReader reader = selectCommand.ExecuteReader();
                for (bool hasRow = reader.Read(); hasRow; hasRow = reader.Read())
                {
                    Project project = new Project();
                    project.Id = reader.GetInt64(0);
                    project.Name = reader.GetString(1);
                    project.CreatedAt = reader.GetString(2);
                    project.VersionCount = reader.GetInt32(3);
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

                string nowIso = DateTime.UtcNow.ToString("o");
                SqliteCommand insertCommand = connection.CreateCommand();
                insertCommand.CommandText = "INSERT INTO projects (name, created_at) VALUES ($name, $created_at); "
                    + "SELECT last_insert_rowid();";
                insertCommand.Parameters.AddWithValue("$name", createRequest.Name);
                insertCommand.Parameters.AddWithValue("$created_at", nowIso);
                object scalarResult = insertCommand.ExecuteScalar();
                long newProjectId = Convert.ToInt64(scalarResult);

                Project project = new Project();
                project.Id = newProjectId;
                project.Name = createRequest.Name;
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

                SqliteCommand updateCommand = connection.CreateCommand();
                updateCommand.CommandText = "UPDATE projects SET name = $name WHERE id = $id;";
                updateCommand.Parameters.AddWithValue("$name", updateRequest.Name);
                updateCommand.Parameters.AddWithValue("$id", id);
                updateCommand.ExecuteNonQuery();

                SqliteCommand selectCommand = connection.CreateCommand();
                selectCommand.CommandText = "SELECT p.id, p.name, p.created_at, "
                    + "(SELECT COUNT(*) FROM versions v WHERE v.project_id = p.id) AS version_count "
                    + "FROM projects p WHERE p.id = $id;";
                selectCommand.Parameters.AddWithValue("$id", id);
                SqliteDataReader reader = selectCommand.ExecuteReader();
                reader.Read();
                Project project = new Project();
                project.Id = reader.GetInt64(0);
                project.Name = reader.GetString(1);
                project.CreatedAt = reader.GetString(2);
                project.VersionCount = reader.GetInt32(3);
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
    }
}
