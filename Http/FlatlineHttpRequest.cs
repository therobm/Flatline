using System;
using System.Collections.Generic;

namespace Flatline.Http
{
    public class FlatlineHttpRequest
    {
        public string Method = "";
        public string Path = "";
        public Dictionary<string, string> QueryString = new Dictionary<string, string>(StringComparer.Ordinal);
        public Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Cookies = new Dictionary<string, string>(StringComparer.Ordinal);
        public byte[] BodyBytes = new byte[0];
    }
}
