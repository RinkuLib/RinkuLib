namespace RinkuPowerTools.Compiler;

public sealed class XamlCompilerException(string code, string message, string? file = null) : Exception(message) {
    public string Code { get; } = code;
    public string? File { get; } = file;

    public override string ToString()
        => File is null
            ? $"error {Code}: {Message}"
            : $"error {Code} ({File}): {Message}";
}