using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RinkuLib.DBActions;

/// <summary>Wrap a collection and provide acces to the ref of the items</summary>
public interface ICollectionRefAccessor<T> {
    /// <summary>Getter to the underlying ennumerable</summary>
    public IEnumerable<T> GetEnumerable { get; }
    /// <summary>Is the collection is not null and has at least one value</summary>
    public bool HasValues { get; }
    /// <summary>The length / count of the underlying item</summary>
    public int Length { get; }
    /// <summary>Access the ref of an item at the index</summary>
    public ref T GetAt(int i);
}

/// <summary>The ref accessor for a list</summary>
public readonly struct ListAccess<T>(List<T> instances) : ICollectionRefAccessor<T> {
    /// <inheritdoc/>
    public static implicit operator ListAccess<T>(List<T> instances) => new(instances);
    private readonly List<T> instances = instances;
    /// <inheritdoc/>
    public IEnumerable<T> GetEnumerable => instances;
    /// <inheritdoc/>
    public bool HasValues => instances is not null && instances.Count > 0;
    /// <inheritdoc/>
    public int Length => instances.Count;

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public ref T GetAt(int i) => ref CollectionsMarshal.AsSpan(instances)[i];
}
/// <summary>The ref accessor for a list</summary>
public readonly struct ArrayAccess<T>(T[] instances) : ICollectionRefAccessor<T> {
    /// <inheritdoc/>
    public static implicit operator ArrayAccess<T>(T[] instances) => new(instances);
    private readonly T[] instances = instances;
    /// <inheritdoc/>
    public IEnumerable<T> GetEnumerable => instances;
    /// <inheritdoc/>
    public bool HasValues => instances is not null && instances.Length > 0;
    /// <inheritdoc/>
    public int Length => instances.Length;
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public ref T GetAt(int i) => ref instances[i];
}