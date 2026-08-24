namespace VirtueCore.Shared
{
    // Resolves the 3.2.1 "title" header field against its pre-3.2.1 legacy
    // name (event files: "experiment", model files: "detector"), so older
    // files written before the rename keep working.
    public static class LegacyField
    {
        public static string Resolve(string title, string legacyValue)
        {
            return string.IsNullOrEmpty(title) ? legacyValue : title;
        }
    }
}
