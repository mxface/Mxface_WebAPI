namespace MxfaceWebAPI.Models.Response.FaceIdentity
{
    public class GetFaceResponse : BiomatricBaseResponse
    {
        public List<FaceInfo> Faces { get; set; }
    }
}
