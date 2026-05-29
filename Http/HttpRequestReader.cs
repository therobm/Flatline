using System;
using System.Text;
using System.Text.Json;

namespace Flatline.Http
{
    public static class HttpRequestReader
    {
        public static string ReadBodyAsString(FlatlineHttpContext context)
        {
            if (context.Request.BodyBytes == null || context.Request.BodyBytes.Length == 0)
            {
                return "";
            }
            return Encoding.UTF8.GetString(context.Request.BodyBytes);
        }

        public static T ReadBodyAsJson<T>(FlatlineHttpContext context)
        {
            string body = ReadBodyAsString(context);
            if (string.IsNullOrEmpty(body))
            {
                return default(T);
            }
            /* A malformed body is a client error: surface it as a 400 rather
             * than letting the JsonException bubble up as an unhandled 500
             * (which also logs a full stack trace for a routine bad request). */
            try
            {
                return JsonSerializer.Deserialize<T>(body, JsonOptions.Default);
            }
            catch (JsonException)
            {
                throw new BadRequestException("Invalid JSON body.");
            }
        }

        public static string GetCookieValue(FlatlineHttpContext context, string name)
        {
            string value;
            if (context.Request.Cookies.TryGetValue(name, out value))
            {
                return value;
            }
            return "";
        }

        public static string GetQueryValue(FlatlineHttpContext context, string name)
        {
            string value;
            if (context.Request.QueryString.TryGetValue(name, out value))
            {
                return value;
            }
            return "";
        }

        public static string GetHeaderValue(FlatlineHttpContext context, string name)
        {
            string value;
            if (context.Request.Headers.TryGetValue(name, out value))
            {
                return value;
            }
            return "";
        }

        public static void SetCookie(FlatlineHttpContext context, string name, string value, DateTime expiresUtc, bool httpOnly, string path)
        {
            context.Response.SetCookie(name, value, expiresUtc, httpOnly, path, context.IsHttps);
        }

        public static void DeleteCookie(FlatlineHttpContext context, string name, string path)
        {
            context.Response.SetCookie(name, "", DateTime.UtcNow.AddDays(-1), true, path, context.IsHttps);
        }
    }
}
