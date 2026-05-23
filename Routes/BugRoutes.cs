using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Data.Sqlite;
using Flatline.Database;
using Flatline.Http;
using Flatline.Models;

namespace Flatline.Routes
{
    public class BugCreateRequest
    {
        public string Title = "";
        public string Description = "";
        public eBugPriority Priority = eBugPriority.Normal;
        public long AssignedTo;
        public long ProjectId;
        public long FoundInVersionId;
        public long FixedInVersionId;
    }

    public class BugUpdateRequest
    {
        public string Title;
        public string Description;
        public eBugStatus Status;
        public bool UpdateStatus;
        public eBugPriority Priority;
        public bool UpdatePriority;
        public long AssignedTo;
        public bool ClearAssignee;
        public long ProjectId;
        public long FoundInVersionId;
        public bool ClearFoundInVersion;
        public long FixedInVersionId;
        public bool ClearFixedInVersion;
    }

    public static class BugRoutes
    {
        public static void HandleListBugs(FlatlineHttpContext context)
        {
            User currentUser = AuthRoutes.GetCurrentUser(context);
            if (currentUser == null)
            {
                HttpResponseWriter.WriteJson(context, 401, new { error = "Not authenticated." });
                return;
            }

            string status = HttpRequestReader.GetQueryValue(context, "status");
            string priority = HttpRequestReader.GetQueryValue(context, "priority");
            string assignedTo = HttpRequestReader.GetQueryValue(context, "assignedTo");
            string sort = HttpRequestReader.GetQueryValue(context, "sort");
            string createdSince = HttpRequestReader.GetQueryValue(context, "createdSince");
            string updatedSince = HttpRequestReader.GetQueryValue(context, "updatedSince");
            string unassigned = HttpRequestReader.GetQueryValue(context, "unassigned");
            string createdBy = HttpRequestReader.GetQueryValue(context, "createdBy");
            string excludeClosed = HttpRequestReader.GetQueryValue(context, "excludeClosed");
            string projectFilter = HttpRequestReader.GetQueryValue(context, "projectId");

            List<int> parsedStatuses = new List<int>();
            if (!string.IsNullOrEmpty(status))
            {
                string[] statusParts = status.Split(',');
                int statusPartCount = statusParts.Length;
                for (int statusPartIndex = 0; statusPartIndex < statusPartCount; statusPartIndex++)
                {
                    string statusPart = statusParts[statusPartIndex].Trim();
                    if (statusPart.Length == 0)
                    {
                        continue;
                    }
                    eBugStatus enumValue;
                    if (!Enum.TryParse<eBugStatus>(statusPart, false, out enumValue))
                    {
                        HttpResponseWriter.WriteJson(context, 400, new { error = "Invalid status filter." });
                        return;
                    }
                    parsedStatuses.Add((int)enumValue);
                }
            }

            List<int> parsedPriorities = new List<int>();
            if (!string.IsNullOrEmpty(priority))
            {
                string[] priorityParts = priority.Split(',');
                int priorityPartCount = priorityParts.Length;
                for (int priorityPartIndex = 0; priorityPartIndex < priorityPartCount; priorityPartIndex++)
                {
                    string priorityPart = priorityParts[priorityPartIndex].Trim();
                    if (priorityPart.Length == 0)
                    {
                        continue;
                    }
                    eBugPriority enumValue;
                    if (!Enum.TryParse<eBugPriority>(priorityPart, false, out enumValue))
                    {
                        HttpResponseWriter.WriteJson(context, 400, new { error = "Invalid priority filter." });
                        return;
                    }
                    parsedPriorities.Add((int)enumValue);
                }
            }

            List<long> parsedAssignedTos = new List<long>();
            if (!string.IsNullOrEmpty(assignedTo))
            {
                string[] assignedToParts = assignedTo.Split(',');
                int assignedToPartCount = assignedToParts.Length;
                for (int assignedToPartIndex = 0; assignedToPartIndex < assignedToPartCount; assignedToPartIndex++)
                {
                    string assignedToPart = assignedToParts[assignedToPartIndex].Trim();
                    if (assignedToPart.Length == 0)
                    {
                        continue;
                    }
                    long parsedValue;
                    if (!long.TryParse(assignedToPart, out parsedValue))
                    {
                        HttpResponseWriter.WriteJson(context, 400, new { error = "Invalid assignedTo filter." });
                        return;
                    }
                    parsedAssignedTos.Add(parsedValue);
                }
            }

            long parsedCreatedBy = 0;
            bool createdByFilterPresent = false;
            if (!string.IsNullOrEmpty(createdBy))
            {
                if (!long.TryParse(createdBy, out parsedCreatedBy))
                {
                    HttpResponseWriter.WriteJson(context, 400, new { error = "Invalid createdBy filter." });
                    return;
                }
                createdByFilterPresent = true;
            }

            long parsedProjectId = 0;
            bool projectFilterPresent = false;
            if (!string.IsNullOrEmpty(projectFilter))
            {
                if (!long.TryParse(projectFilter, out parsedProjectId))
                {
                    HttpResponseWriter.WriteJson(context, 400, new { error = "Invalid projectId filter." });
                    return;
                }
                projectFilterPresent = true;
            }

            List<Bug> bugList = new List<Bug>();
            SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
            try
            {
                SqliteCommand selectCommand = connection.CreateCommand();
                StringBuilder sqlBuilder = new StringBuilder();
                AppendBugSelectFromClause(sqlBuilder);
                sqlBuilder.Append(" WHERE 1 = 1");

                if (parsedStatuses.Count > 0)
                {
                    sqlBuilder.Append(" AND b.status IN (");
                    int statusCount = parsedStatuses.Count;
                    for (int statusIndex = 0; statusIndex < statusCount; statusIndex++)
                    {
                        if (statusIndex > 0)
                        {
                            sqlBuilder.Append(",");
                        }
                        string paramName = "$status_" + statusIndex;
                        sqlBuilder.Append(paramName);
                        selectCommand.Parameters.AddWithValue(paramName, parsedStatuses[statusIndex]);
                    }
                    sqlBuilder.Append(")");
                }
                if (parsedPriorities.Count > 0)
                {
                    sqlBuilder.Append(" AND b.priority IN (");
                    int priorityCount = parsedPriorities.Count;
                    for (int priorityIndex = 0; priorityIndex < priorityCount; priorityIndex++)
                    {
                        if (priorityIndex > 0)
                        {
                            sqlBuilder.Append(",");
                        }
                        string paramName = "$priority_" + priorityIndex;
                        sqlBuilder.Append(paramName);
                        selectCommand.Parameters.AddWithValue(paramName, parsedPriorities[priorityIndex]);
                    }
                    sqlBuilder.Append(")");
                }
                if (parsedAssignedTos.Count > 0)
                {
                    sqlBuilder.Append(" AND b.assigned_to IN (");
                    int assignedToCount = parsedAssignedTos.Count;
                    for (int assignedToIndex = 0; assignedToIndex < assignedToCount; assignedToIndex++)
                    {
                        if (assignedToIndex > 0)
                        {
                            sqlBuilder.Append(",");
                        }
                        string paramName = "$assigned_to_" + assignedToIndex;
                        sqlBuilder.Append(paramName);
                        selectCommand.Parameters.AddWithValue(paramName, parsedAssignedTos[assignedToIndex]);
                    }
                    sqlBuilder.Append(")");
                }
                if (createdByFilterPresent)
                {
                    sqlBuilder.Append(" AND b.created_by = $created_by");
                    selectCommand.Parameters.AddWithValue("$created_by", parsedCreatedBy);
                }
                if (projectFilterPresent)
                {
                    sqlBuilder.Append(" AND b.project_id = $project_id");
                    selectCommand.Parameters.AddWithValue("$project_id", parsedProjectId);
                }
                if (!string.IsNullOrEmpty(createdSince))
                {
                    sqlBuilder.Append(" AND b.created_at >= $created_since");
                    selectCommand.Parameters.AddWithValue("$created_since", createdSince);
                }
                if (!string.IsNullOrEmpty(updatedSince))
                {
                    sqlBuilder.Append(" AND b.updated_at >= $updated_since");
                    selectCommand.Parameters.AddWithValue("$updated_since", updatedSince);
                }
                if (unassigned == "true")
                {
                    sqlBuilder.Append(" AND b.assigned_to IS NULL");
                }
                if (excludeClosed == "true")
                {
                    sqlBuilder.Append(" AND b.status != $excluded_closed_status");
                    selectCommand.Parameters.AddWithValue("$excluded_closed_status", (int)eBugStatus.Closed);
                }

                if (sort == "priority")
                {
                    sqlBuilder.Append(" ORDER BY b.priority DESC, b.created_at DESC");
                }
                else if (sort == "status")
                {
                    sqlBuilder.Append(" ORDER BY b.status ASC, b.created_at DESC");
                }
                else if (sort == "updated")
                {
                    sqlBuilder.Append(" ORDER BY b.updated_at DESC");
                }
                else
                {
                    sqlBuilder.Append(" ORDER BY b.created_at DESC");
                }
                sqlBuilder.Append(";");

                selectCommand.CommandText = sqlBuilder.ToString();
                SqliteDataReader reader = selectCommand.ExecuteReader();
                for (bool hasRow = reader.Read(); hasRow; hasRow = reader.Read())
                {
                    Bug bug = ReadBugFromReader(reader);
                    bugList.Add(bug);
                }
                reader.Close();
            }
            finally
            {
                connection.Close();
            }

            HttpResponseWriter.WriteJson(context, 200, bugList);
        }

