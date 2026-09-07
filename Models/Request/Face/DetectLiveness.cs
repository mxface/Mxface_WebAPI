namespace MxfaceWebAPI.Models.Request.Face
{
    public class DetectLiveness : CommonProperty
    {
        public string encoded_image { get; set; }
    }
}
