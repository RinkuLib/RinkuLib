using System.IO;

namespace RinkuPowerTools.VisualStudio.Content.Config;

public sealed class ConfigFile {
    private readonly string _filePath = string.Empty;
    public readonly string? _name = string.Empty;
    public required string FilePath { get => _filePath; init {
            _filePath = value;
            if (!Path.GetFileName(FilePath).TryExtractClassName(out var n))
                throw new Exception($"Unable to extract the config name from {value}");
            _name = n == string.Empty ? null : n;
        }
    }
    public bool EqualName(string name) => _name is null ? name == string.Empty : _name.Equals(name, StringComparison.OrdinalIgnoreCase);
    public string Name => _name ?? "(default)"; 
    public ConnectionSourceType ConnectionSourceType { get; set; }
    public string ConnectionTarget { get; set; } = string.Empty;
    public string? ConnectionExtractionPath { get; set; }
    public string OutputPath { get; set; } = string.Empty;
    public string? Namespace { get; set; }
    public bool IsInternal { get; set; }
}