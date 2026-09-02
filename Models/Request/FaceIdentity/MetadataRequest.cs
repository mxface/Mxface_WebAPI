using System.Collections.Generic;

namespace MxfaceWebAPI.Models.Request.FaceIdentity
{
    public class MetadataRequest
    {
        public Dictionary<string, string> Metadata { get; set; }
        //public bool Overwrite { get; set; } = false;
    }
}
