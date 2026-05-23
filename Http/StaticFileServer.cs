using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Flatline.Http
{
    public static class StaticFileServer
    {
        private const string EmbeddedPrefix = "wwwroot/";

        private static Dictionary<string, byte[]> s_ResourceCache = LoadResources();

        public static void Serve(FlatlineHttpContext context, string requestPath)
        {
            string relativePath = requestPath.TrimStart('/');
            if (string.IsNullOrEmpty(relativePath))
            {
                relativePath = "index.html";
            }

            if (relativePath.Contains(".."))
            {
                HttpResponseWriter.WriteEmpty(context, 400);
                return;
            }

            byte[] bytes;
            if (!s_ResourceCache.TryGetValue(relativePath, out bytes))
            {
                HttpResponseWriter.WriteEmpty(context, 404);
                return;
            }

            string contentType = GetContentType(relativePath);
            HttpResponseWriter.WriteBytes(context, 200, contentType, bytes);
        }

        private static Dictionary<string, byte[]> LoadResources()
        {
            Dictionary<string, byte[]> resourceMap = new Dictionary<string, byte[]>();
            Assembly assembly = typeof(StaticFileServer).Assembly;
            string[] resourceNames = assembly.GetManifestResourceNames();
            int resourceCount = resourceNames.Length;
            for (int resourceIndex = 0; resourceIndex < resourceCount; resourceIndex++)
            {
                string resourceName = resourceNames[resourceIndex];
                if (!resourceName.StartsWith(EmbeddedPrefix))
                {
                    continue;
                }
                string relativePath = resourceName.Substring(EmbeddedPrefix.Length);
                Stream resourceStream = assembly.GetManifestResourceStream(resourceName);
                if (resourceStream == null)
                {
                    continue;
                }
                try
                {
                    MemoryStream memoryStream = new MemoryStream();
                    resourceStream.CopyTo(memoryStream);
                    resourceMap[relativePath] = memoryStream.ToArray();
                }
                finally
                {
                    resourceStream.Dispose();
                }
            }
            return resourceMap;
        }

        private static string GetContentType(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".html")
            {
                return "text/html; charset=utf-8";
            }
            if (extension == ".css")
            {
                return "text/css; charset=utf-8";
            }
            if (extension == ".js")
            {
                return "application/javascript; charset=utf-8";
            }
            if (extension == ".json")
            {
                return "application/json; charset=utf-8";
            }
            if (extension == ".png")
            {
                return "image/png";
            }
            if (extension == ".jpg" || extension == ".jpeg")
            {
                return "image/jpeg";
            }
            if (extension == ".svg")
            {
                return "image/svg+xml";
            }
            if (extension == ".ico")
            {
                return "image/x-icon";
            }
            return "application/octet-stream";
        }
    }
}
