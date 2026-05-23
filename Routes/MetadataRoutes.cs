using System.Collections.Generic;
using Flatline.Http;
using Flatline.Models;

namespace Flatline.Routes
{
    public class BugMetadataResponse
    {
        public Dictionary<eBugStatus, string> Statuses;
        public Dictionary<eBugPriority, string> Priorities;
        public eBugStatus DefaultStatus;
        public eBugPriority DefaultPriority;
    }

    public static class MetadataRoutes
    {
        public static void HandleGetMetadata(FlatlineHttpContext context)
        {
            User currentUser = AuthRoutes.GetCurrentUser(context);
            if (currentUser == null)
            {
                HttpResponseWriter.WriteJson(context, 401, new { error = "Not authenticated." });
                return;
            }

            BugMetadataResponse response = new BugMetadataResponse();
            response.Statuses = EnumLabels.StatusLabels;
            response.Priorities = EnumLabels.PriorityLabels;
            response.DefaultStatus = eBugStatus.Open;
            response.DefaultPriority = eBugPriority.Normal;
            HttpResponseWriter.WriteJson(context, 200, response);
        }
    }
}
