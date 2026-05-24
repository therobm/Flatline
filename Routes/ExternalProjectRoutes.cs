using Flatline.Http;
using Flatline.Models;

namespace Flatline.Routes
{
    public static class ExternalProjectRoutes
    {
        public static void HandleListExternalProjects(FlatlineHttpContext context)
        {
            User keyOwner = ApiKeyRoutes.GetUserFromApiKey(context);
            if (keyOwner == null)
            {
                HttpResponseWriter.WriteJson(context, 401, new { error = "Invalid or missing API key." });
                return;
            }
            ProjectRoutes.ListProjects(context);
        }
    }
}
