namespace MxfaceWebAPI.Common
{
    // TODO: placeholder numbering — no business/DB-confirmed featuretype scheme found anywhere in
    // this codebase. Confirm against any downstream consumer of transactions.featuretype
    // (billing/reporting) before relying on these values outside this app.
    public static class BiometricFeatureType
    {
        public const int IrisVerify = 1;
        public const int IrisEnroll = 2;
        public const int IrisSearch = 3;
        public const int IrisDelete = 4;
        public const int IrisLiveness = 5;
        public const int FingerPrintVerify = 6;
        public const int FingerPrintEnroll = 7;
        public const int FingerPrintSearch = 8;
        public const int FingerPrintDelete = 9;
        public const int FingerPrintLiveness = 10;
        public const int FaceEnroll = 11;
    }
}
