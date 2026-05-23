using System;
using Microsoft.Data.Sqlite;
using Flatline.Database;
using Flatline.Http;
using Flatline.Models;

namespace Flatline.Routes
{
    public class BugStatusUpdateRequest
    {
        public eBugStatus Status;
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

        public static void HandleUpdateExternalBugStatus(FlatlineHttpContext context, long id)
        {
            User keyOwner = ApiKeyRoutes.GetUserFromApiKey(context);
            if (keyOwner == null)
            {
                HttpResponseWriter.WriteJson(context, 401, new { error = "Invalid or missing API key." });
                return;
            }

            BugStatusUpdateRequest updateRequest = HttpRequestReader.ReadBodyAsJson<BugStatusUpdateRequest>(context);
            if (updateRequest == null)
            {
                HttpResponseWriter.WriteJson(context, 400, new { error = "Body is required." });
                return;
            }
            if (!Enum.IsDefined(typeof(eBugStatus), updateRequest.Status))
            {
                HttpResponseWriter.WriteJson(context, 400, new { error = "Invalid status." });
                return;
            }

            string nowIso = DateTime.UtcNow.ToString("o");
            SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
            try
            {
                SqliteCommand updateCommand = connection.CreateCommand();
                updateCommand.CommandText = "UPDATE bugs SET status = $status, updated_at = $updated_at WHERE id = $id;";
                updateCommand.Parameters.AddWithValue("$status", (int)updateRequest.Status);
                updateCommand.Parameters.AddWithValue("$updated_at", nowIso);
                updateCommand.Parameters.AddWithValue("$id", id);
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
