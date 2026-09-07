namespace MxfaceWebAPI.Models.Response
{
    public class FaceLandmarkResponse:BiomatricBaseResponse
    {
        public List<Landmarks> Faces
        {
            get; set;
        }
    }
}
