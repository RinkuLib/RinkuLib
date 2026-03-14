using System.Data;
using System.Runtime.CompilerServices;
using RinkuLib.DbParsing;
using RinkuLib.Tools;

namespace RinkuLib.DBActions;
/// <summary></summary>
[method: AreReadable]
public record struct DBTrio<T1, T2, TItem>([CanNotLookAnywhere][NoName] T1 ID1, [CanNotLookAnywhere][NoName] T2 ID2, [NoName] TItem Object);

/// <summary></summary>
public class ToManyRelationTwoLevel<TParent, TParentID, TTransient, TTransientID, TItem, TAccessor>(Getter<TParent, TParentID> GetParentID, Getter<TParent, TAccessor> GetTransients, Getter<TTransient, TTransientID> GetTransientID, Setter<TTransient, PooledArray<TItem>> SetTransientCollection) : DbAction<TParent> where TAccessor : notnull, ICollectionRefAccessor<TTransient> {
    /// <summary></summary>
    private readonly Getter<TParent, TParentID> GetParentID = GetParentID;
    /// <summary></summary>
    private readonly Getter<TParent, TAccessor> GetTransients = GetTransients;
    /// <summary></summary>
    private readonly Getter<TTransient, TTransientID> GetTransientID = GetTransientID;
    /// <summary></summary>
    private readonly Setter<TTransient, PooledArray<TItem>> SetTransientCollection = SetTransientCollection;
    /// <inheritdoc/>
    public override void Handle<TParserGetter>(IEnumerable<TParent> parents, TParserGetter parserGetter) {
        var comparer1 = EqualityComparer<TParentID>.Default;
        var comparer2 = EqualityComparer<TTransientID>.Default;
        using var reader = parserGetter.GetParserAndReader<DBTrio<TParentID, TTransientID, TItem>>(CommandBehavior.SingleResult, out var parser);
        using PooledArray<TItem> sharedArray = new(0);
        bool hasData = reader.Read();
        var currentTrio = hasData ? parser.Parse(reader) : default;

        foreach (var p in parents) {
            var parent = p;
            TParentID parentId = GetParentID(ref parent);
            var transients = GetTransients(ref parent);
            var len = transients.Length;
            for (int i = 0; i < len; i++) {
                ref var transient = ref transients.GetAt(i);
                TTransientID transientID = GetTransientID(ref transient);
                while (hasData && comparer1.Equals(currentTrio.ID1, parentId)
                    && comparer2.Equals(currentTrio.ID2, transientID)) {
                    sharedArray.Add(currentTrio.Object);
                    hasData = reader.Read();
                    if (hasData) {
                        currentTrio = parser.Parse(reader);
                    }
                }
                SetTransientCollection(ref transient, sharedArray);
                sharedArray.Clear();
            }
        }
        if (hasData)
            throw new Exception("Should have consumed all the rows");
    }
    /// <inheritdoc/>
    public override void Handle<TAccess, TParserGetter>(TAccess parents, TParserGetter parserGetter) {
        var comparer1 = EqualityComparer<TParentID>.Default;
        var comparer2 = EqualityComparer<TTransientID>.Default;
        using var reader = parserGetter.GetParserAndReader<DBTrio<TParentID, TTransientID, TItem>>(CommandBehavior.SingleResult, out var parser);
        using PooledArray<TItem> sharedArray = new(0);
        bool hasData = reader.Read();
        var currentTrio = hasData ? parser.Parse(reader) : default;
        var count = parents.Length;
        for (int i = 0; i < count; i++) {
            ref var parent = ref parents.GetAt(i);
            TParentID parentId = GetParentID(ref parent);
            var transients = GetTransients(ref parent);
            var len = transients.Length;
            for (int j = 0; j < len; j++) {
                ref var transient = ref transients.GetAt(j);
                TTransientID transientID = GetTransientID(ref transient);
                while (hasData && comparer1.Equals(currentTrio.ID1, parentId)
                    && comparer2.Equals(currentTrio.ID2, transientID)) {
                    sharedArray.Add(currentTrio.Object);
                    hasData = reader.Read();
                    if (hasData) {
                        currentTrio = parser.Parse(reader);
                    }
                }
                SetTransientCollection(ref transient, sharedArray);
                sharedArray.Clear();
            }
        }
        if (hasData)
            throw new Exception("Should have consumed all the rows");
    }
    /// <inheritdoc/>
    public override void Handle<TParserGetter>(ref TParent parent, TParserGetter parserGetter) {
        var comparer1 = EqualityComparer<TParentID>.Default;
        var comparer2 = EqualityComparer<TTransientID>.Default;
        using var reader = parserGetter.GetParserAndReader<DBTrio<TParentID, TTransientID, TItem>>(CommandBehavior.SingleResult, out var parser);
        using PooledArray<TItem> sharedArray = new(0);
        bool hasData = reader.Read();
        var currentTrio = hasData ? parser.Parse(reader) : default;

        TParentID parentId = GetParentID(ref parent);
        var transients = GetTransients(ref parent);
        var len = transients.Length;
        for (int i = 0; i < len; i++) {
            ref var transient = ref transients.GetAt(i);
            TTransientID transientID = GetTransientID(ref transient);
            while (hasData && comparer1.Equals(currentTrio.ID1, parentId)
                && comparer2.Equals(currentTrio.ID2, transientID)) {
                sharedArray.Add(currentTrio.Object);
                hasData = reader.Read();
                if (hasData) {
                    currentTrio = parser.Parse(reader);
                }
            }
            SetTransientCollection(ref transient, sharedArray);
            sharedArray.Clear();
        }
        if (hasData)
            throw new Exception("Should have consumed all the rows");
    }
    /// <inheritdoc/>
    public override async ValueTask HandleAsync<TParserGetter>(IEnumerable<TParent> parents, TParserGetter parserGetter, CancellationToken ct = default) {
        var comparer1 = EqualityComparer<TParentID>.Default;
        var comparer2 = EqualityComparer<TTransientID>.Default;
        using var reader = parserGetter.GetParserAndReader<DBTrio<TParentID, TTransientID, TItem>>(CommandBehavior.SingleResult, out var parser);
        using PooledArray<TItem> sharedArray = new(0);
        bool hasData = await reader.ReadAsync(ct).ConfigureAwait(false);
        var currentTrio = hasData ? parser.Parse(reader) : default;

        foreach (var p in parents) {
            var parent = p;
            TParentID parentId = GetParentID(ref parent);
            var transients = GetTransients(ref parent);
            var len = transients.Length;
            for (int i = 0; i < len; i++) {
                TTransientID transientID = GetTransientID(ref transients.GetAt(i));
                while (hasData && comparer1.Equals(currentTrio.ID1, parentId)
                    && comparer2.Equals(currentTrio.ID2, transientID)) {
                    sharedArray.Add(currentTrio.Object);
                    hasData = await reader.ReadAsync(ct).ConfigureAwait(false);
                    if (hasData) {
                        currentTrio = parser.Parse(reader);
                    }
                }
                SetTransientCollection(ref transients.GetAt(i), sharedArray);
                sharedArray.Clear();
            }
        }
        if (hasData)
            throw new Exception("Should have consumed all the rows");
    }
    /// <inheritdoc/>
    public override async ValueTask HandleAsync<TAccess, TParserGetter>(TAccess parents, TParserGetter parserGetter, CancellationToken ct = default) {
        var comparer1 = EqualityComparer<TParentID>.Default;
        var comparer2 = EqualityComparer<TTransientID>.Default;
        using var reader = parserGetter.GetParserAndReader<DBTrio<TParentID, TTransientID, TItem>>(CommandBehavior.SingleResult, out var parser);
        using PooledArray<TItem> sharedArray = new(0);
        bool hasData = await reader.ReadAsync(ct).ConfigureAwait(false);
        var currentTrio = hasData ? parser.Parse(reader) : default;
        var count = parents.Length;
        for (int i = 0; i < count; i++) {
            ref var parent = ref parents.GetAt(i);
            TParentID parentId = GetParentID(ref parent);
            var transients = GetTransients(ref parent);
            var len = transients.Length;
            for (int j = 0; j < len; j++) {
                TTransientID transientID = GetTransientID(ref transients.GetAt(j));
                while (hasData && comparer1.Equals(currentTrio.ID1, parentId)
                    && comparer2.Equals(currentTrio.ID2, transientID)) {
                    sharedArray.Add(currentTrio.Object);
                    hasData = await reader.ReadAsync(ct).ConfigureAwait(false);
                    if (hasData) {
                        currentTrio = parser.Parse(reader);
                    }
                }
                SetTransientCollection(ref transients.GetAt(j), sharedArray);
                sharedArray.Clear();
            }
        }
        if (hasData)
            throw new Exception("Should have consumed all the rows");
    }
    /// <inheritdoc/>
    public override async ValueTask HandleAsync<TParserGetter>(TParent parent, TParserGetter parserGetter, CancellationToken ct = default) {
        var comparer1 = EqualityComparer<TParentID>.Default;
        var comparer2 = EqualityComparer<TTransientID>.Default;
        using var reader = parserGetter.GetParserAndReader<DBTrio<TParentID, TTransientID, TItem>>(CommandBehavior.SingleResult, out var parser);
        using PooledArray<TItem> sharedArray = new(0);
        bool hasData = await reader.ReadAsync(ct).ConfigureAwait(false);
        var currentTrio = hasData ? parser.Parse(reader) : default;

        TParentID parentId = GetParentID(ref parent);
        var transients = GetTransients(ref parent);
        var len = transients.Length;
        for (int i = 0; i < len; i++) {
            TTransientID transientID = GetTransientID(ref transients.GetAt(i));
            while (hasData && comparer1.Equals(currentTrio.ID1, parentId)
                && comparer2.Equals(currentTrio.ID2, transientID)) {
                sharedArray.Add(currentTrio.Object);
                hasData = await reader.ReadAsync(ct).ConfigureAwait(false);
                if (hasData) {
                    currentTrio = parser.Parse(reader);
                }
            }
            SetTransientCollection(ref transients.GetAt(i), sharedArray);
            sharedArray.Clear();
        }
        if (hasData)
            throw new Exception("Should have consumed all the rows");
    }
}