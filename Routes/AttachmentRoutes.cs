using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using Flatline.Database;
using Flatline.Http;
using Flatline.Models;

namespace Flatline.Routes
{
    public static class AttachmentRoutes
    {
        public static void HandleListForBug(FlatlineHttpContext context, long bugId)
        {
            User currentUser = AuthRoutes.GetCurrentUser(context);
            if (currentUser == null)
            {
                HttpResponseWriter.WriteJson(context, 401, new { error = "Not authenticated." });
                return;
            }

            List<Attachment> attachmentList = LoadAttachmentsForBug(bugId);
            HttpResponseWriter.WriteJson(context, 200, attachmentList);
        }

        public static List<Attachment> LoadAttachmentsForBug(long bugId)
        {
            List<Attachment> attachmentList = new List<Attachment>();
            SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
            try
            {
                SqliteCommand selectCommand = connection.CreateCommand();
                selectCommand.CommandText = "SELECT a.id, a.bug_id, a.filename, a.content_type, a.size_bytes, "
                    + "a.uploaded_by, u.username, u.display_name, a.uploaded_at "
                    + "FROM attachments a "
                    + "INNER JOIN users u ON u.id = a.uploaded_by "
                    + "WHERE a.bug_id = $bug_id "
                    + "ORDER BY a.uploaded_at ASC;";
                selectCommand.Parameters.AddWithValue("$bug_id", bugId);

                SqliteDataReader reader = selectCommand.ExecuteReader();
                const int maxRows = 100000;
                for (int rowIndex = 0; rowIndex < maxRows; rowIndex++)
                {
                    if (!reader.Read())
                    {
                        break;
                    }
                    Attachment attachment = new Attachment();
                    attachment.Id = reader.GetInt64(0);
                    attachment.BugId = reader.GetInt64(1);
                    attachment.Filename = reader.GetString(2);
                    attachment.ContentType = reader.GetString(3);
                    attachment.SizeBytes = reader.GetInt64(4);
                    attachment.UploadedBy = reader.GetInt64(5);
                    attachment.UploadedByUsername = reader.GetString(6);
                    attachment.UploadedByDisplayName = reader.GetString(7);
                    attachment.UploadedAt = reader.GetString(8);
                    attachmentList.Add(attachment);
                }
                reader.Close();
            }
            finally
            {
                connection.Close();
            }
            return attachmentList;
        }

        public static void HandleUpload(FlatlineHttpContext context, long bugId)
        {
            User currentUser = AuthRoutes.GetCurrentUser(context);
            if (currentUser == null)
            {
                HttpResponseWriter.WriteJson(context, 401, new { error = "Not authenticated." });
                return;
            }
            UploadForUser(context, currentUser, bugId);
        }

        public static void UploadForUser(FlatlineHttpContext context, User currentUser, long bugId)
        {
            string contentTypeHeader = HttpRequestReader.GetHeaderValue(context, "Content-Type");
            string boundary;
            if (!MultipartParser.TryGetBoundary(contentTypeHeader, out boundary))
            {
                HttpResponseWriter.WriteJson(context, 400, new { error = "Request body must be multipart/form-data with a boundary." });
                return;
            }

            List<MultipartFilePart> fileParts = MultipartParser.ParseFileParts(context.Request.BodyBytes, boundary);
            if (fileParts.Count == 0)
            {
                HttpResponseWriter.WriteJson(context, 400, new { error = "No file part found in upload." });
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

                List<Attachment> created = new List<Attachment>();
                int filePartCount = fileParts.Count;
                for (int filePartIndex = 0; filePartIndex < filePartCount; filePartIndex++)
                {
                    MultipartFilePart part = fileParts[filePartIndex];
                    string storedName = AttachmentStorage.GenerateStoredName();
                    AttachmentStorage.WriteFile(bugId, storedName, part.Bytes);

                    string nowIso = DateTime.UtcNow.ToString("o");
                    SqliteCommand insertCommand = connection.CreateCommand();
                    insertCommand.CommandText = "INSERT INTO attachments (bug_id, filename, content_type, size_bytes, stored_name, uploaded_by, uploaded_at) "
                        + "VALUES ($bug_id, $filename, $content_type, $size_bytes, $stored_name, $uploaded_by, $uploaded_at); "
                        + "SELECT last_insert_rowid();";
                    insertCommand.Parameters.AddWithValue("$bug_id", bugId);
                    insertCommand.Parameters.AddWithValue("$filename", part.Filename);
                    insertCommand.Parameters.AddWithValue("$content_type", part.ContentType);
                    insertCommand.Parameters.AddWithValue("$size_bytes", part.Bytes.Length);
                    insertCommand.Parameters.AddWithValue("$stored_name", storedName);
                    insertCommand.Parameters.AddWithValue("$uploaded_by", currentUser.Id);
                    insertCommand.Parameters.AddWithValue("$uploaded_at", nowIso);
                    long attachmentId = (long)insertCommand.ExecuteScalar();

                    Attachment attachment = new Attachment();
                    attachment.Id = attachmentId;
                    attachment.BugId = bugId;
                    attachment.Filename = part.Filename;
                    attachment.ContentType = part.ContentType;
                    attachment.SizeBytes = part.Bytes.Length;
                    attachment.UploadedBy = currentUser.Id;
                    attachment.UploadedByUsername = currentUser.Username;
                    attachment.UploadedByDisplayName = currentUser.DisplayName;
                    attachment.UploadedAt = nowIso;
                    created.Add(attachment);
                }

                HttpResponseWriter.WriteJson(context, 200, created);
            }
            finally
            {
                connection.Close();
            }
        }

