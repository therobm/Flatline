using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Flatline.Database;
using Flatline.Http;
using Flatline.Models;

namespace Flatline.Routes
{
    public class RelatedBugSummary
    {
        public long Id;
        public string Title = "";
        public eBugStatus Status;
        public eBugPriority Priority;
        public string ProjectPrefix = "";
        public long ProjectBugNumber;
    }

    public class RelatedBugCreateRequest
    {
        public long RelatedBugId;
    }

    public static class RelatedBugRoutes
    {
        public static void HandleListRelated(FlatlineHttpContext context, long bugId)
        {
            User currentUser = AuthRoutes.GetCurrentUser(context);
            if (currentUser == null)
            {
                HttpResponseWriter.WriteJson(context, 401, new { error = "Not authenticated." });
                return;
            }

            SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
            try
            {
                SqliteCommand existsCommand = connection.CreateCommand();
                existsCommand.CommandText = "SELECT id FROM bugs WHERE id = $id;";
                existsCommand.Parameters.AddWithValue("$id", bugId);
                object existsResult = existsCommand.ExecuteScalar();
                if (existsResult == null)
                {
                    HttpResponseWriter.WriteJson(context, 404, new { error = "Bug not found." });
                    return;
                }
            }
            finally
            {
                connection.Close();
            }

            List<RelatedBugSummary> relatedList = LoadRelatedForBug(bugId);
            HttpResponseWriter.WriteJson(context, 200, relatedList);
        }

        public static List<RelatedBugSummary> LoadRelatedForBug(long bugId)
        {
            List<RelatedBugSummary> relatedList = new List<RelatedBugSummary>();
            SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
            try
            {
                SqliteCommand selectCommand = connection.CreateCommand();
                selectCommand.CommandText = "SELECT b.id, b.title, b.status, b.priority, p.prefix, b.project_bug_number "
                    + "FROM related_bugs r "
                    + "INNER JOIN bugs b ON b.id = r.related_bug_id "
                    + "INNER JOIN projects p ON p.id = b.project_id "
                    + "WHERE r.bug_id = $bug_id "
                    + "ORDER BY b.id ASC;";
                selectCommand.Parameters.AddWithValue("$bug_id", bugId);
                SqliteDataReader reader = selectCommand.ExecuteReader();
                const int maxRows = 100000;
                for (int rowIndex = 0; rowIndex < maxRows; rowIndex++)
                {
                    if (!reader.Read())
                    {
                        break;
                    }
                    RelatedBugSummary summary = new RelatedBugSummary();
                    summary.Id = reader.GetInt64(0);
                    summary.Title = reader.GetString(1);
                    summary.Status = (eBugStatus)reader.GetInt32(2);
                    summary.Priority = (eBugPriority)reader.GetInt32(3);
                    summary.ProjectPrefix = reader.GetString(4);
                    summary.ProjectBugNumber = reader.GetInt64(5);
                    relatedList.Add(summary);
                }
                reader.Close();
            }
            finally
            {
                connection.Close();
            }
            return relatedList;
        }

        public static void HandleAddRelated(FlatlineHttpContext context, long bugId)
        {
            User currentUser = AuthRoutes.GetCurrentUser(context);
            if (currentUser == null)
            {
                HttpResponseWriter.WriteJson(context, 401, new { error = "Not authenticated." });
                return;
            }
            AddRelatedForBug(context, bugId, false);
        }

