using static System.Runtime.InteropServices.JavaScript.JSType;

namespace MxfaceWebAPI.Models
{
    public class BiomatricBaseResponse
    {
        public int? Code { get; set; }
        public string? Message { get; set; }
        public string? ErrorMessage { get; set; }
        public int? ErrorCode { get; set; }


    }
}
