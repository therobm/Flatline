using System;
using System.Collections.Generic;
using System.Text;

namespace Flatline.Http
{
    public class MultipartFilePart
    {
        public string FieldName = "";
        public string Filename = "";
        public string ContentType = "";
        public byte[] Bytes = new byte[0];
    }

    /* Minimal multipart/form-data parser. Only extracts file parts (parts
     * that have a filename attribute on Content-Disposition); plain text
     * fields are ignored, which is all the upload endpoint needs. Operates
     * on the raw request body bytes already buffered by Http11Parser. */
    public static class MultipartParser
    {
        public static bool TryGetBoundary(string contentTypeHeader, out string boundary)
        {
            boundary = "";
            if (string.IsNullOrEmpty(contentTypeHeader))
            {
                return false;
            }
            string lower = contentTypeHeader.ToLowerInvariant();
            if (!lower.StartsWith("multipart/form-data"))
            {
                return false;
            }
            const string marker = "boundary=";
            int markerIndex = lower.IndexOf(marker);
            if (markerIndex < 0)
            {
                return false;
            }
            string rawBoundary = contentTypeHeader.Substring(markerIndex + marker.Length);
            int semicolon = rawBoundary.IndexOf(';');
            if (semicolon >= 0)
            {
                rawBoundary = rawBoundary.Substring(0, semicolon);
            }
            rawBoundary = rawBoundary.Trim();
            if (rawBoundary.Length >= 2 && rawBoundary[0] == '"' && rawBoundary[rawBoundary.Length - 1] == '"')
            {
                rawBoundary = rawBoundary.Substring(1, rawBoundary.Length - 2);
            }
            if (rawBoundary.Length == 0)
            {
                return false;
            }
            boundary = rawBoundary;
            return true;
        }

        public static List<MultipartFilePart> ParseFileParts(byte[] body, string boundary)
        {
            List<MultipartFilePart> fileParts = new List<MultipartFilePart>();
            byte[] delimiter = Encoding.ASCII.GetBytes("--" + boundary);

            int cursor = 0;
            int bodyLength = body.Length;
            const int maxParts = 64;

            for (int partIndex = 0; partIndex < maxParts; partIndex++)
            {
                int delimiterStart = IndexOf(body, delimiter, cursor);
                if (delimiterStart < 0)
                {
                    break;
                }
                cursor = delimiterStart + delimiter.Length;
                /* Closing delimiter is "--BOUNDARY--". */
                if (cursor + 2 <= bodyLength && body[cursor] == (byte)'-' && body[cursor + 1] == (byte)'-')
                {
                    break;
                }
                /* Skip CRLF after the delimiter. */
                if (cursor + 2 <= bodyLength && body[cursor] == (byte)'\r' && body[cursor + 1] == (byte)'\n')
                {
                    cursor += 2;
                }
                else
                {
                    /* Malformed: bail. */
                    break;
                }

                /* Headers run until a blank line (CRLFCRLF). */
                byte[] headerTerminator = new byte[] { (byte)'\r', (byte)'\n', (byte)'\r', (byte)'\n' };
                int headersEnd = IndexOf(body, headerTerminator, cursor);
                if (headersEnd < 0)
                {
                    break;
                }
                string headerBlock = Encoding.ASCII.GetString(body, cursor, headersEnd - cursor);
                cursor = headersEnd + headerTerminator.Length;

                /* Find the next boundary to know where this part's body ends.
                 * The bytes immediately before it are the body, minus the
                 * trailing CRLF that separates the body from the delimiter. */
                int nextDelimiter = IndexOf(body, delimiter, cursor);
                if (nextDelimiter < 0)
                {
                    break;
                }
                int bodyEnd = nextDelimiter;
                /* Strip the CRLF that separates the body from the delimiter,
                 * but only when those two bytes lie within this part's body
                 * (bodyEnd - 2 >= cursor). Without that bound, a part whose
                 * delimiter immediately follows the CRLFCRLF header
                 * terminator would strip the terminator's own CRLF and drive
                 * partBodyLength negative, throwing on new byte[negative]. */
                if (bodyEnd - 2 >= cursor && body[bodyEnd - 2] == (byte)'\r' && body[bodyEnd - 1] == (byte)'\n')
                {
                    bodyEnd -= 2;
                }
                int partBodyLength = bodyEnd - cursor;
                if (partBodyLength < 0)
                {
                    partBodyLength = 0;
                }

                MultipartFilePart filePart = new MultipartFilePart();
                if (TryParseFileHeaders(headerBlock, filePart))
                {
                    byte[] partBytes = new byte[partBodyLength];
                    Array.Copy(body, cursor, partBytes, 0, partBodyLength);
                    filePart.Bytes = partBytes;
                    fileParts.Add(filePart);
                }
                cursor = nextDelimiter;
            }

            return fileParts;
        }

        private static bool TryParseFileHeaders(string headerBlock, MultipartFilePart filePart)
        {
            string[] headerLines = headerBlock.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            bool sawDisposition = false;
            int lineCount = headerLines.Length;
            for (int lineIndex = 0; lineIndex < lineCount; lineIndex++)
            {
                string headerLine = headerLines[lineIndex];
                int colonIndex = headerLine.IndexOf(':');
                if (colonIndex <= 0)
                {
                    continue;
                }
                string headerName = headerLine.Substring(0, colonIndex).Trim().ToLowerInvariant();
                string headerValue = headerLine.Substring(colonIndex + 1).Trim();
                if (headerName == "content-disposition")
                {
                    sawDisposition = true;
                    filePart.FieldName = ExtractParameter(headerValue, "name");
                    filePart.Filename = ExtractParameter(headerValue, "filename");
                }
                else if (headerName == "content-type")
                {
                    filePart.ContentType = headerValue;
                }
            }
            /* Only keep parts that actually have a filename — everything
             * else is a plain form field, which this parser ignores. */
            if (!sawDisposition || string.IsNullOrEmpty(filePart.Filename))
            {
                return false;
            }
            if (string.IsNullOrEmpty(filePart.ContentType))
            {
                filePart.ContentType = "application/octet-stream";
            }
            return true;
        }

        private static string ExtractParameter(string headerValue, string parameterName)
        {
            string marker = parameterName + "=";
            int markerIndex = headerValue.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                return "";
            }
            int valueStart = markerIndex + marker.Length;
            if (valueStart >= headerValue.Length)
            {
                return "";
            }
            string rawValue;
            if (headerValue[valueStart] == '"')
            {
                int endQuote = headerValue.IndexOf('"', valueStart + 1);
                if (endQuote < 0)
                {
                    return "";
                }
                rawValue = headerValue.Substring(valueStart + 1, endQuote - valueStart - 1);
            }
            else
            {
                int endIndex = headerValue.IndexOf(';', valueStart);
                if (endIndex < 0)
                {
                    endIndex = headerValue.Length;
                }
                rawValue = headerValue.Substring(valueStart, endIndex - valueStart).Trim();
            }
            return rawValue;
        }

        private static int IndexOf(byte[] haystack, byte[] needle, int start)
        {
            int haystackLength = haystack.Length;
            int needleLength = needle.Length;
            int limit = haystackLength - needleLength;
            for (int outerIndex = start; outerIndex <= limit; outerIndex++)
            {
                bool match = true;
                for (int innerIndex = 0; innerIndex < needleLength; innerIndex++)
                {
                    if (haystack[outerIndex + innerIndex] != needle[innerIndex])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    return outerIndex;
                }
            }
            return -1;
        }
    }
}
