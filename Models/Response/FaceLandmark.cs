namespace MxfaceWebAPI.Models.Response
{
    public class FaceLandmark
    {
        public PointLocation? MouthCenter { get; set; }
        public PointLocation? Nose { get; set; }
        public PointLocation? eye_left { get; set; }
        public PointLocation? eye_right { get; set; }

        public List<PointLocation> FeaturePoints { get; set; } = new List<PointLocation>();
    }
}
