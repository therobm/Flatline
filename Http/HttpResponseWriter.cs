using System.Text;
using System.Text.Json;

namespace Flatline.Http
{
    public static class HttpResponseWriter
    {
        public static void WriteJson(FlatlineHttpContext context, int statusCode, object body)
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json; charset=utf-8";
            string json = JsonSerializer.Serialize(body, body.GetType(), JsonOptions.Default);
            context.Response.BodyBytes = Encoding.UTF8.GetBytes(json);
        }

        public static void WriteEmpty(FlatlineHttpContext context, int statusCode)
        {
            context.Response.StatusCode = statusCode;
            context.Response.BodyBytes = new byte[0];
        }

        public static void WriteBytes(FlatlineHttpContext context, int statusCode, string contentType, byte[] bytes)
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = contentType;
            context.Response.BodyBytes = bytes;
        }
    }
}
