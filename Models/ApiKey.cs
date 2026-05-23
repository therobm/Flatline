namespace Flatline.Models
{
    public class ApiKey
    {
        public long Id;
        public long UserId;
        public string UserDisplayName = "";
        public string UserUsername = "";
        public string Name = "";
        public string KeyPrefix = "";
        public string CreatedAt = "";
        public string LastUsedAt = "";
    }
}
