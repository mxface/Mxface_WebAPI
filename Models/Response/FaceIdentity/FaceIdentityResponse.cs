namespace MxfaceWebAPI.Models.Response.FaceIdentity
{
    public class FaceIdentityResponse :BiomatricBaseResponse
    {
        public List<FaceIdentityInfo> FaceIdentities { get; set; }
        public long TotalFaceIdentities { get; set; }
    }
}
