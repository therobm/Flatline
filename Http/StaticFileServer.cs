using System.IO;

namespace Flatline.Http
{
    public static class StaticFileServer
    {
        private const string WwwRoot = "wwwroot";

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

            string fullPath = Path.Combine(WwwRoot, relativePath);
            if (!File.Exists(fullPath))
            {
                HttpResponseWriter.WriteEmpty(context, 404);
                return;
            }

            byte[] bytes = File.ReadAllBytes(fullPath);
            string contentType = GetContentType(relativePath);
            HttpResponseWriter.WriteBytes(context, 200, contentType, bytes);
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
