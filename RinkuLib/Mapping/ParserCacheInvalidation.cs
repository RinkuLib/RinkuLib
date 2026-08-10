using System.Collections.ObjectModel;
using System.ComponentModel;
using Rinku.Mapping.Parsers;

namespace Rinku.Mapping;

/// <summary>Controls how caches retaining a parser respond while that parser is being invalidated.</summary>
public enum ParserInvalidationMode : byte {
    /// <summary>Keep retained references and cancel disposal when the parser is still in use.</summary>
    CheckUsage,
    /// <summary>Remove retained references so the invalidated parser can be disposed.</summary>
    InvalidateReferences
}

/// <summary>Describes a parser that has left its originating cache and is about to be disposed.</summary>
public sealed class ParserDisposingEventArgs(ITypeParser parser, ParserInvalidationMode mode) : CancelEventArgs {
    /// <summary>The exact parser instance being released.</summary>
    public ITypeParser Parser { get; } = parser;
    /// <summary>How caches retaining <see cref="Parser"/> should respond.</summary>
    public ParserInvalidationMode Mode { get; } = mode;
}

/// <summary>The ordered parser-maker registrations.</summary>
public sealed class TypeParserMakerCollection : Collection<ITypeParserMaker> {
    /// <inheritdoc/>
    protected override void InsertItem(int index, ITypeParserMaker item) {
        ArgumentNullException.ThrowIfNull(item);
        lock (this)
            base.InsertItem(index, item);
    }
    /// <inheritdoc/>
    protected override void SetItem(int index, ITypeParserMaker item) {
        ArgumentNullException.ThrowIfNull(item);
        lock (this)
            base.SetItem(index, item);
    }
    /// <inheritdoc/>
    protected override void RemoveItem(int index) {
        lock (this)
            base.RemoveItem(index);
    }
    /// <inheritdoc/>
    protected override void ClearItems() {
        lock (this) {
            if (Count == 0)
                return;
            base.ClearItems();
        }
    }
}
