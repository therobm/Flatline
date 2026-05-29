namespace Flatline.Models
{
    public class Bug
    {
        public long Id;
        public long ProjectId;
        public string ProjectName = "";
        public string ProjectPrefix = "";
        public long ProjectBugNumber;
        public string Title = "";
        public string Description = "";
        public eBugStatus Status;
        public eBugPriority Priority;
        public long CreatedBy;
        public string CreatedByUsername = "";
        public string CreatedByDisplayName = "";
        public long AssignedTo;
        public string AssignedToUsername = "";
        public string AssignedToDisplayName = "";
        public long FoundInVersionId;
        public string FoundInVersionName = "";
        public long FixedInVersionId;
        public string FixedInVersionName = "";
        public string CreatedAt = "";
        public string UpdatedAt = "";
    }
}
