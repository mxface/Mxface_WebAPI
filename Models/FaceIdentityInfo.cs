using System;
using System.Collections.Generic;
namespace MxfaceWebAPI.Models
{
    public class FaceIdentityInfoNew:BiomatricBaseResponse
    {
        public int? FaceIdentityId { get; set; }
        public List<FaceInfonew> Faces { get; set; }
        public IEnumerable<Models.Response.Group.GroupResponse> Groups { get; set; }
        // public string encoded_image { get; set; }
        public DateTimeOffset? CreatedDate { get; set; }
        public string externalId { get; set; }
        public DateTimeOffset? UpdatedDate { get; set; }

        /// <summary>
        /// Additional Metadata field <see href="https://github.com/mxface/mxface.api/issues/1"/>
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; } = [];
    }
}