        public static void HandleGetBug(FlatlineHttpContext context, long id)
        {
            User currentUser = AuthRoutes.GetCurrentUser(context);
            if (currentUser == null)
            {
                HttpResponseWriter.WriteJson(context, 401, new { error = "Not authenticated." });
                return;
            }

            Bug bug = LoadBugById(id);
            if (bug == null)
            {
                HttpResponseWriter.WriteJson(context, 404, new { error = "Bug not found." });
                return;
            }
            HttpResponseWriter.WriteJson(context, 200, bug);
        }

        public static void HandleCreateBug(FlatlineHttpContext context)
        {
            User currentUser = AuthRoutes.GetCurrentUser(context);
            if (currentUser == null)
            {
                HttpResponseWriter.WriteJson(context, 401, new { error = "Not authenticated." });
                return;
            }

            BugCreateRequest createRequest = HttpRequestReader.ReadBodyAsJson<BugCreateRequest>(context);
            if (createRequest == null)
            {
                HttpResponseWriter.WriteJson(context, 400, new { error = "Body is required." });
                return;
            }
            CreateBugForUser(context, currentUser, createRequest);
        }

        public static void CreateBugForUser(FlatlineHttpContext context, User createdByUser, BugCreateRequest createRequest)
        {
            if (string.IsNullOrWhiteSpace(createRequest.Title))
            {
                HttpResponseWriter.WriteJson(context, 400, new { error = "Title is required." });
                return;
            }
            if (createRequest.ProjectId <= 0)
            {
                HttpResponseWriter.WriteJson(context, 400, new { error = "Project is required." });
                return;
            }

            long newBugId = 0;
            SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
            try
            {
                if (!ProjectExists(connection, createRequest.ProjectId))
                {
                    HttpResponseWriter.WriteJson(context, 400, new { error = "Project not found." });
                    return;
                }
                if (createRequest.FoundInVersionId > 0 && !VersionBelongsToProject(connection, createRequest.FoundInVersionId, createRequest.ProjectId))
                {
                    HttpResponseWriter.WriteJson(context, 400, new { error = "Found-in version does not belong to the selected project." });
                    return;
                }
                if (createRequest.FixedInVersionId > 0 && !VersionBelongsToProject(connection, createRequest.FixedInVersionId, createRequest.ProjectId))
                {
                    HttpResponseWriter.WriteJson(context, 400, new { error = "Fixed-in version does not belong to the selected project." });
                    return;
                }

                string nowIso = DateTime.UtcNow.ToString("o");
                SqliteCommand insertCommand = connection.CreateCommand();
                insertCommand.CommandText = "INSERT INTO bugs (project_id, title, description, status, priority, created_by, assigned_to, found_in_version_id, fixed_in_version_id, created_at, updated_at) "
                    + "VALUES ($project_id, $title, $description, $status, $priority, $created_by, $assigned_to, $found_in_version_id, $fixed_in_version_id, $created_at, $updated_at); "
                    + "SELECT last_insert_rowid();";
                insertCommand.Parameters.AddWithValue("$project_id", createRequest.ProjectId);
                insertCommand.Parameters.AddWithValue("$title", createRequest.Title);
                if (createRequest.Description == null)
                {
                    insertCommand.Parameters.AddWithValue("$description", "");
                }
                else
                {
                    insertCommand.Parameters.AddWithValue("$description", createRequest.Description);
                }
                insertCommand.Parameters.AddWithValue("$status", (int)eBugStatus.Open);
                insertCommand.Parameters.AddWithValue("$priority", (int)createRequest.Priority);
                insertCommand.Parameters.AddWithValue("$created_by", createdByUser.Id);
                if (createRequest.AssignedTo > 0)
                {
                    insertCommand.Parameters.AddWithValue("$assigned_to", createRequest.AssignedTo);
                }
                else
                {
                    insertCommand.Parameters.AddWithValue("$assigned_to", DBNull.Value);
                }
                if (createRequest.FoundInVersionId > 0)
                {
                    insertCommand.Parameters.AddWithValue("$found_in_version_id", createRequest.FoundInVersionId);
                }
                else
                {
                    insertCommand.Parameters.AddWithValue("$found_in_version_id", DBNull.Value);
                }
                if (createRequest.FixedInVersionId > 0)
                {
                    insertCommand.Parameters.AddWithValue("$fixed_in_version_id", createRequest.FixedInVersionId);
                }
                else
                {
                    insertCommand.Parameters.AddWithValue("$fixed_in_version_id", DBNull.Value);
                }
                insertCommand.Parameters.AddWithValue("$created_at", nowIso);
                insertCommand.Parameters.AddWithValue("$updated_at", nowIso);

                object scalarResult = insertCommand.ExecuteScalar();
                newBugId = Convert.ToInt64(scalarResult);
            }
            finally
            {
                connection.Close();
            }

            Bug bug = LoadBugById(newBugId);
            HttpResponseWriter.WriteJson(context, 200, bug);
        }

