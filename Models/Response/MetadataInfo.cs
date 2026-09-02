namespace MxfaceWebAPI.Models.Response
{
    public class MetadataInfo : BiomatricBaseResponse
    {
        /// <summary>
        /// Metadata object
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; }

        /// <summary>
        /// Face identity Id
        /// </summary>
        public int FaceIdentityId { get; set; }
    }
}
