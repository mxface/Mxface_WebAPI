namespace MxfaceWebAPI.Models.Response
{
    public class Imageface
    {
        public FaceRectangle faceRectangle { get; set; }
        public List<Points> Points { get; set; }
        public float quality { get; set; }
        public string croppedFace { get; set; }
    }
}
