using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace Flatline.Http
{
    public class StaticResource
    {
        public byte[] Bytes = new byte[0];
        public string ETag = "";
        public string ContentType = "";
    }

    public static class StaticFileServer
    {
        private const string EmbeddedPrefix = "wwwroot/";

        private static Dictionary<string, StaticResource> s_ResourceCache = LoadResources();

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

            StaticResource resource;
            if (!s_ResourceCache.TryGetValue(relativePath, out resource))
            {
                HttpResponseWriter.WriteEmpty(context, 404);
                return;
            }

            string clientETag;
            if (context.Request.Headers.TryGetValue("If-None-Match", out clientETag))
            {
                if (clientETag == resource.ETag)
                {
                    /* 304 Not Modified: same ETag headers as the 200 path so the
                     * client keeps caching, but no body and no Content-Type. */
                    context.Response.StatusCode = 304;
                    context.Response.BodyBytes = new byte[0];
                    context.Response.Headers["ETag"] = resource.ETag;
                    context.Response.Headers["Cache-Control"] = "no-cache";
                    return;
                }
            }

            context.Response.Headers["ETag"] = resource.ETag;
            context.Response.Headers["Cache-Control"] = "no-cache";
            HttpResponseWriter.WriteBytes(context, 200, resource.ContentType, resource.Bytes);
        }

        private static Dictionary<string, StaticResource> LoadResources()
        {
            Dictionary<string, StaticResource> resourceMap = new Dictionary<string, StaticResource>();
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
                    byte[] bytes = memoryStream.ToArray();

                    StaticResource resource = new StaticResource();
                    resource.Bytes = bytes;
                    resource.ETag = ComputeETag(bytes);
                    resource.ContentType = GetContentType(relativePath);
                    resourceMap[relativePath] = resource;
                }
                finally
                {
                    resourceStream.Dispose();
                }
            }
            return resourceMap;
        }

        private static string ComputeETag(byte[] bytes)
        {
            /* SHA-256 of the bytes truncated to 16 hex chars. With wwwroot baked
             * into the exe the ETag is stable for the lifetime of the process
             * and changes whenever a new build replaces the resource. */
            byte[] hash = SHA256.HashData(bytes);
            char[] hex = new char[16];
            for (int hashIndex = 0; hashIndex < 8; hashIndex++)
            {
                byte hashByte = hash[hashIndex];
                hex[hashIndex * 2] = HexDigit(hashByte >> 4);
                hex[hashIndex * 2 + 1] = HexDigit(hashByte & 0xF);
            }
            return "\"" + new string(hex) + "\"";
        }

        private static char HexDigit(int value)
        {
            if (value < 10)
            {
                return (char)('0' + value);
            }
            return (char)('a' + value - 10);
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
