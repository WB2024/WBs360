namespace BadBuilder.Utilities
{
    internal static class Constants
    {
        internal static readonly string WORKING_DIR = "Work";
        internal static readonly string DOWNLOAD_DIR = Path.Combine(WORKING_DIR, "Download");
        internal static readonly string EXTRACTED_DIR = Path.Combine(WORKING_DIR, "Extract");

        internal const long KB = 1024L;
        internal const long MB = 1048576L;
        internal const long GB = 1073741824L;
        internal const long TB = 1099511627776L;
    }
}