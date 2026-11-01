using System.Text.RegularExpressions;

namespace RinkuPowerTools; 
public partial class SharedRegex {

    [GeneratedRegex(@"^([a-zA-Z0-9_]+)(?:\s*\(\s*([a-zA-Z0-9]+)(?:\s*,\s*(\d+))?\s*\))?$", RegexOptions.IgnoreCase)]
    public static partial Regex SQLTypeRegex();

    public const string ConfigFilePattern = @"(?:^|[\\/])rinku(?:pt)?(\.[^.]+)?\.json$";

    [GeneratedRegex(ConfigFilePattern, RegexOptions.IgnoreCase | RegexOptions.Compiled, "en-CA")]
    public static partial Regex ConfigFileName();
}
