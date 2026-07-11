using System.Text.RegularExpressions;

namespace RinkuPowerTools; 
public partial class SharedRegex {

    [GeneratedRegex(@"^([a-zA-Z0-9_]+)(?:\s*\(\s*([a-zA-Z0-9]+)(?:\s*,\s*(\d+))?\s*\))?$", RegexOptions.IgnoreCase)]
    public static partial Regex SQLTypeRegex();

    [GeneratedRegex(@".*rinkupt(\.[^.]+)?\.json$", RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-CA")]
    public static partial Regex ConfigFileName();
}
