using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using RinkuLib.DbParsing;
using RinkuLib.Queries;
using RinkuLib.Tools;

namespace RinkuLib.ToManyRelation;
/// <summary></summary>
[method: AreReadable]
public record struct DBPair<T, TItem>([CanNotLookAnywhere][NoName] T ID, [NoName]TItem Object);
/// <summary></summary>
[method: AreReadable]
public record struct DBTrio<T1, T2, TItem>([CanNotLookAnywhere][NoName] T1 ID1, [CanNotLookAnywhere][NoName] T2 ID2, [NoName] TItem Object);
/// <summary></summary>
public interface IToManyHandler<TParent, TParsed, TItem> {
    /// <summary></summary>
    public bool Handle<TParser>(IEnumerable<TParent> parents, DbDataReader reader, TParser parser, PooledArray<TItem> sharedArray) where TParser : ISchemaParser<TParsed>;
}
/// <summary></summary>
public static class ToManyPopulator<TParent, TItem> {
    /// <summary></summary>
    public static void Init<TParser, TParsed, THandler>(DbCommand cmd, TParser parser, THandler toManyHandler, IEnumerable<TParent> parents, bool disposeCommand) where THandler : IToManyHandler<TParent, TParsed, TItem> where TParser : ISchemaParser<TParsed> {
        var cnn = cmd.Connection ?? throw new Exception("no connections was set with the command");
        var wasClosed = cnn.State != ConnectionState.Open;
        DbDataReader? reader = null;
        try {
            var behavior = parser.Behavior | CommandBehavior.SingleResult;
            if (wasClosed) {
                cnn.Open();
                behavior |= CommandBehavior.CloseConnection;
                wasClosed = false;
            }
            reader = cmd.ExecuteReader(behavior);
            parser.Init(reader, cmd);
            using var sharedArray = new PooledArray<TItem>();
            if (toManyHandler.Handle(parents, reader, parser, sharedArray))
                throw new Exception("Should have consumed all the rows");
            while (reader.NextResult()) { }
        }
        finally {
            if (reader is not null) {
                if (!reader.IsClosed) {
                    try { cmd.Cancel(); }
                    catch { }
                }
                reader.Dispose();
            }
            if (disposeCommand) {
                cmd.Parameters.Clear();
                cmd.Dispose();
            }
            if (wasClosed)
                cnn.Close();
        }
    }
}
/// <summary></summary>
public static class ToManyRelation<TParsed, TItem> {
    /// <summary></summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Init<TParent, TParser, THandler>(DbCommand cmd, TParser parser, THandler toManyHandler, IEnumerable<TParent> parents, bool disposeCommand) where THandler : IToManyHandler<TParent, TParsed, TItem> where TParser : ISchemaParser<TParsed>
        => ToManyPopulator<TParent, TItem>.Init<TParser, TParsed, THandler>(cmd, parser, toManyHandler, parents, disposeCommand);
}
/// <summary></summary>
public static class ToManyRelation<TItem> {
    /// <summary></summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Init<TParent, TParser, TID>(DbCommand cmd, TParser parser, Func<TParent, TID> GetID, Action<TParent, PooledArray<TItem>> SetCol, IEnumerable<TParent> parents, bool disposeCommand) where TParser : ISchemaParser<DBPair<TID, TItem>>
        => ToManyPopulator<TParent, TItem>.Init<TParser, DBPair<TID, TItem>, HandleUsingOneID<TParent, TID, TItem>>(cmd, parser, new(GetID, SetCol), parents, disposeCommand);
    /// <summary></summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Init<TParent, TParser, TParentID, TTransient, TTransientID>(DbCommand cmd, TParser parser, Func<TParent, TParentID> GetParentID, Func<TParent, IEnumerable<TTransient>> GetTransient, Func<TTransient, TTransientID> GetTransientID, Action<TTransient, PooledArray<TItem>> SetCol, IEnumerable<TParent> parents, bool disposeCommand) where TParser : ISchemaParser<DBTrio<TParentID, TTransientID, TItem>>
        => ToManyPopulator<TParent, TItem>.Init<TParser, DBTrio<TParentID, TTransientID, TItem>, UsingOneIDDepthTwo<TParent, TParentID, TTransient, TTransientID, TItem>>(cmd, parser, new(GetParentID, GetTransient, GetTransientID, SetCol), parents, disposeCommand);
}

/// <summary></summary>
public struct HandleUsingOneID<TParent, TID, TItem>(Func<TParent, TID> GetID, Action<TParent, PooledArray<TItem>> SetCol) : IToManyHandler<TParent, DBPair<TID, TItem>, TItem> {
    /// <summary></summary>
    public Func<TParent, TID> GetID = GetID;
    /// <summary></summary>
    public Action<TParent, PooledArray<TItem>> SetCol = SetCol;
    /// <inheritdoc/>
    public readonly bool Handle<TParser>(IEnumerable<TParent> parents, DbDataReader reader, TParser parser, PooledArray<TItem> sharedArray) where TParser : ISchemaParser<DBPair<TID, TItem>> {
        var comparer = EqualityComparer<TID>.Default;
        bool hasData = reader.Read();
        var currentPair = hasData ? parser.Parse(reader) : default;

        foreach (var parent in parents) {
            TID parentId = GetID(parent);
            while (hasData && comparer.Equals(currentPair.ID, parentId)) {
                sharedArray.Add(currentPair.Object);

                hasData = reader.Read();
                if (hasData) {
                    currentPair = parser.Parse(reader);
                }
            }
            SetCol(parent, sharedArray);
            sharedArray.Clear();
        }

        return hasData;
    }
}
/// <summary></summary>
public struct UsingOneIDDepthTwo<TParent, TParentID, TTransient, TTransientID, TItem>(Func<TParent, TParentID> GetParentID, Func<TParent, IEnumerable<TTransient>> GetTransient, Func<TTransient, TTransientID> GetTransientID, Action<TTransient, PooledArray<TItem>> SetCol) : IToManyHandler<TParent, DBTrio<TParentID, TTransientID, TItem>, TItem> {
    /// <summary></summary>
    public Func<TParent, TParentID> GetParentID = GetParentID;
    /// <summary></summary>
    public Func<TParent, IEnumerable<TTransient>> GetTransient = GetTransient;
    /// <summary></summary>
    public Func<TTransient, TTransientID> GetTransientID = GetTransientID;
    /// <summary></summary>
    public Action<TTransient, PooledArray<TItem>> SetCol = SetCol;
    /// <inheritdoc/>
    public readonly bool Handle<TParser>(IEnumerable<TParent> parents, DbDataReader reader, TParser parser, PooledArray<TItem> sharedArray) where TParser : ISchemaParser<DBTrio<TParentID, TTransientID, TItem>> {
        var comparer1 = EqualityComparer<TParentID>.Default;
        var comparer2 = EqualityComparer<TTransientID>.Default;
        bool hasData = reader.Read();
        var currentTrio = hasData ? parser.Parse(reader) : default;

        foreach (var parent in parents) {
            TParentID parentId = GetParentID(parent);
            var transients = GetTransient(parent);
            foreach (var transient in transients) {
                TTransientID transientID = GetTransientID(transient);
                while (hasData && comparer1.Equals(currentTrio.ID1, parentId) 
                    && comparer2.Equals(currentTrio.ID2, transientID)) {
                    sharedArray.Add(currentTrio.Object);
                    hasData = reader.Read();
                    if (hasData) {
                        currentTrio = parser.Parse(reader);
                    }
                }
                SetCol(transient, sharedArray);
                sharedArray.Clear();
            }
        }

        return hasData;
    }
}