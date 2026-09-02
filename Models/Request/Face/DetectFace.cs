namespace MxfaceWebAPI.Models.Request.Face
{
    public class DetectFace : CommonProperty
    {
        public string encoded_image { get; set; }
        public bool attributes { get; set; }
        public bool GetCroppedFace { get; set; }
        public string Algorithm { get; set; }
    }
    public class PassiveLivenessFaces
    {
        public string encoded_image1 { get; set; }
        public string encoded_image2 { get; set; }
        public string encoded_image3 { get; set; }
        public string encoded_image4 { get; set; }
        public bool attributes { get; set; }
    }
}
