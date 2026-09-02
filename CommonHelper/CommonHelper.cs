using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.RegularExpressions;

namespace MxfaceWebAPI.CommonHelper
{
    public static class CommonHelper
    {
        public const string InValidBiometricRequest = "Both biometric samples are required.";

        public static readonly string AuthenticationType = "hmac ";
        public static readonly string GeneralErrorMessage = "Something went wrong, please try again later";
        public static readonly string character = @"only \, _,[,],(,),- special characters and space is allowed";


        public static readonly string InvalidFaceSize = "Invalid face size";
        public static readonly string InvalidImage = "Invalid image. Please pass base-64 string of any .jpg .jpeg .png .bmp.";
        public static readonly string InvalidBase64 = "The input is not a valid Base-64 string.";
        public static readonly string InValidRequest = "Invalid request. Please pass valid json with all required parameters.";
        public static readonly string UnHandledException = "Something went wrong! Try again";
        public static readonly string NotFound = "Request not found. The specified url does not exist.";
        public static readonly string Exception = "Request responded with exceptions.";
        public static readonly string IsExists = "Data already exists.";
        public static readonly string LowQuality = "The quality of face is very low. Please check quality of face using Detect or Quality API.";
        public static readonly string LowQualityOrNoFace = "No face detected or quality of face is very low.";
        public static readonly string IdentityNotExists = "Could not find a Face Identity with the specified ID.";
        public static readonly string ExternalIdNotExists = "Could not find a Face Identity with the specified External ID.";

        public static readonly string FaceDelete = "Can not delete all the faces from identity, please use delete identity api.";
        public static readonly string ClientQualityThersholdMsg = "Quality threshold value is very low. Minimum value is 20";
        public static readonly string ClientQualityThersholdMsgInvalid = "Quality threshold value is not valid. Range 20-100";
        public static readonly int DailyFreeAPILimit = 100;
        public static readonly int FreeLivenessAPILimit = 50;
        public static readonly string NoPeopleDetect = "No People detected or quality of Image is very low.";
        
        public static readonly string OCRFileValidation = "Invalid file, please pass valid files with extention .JPG, .JPEG, .BMP, .PNG, .PDF";
        public static readonly string OCRFileSizeValidation = "Invalid file size, please pass valid files with file size less then 5 MB";
        public static readonly string ExternalIdNotValid = "ExternalId is not valid";

        // Decodes width/height directly from a BMP header (offsets 18/22, 4-byte little-endian each)
        // so callers never need to supply these themselves for format="RAW" bioData entries — height
        // can be negative in the header for a top-down DIB, hence the Math.Abs.
        public static (int Width, int Height) GetBmpDimensions(string base64Image)
        {
            var bytes = Convert.FromBase64String(base64Image);
            if (bytes.Length < 26 || bytes[0] != (byte)'B' || bytes[1] != (byte)'M')
            {
                // Distinct from FormatException (which Convert.FromBase64String already throws for
                // a literal base64 decode failure) so callers can tell "not base64" apart from
                // "valid base64 but not a BMP" and map each to its own error-contract message.
                throw new InvalidDataException("Expected a BMP image (magic bytes 'BM') to derive width/height for format=RAW.");
            }

            var width = BitConverter.ToInt32(bytes, 18);
            var height = BitConverter.ToInt32(bytes, 22);
            return (width, Math.Abs(height));
        }

        // Face images (unlike the Finger/Iris test captures used so far, which are always BMP)
        // commonly arrive as JPEG or PNG — detects the real format from magic bytes instead of
        // assuming BMP. Width/Height are only [M]andatory for format="RAW" per the master's
        // Biometric Data Formats table — BMP/JPEG/PNG carry their own dimensions in the file, so
        // only BMP's are parsed out here (matching GetBmpDimensions); JPEG/PNG return null/null.
        public static (string Format, int? Width, int? Height) DetectImageFormat(string base64Image)
        {
            var bytes = Convert.FromBase64String(base64Image);

            if (bytes.Length >= 26 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M')
            {
                var width = BitConverter.ToInt32(bytes, 18);
                var height = BitConverter.ToInt32(bytes, 22);
                return ("BMP", width, Math.Abs(height));
            }

            if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            {
                return ("JPEG", null, null);
            }

            if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
                && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
            {
                return ("PNG", null, null);
            }

            throw new InvalidDataException("Expected a BMP, JPEG, or PNG image (unrecognized magic bytes).");
        }

        #region GetCustomErrorMessage
        public static string GetCustomErrorMessage(string excMessage)
        {
            if (excMessage.Contains("The input is not a valid Base-64"))
            {
                return InvalidBase64;
            }
            else if (excMessage.Contains("System.Exception"))
            {
                return UnHandledException;
            }
            else if (excMessage.Contains("No connection could be made because the target machine actively refused it."))
            {
                return NotFound;
            }
            else if (excMessage.Contains("Duplicate entry") == true)
            {
                return IsExists;
            }
            else
            {
                return Exception;
            }
        }
        #endregion
    }
}
