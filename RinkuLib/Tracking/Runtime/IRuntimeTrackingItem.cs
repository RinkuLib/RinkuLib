using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Rinku.Mapping;
using Rinku.Querying.Parameters;

namespace Rinku.Tracking.Runtime;

// Marker/capability for an interaction contract that Rinku may generate from TOriginal.
// Runtime generation itself does not imply dynamic name/index access or notifications.
/// <summary>Marks a tracking item whose runtime shape may be generated.</summary>
[RuntimeTrackingParameterSource]
public interface IRuntimeTrackingItem<TOriginal> : IEditableTrackingItem<TOriginal> { }

// Optional dynamic member access. Strong generated contracts do not inherit this unless requested.
/// <summary>Provides indexed and named access to generated members.</summary>
public interface IRuntimeMemberAccess {
    /// <summary>Gets the generated member mapper.</summary>
    Mapper Mapper { get; }
    /// <summary>Reads a generated member by index.</summary>
    bool TryGet<T>(int index, [MaybeNullWhen(false)] out T value);
    /// <summary>Writes a generated member by index.</summary>
    bool Set<T>(int index, T value);

    /// <summary>Gets a member by index.</summary>
    T Get<T>(int index) {
        if (TryGet(index, out T? value)) return value!;
        throw new InvalidOperationException($"Unable to read runtime tracking member at index {index} as {typeof(T)}.");
    }

    /// <summary>Gets a member index by name.</summary>
    int GetIndex(string name) {
        int index = Mapper.GetIndex(name);
        return index >= 0 ? index : throw new KeyNotFoundException(name);
    }

    /// <summary>Gets a member index by character span.</summary>
    int GetIndex(ReadOnlySpan<char> name) {
        int index = Mapper.GetIndex(name);
        return index >= 0 ? index : throw new KeyNotFoundException(name.ToString());
    }

    /// <summary>Gets a member by name.</summary>
    T Get<T>(string name) => Get<T>(GetIndex(name));
    /// <summary>Gets a member by character span.</summary>
    T Get<T>(ReadOnlySpan<char> name) => Get<T>(GetIndex(name));

    /// <summary>Sets a member by name.</summary>
    bool Set<T>(string name, T value) {
        int index = Mapper.GetIndex(name);
        return index >= 0 && Set(index, value);
    }

    /// <summary>Sets a member by character span.</summary>
    bool Set<T>(ReadOnlySpan<char> name, T value) {
        int index = Mapper.GetIndex(name);
        return index >= 0 && Set(index, value);
    }
}

// Useful named bundles for the common UI/runtime paths.
/// <summary>Combines generated tracking with change notifications.</summary>
public interface IRuntimeNotifyTrackingItem<TOriginal> : IRuntimeTrackingItem<TOriginal>, INotifyPropertyChanged { }
/// <summary>Combines generated tracking, notifications, and member access.</summary>
public interface IRuntimeDynamicTrackingItem<TOriginal> : IRuntimeNotifyTrackingItem<TOriginal>, IRuntimeMemberAccess { }

/// <summary>Provides indexed access helpers for tracking item lists.</summary>
public static class RuntimeTrackingAccess {
    /// <summary>Reads a member from a list row.</summary>
    public static T Get<T>(this IReadOnlyList<IRuntimeMemberAccess> list, int row, int member)
        => list[row].Get<T>(member);

    /// <summary>Writes a member on a list row.</summary>
    public static bool Set<T>(this IReadOnlyList<IRuntimeMemberAccess> list, int row, int member, T value)
        => list[row].Set(member, value);
}
