namespace MxfaceWebAPI.Models.Request.FaceIdentity
{
    public class CreateFaceIdentityRequest :BiomatricBaseResponse
    {
        public List<int> GroupIds { get; set; }
        public string Encoded_Image { get; set; }
        public string externalId { get; set; }
        [Obsolete()]
        public int ConfidenceThreshold { get; set; }
        public int? QualityThreshold { get; set; } = null;
        public bool ForceAdd { get; set; } = false;

        /// <summary>
        /// Additional Metadata field <see href="https://github.com/mxface/mxface.api/issues/1"/>
        /// </summary>
        public Dictionary<string, string> AdditionalMetadata { get; set; } = [];
    }
}
