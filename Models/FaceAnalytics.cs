using MxfaceWebAPI.Models.Response;

namespace MxfaceWebAPI.Models
{
    public class FaceAnalytics
    {
        public FaceRectangle FaceRectangle { get; set; }
        public FaceLandmark FaceLandmark { get; set; }
        public FaceAnalytics FaceAttribute { get; set; }

        public Blur Blur { get; set; }
        public Exposure Exposure { get; set; }
        public Noise Noise { get; set; }
    }
}
