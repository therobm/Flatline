using Microsoft.Data.Sqlite;

namespace Flatline.Database
{
    public static class SqliteConnectionFactory
    {
        private static string DatabaseFilePath = "flatline.db";

        public static void SetDatabaseFilePath(string filePath)
        {
            DatabaseFilePath = filePath;
        }

        public static string GetConnectionString()
        {
            SqliteConnectionStringBuilder builder = new SqliteConnectionStringBuilder();
            builder.DataSource = DatabaseFilePath;
            builder.ForeignKeys = true;
            return builder.ToString();
        }

        public static SqliteConnection OpenConnection()
        {
            SqliteConnection connection = new SqliteConnection(GetConnectionString());
            connection.Open();
            return connection;
        }
    }
}