        /* Shared add path used by both the cookie-auth route and the
         * external API-key route. treatDuplicateAsSuccess flips the
         * already-related behaviour: the web UI surfaces it as a 409 so
         * the user sees the click went nowhere; the external API is
         * idempotent (re-posting an existing link is a no-op 200 with
         * the existing relation summary). */
        public static void AddRelatedForBug(FlatlineHttpContext context, long bugId, bool treatDuplicateAsSuccess)
        {
            RelatedBugCreateRequest createRequest = HttpRequestReader.ReadBodyAsJson<RelatedBugCreateRequest>(context);
            if (createRequest == null || createRequest.RelatedBugId <= 0)
            {
                HttpResponseWriter.WriteJson(context, 400, new { error = "RelatedBugId is required." });
                return;
            }
            if (createRequest.RelatedBugId == bugId)
            {
                HttpResponseWriter.WriteJson(context, 400, new { error = "A bug cannot be related to itself." });
                return;
            }

            SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
            try
            {
                SqliteCommand bothExistCommand = connection.CreateCommand();
                bothExistCommand.CommandText = "SELECT COUNT(*) FROM bugs WHERE id IN ($a, $b);";
                bothExistCommand.Parameters.AddWithValue("$a", bugId);
                bothExistCommand.Parameters.AddWithValue("$b", createRequest.RelatedBugId);
                object countResult = bothExistCommand.ExecuteScalar();
                long bothCount = 0;
                if (countResult != null)
                {
                    bothCount = Convert.ToInt64(countResult);
                }
                if (bothCount < 2)
                {
                    HttpResponseWriter.WriteJson(context, 404, new { error = "One or both bugs not found." });
                    return;
                }

                SqliteCommand duplicateCommand = connection.CreateCommand();
                duplicateCommand.CommandText = "SELECT bug_id FROM related_bugs WHERE bug_id = $a AND related_bug_id = $b;";
                duplicateCommand.Parameters.AddWithValue("$a", bugId);
                duplicateCommand.Parameters.AddWithValue("$b", createRequest.RelatedBugId);
                object duplicateResult = duplicateCommand.ExecuteScalar();
                if (duplicateResult != null)
                {
                    if (!treatDuplicateAsSuccess)
                    {
                        HttpResponseWriter.WriteJson(context, 409, new { error = "Bugs are already related." });
                        return;
                    }
                    RelatedBugSummary existingSummary = LoadSummary(connection, createRequest.RelatedBugId);
                    HttpResponseWriter.WriteJson(context, 200, existingSummary);
                    return;
                }

                /* Store both directions so listing related bugs for either side is
                 * a single bug_id = ? lookup. Both rows are inserted in a single
                 * transaction so partial state can never appear. */
                string nowIso = DateTime.UtcNow.ToString("o");
                SqliteTransaction transaction = connection.BeginTransaction();
                try
                {
                    SqliteCommand insertForwardCommand = connection.CreateCommand();
                    insertForwardCommand.Transaction = transaction;
                    insertForwardCommand.CommandText = "INSERT INTO related_bugs (bug_id, related_bug_id, created_at) VALUES ($bug_id, $related_bug_id, $created_at);";
                    insertForwardCommand.Parameters.AddWithValue("$bug_id", bugId);
                    insertForwardCommand.Parameters.AddWithValue("$related_bug_id", createRequest.RelatedBugId);
                    insertForwardCommand.Parameters.AddWithValue("$created_at", nowIso);
                    insertForwardCommand.ExecuteNonQuery();

                    SqliteCommand insertReverseCommand = connection.CreateCommand();
                    insertReverseCommand.Transaction = transaction;
                    insertReverseCommand.CommandText = "INSERT INTO related_bugs (bug_id, related_bug_id, created_at) VALUES ($bug_id, $related_bug_id, $created_at);";
                    insertReverseCommand.Parameters.AddWithValue("$bug_id", createRequest.RelatedBugId);
                    insertReverseCommand.Parameters.AddWithValue("$related_bug_id", bugId);
                    insertReverseCommand.Parameters.AddWithValue("$created_at", nowIso);
                    insertReverseCommand.ExecuteNonQuery();

                    transaction.Commit();
                }
                catch (Exception)
                {
                    transaction.Rollback();
                    throw;
                }

                RelatedBugSummary summary = LoadSummary(connection, createRequest.RelatedBugId);
                HttpResponseWriter.WriteJson(context, 200, summary);
            }
            finally
            {
                connection.Close();
            }
        }

        public static void HandleDeleteRelated(FlatlineHttpContext context, long bugId, long relatedBugId)
        {
            User currentUser = AuthRoutes.GetCurrentUser(context);
            if (currentUser == null)
            {
                HttpResponseWriter.WriteJson(context, 401, new { error = "Not authenticated." });
                return;
            }
            DeleteRelatedForBug(context, bugId, relatedBugId);
        }

        public static void DeleteRelatedForBug(FlatlineHttpContext context, long bugId, long relatedBugId)
        {
            SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
            try
            {
                SqliteTransaction transaction = connection.BeginTransaction();
                int totalRows = 0;
                try
                {
                    SqliteCommand deleteForwardCommand = connection.CreateCommand();
                    deleteForwardCommand.Transaction = transaction;
                    deleteForwardCommand.CommandText = "DELETE FROM related_bugs WHERE bug_id = $a AND related_bug_id = $b;";
                    deleteForwardCommand.Parameters.AddWithValue("$a", bugId);
                    deleteForwardCommand.Parameters.AddWithValue("$b", relatedBugId);
                    totalRows += deleteForwardCommand.ExecuteNonQuery();

                    SqliteCommand deleteReverseCommand = connection.CreateCommand();
                    deleteReverseCommand.Transaction = transaction;
                    deleteReverseCommand.CommandText = "DELETE FROM related_bugs WHERE bug_id = $a AND related_bug_id = $b;";
                    deleteReverseCommand.Parameters.AddWithValue("$a", relatedBugId);
                    deleteReverseCommand.Parameters.AddWithValue("$b", bugId);
                    totalRows += deleteReverseCommand.ExecuteNonQuery();

                    transaction.Commit();
                }
                catch (Exception)
                {
                    transaction.Rollback();
                    throw;
                }

                if (totalRows == 0)
                {
                    HttpResponseWriter.WriteJson(context, 404, new { error = "Relation not found." });
                    return;
                }
            }
            finally
            {
                connection.Close();
            }

            HttpResponseWriter.WriteJson(context, 200, new { ok = true });
        }

        private static RelatedBugSummary LoadSummary(SqliteConnection connection, long bugId)
        {
            SqliteCommand summaryCommand = connection.CreateCommand();
            summaryCommand.CommandText = "SELECT b.id, b.title, b.status, b.priority, p.prefix, b.project_bug_number "
                + "FROM bugs b INNER JOIN projects p ON p.id = b.project_id WHERE b.id = $id;";
            summaryCommand.Parameters.AddWithValue("$id", bugId);
            SqliteDataReader summaryReader = summaryCommand.ExecuteReader();
            summaryReader.Read();
            RelatedBugSummary summary = new RelatedBugSummary();
            summary.Id = summaryReader.GetInt64(0);
            summary.Title = summaryReader.GetString(1);
            summary.Status = (eBugStatus)summaryReader.GetInt32(2);
            summary.Priority = (eBugPriority)summaryReader.GetInt32(3);
            summary.ProjectPrefix = summaryReader.GetString(4);
            summary.ProjectBugNumber = summaryReader.GetInt64(5);
            summaryReader.Close();
            return summary;
        }
    }
}