        public static void HandleUpdateBug(FlatlineHttpContext context, long id)
        {
            User currentUser = AuthRoutes.GetCurrentUser(context);
            if (currentUser == null)
            {
                HttpResponseWriter.WriteJson(context, 401, new { error = "Not authenticated." });
                return;
            }

            BugUpdateRequest updateRequest = HttpRequestReader.ReadBodyAsJson<BugUpdateRequest>(context);
            if (updateRequest == null)
            {
                HttpResponseWriter.WriteJson(context, 400, new { error = "Body is required." });
                return;
            }

            SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
            try
            {
                long currentProjectId = 0;
                SqliteCommand existsCommand = connection.CreateCommand();
                existsCommand.CommandText = "SELECT project_id FROM bugs WHERE id = $id;";
                existsCommand.Parameters.AddWithValue("$id", id);
                object existsResult = existsCommand.ExecuteScalar();
                if (existsResult == null)
                {
                    HttpResponseWriter.WriteJson(context, 404, new { error = "Bug not found." });
                    return;
                }
                currentProjectId = Convert.ToInt64(existsResult);

                long effectiveProjectId = currentProjectId;
                if (updateRequest.ProjectId > 0)
                {
                    if (!ProjectExists(connection, updateRequest.ProjectId))
                    {
                        HttpResponseWriter.WriteJson(context, 400, new { error = "Project not found." });
                        return;
                    }
                    effectiveProjectId = updateRequest.ProjectId;
                }

                if (!updateRequest.ClearFoundInVersion && updateRequest.FoundInVersionId > 0)
                {
                    if (!VersionBelongsToProject(connection, updateRequest.FoundInVersionId, effectiveProjectId))
                    {
                        HttpResponseWriter.WriteJson(context, 400, new { error = "Found-in version does not belong to the selected project." });
                        return;
                    }
                }
                if (!updateRequest.ClearFixedInVersion && updateRequest.FixedInVersionId > 0)
                {
                    if (!VersionBelongsToProject(connection, updateRequest.FixedInVersionId, effectiveProjectId))
                    {
                        HttpResponseWriter.WriteJson(context, 400, new { error = "Fixed-in version does not belong to the selected project." });
                        return;
                    }
                }

                List<string> setClauses = new List<string>();
                SqliteCommand updateCommand = connection.CreateCommand();

                if (updateRequest.Title != null)
                {
                    setClauses.Add("title = $title");
                    updateCommand.Parameters.AddWithValue("$title", updateRequest.Title);
                }
                if (updateRequest.Description != null)
                {
                    setClauses.Add("description = $description");
                    updateCommand.Parameters.AddWithValue("$description", updateRequest.Description);
                }
                if (updateRequest.UpdateStatus)
                {
                    setClauses.Add("status = $status");
                    updateCommand.Parameters.AddWithValue("$status", (int)updateRequest.Status);
                }
                if (updateRequest.UpdatePriority)
                {
                    setClauses.Add("priority = $priority");
                    updateCommand.Parameters.AddWithValue("$priority", (int)updateRequest.Priority);
                }
                if (updateRequest.ClearAssignee)
                {
                    setClauses.Add("assigned_to = NULL");
                }
                else if (updateRequest.AssignedTo > 0)
                {
                    setClauses.Add("assigned_to = $assigned_to");
                    updateCommand.Parameters.AddWithValue("$assigned_to", updateRequest.AssignedTo);
                }
                if (updateRequest.ProjectId > 0)
                {
                    setClauses.Add("project_id = $project_id");
                    updateCommand.Parameters.AddWithValue("$project_id", updateRequest.ProjectId);
                }
                if (updateRequest.ClearFoundInVersion)
                {
                    setClauses.Add("found_in_version_id = NULL");
                }
                else if (updateRequest.FoundInVersionId > 0)
                {
                    setClauses.Add("found_in_version_id = $found_in_version_id");
                    updateCommand.Parameters.AddWithValue("$found_in_version_id", updateRequest.FoundInVersionId);
                }
                if (updateRequest.ClearFixedInVersion)
                {
                    setClauses.Add("fixed_in_version_id = NULL");
                }
                else if (updateRequest.FixedInVersionId > 0)
                {
                    setClauses.Add("fixed_in_version_id = $fixed_in_version_id");
                    updateCommand.Parameters.AddWithValue("$fixed_in_version_id", updateRequest.FixedInVersionId);
                }

                if (setClauses.Count > 0)
                {
                    setClauses.Add("updated_at = $updated_at");
                    updateCommand.Parameters.AddWithValue("$updated_at", DateTime.UtcNow.ToString("o"));
                    updateCommand.Parameters.AddWithValue("$id", id);

                    string setSql = string.Join(", ", setClauses);
                    updateCommand.CommandText = "UPDATE bugs SET " + setSql + " WHERE id = $id;";
                    updateCommand.ExecuteNonQuery();
                }
            }
            finally
            {
                connection.Close();
            }

            Bug bug = LoadBugById(id);
            HttpResponseWriter.WriteJson(context, 200, bug);
        }

