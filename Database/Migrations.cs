using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace Flatline.Database
{
    internal class MigrationStep
    {
        public int Version;
        public string Sql = "";
    }

    public static class Migrations
    {
        public static void RunMigrations()
        {
            SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
            try
            {
                EnsureSchemaVersionsTable(connection);
                int currentVersion = GetCurrentSchemaVersion(connection);

                List<MigrationStep> allMigrations = BuildMigrationList();
                int migrationCount = allMigrations.Count;
                for (int migrationIndex = 0; migrationIndex < migrationCount; migrationIndex++)
                {
                    MigrationStep step = allMigrations[migrationIndex];
                    if (step.Version > currentVersion)
                    {
                        ApplyMigration(connection, step);
                    }
                }
            }
            finally
            {
                connection.Close();
            }
        }

        private static void EnsureSchemaVersionsTable(SqliteConnection connection)
        {
            SqliteCommand command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE IF NOT EXISTS schema_versions (version INTEGER PRIMARY KEY, applied_at TEXT NOT NULL);";
            command.ExecuteNonQuery();
        }

        private static int GetCurrentSchemaVersion(SqliteConnection connection)
        {
            SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_versions;";
            object result = command.ExecuteScalar();
            if (result == null)
            {
                return 0;
            }
            return Convert.ToInt32(result);
        }

        private static void ApplyMigration(SqliteConnection connection, MigrationStep step)
        {
            SqliteTransaction transaction = connection.BeginTransaction();
            try
            {
                SqliteCommand migrationCommand = connection.CreateCommand();
                migrationCommand.Transaction = transaction;
                migrationCommand.CommandText = step.Sql;
                migrationCommand.ExecuteNonQuery();

                SqliteCommand recordCommand = connection.CreateCommand();
                recordCommand.Transaction = transaction;
                recordCommand.CommandText = "INSERT INTO schema_versions (version, applied_at) VALUES ($version, $applied_at);";
                recordCommand.Parameters.AddWithValue("$version", step.Version);
                recordCommand.Parameters.AddWithValue("$applied_at", DateTime.UtcNow.ToString("o"));
                recordCommand.ExecuteNonQuery();

                transaction.Commit();
            }
            catch (Exception)
            {
                transaction.Rollback();
                throw;
            }
        }

        private static List<MigrationStep> BuildMigrationList()
        {
            List<MigrationStep> migrationList = new List<MigrationStep>();

            MigrationStep version1 = new MigrationStep();
            version1.Version = 1;
            version1.Sql = @"
                CREATE TABLE users (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    username TEXT NOT NULL UNIQUE,
                    password_hash TEXT NOT NULL,
                    display_name TEXT NOT NULL,
                    is_admin INTEGER NOT NULL DEFAULT 0,
                    created_at TEXT NOT NULL
                );

                CREATE TABLE sessions (
                    token TEXT PRIMARY KEY,
                    user_id INTEGER NOT NULL,
                    created_at TEXT NOT NULL,
                    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
                );

                CREATE TABLE projects (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL UNIQUE,
                    created_at TEXT NOT NULL
                );

                CREATE TABLE versions (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    project_id INTEGER NOT NULL,
                    name TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
                    UNIQUE(project_id, name)
                );

                CREATE INDEX idx_versions_project_id ON versions(project_id);

                CREATE TABLE bugs (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    project_id INTEGER NOT NULL,
                    title TEXT NOT NULL,
                    description TEXT NOT NULL,
                    status INTEGER NOT NULL,
                    priority INTEGER NOT NULL,
                    created_by INTEGER NOT NULL,
                    assigned_to INTEGER,
                    found_in_version_id INTEGER,
                    fixed_in_version_id INTEGER,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    FOREIGN KEY (created_by) REFERENCES users(id),
                    FOREIGN KEY (assigned_to) REFERENCES users(id),
                    FOREIGN KEY (project_id) REFERENCES projects(id),
                    FOREIGN KEY (found_in_version_id) REFERENCES versions(id),
                    FOREIGN KEY (fixed_in_version_id) REFERENCES versions(id)
                );

                CREATE INDEX idx_bugs_project_id ON bugs(project_id);
                CREATE INDEX idx_bugs_status ON bugs(status);
                CREATE INDEX idx_bugs_priority ON bugs(priority);
                CREATE INDEX idx_bugs_assigned_to ON bugs(assigned_to);

                CREATE TABLE comments (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    bug_id INTEGER NOT NULL,
                    user_id INTEGER NOT NULL,
                    text TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    FOREIGN KEY (bug_id) REFERENCES bugs(id) ON DELETE CASCADE,
                    FOREIGN KEY (user_id) REFERENCES users(id)
                );

                CREATE INDEX idx_comments_bug_id ON comments(bug_id);

                CREATE TABLE api_keys (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    user_id INTEGER NOT NULL,
                    name TEXT NOT NULL,
                    key_hash TEXT NOT NULL UNIQUE,
                    key_prefix TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    last_used_at TEXT,
                    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
                );

                CREATE INDEX idx_api_keys_user_id ON api_keys(user_id);

                CREATE TABLE related_bugs (
                    bug_id INTEGER NOT NULL,
                    related_bug_id INTEGER NOT NULL,
                    created_at TEXT NOT NULL,
                    PRIMARY KEY (bug_id, related_bug_id),
                    FOREIGN KEY (bug_id) REFERENCES bugs(id) ON DELETE CASCADE,
                    FOREIGN KEY (related_bug_id) REFERENCES bugs(id) ON DELETE CASCADE,
                    CHECK (bug_id != related_bug_id)
                );

                CREATE INDEX idx_related_bugs_bug_id ON related_bugs(bug_id);
            ";
            migrationList.Add(version1);

            // v2: per-bug file attachments. Bytes live on the filesystem under
            // the configured attachments directory; this table is just the
            // metadata index. stored_name is a server-generated unique token
            // used as the on-disk filename so arbitrary user-supplied names
            // never reach the filesystem.
            MigrationStep version2 = new MigrationStep();
            version2.Version = 2;
            version2.Sql = @"
                CREATE TABLE IF NOT EXISTS attachments (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    bug_id INTEGER NOT NULL,
                    filename TEXT NOT NULL,
                    content_type TEXT NOT NULL,
                    size_bytes INTEGER NOT NULL,
                    stored_name TEXT NOT NULL UNIQUE,
                    uploaded_by INTEGER NOT NULL,
                    uploaded_at TEXT NOT NULL,
                    FOREIGN KEY (bug_id) REFERENCES bugs(id) ON DELETE CASCADE,
                    FOREIGN KEY (uploaded_by) REFERENCES users(id)
                );

                CREATE INDEX IF NOT EXISTS idx_attachments_bug_id ON attachments(bug_id);
            ";
            migrationList.Add(version2);

            // v3: heal databases where v2's schema_versions row was recorded
            // but the actual CREATE TABLE never landed (seen in the wild on
            // one deploy of v2 — root cause unclear, but this guarantees the
            // table exists regardless of what happened during v2). No-op on
            // any DB that already has the attachments table.
            MigrationStep version3 = new MigrationStep();
            version3.Version = 3;
            version3.Sql = @"
                CREATE TABLE IF NOT EXISTS attachments (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    bug_id INTEGER NOT NULL,
                    filename TEXT NOT NULL,
                    content_type TEXT NOT NULL,
                    size_bytes INTEGER NOT NULL,
                    stored_name TEXT NOT NULL UNIQUE,
                    uploaded_by INTEGER NOT NULL,
                    uploaded_at TEXT NOT NULL,
                    FOREIGN KEY (bug_id) REFERENCES bugs(id) ON DELETE CASCADE,
                    FOREIGN KEY (uploaded_by) REFERENCES users(id)
                );

                CREATE INDEX IF NOT EXISTS idx_attachments_bug_id ON attachments(bug_id);
            ";
            migrationList.Add(version3);

            return migrationList;
        }
    }
}
