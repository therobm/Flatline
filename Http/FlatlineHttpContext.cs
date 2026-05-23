namespace Flatline.Http
{
    public class FlatlineHttpContext
    {
        public FlatlineHttpRequest Request = new FlatlineHttpRequest();
        public FlatlineHttpResponse Response = new FlatlineHttpResponse();
        public bool IsHttps;
    }
}
