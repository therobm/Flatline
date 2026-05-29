using System;
using System.IO;
using System.Text;

namespace Flatline.Http
{
    public static class Http11Parser
    {
        public const int MaxLineLength = 8192;
        private const int MaxHeaderCount = 100;
        private const int MaxBodyLength = 16 * 1024 * 1024;

        public static FlatlineHttpRequest ReadRequest(Stream networkStream, byte[] lineBuffer)
        {
            string requestLine = ReadLine(networkStream, lineBuffer);
            if (requestLine == null)
            {
                return null;
            }

            FlatlineHttpRequest request = new FlatlineHttpRequest();
            ParseRequestLine(requestLine, request);

            for (int headerIndex = 0; headerIndex < MaxHeaderCount; headerIndex++)
            {
                string headerLine = ReadLine(networkStream, lineBuffer);
                if (headerLine == null)
                {
                    return null;
                }
                if (headerLine.Length == 0)
                {
                    break;
                }
                ParseHeaderLine(headerLine, request);
            }

            ParseCookieHeader(request);

            /* Reject Transfer-Encoding outright. We do not decode chunked
             * bodies, and silently treating a chunked request as bodyless
             * would leave the chunk data unread in the keep-alive stream,
             * where it gets parsed as the next request (connection desync /
             * request smuggling). */
            string transferEncodingValue;
            if (request.Headers.TryGetValue("Transfer-Encoding", out transferEncodingValue))
            {
                throw new BadRequestException("Transfer-Encoding is not supported.");
            }

            int contentLength = 0;
            string contentLengthValue;
            if (request.Headers.TryGetValue("Content-Length", out contentLengthValue))
            {
                /* A present-but-unparseable, negative, or int-overflowing
                 * Content-Length must be rejected, not defaulted to 0. If we
                 * defaulted to 0 we would leave the declared body unread in
                 * the stream and reinterpret it as the next request. */
                if (!int.TryParse(contentLengthValue, out contentLength) || contentLength < 0)
                {
                    throw new BadRequestException("Invalid Content-Length header.");
                }
            }
            if (contentLength > MaxBodyLength)
            {
                throw new InvalidOperationException("Request body exceeds maximum size.");
            }
            if (contentLength > 0)
            {
                request.BodyBytes = ReadExact(networkStream, contentLength);
            }

            return request;
        }

        private static void ParseRequestLine(string requestLine, FlatlineHttpRequest request)
        {
            int firstSpace = requestLine.IndexOf(' ');
            if (firstSpace < 0)
            {
                throw new InvalidOperationException("Malformed request line.");
            }
            int secondSpace = requestLine.IndexOf(' ', firstSpace + 1);
            if (secondSpace < 0)
            {
                throw new InvalidOperationException("Malformed request line.");
            }
            request.Method = requestLine.Substring(0, firstSpace);
            string target = requestLine.Substring(firstSpace + 1, secondSpace - firstSpace - 1);
            int queryStart = target.IndexOf('?');
            if (queryStart < 0)
            {
                request.Path = Uri.UnescapeDataString(target);
            }
            else
            {
                request.Path = Uri.UnescapeDataString(target.Substring(0, queryStart));
                string queryPart = target.Substring(queryStart + 1);
                ParseQueryString(queryPart, request);
            }
        }

        private static void ParseQueryString(string queryPart, FlatlineHttpRequest request)
        {
            string[] pairs = queryPart.Split('&');
            int pairCount = pairs.Length;
            for (int pairIndex = 0; pairIndex < pairCount; pairIndex++)
            {
                string pair = pairs[pairIndex];
                if (pair.Length == 0)
                {
                    continue;
                }
                int equalsIndex = pair.IndexOf('=');
                string name;
                string value;
                if (equalsIndex < 0)
                {
                    name = Uri.UnescapeDataString(pair);
                    value = "";
                }
                else
                {
                    name = Uri.UnescapeDataString(pair.Substring(0, equalsIndex));
                    value = Uri.UnescapeDataString(pair.Substring(equalsIndex + 1).Replace('+', ' '));
                }
                request.QueryString[name] = value;
            }
        }

        private static void ParseHeaderLine(string headerLine, FlatlineHttpRequest request)
        {
            int colonIndex = headerLine.IndexOf(':');
            if (colonIndex < 0)
            {
                return;
            }
            string name = headerLine.Substring(0, colonIndex).Trim();
            string value = headerLine.Substring(colonIndex + 1).Trim();
            string existingValue;
            if (request.Headers.TryGetValue(name, out existingValue))
            {
                /* Two Content-Length headers with different values is a
                 * classic request-smuggling primitive (front-end and
                 * back-end disagree on body length). Reject rather than
                 * letting the last value silently win. */
                if (string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase) && existingValue != value)
                {
                    throw new BadRequestException("Conflicting Content-Length headers.");
                }
            }
            request.Headers[name] = value;
        }

        private static void ParseCookieHeader(FlatlineHttpRequest request)
        {
            string cookieHeader;
            if (!request.Headers.TryGetValue("Cookie", out cookieHeader))
            {
                return;
            }
            string[] pairs = cookieHeader.Split(';');
            int pairCount = pairs.Length;
            for (int pairIndex = 0; pairIndex < pairCount; pairIndex++)
            {
                string pair = pairs[pairIndex].Trim();
                if (pair.Length == 0)
                {
                    continue;
                }
                int equalsIndex = pair.IndexOf('=');
                if (equalsIndex < 0)
                {
                    continue;
                }
                string name = pair.Substring(0, equalsIndex).Trim();
                string value = pair.Substring(equalsIndex + 1).Trim();
                request.Cookies[name] = value;
            }
        }

        private static string ReadLine(Stream networkStream, byte[] lineBuffer)
        {
            /* Writes into the caller's reusable byte[] (allocated once per
             * connection in FlatlineHttpServer.HandleConnection) so each header
             * line stops minting a fresh MemoryStream + ToArray() copy.
             * Decoded as Latin1 per RFC 7230 (header field values are legacy
             * ISO-8859-1; Latin1 is the only single-byte encoding that
             * round-trips every byte without loss). */
            int bufferLength = lineBuffer.Length;
            int writeIndex = 0;
            int previousByte = -1;
            for (;;)
            {
                int currentByte = networkStream.ReadByte();
                if (currentByte == -1)
                {
                    if (writeIndex == 0)
                    {
                        return null;
                    }
                    return Encoding.Latin1.GetString(lineBuffer, 0, writeIndex);
                }
                if (previousByte == '\r' && currentByte == '\n')
                {
                    int length = writeIndex - 1;
                    if (length < 0)
                    {
                        length = 0;
                    }
                    return Encoding.Latin1.GetString(lineBuffer, 0, length);
                }
                if (writeIndex >= bufferLength)
                {
                    throw new InvalidOperationException("Header line exceeds maximum length.");
                }
                lineBuffer[writeIndex] = (byte)currentByte;
                writeIndex++;
                previousByte = currentByte;
            }
        }

        private static byte[] ReadExact(Stream networkStream, int byteCount)
        {
            byte[] buffer = new byte[byteCount];
            int totalRead = 0;
            for (; totalRead < byteCount; )
            {
                int bytesRead = networkStream.Read(buffer, totalRead, byteCount - totalRead);
                if (bytesRead == 0)
                {
                    throw new InvalidOperationException("Unexpected end of stream while reading body.");
                }
                totalRead += bytesRead;
            }
            return buffer;
        }
    }
}
