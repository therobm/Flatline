using System;
using System.IO;
using System.Security.Cryptography;

namespace Flatline.Http
{
    /* Manages the on-disk attachments directory. Bytes live under
     * <BaseDir>/attachments/<bug_id>/<stored_name>. <stored_name> is a
     * server-generated random token; the original filename is preserved
     * in the database and used only for Content-Disposition on download.
     * This keeps arbitrary user-supplied filenames off the filesystem
     * entirely and removes any need to sanitize them. */
    public static class AttachmentStorage
    {
        private static string s_RootDirectory = "";

        public static void Initialize(string rootDirectory)
        {
            s_RootDirectory = rootDirectory;
            Directory.CreateDirectory(s_RootDirectory);
        }

        public static string GenerateStoredName()
        {
            byte[] randomBytes = new byte[16];
            RandomNumberGenerator.Fill(randomBytes);
            char[] hex = new char[32];
            for (int byteIndex = 0; byteIndex < 16; byteIndex++)
            {
                byte b = randomBytes[byteIndex];
                hex[byteIndex * 2] = HexDigit(b >> 4);
                hex[byteIndex * 2 + 1] = HexDigit(b & 0xF);
            }
            return new string(hex);
        }

        public static string GetFilePath(long bugId, string storedName)
        {
            string bugDirectory = Path.Combine(s_RootDirectory, bugId.ToString());
            return Path.Combine(bugDirectory, storedName);
        }

        public static void WriteFile(long bugId, string storedName, byte[] bytes)
        {
            string bugDirectory = Path.Combine(s_RootDirectory, bugId.ToString());
            Directory.CreateDirectory(bugDirectory);
            string filePath = Path.Combine(bugDirectory, storedName);
            File.WriteAllBytes(filePath, bytes);
        }

        public static void DeleteFile(long bugId, string storedName)
        {
            string filePath = GetFilePath(bugId, storedName);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        private static char HexDigit(int value)
        {
            if (value < 10)
            {
                return (char)('0' + value);
            }
            return (char)('a' + value - 10);
        }
    }
}
