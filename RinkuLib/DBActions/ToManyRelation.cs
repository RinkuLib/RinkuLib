using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using RinkuLib.DbParsing;
using RinkuLib.Tools;

namespace RinkuLib.DBActions;
/// <summary></summary>
[method: AreReadable]
public record struct DBPair<T, TItem>([CanNotLookAnywhere][NoName] T ID, [NoName] TItem Object);

/// <summary></summary>
public class ToManyRelation<TParent, TID, TItem>(Getter<TParent, TID> GetID, Setter<TParent, PooledArray<TItem>> SetCollection) : DbAction<TParent> {
    private readonly Getter<TParent, TID> GetID = GetID;
    private readonly Setter<TParent, PooledArray<TItem>> SetCollection = SetCollection;

    /// <inheritdoc/>
    public override void Handle<TParserGetter>(IEnumerable<TParent> parents, TParserGetter parserGetter) {
        var comparer = EqualityComparer<TID>.Default;
        using var reader = parserGetter.GetParserAndReader<DBPair<TID, TItem>>(CommandBehavior.SingleResult, out var parser);
        using PooledArray<TItem> sharedArray = new(0);
        bool hasData = reader.Read();
        var currentTrio = hasData ? parser.Parse(reader) : default;

        foreach (var p in parents) {
            var parent = p;
            var parentId = GetID(ref parent);
            while (hasData && comparer.Equals(currentTrio.ID, parentId)) {
                sharedArray.Add(currentTrio.Object);
                hasData = reader.Read();
                if (hasData) {
                    currentTrio = parser.Parse(reader);
                }
            }
            SetCollection(ref parent, sharedArray);
            sharedArray.Clear();
        }
        if (hasData)
            throw new Exception("Should have consumed all the rows");
    }
    /// <inheritdoc/>
    public override void Handle<TAccess, TParserGetter>(TAccess parents, TParserGetter parserGetter) {
        var comparer = EqualityComparer<TID>.Default;
        using var reader = parserGetter.GetParserAndReader<DBPair<TID, TItem>>(CommandBehavior.SingleResult, out var parser);
        using PooledArray<TItem> sharedArray = new(0);
        bool hasData = reader.Read();
        var currentPair = hasData ? parser.Parse(reader) : default;
        var count = parents.Length;
        for (int i = 0; i < count; i++) {
            ref var parent = ref parents.GetAt(i);
            var parentId = GetID(ref parent);
            while (hasData && comparer.Equals(currentPair.ID, parentId)) {
                sharedArray.Add(currentPair.Object);
                hasData = reader.Read();
                if (hasData) {
                    currentPair = parser.Parse(reader);
                }
            }
            SetCollection(ref parent, sharedArray);
            sharedArray.Clear();
        }
        if (hasData)
            throw new Exception("Should have consumed all the rows");
    }
    /// <inheritdoc/>
    public override void Handle<TParserGetter>(ref TParent parent, TParserGetter parserGetter) {
        var comparer = EqualityComparer<TID>.Default;
        using var reader = parserGetter.GetParserAndReader<DBPair<TID, TItem>>(CommandBehavior.SingleResult, out var parser);
        using PooledArray<TItem> sharedArray = new(0);
        bool hasData = reader.Read();
        var currentPair = hasData ? parser.Parse(reader) : default;

        var parentId = GetID(ref parent);
        while (hasData && comparer.Equals(currentPair.ID, parentId)) {
            sharedArray.Add(currentPair.Object);
            hasData = reader.Read();
            if (hasData) {
                currentPair = parser.Parse(reader);
            }
        }
        SetCollection(ref parent, sharedArray);
        if (hasData)
            throw new Exception("Should have consumed all the rows");
    }
    /// <inheritdoc/>
    public override async ValueTask HandleAsync<TParserGetter>(IEnumerable<TParent> parents, TParserGetter parserGetter, CancellationToken ct = default) {
        var comparer = EqualityComparer<TID>.Default;
        using var reader = parserGetter.GetParserAndReader<DBPair<TID, TItem>>(CommandBehavior.SingleResult, out var parser);
        using PooledArray<TItem> sharedArray = new(0);
        bool hasData = await reader.ReadAsync(ct).ConfigureAwait(false);
        var currentPair = hasData ? parser.Parse(reader) : default;

        foreach (var p in parents) {
            var parent = p;
            var parentId = GetID(ref parent);
            while (hasData && comparer.Equals(currentPair.ID, parentId)) {
                sharedArray.Add(currentPair.Object);
                hasData = await reader.ReadAsync(ct).ConfigureAwait(false);
                if (hasData) {
                    currentPair = parser.Parse(reader);
                }
            }
            SetCollection(ref parent, sharedArray);
            sharedArray.Clear();
        }
        if (hasData)
            throw new Exception("Should have consumed all the rows");
    }
    /// <inheritdoc/>
    public override async ValueTask HandleAsync<TAccess, TParserGetter>(TAccess parents, TParserGetter parserGetter, CancellationToken ct = default) {
        var comparer = EqualityComparer<TID>.Default;
        using var reader = parserGetter.GetParserAndReader<DBPair<TID, TItem>>(CommandBehavior.SingleResult, out var parser);
        using PooledArray<TItem> sharedArray = new(0);
        bool hasData = await reader.ReadAsync(ct).ConfigureAwait(false);
        var currentPair = hasData ? parser.Parse(reader) : default;
        var count = parents.Length;
        for (int i = 0; i < count; i++) {
            var parentId = GetID(ref parents.GetAt(i));
            while (hasData && comparer.Equals(currentPair.ID, parentId)) {
                sharedArray.Add(currentPair.Object);
                hasData = await reader.ReadAsync(ct).ConfigureAwait(false);
                if (hasData) {
                    currentPair = parser.Parse(reader);
                }
            }
            SetCollection(ref parents.GetAt(i), sharedArray);
            sharedArray.Clear();
        }
        if (hasData)
            throw new Exception("Should have consumed all the rows");
    }
    /// <inheritdoc/>
    public override async ValueTask HandleAsync<TParserGetter>(TParent parent, TParserGetter parserGetter, CancellationToken ct = default) {
        var comparer = EqualityComparer<TID>.Default;
        using var reader = parserGetter.GetParserAndReader<DBPair<TID, TItem>>(CommandBehavior.SingleResult, out var parser);
        using PooledArray<TItem> sharedArray = new(0);
        bool hasData = await reader.ReadAsync(ct).ConfigureAwait(false);
        var currentPair = hasData ? parser.Parse(reader) : default;

        var parentId = GetID(ref parent);
        while (hasData && comparer.Equals(currentPair.ID, parentId)) {
            sharedArray.Add(currentPair.Object);
            hasData = await reader.ReadAsync(ct).ConfigureAwait(false);
            if (hasData) {
                currentPair = parser.Parse(reader);
            }
        }
        SetCollection(ref parent, sharedArray);
        if (hasData)
            throw new Exception("Should have consumed all the rows");
    }
}
