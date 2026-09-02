using System;
using System.Collections.Generic;

namespace MxfaceWebAPI.Models.Response.FaceIdentity
{
    public class SearchFaceIdentityResponse : BiomatricBaseResponse
    {
        public List<LookupIdentities> SearchedIdentities { get; set; }
    }
    public class LookupIdentities : BiomatricBaseResponse
    {
        public List<IdentityConfidences> identityConfidences { get; set; }
    }
    public class IdentityConfidences
    {
        public Identity identity { get; set; }

        public short? matchResult { get; set; } = 0;
        public double? confidence { get; set; }
    }
    public class Identity
    {
        public int IdentityId { get; set; }
        public DateTimeOffset? CreatedDate { get; set; }
        public DateTimeOffset? UpdatedDate { get; set; }
        public List<int> GroupIds { get; set; }
        public string ExternalId { get; set; }
        public Dictionary<string, string> Metadata { get; set; }

    }
}
