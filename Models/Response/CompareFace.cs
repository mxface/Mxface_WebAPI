namespace MxfaceWebAPI.Models.Response
{
    public class CompareFace
    {
        public short? matchResult { get; set; } = null;
        public float? confidence { get; set; }
        public Imageface image1_face { get; set; }
        public Imageface image2_face { get; set; }
    }
}
