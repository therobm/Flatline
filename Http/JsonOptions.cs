using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flatline.Http
{
    public static class JsonOptions
    {
        public static JsonSerializerOptions Default;

        static JsonOptions()
        {
            Default = new JsonSerializerOptions();
            Default.IncludeFields = true;
            Default.PropertyNamingPolicy = null;
            Default.PropertyNameCaseInsensitive = false;
            JsonStringEnumConverter enumConverter = new JsonStringEnumConverter(null, false);
            Default.Converters.Add(enumConverter);
        }
    }
}
