using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Flatline.Database;
using Flatline.Http;
using Flatline.Models;

namespace Flatline.Routes
{
    public class CommentCreateRequest
    {
        public string Text = "";
    }

    public static class CommentRoutes
    {
        public static void HandleListComments(FlatlineHttpContext context, long bugId)
        {
            User currentUser = AuthRoutes.GetCurrentUser(context);
            if (currentUser == null)
            {
                HttpResponseWriter.WriteJson(context, 401, new { error = "Not authenticated." });
                return;
            }

            List<Comment> commentList = new List<Comment>();
            SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
            try
            {
                SqliteCommand selectCommand = connection.CreateCommand();
                selectCommand.CommandText = "SELECT c.id, c.bug_id, c.user_id, c.text, c.created_at, u.username, u.display_name "
                    + "FROM comments c "
                    + "INNER JOIN users u ON u.id = c.user_id "
                    + "WHERE c.bug_id = $bug_id "
                    + "ORDER BY c.created_at ASC;";
                selectCommand.Parameters.AddWithValue("$bug_id", bugId);

                SqliteDataReader reader = selectCommand.ExecuteReader();
                for (bool hasRow = reader.Read(); hasRow; hasRow = reader.Read())
                {
                    Comment comment = new Comment();
                    comment.Id = reader.GetInt64(0);
                    comment.BugId = reader.GetInt64(1);
                    comment.UserId = reader.GetInt64(2);
                    comment.Text = reader.GetString(3);
                    comment.CreatedAt = reader.GetString(4);
                    comment.Username = reader.GetString(5);
                    comment.DisplayName = reader.GetString(6);
                    commentList.Add(comment);
                }
                reader.Close();
            }
            finally
            {
                connection.Close();
            }

            HttpResponseWriter.WriteJson(context, 200, commentList);
        }

        public static void HandleCreateComment(FlatlineHttpContext context, long bugId)
        {
            User currentUser = AuthRoutes.GetCurrentUser(context);
            if (currentUser == null)
            {
                HttpResponseWriter.WriteJson(context, 401, new { error = "Not authenticated." });
                return;
            }

            CommentCreateRequest createRequest = HttpRequestReader.ReadBodyAsJson<CommentCreateRequest>(context);
            if (createRequest == null || string.IsNullOrWhiteSpace(createRequest.Text))
            {
                HttpResponseWriter.WriteJson(context, 400, new { error = "Comment text is required." });
                return;
            }

            SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
            try
            {
                SqliteCommand bugExistsCommand = connection.CreateCommand();
                bugExistsCommand.CommandText = "SELECT id FROM bugs WHERE id = $id;";
                bugExistsCommand.Parameters.AddWithValue("$id", bugId);
                object bugExistsResult = bugExistsCommand.ExecuteScalar();
                if (bugExistsResult == null)
                {
                    HttpResponseWriter.WriteJson(context, 404, new { error = "Bug not found." });
                    return;
                }

                string nowIso = DateTime.UtcNow.ToString("o");
                SqliteCommand insertCommand = connection.CreateCommand();
                insertCommand.CommandText = "INSERT INTO comments (bug_id, user_id, text, created_at) VALUES ($bug_id, $user_id, $text, $created_at); "
                    + "SELECT last_insert_rowid();";
                insertCommand.Parameters.AddWithValue("$bug_id", bugId);
                insertCommand.Parameters.AddWithValue("$user_id", currentUser.Id);
                insertCommand.Parameters.AddWithValue("$text", createRequest.Text);
                insertCommand.Parameters.AddWithValue("$created_at", nowIso);

                object scalarResult = insertCommand.ExecuteScalar();
                long newCommentId = Convert.ToInt64(scalarResult);

                SqliteCommand touchBugCommand = connection.CreateCommand();
                touchBugCommand.CommandText = "UPDATE bugs SET updated_at = $updated_at WHERE id = $id;";
                touchBugCommand.Parameters.AddWithValue("$updated_at", nowIso);
                touchBugCommand.Parameters.AddWithValue("$id", bugId);
                touchBugCommand.ExecuteNonQuery();

                Comment comment = new Comment();
                comment.Id = newCommentId;
                comment.BugId = bugId;
                comment.UserId = currentUser.Id;
                comment.Text = createRequest.Text;
                comment.CreatedAt = nowIso;
                comment.Username = currentUser.Username;
                comment.DisplayName = currentUser.DisplayName;
                HttpResponseWriter.WriteJson(context, 200, comment);
            }
            finally
            {
                connection.Close();
            }
        }
    }
}
