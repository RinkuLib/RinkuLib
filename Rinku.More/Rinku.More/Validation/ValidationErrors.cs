using Rinku.More.Globalization;
using System.Collections.ObjectModel;

namespace Rinku.More;

public sealed class ValidationErrors : Collection<(Word Message, string? Field, ValidationSeverity Severity, object?[]? Arguments)>, IValidationMessages {
    public static readonly ValidationErrors Empty = new(Array.Empty<(Word, string?, ValidationSeverity, object?[]?)>());
    public bool IsReadOnly => Items.IsReadOnly;
    public bool HasErrors {
        get {
            for (int index = 0; index < Count; index++)
                if (this[index].Severity == ValidationSeverity.Error)
                    return true;
            return false;
        }
    }
    public void Add((Word Message, string? Field, ValidationSeverity Severity) message) => Add((message.Message, message.Field, message.Severity, null));
    public void Add(Word message, string? field = null, ValidationSeverity severity = ValidationSeverity.Error) => Add((message, field, severity, null));

    public bool RemoveField(string field) {
        bool changed = false;
        for (int index = Count - 1; index >= 0; index--)
            if (this[index].Field == field) {
                RemoveAt(index);
                changed = true;
            }
        return changed;
    }
    public void Reset(string? field = null) {
        if (field is null)
            Clear();
        else
            RemoveField(field);
    }
    public void ReplaceWith(IEnumerable<(Word Message, string? Field, ValidationSeverity Severity, object?[]? Arguments)> messages) {
        if (ReferenceEquals(this, messages))
            return;
        Clear();
        foreach (var message in messages)
            Add(message);
    }
    public ValidationErrors() { }
    private ValidationErrors(IList<(Word Message, string? Field, ValidationSeverity Severity, object?[]? Arguments)> items) : base(items) { }
}