        public static void HandleDownload(FlatlineHttpContext context, long attachmentId)
        {
            User currentUser = AuthRoutes.GetCurrentUser(context);
            if (currentUser == null)
            {
                HttpResponseWriter.WriteJson(context, 401, new { error = "Not authenticated." });
                return;
            }
            ServeAttachment(context, attachmentId);
        }

        public static void ServeAttachment(FlatlineHttpContext context, long attachmentId)
        {
            long bugId;
            string filename;
            string contentType;
            string storedName;
            if (!LookupAttachmentForServe(attachmentId, out bugId, out filename, out contentType, out storedName))
            {
                HttpResponseWriter.WriteJson(context, 404, new { error = "Attachment not found." });
                return;
            }

            string filePath = AttachmentStorage.GetFilePath(bugId, storedName);
            if (!File.Exists(filePath))
            {
                HttpResponseWriter.WriteJson(context, 404, new { error = "Attachment file missing on disk." });
                return;
            }

            byte[] bytes = File.ReadAllBytes(filePath);
            /* inline disposition so images render inside <img src=...>; the
             * filename hint is still useful for browsers that offer a
             * right-click "save as". */
            context.Response.Headers["Content-Disposition"] = "inline; filename=\"" + SanitizeFilenameForHeader(filename) + "\"";
            HttpResponseWriter.WriteBytes(context, 200, contentType, bytes);
        }

        public static void HandleDelete(FlatlineHttpContext context, long attachmentId)
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
                SqliteCommand selectCommand = connection.CreateCommand();
                selectCommand.CommandText = "SELECT bug_id, stored_name FROM attachments WHERE id = $id;";
                selectCommand.Parameters.AddWithValue("$id", attachmentId);
                SqliteDataReader reader = selectCommand.ExecuteReader();
                if (!reader.Read())
                {
                    reader.Close();
                    HttpResponseWriter.WriteJson(context, 404, new { error = "Attachment not found." });
                    return;
                }
                long bugId = reader.GetInt64(0);
                string storedName = reader.GetString(1);
                reader.Close();

                SqliteCommand deleteCommand = connection.CreateCommand();
                deleteCommand.CommandText = "DELETE FROM attachments WHERE id = $id;";
                deleteCommand.Parameters.AddWithValue("$id", attachmentId);
                deleteCommand.ExecuteNonQuery();

                AttachmentStorage.DeleteFile(bugId, storedName);
            }
            finally
            {
                connection.Close();
            }
            HttpResponseWriter.WriteJson(context, 200, new { ok = true });
        }

        private static bool LookupAttachmentForServe(long attachmentId, out long bugId, out string filename, out string contentType, out string storedName)
        {
            bugId = 0;
            filename = "";
            contentType = "";
            storedName = "";
            SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
            try
            {
                SqliteCommand selectCommand = connection.CreateCommand();
                selectCommand.CommandText = "SELECT bug_id, filename, content_type, stored_name FROM attachments WHERE id = $id;";
                selectCommand.Parameters.AddWithValue("$id", attachmentId);
                SqliteDataReader reader = selectCommand.ExecuteReader();
                if (!reader.Read())
                {
                    reader.Close();
                    return false;
                }
                bugId = reader.GetInt64(0);
                filename = reader.GetString(1);
                contentType = reader.GetString(2);
                storedName = reader.GetString(3);
                reader.Close();
                return true;
            }
            finally
            {
                connection.Close();
            }
        }

        /* Strip characters that would break a Content-Disposition header.
         * Browsers will pick up the rest as a hint; the on-disk filename
         * is unrelated. */
        private static string SanitizeFilenameForHeader(string filename)
        {
            char[] cleaned = new char[filename.Length];
            int length = filename.Length;
            for (int index = 0; index < length; index++)
            {
                char c = filename[index];
                if (c == '"' || c == '\r' || c == '\n')
                {
                    cleaned[index] = '_';
                }
                else
                {
                    cleaned[index] = c;
                }
            }
            return new string(cleaned);
        }
    }
}
