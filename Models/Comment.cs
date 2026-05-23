namespace Flatline.Models
{
    public class Comment
    {
        public long Id;
        public long BugId;
        public long UserId;
        public string Username = "";
        public string DisplayName = "";
        public string Text = "";
        public string CreatedAt = "";
    }
}
