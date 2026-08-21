using System.Diagnostics.CodeAnalysis;

namespace Rinku.Tracking;

/// <summary>Exposes an original/source value when one is currently available.</summary>
public interface IOriginal<TOriginal>
{
    /// <summary>Tries to get the accepted original value.</summary>
    bool TryGetOriginal([MaybeNullWhen(false)] out TOriginal original);
}

/// <summary>Controls an editable snapshot.</summary>
public interface IEditable
{
    /// <summary>Gets whether an edit snapshot exists.</summary>
    bool IsEditing { get; }
    /// <summary>Ensures that an edit snapshot exists.</summary>
    bool EnsureEditing();
    /// <summary>Accepts the current edit snapshot.</summary>
    bool ConfirmEdit();
    /// <summary>Discards the current edit snapshot.</summary>
    bool CancelEdit();
}
