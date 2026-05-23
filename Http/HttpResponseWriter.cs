using System.Text.Json;

namespace Flatline.Http
{
    public static class HttpResponseWriter
    {
        public static void WriteJson(FlatlineHttpContext context, int statusCode, object body)
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json; charset=utf-8";
            /* SerializeToUtf8Bytes writes JSON straight into a UTF-8 byte[] with
             * a single allocation. The previous Serialize -> string -> GetBytes
             * path produced two large allocations (string + byte[]) on every
             * JSON response and a UTF-16 -> UTF-8 transcode step. */
            context.Response.BodyBytes = JsonSerializer.SerializeToUtf8Bytes(body, body.GetType(), JsonOptions.Default);
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
