using System;

namespace Flatline.Http
{
    /* Thrown when an incoming request is malformed at the protocol or
     * body-parsing layer (bad framing, unparseable JSON, and the like).
     * The connection dispatcher catches this and returns a 400 to the
     * client instead of treating it as a 500 server fault. */
    public class BadRequestException : Exception
    {
        public BadRequestException(string message)
            : base(message)
        {
        }
    }
}