        private static bool ProjectExists(SqliteConnection connection, long projectId)
        {
            SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT id FROM projects WHERE id = $id;";
            command.Parameters.AddWithValue("$id", projectId);
            object result = command.ExecuteScalar();
            return result != null;
        }

        private static bool VersionBelongsToProject(SqliteConnection connection, long versionId, long projectId)
        {
            SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT id FROM versions WHERE id = $id AND project_id = $project_id;";
            command.Parameters.AddWithValue("$id", versionId);
            command.Parameters.AddWithValue("$project_id", projectId);
            object result = command.ExecuteScalar();
            return result != null;
        }

        private static void AppendBugSelectFromClause(StringBuilder sqlBuilder)
        {
            sqlBuilder.Append("SELECT b.id, b.project_id, p.name, b.title, b.description, b.status, b.priority, ");
            sqlBuilder.Append("b.created_by, b.assigned_to, b.found_in_version_id, b.fixed_in_version_id, ");
            sqlBuilder.Append("b.created_at, b.updated_at, ");
            sqlBuilder.Append("creator.username, creator.display_name, ");
            sqlBuilder.Append("assignee.username, assignee.display_name, ");
            sqlBuilder.Append("found_v.name, fixed_v.name ");
            sqlBuilder.Append("FROM bugs b ");
            sqlBuilder.Append("INNER JOIN projects p ON p.id = b.project_id ");
            sqlBuilder.Append("INNER JOIN users creator ON creator.id = b.created_by ");
            sqlBuilder.Append("LEFT JOIN users assignee ON assignee.id = b.assigned_to ");
            sqlBuilder.Append("LEFT JOIN versions found_v ON found_v.id = b.found_in_version_id ");
            sqlBuilder.Append("LEFT JOIN versions fixed_v ON fixed_v.id = b.fixed_in_version_id");
        }

