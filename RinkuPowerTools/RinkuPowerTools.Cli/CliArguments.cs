namespace RinkuPowerTools.Cli;

internal sealed class CliArguments {
    private readonly List<string> _positionals = [];
    private readonly Dictionary<string, string?> _options = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> Positionals => _positionals;

    public static CliArguments Parse(ReadOnlySpan<string> args) {
        var result = new CliArguments();

        for (int i = 0; i < args.Length; i++) {
            string arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal)) {
                result._positionals.Add(arg);
                continue;
            }

            string option = arg[2..];
            int equals = option.IndexOf('=');
            if (equals >= 0) {
                result._options[option[..equals]] = option[(equals + 1)..];
                continue;
            }

            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                result._options[option] = args[++i];
            else
                result._options[option] = null;
        }

        return result;
    }

    public bool Has(string name) => _options.ContainsKey(name);

    public string? Get(string name) => _options.TryGetValue(name, out string? value) ? value : null;

    public string Require(string name) => Get(name) ?? throw new CliException($"Missing --{name}.", 2);

    public bool GetBoolean(string name, bool currentValue) {
        if (!_options.TryGetValue(name, out string? value))
            return currentValue;
        if (value is null)
            return true;
        if (bool.TryParse(value, out bool parsed))
            return parsed;
        throw new CliException($"--{name} expects true or false.", 2);
    }
}

internal sealed class CliException(string message, int exitCode = 1) : Exception(message) {
    public int ExitCode { get; } = exitCode;
}
