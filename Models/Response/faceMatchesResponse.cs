using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MxfaceWebAPI.Models.Response
{
    public class MatchedFaceResponse : BiomatricBaseResponse
    {
        public List<CompareFace> MatchedFaces { get; set; }
    }
}
