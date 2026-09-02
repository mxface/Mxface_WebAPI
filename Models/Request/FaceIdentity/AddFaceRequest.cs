namespace MxfaceWebAPI.Models.Request.FaceIdentity
{
    public class AddFaceRequest
    {
        public string Encoded_Image { get; set; }
        [Obsolete]
        public int ConfidenceThreshold { get; set; }
        public int? QualityThreshold { get; set; } = null;
        //public bool StoreFace { get; set; }
    }
}
