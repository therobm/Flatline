namespace Flatline.Models
{
    public class Attachment
    {
        public long Id;
        public long BugId;
        public string Filename = "";
        public string ContentType = "";
        public long SizeBytes;
        public long UploadedBy;
        public string UploadedByUsername = "";
        public string UploadedByDisplayName = "";
        public string UploadedAt = "";
    }
}
