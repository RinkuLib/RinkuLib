using Rinku.More.Globalization;

namespace Rinku.More;

public enum ValidationSeverity { Information, Warning, Error }
public interface IValidationMessages : IReadOnlyList<(Word Message, string? Field, ValidationSeverity Severity, object?[]? Arguments)>;
