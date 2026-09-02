using MxfaceWebAPI.Models.Response;

namespace MxfaceWebAPI.Models
{
    public class FaceDetect
    {
        public float Quality { get; set; }
        public List<Points> Points { get; set; }
        public FaceRectangle FaceRectangle { get; set; }
        public string croppedFace { get; set; }
    }
}
