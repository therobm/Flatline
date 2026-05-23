using System.Collections.Generic;

namespace Flatline.Models
{
    public static class EnumLabels
    {
        public static Dictionary<eBugStatus, string> StatusLabels;
        public static Dictionary<eBugPriority, string> PriorityLabels;

        static EnumLabels()
        {
            StatusLabels = new Dictionary<eBugStatus, string>();
            StatusLabels[eBugStatus.Open] = "Open";
            StatusLabels[eBugStatus.InProgress] = "In progress";
            StatusLabels[eBugStatus.Resolved] = "Resolved";
            StatusLabels[eBugStatus.Closed] = "Closed";
            StatusLabels[eBugStatus.WontFix] = "Won't fix";

            PriorityLabels = new Dictionary<eBugPriority, string>();
            PriorityLabels[eBugPriority.Critical] = "Critical";
            PriorityLabels[eBugPriority.High] = "High";
            PriorityLabels[eBugPriority.Normal] = "Normal";
            PriorityLabels[eBugPriority.Low] = "Low";
        }
    }
}
