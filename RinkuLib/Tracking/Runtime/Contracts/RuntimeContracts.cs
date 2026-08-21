using System.Diagnostics.CodeAnalysis;
using Rinku.Mapping;

namespace Rinku.Tracking.Runtime;

/// <summary>String/index based access to the runtime-generated public member surface.</summary>
public interface IRuntimeMemberAccess
{
    /// <summary>Gets the runtime member name map.</summary>
    Mapper Mapper { get; }
    /// <summary>Tries to get a member value by index.</summary>
    bool TryGet<T>(int index, [MaybeNullWhen(false)] out T value);
    /// <summary>Sets a member value by index.</summary>
    bool Set<T>(int index, T value);

    /// <summary>Gets a member value by index.</summary>
    [return: MaybeNull]
    T Get<T>(int index)
    {
        if (TryGet(index, out T? value)) return value;
        throw new InvalidOperationException($"Unable to read runtime tracking member at index {index} as {typeof(T)}.");
    }

    /// <summary>Gets the index for a member name.</summary>
    int GetIndex(string name)
    {
        int index = Mapper.GetIndex(name);
        return index >= 0 ? index : throw new KeyNotFoundException(name);
    }

    /// <summary>Gets the index for a member name.</summary>
    int GetIndex(ReadOnlySpan<char> name)
    {
        int index = Mapper.GetIndex(name);
        return index >= 0 ? index : throw new KeyNotFoundException(name.ToString());
    }

    /// <summary>Gets a member value by name.</summary>
    [return: MaybeNull]
    T Get<T>(string name) => Get<T>(GetIndex(name));
    /// <summary>Gets a member value by name.</summary>
    [return: MaybeNull]
    T Get<T>(ReadOnlySpan<char> name) => Get<T>(GetIndex(name));
    /// <summary>Sets a member value by name.</summary>
    bool Set<T>(string name, T value)
    {
        int index = Mapper.GetIndex(name);
        return index >= 0 && Set(index, value);
    }

    /// <summary>Sets a member value by name.</summary>
    bool Set<T>(ReadOnlySpan<char> name, T value)
    {
        int index = Mapper.GetIndex(name);
        return index >= 0 && Set(index, value);
    }
}

/// <summary>
/// Public compile-time surface used when the concrete generated CLR type is only known at runtime.
/// Domain properties still exist on the generated concrete type for reflection/binding, but callers use runtime member access here.
/// </summary>
public interface IRuntimeTrackingItem<TOriginal> :
    IEditable,
    IOriginal<TOriginal>,
    ITrackingListNewState,
    IRuntimeMemberAccess,
    ITrackingChanges
{
}

/// <summary>Confirms the new state of a generated item.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public interface IRuntimeNewStateControl
{
    /// <summary>Confirms the new state.</summary>
    bool ConfirmNew();
}
