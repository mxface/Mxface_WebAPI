namespace MxfaceWebAPI.Models.Response
{
    public class FaceAnalyticsResponse : BiomatricBaseResponse
    {
        public List<Analytics> Faces { get; set; }
    }
}