        private static Bug LoadBugById(long id)
        {
            SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
            try
            {
                SqliteCommand selectCommand = connection.CreateCommand();
                StringBuilder sqlBuilder = new StringBuilder();
                AppendBugSelectFromClause(sqlBuilder);
                sqlBuilder.Append(" WHERE b.id = $id;");
                selectCommand.CommandText = sqlBuilder.ToString();
                selectCommand.Parameters.AddWithValue("$id", id);

                SqliteDataReader reader = selectCommand.ExecuteReader();
                if (!reader.Read())
                {
                    reader.Close();
                    return null;
                }
                Bug bug = ReadBugFromReader(reader);
                reader.Close();
                return bug;
            }
            finally
            {
                connection.Close();
            }
        }

        private static Bug ReadBugFromReader(SqliteDataReader reader)
        {
            Bug bug = new Bug();
            bug.Id = reader.GetInt64(0);
            bug.ProjectId = reader.GetInt64(1);
            bug.ProjectName = reader.GetString(2);
            bug.Title = reader.GetString(3);
            bug.Description = reader.GetString(4);
            bug.Status = (eBugStatus)reader.GetInt32(5);
            bug.Priority = (eBugPriority)reader.GetInt32(6);
            bug.CreatedBy = reader.GetInt64(7);
            if (reader.IsDBNull(8))
            {
                bug.AssignedTo = 0;
            }
            else
            {
                bug.AssignedTo = reader.GetInt64(8);
            }
            if (reader.IsDBNull(9))
            {
                bug.FoundInVersionId = 0;
            }
            else
            {
                bug.FoundInVersionId = reader.GetInt64(9);
            }
            if (reader.IsDBNull(10))
            {
                bug.FixedInVersionId = 0;
            }
            else
            {
                bug.FixedInVersionId = reader.GetInt64(10);
            }
            bug.CreatedAt = reader.GetString(11);
            bug.UpdatedAt = reader.GetString(12);
            bug.CreatedByUsername = reader.GetString(13);
            bug.CreatedByDisplayName = reader.GetString(14);
            if (reader.IsDBNull(15))
            {
                bug.AssignedToUsername = "";
            }
            else
            {
                bug.AssignedToUsername = reader.GetString(15);
            }
            if (reader.IsDBNull(16))
            {
                bug.AssignedToDisplayName = "";
            }
            else
            {
                bug.AssignedToDisplayName = reader.GetString(16);
            }
            if (reader.IsDBNull(17))
            {
                bug.FoundInVersionName = "";
            }
            else
            {
                bug.FoundInVersionName = reader.GetString(17);
            }
            if (reader.IsDBNull(18))
            {
                bug.FixedInVersionName = "";
            }
            else
            {
                bug.FixedInVersionName = reader.GetString(18);
            }
            return bug;
        }
    }
}
