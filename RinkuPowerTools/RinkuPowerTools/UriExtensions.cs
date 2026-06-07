namespace RinkuPowerTools;
public static class UriExtensions {
    public static string? GetFileNameWithTrueCasing(this Uri uri) {
        var truePath = uri.GetLocalPathWithTrueCasing();
        return truePath != null ? Path.GetFileName(truePath) : null;
    }
    public static string? GetLocalPathWithTrueCasing(this Uri uri) {
        if (uri == null || !uri.IsFile)
            return uri?.LocalPath;

        string normalizedPath = uri.LocalPath;

        if (!File.Exists(normalizedPath))
            return normalizedPath;

        var directory = Path.GetDirectoryName(normalizedPath);
        var filename = Path.GetFileName(normalizedPath);

        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(filename))
            return normalizedPath;

        try {
            string[] exactMatches = Directory.GetFiles(directory, filename);
            if (exactMatches.Length > 0)
                return exactMatches[0];
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) {
        }

        return normalizedPath;
    }
}