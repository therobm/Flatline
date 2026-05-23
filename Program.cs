using System;
using System.IO;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using Microsoft.Data.Sqlite;
using Flatline.Database;
using Flatline.Http;
using Flatline.Logging;

namespace Flatline
{
    public class Program
    {
        private static ManualResetEvent ShutdownEvent = new ManualResetEvent(false);

        public static void Main(string[] args)
        {
            string configPath = Path.Combine(AppContext.BaseDirectory, "flatline.json");
            FlatlineConfig config = FlatlineConfig.LoadOrDefault(configPath);
            IPAddress bindAddress = config.ResolveBindAddress();

            Migrations.RunMigrations();
            SeedInitialAdminIfNeeded();

            X509Certificate2 serverCertificate = CertificateProvider.EnsureServerCertificate();

            FlatlineHttpServer server = new FlatlineHttpServer();
            server.Start(bindAddress, config.HttpPort, config.HttpsPort, serverCertificate);

            Console.CancelKeyPress += OnCancelKeyPress;
            ShutdownEvent.WaitOne();

            Log.Info("Stopping server.");
            server.StopAndWait();
            CertificateProvider.RemoveFromCurrentUserStore(serverCertificate);
            serverCertificate.Dispose();
            Log.Info("Server stopped.");
        }

        private static void OnCancelKeyPress(object sender, ConsoleCancelEventArgs eventArgs)
        {
            eventArgs.Cancel = true;
            ShutdownEvent.Set();
        }

        private static void SeedInitialAdminIfNeeded()
        {
            SqliteConnection connection = SqliteConnectionFactory.OpenConnection();
            try
            {
                SqliteCommand countCommand = connection.CreateCommand();
                countCommand.CommandText = "SELECT COUNT(*) FROM users;";
                object countResult = countCommand.ExecuteScalar();
                long userCount = 0;
                if (countResult != null)
                {
                    userCount = Convert.ToInt64(countResult);
                }
                if (userCount > 0)
                {
                    return;
                }

                string defaultUsername = "admin";
                string defaultPassword = "admin";
                string defaultDisplayName = "Administrator";
                string passwordHash = BCrypt.Net.BCrypt.HashPassword(defaultPassword);
                string nowIso = DateTime.UtcNow.ToString("o");

                SqliteCommand insertCommand = connection.CreateCommand();
                insertCommand.CommandText = "INSERT INTO users (username, password_hash, display_name, is_admin, created_at) "
                    + "VALUES ($username, $password_hash, $display_name, 1, $created_at);";
                insertCommand.Parameters.AddWithValue("$username", defaultUsername);
                insertCommand.Parameters.AddWithValue("$password_hash", passwordHash);
                insertCommand.Parameters.AddWithValue("$display_name", defaultDisplayName);
                insertCommand.Parameters.AddWithValue("$created_at", nowIso);
                insertCommand.ExecuteNonQuery();

                Log.Info("------------------------------------------------------------");
                Log.Info("Flatline: created initial admin user.");
                Log.Info("  Username: admin");
                Log.Info("  Password: admin");
                Log.Info("Change this password immediately after first login.");
                Log.Info("------------------------------------------------------------");
            }
            finally
            {
                connection.Close();
            }
        }
    }
}
