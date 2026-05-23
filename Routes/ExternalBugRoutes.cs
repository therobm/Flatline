using Flatline.Http;
using Flatline.Models;

namespace Flatline.Routes
{
    public static class ExternalBugRoutes
    {
        public static void HandleCreateExternalBug(FlatlineHttpContext context)
        {
            User keyOwner = ApiKeyRoutes.GetUserFromApiKey(context);
            if (keyOwner == null)
            {
                HttpResponseWriter.WriteJson(context, 401, new { error = "Invalid or missing API key." });
                return;
            }

            BugCreateRequest createRequest = HttpRequestReader.ReadBodyAsJson<BugCreateRequest>(context);
            if (createRequest == null)
            {
                HttpResponseWriter.WriteJson(context, 400, new { error = "Body is required." });
                return;
            }

            BugRoutes.CreateBugForUser(context, keyOwner, createRequest);
        }
    }
}
