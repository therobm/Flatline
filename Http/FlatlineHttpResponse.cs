using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Flatline.Http
{
    public class FlatlineHttpResponse
    {
        public int StatusCode = 200;
        public string ContentType = "";
        public Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<string> SetCookieHeaders = new List<string>();
        public byte[] BodyBytes = new byte[0];

        public void SetCookie(string name, string value, DateTime expiresUtc, bool httpOnly, string path, bool secure)
        {
            StringBuilder cookieBuilder = new StringBuilder();
            cookieBuilder.Append(name);
            cookieBuilder.Append("=");
            cookieBuilder.Append(value);
            cookieBuilder.Append("; Path=");
            cookieBuilder.Append(path);
            cookieBuilder.Append("; Expires=");
            cookieBuilder.Append(expiresUtc.ToUniversalTime().ToString("R"));
            cookieBuilder.Append("; SameSite=Lax");
            if (httpOnly)
            {
                cookieBuilder.Append("; HttpOnly");
            }
            if (secure)
            {
                cookieBuilder.Append("; Secure");
            }
            SetCookieHeaders.Add(cookieBuilder.ToString());
        }

        public void WriteTo(Stream networkStream)
        {
            StringBuilder headersBuilder = new StringBuilder();
            headersBuilder.Append("HTTP/1.1 ");
            headersBuilder.Append(StatusCode);
            headersBuilder.Append(" ");
            headersBuilder.Append(GetStatusReason(StatusCode));
            headersBuilder.Append("\r\n");

            if (!string.IsNullOrEmpty(ContentType))
            {
                headersBuilder.Append("Content-Type: ");
                headersBuilder.Append(ContentType);
                headersBuilder.Append("\r\n");
            }
            headersBuilder.Append("Content-Length: ");
            headersBuilder.Append(BodyBytes.Length);
            headersBuilder.Append("\r\n");

            foreach (KeyValuePair<string, string> headerPair in Headers)
            {
                headersBuilder.Append(headerPair.Key);
                headersBuilder.Append(": ");
                headersBuilder.Append(headerPair.Value);
                headersBuilder.Append("\r\n");
            }

            int cookieCount = SetCookieHeaders.Count;
            for (int cookieIndex = 0; cookieIndex < cookieCount; cookieIndex++)
            {
                headersBuilder.Append("Set-Cookie: ");
                headersBuilder.Append(SetCookieHeaders[cookieIndex]);
                headersBuilder.Append("\r\n");
            }

            headersBuilder.Append("Connection: close\r\n");
            headersBuilder.Append("\r\n");

            byte[] headerBytes = Encoding.ASCII.GetBytes(headersBuilder.ToString());
            networkStream.Write(headerBytes, 0, headerBytes.Length);
            if (BodyBytes.Length > 0)
            {
                networkStream.Write(BodyBytes, 0, BodyBytes.Length);
            }
            networkStream.Flush();
        }

        private static string GetStatusReason(int statusCode)
        {
            if (statusCode == 200) { return "OK"; }
            if (statusCode == 201) { return "Created"; }
            if (statusCode == 204) { return "No Content"; }
            if (statusCode == 301) { return "Moved Permanently"; }
            if (statusCode == 302) { return "Found"; }
            if (statusCode == 304) { return "Not Modified"; }
            if (statusCode == 400) { return "Bad Request"; }
            if (statusCode == 401) { return "Unauthorized"; }
            if (statusCode == 403) { return "Forbidden"; }
            if (statusCode == 404) { return "Not Found"; }
            if (statusCode == 405) { return "Method Not Allowed"; }
            if (statusCode == 409) { return "Conflict"; }
            if (statusCode == 500) { return "Internal Server Error"; }
            return "Unknown";
        }
    }
}
