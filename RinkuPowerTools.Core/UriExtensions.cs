namespace RinkuPowerTools.Core;
public static class UriExtensions {
    /// <summary>
    /// Retrieves ONLY the file name from a URI with its exact file system casing.
    /// </summary>
    public static string? GetFileNameWithTrueCasing(this Uri uri) {
        var truePath = uri.GetLocalPathWithTrueCasing();
        return truePath != null ? Path.GetFileName(truePath) : null;
    }

    /// <summary>
    /// Retrieves the local file path from a URI, restoring the exact file system casing for the file name.
    /// </summary>
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