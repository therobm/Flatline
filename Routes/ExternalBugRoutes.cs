using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Data.Sqlite;
using Flatline.Database;
using Flatline.Http;
using Flatline.Models;

namespace Flatline.Routes
{
    public class ExternalBugUpdateRequest
    {
        // Sentinel: empty string means "no status change". Any other value
        // must parse as a valid eBugStatus.
        public string Status = "";
        // Sentinel: -1 means "no assignment change". 0 means "unassign".
        // >0 must refer to an existing user.
        public long AssignedTo = -1;
    }

    public static class ExternalBugRoutes
    {
        public static void HandleCreateExternalBug(FlatlineHttpContext context)
        {
            User keyOwner = ApiKeyRoutes.GetUserFromApiKey(context);
            if (keyOwner == null)
            {
                HttpResponseWriter.WriteJson(context, 401, new { error = "Invalid or missing API key." });
                return;
            }

            BugCreateRequest createRequest = HttpRequestReader.ReadBodyAsJson<BugCreateRequest>(context);
            if (createRequest == null)
            {
                HttpResponseWriter.WriteJson(context, 400, new { error = "Body is required." });
                return;
            }

            BugRoutes.CreateBugForUser(context, keyOwner, createRequest);
        }

        public static void HandleListExternalBugs(FlatlineHttpContext context)
        {
            User keyOwner = ApiKeyRoutes.GetUserFromApiKey(context);
            if (keyOwner == null)
            {
                HttpResponseWriter.WriteJson(context, 401, new { error = "Invalid or missing API key." });
                return;
            }
            BugRoutes.ListBugs(context);
        }

        public static void HandleGetExternalBug(FlatlineHttpContext context, long id)
        {
            User keyOwner = ApiKeyRoutes.GetUserFromApiKey(context);
            if (keyOwner == null)
            {
                HttpResponseWriter.WriteJson(context, 401, new { error = "Invalid or missing API key." });
                return;
            }
            Bug bug = BugRoutes.LoadBugById(id);
            if (bug == null)
            {
                HttpResponseWriter.WriteJson(context, 404, new { error = "Bug not found." });
                return;
            }
            HttpResponseWriter.WriteJson(context, 200, bug);
        }

        public static void HandleCreateExternalBugComment(FlatlineHttpContext context, long bugId)
        {
            User keyOwner = ApiKeyRoutes.GetUserFromApiKey(context);
            if (keyOwner == null)
            {
                HttpResponseWriter.WriteJson(context, 401, new { error = "Invalid or missing API key." });
                return;
            }
            CommentRoutes.CreateCommentForUser(context, keyOwner, bugId);
        }

        public static void HandleListExternalBugComments(FlatlineHttpContext context, long bugId)
        {
            User keyOwner = ApiKeyRoutes.GetUserFromApiKey(context);
            if (keyOwner == null)
            {
                HttpResponseWriter.WriteJson(context, 401, new { error = "Invalid or missing API key." });
                return;
            }
            List<Comment> commentList = CommentRoutes.LoadCommentsForBug(bugId);
            HttpResponseWriter.WriteJson(context, 200, commentList);
        }

        public static void HandleUpdateExternalBug(FlatlineHttpContext context, long id)
        {
            User keyOwner = ApiKeyRoutes.GetUserFromApiKey(context);
            if (keyOwner == null)
            {
                HttpResponseWriter.WriteJson(context, 401, new { error = "Invalid or missing API key." });
                return;
            }

            ExternalBugUpdateRequest updateRequest = HttpRequestReader.ReadBodyAsJson<ExternalBugUpdateRequest>(context);
            if (updateRequest == null)
            {
                HttpResponseWriter.WriteJson(context, 400, new { error = "Body is required." });
                return;
            }

            bool hasStatusChange = updateRequest.Status.Length > 0;
            bool hasAssignmentChange = updateRequest.AssignedTo >= 0;
            if (!hasStatusChange && !hasAssignmentChange)
            {
                HttpResponseWriter.WriteJson(context, 400, new { error = "Nothing to update. Provide Status, AssignedTo, or both." });
                return;
            }

            eBugStatus parsedStatus = eBugStatus.Open;
            if (hasStatusChange)
            {
                if (!Enum.TryParse<eBugStatus>(updateRequest.Status, false, out parsedStatus))
                {
                    HttpResponseWriter.WriteJson(context, 400, new { error = "Invalid status." });
                    return;
                }
            }

            string nowIso = DateTime.UtcNow.ToString("o");
            SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
            try
            {
                if (hasAssignmentChange && updateRequest.AssignedTo > 0)
                {
                    SqliteCommand userCheck = connection.CreateCommand();
                    userCheck.CommandText = "SELECT 1 FROM users WHERE id = $assigned_to;";
                    userCheck.Parameters.AddWithValue("$assigned_to", updateRequest.AssignedTo);
                    object userResult = userCheck.ExecuteScalar();
                    if (userResult == null)
                    {
                        HttpResponseWriter.WriteJson(context, 400, new { error = "AssignedTo user not found." });
                        return;
                    }
                }

                StringBuilder updateSql = new StringBuilder();
                updateSql.Append("UPDATE bugs SET updated_at = $updated_at");
                SqliteCommand updateCommand = connection.CreateCommand();
                updateCommand.Parameters.AddWithValue("$updated_at", nowIso);
                if (hasStatusChange)
                {
                    updateSql.Append(", status = $status");
                    updateCommand.Parameters.AddWithValue("$status", (int)parsedStatus);
                }
                if (hasAssignmentChange)
                {
                    if (updateRequest.AssignedTo == 0)
                    {
                        updateSql.Append(", assigned_to = NULL");
                    }
                    else
                    {
                        updateSql.Append(", assigned_to = $assigned_to");
                        updateCommand.Parameters.AddWithValue("$assigned_to", updateRequest.AssignedTo);
                    }
                }
                updateSql.Append(" WHERE id = $id;");
                updateCommand.Parameters.AddWithValue("$id", id);
                updateCommand.CommandText = updateSql.ToString();

                int rowsAffected = updateCommand.ExecuteNonQuery();
                if (rowsAffected == 0)
                {
                    HttpResponseWriter.WriteJson(context, 404, new { error = "Bug not found." });
                    return;
                }
            }
            finally
            {
                connection.Close();
            }

            Bug updatedBug = BugRoutes.LoadBugById(id);
            HttpResponseWriter.WriteJson(context, 200, updatedBug);
        }
    }
}
