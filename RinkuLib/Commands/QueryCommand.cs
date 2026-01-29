using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RinkuLib.Commands;
using RinkuLib.DbParsing;
using RinkuLib.Queries;

namespace RinkuLib.Commands;

public abstract class QueryCommand<T>(QueryFactory factory) : QueryCommand(factory) {
    public new static QueryCommand<T> New(string query, bool extractSelects, char variableChar = default) {
        var factory = new QueryFactory(query, extractSelects, variableChar == default ? IQueryCommand.DefaultVariableChar : variableChar, SpecialHandler.SpecialHandlerGetter.PresenceMap);
        var length = factory.NbSelects;
        if (length <= 0)
            throw new Exception($"no select columns were detected in the query, use {nameof(QueryCommand)} instead of {nameof(QueryCommand<>)}");
        if (!extractSelects)
            return new StaticQueryCommand<T>(factory);
        if (length <= 32)
            return new DynamicQueryCommand<T, Masker32, uint>(factory);
        if (length <= 64)
            return new DynamicQueryCommand<T, Masker64, ulong>(factory);
        if (length <= 128)
            return new DynamicQueryCommand<T, Masker128, Int128>(factory);
        if (length <= 256)
            return new DynamicQueryCommand<T, Masker256, Int256>(factory);
        return new DynamicQueryCommand<T, MaskerInfinite, ulong[]>(factory);
    }
    public abstract Func<DbDataReader, T>? GetFuncAndCache(object?[] variables, out CommandBehavior behavior, out ICache? cache);
    public static KeyValuePair<string, Type>[] GetColumns(DbDataReader reader) {
        var schema = reader.GetColumnSchema();
        var columns = new KeyValuePair<string, Type>[schema.Count];
        for (var i = 0; i < columns.Length; i++)
            columns[i] = new(schema[i].ColumnName, schema[i].DataType ?? typeof(object));
        return columns;
    }
}
internal sealed class StaticQueryCommand<T> : QueryCommand<T> {
    private Func<DbDataReader, T>? MethodFunc;
    private CommandBehavior DefaultBehavior;
    internal StaticQueryCommand(QueryFactory factory) : base(factory) {
        MethodFunc = null;
    }
    public override Func<DbDataReader, T>? GetFuncAndCache(object?[] variables, out CommandBehavior behavior, out ICache? cache) {
        behavior = DefaultBehavior;
        cache = Parameters.NeedToCache(variables) ? this : null;
        return MethodFunc;
    }
    public override void UpdateCache<TImp>(DbDataReader reader, IDbCommand cmd, ref Func<DbDataReader, TImp>? parsingFunc) {
        if (parsingFunc is null && typeof(TImp) == typeof(T) 
            && (MethodFunc is not null || TypeParser<T>.TryGetParser(reader.GetColumns(), out DefaultBehavior, out MethodFunc)))
                parsingFunc = Unsafe.As<Func<DbDataReader, T>, Func<DbDataReader, TImp>>(ref MethodFunc);
        base.UpdateCache(reader, cmd, ref parsingFunc);
    }
    public override void UpdateCache<TImp>(DbDataReader reader, IDbCommand cmd, ref Func<DbDataReader, Task<TImp>>? parsingFunc) {
        if (parsingFunc is null && typeof(TImp) == typeof(T) 
            && (MethodFunc is not null || TypeParser<T>.TryGetParser(reader.GetColumns(), out DefaultBehavior, out MethodFunc)))
                parsingFunc = r => Task.FromResult(Unsafe.As<Func<DbDataReader, T>, Func<DbDataReader, TImp>>(ref MethodFunc)(r));
        base.UpdateCache(reader, cmd, ref parsingFunc);
    }
}
public interface IKeyMasker<TMask> {
    public abstract static TMask ToMask(object?[] variables);
    public abstract static bool Equals(TMask k1, TMask k2);
}
internal sealed class DynamicQueryCommand<T, TMasker, TMask>(QueryFactory factory) : QueryCommand<T>(factory) where TMasker : IKeyMasker<TMask> {
    public static readonly Lock SharedLock = new();
    private InternalCache[] Cache = [];
    private TMask[] Keys = [];
    private class InternalCache(QueryCommand QueryCommand) : ICache {
        public QueryCommand QueryCommand = QueryCommand;
        public CommandBehavior Behavior;
        public Func<DbDataReader, T>? Parser;
        public void UpdateCache<TImp>(DbDataReader reader, IDbCommand cmd, ref Func<DbDataReader, TImp>? parsingFunc) {
            if (parsingFunc is null && typeof(TImp) == typeof(T)
                && (Parser is not null || TypeParser<T>.TryGetParser(reader.GetColumns(), out Behavior, out Parser)))
                parsingFunc = Unsafe.As<Func<DbDataReader, T>, Func<DbDataReader, TImp>>(ref Parser);
            QueryCommand.UpdateCache(reader, cmd, ref parsingFunc);
        }
        public void UpdateCache<TImp>(DbDataReader reader, IDbCommand cmd, ref Func<DbDataReader, Task<TImp>>? parsingFunc) {
            if (parsingFunc is null && typeof(TImp) == typeof(T)
                && (Parser is not null || TypeParser<T>.TryGetParser(reader.GetColumns(), out Behavior, out Parser)))
                parsingFunc = r => Task.FromResult(Unsafe.As<Func<DbDataReader, T>, Func<DbDataReader, TImp>>(ref Parser)(r));
            QueryCommand.UpdateCache(reader, cmd, ref parsingFunc);
        }
    }
    public override Func<DbDataReader, T>? GetFuncAndCache(object?[] variables, out CommandBehavior behavior, out ICache? cache) {
        var mask = TMasker.ToMask(variables);
        var keys = Keys;
        for (int i = 0; i < keys.Length; i++)
            if (TMasker.Equals(keys[i], mask)) {
                var item = Cache[i];
                behavior = item.Behavior;
                cache = Parameters.NeedToCache(variables) ? this : null;
                return item.Parser;
            }
        behavior = default;
        lock (SharedLock) {
            Cache = [.. Cache, new InternalCache(this)];
            Keys = [.. Keys, mask];
            cache = Cache[^1];
        }
        return null;
    }
}
public class Masker32 : IKeyMasker<uint> {
    public static uint ToMask(object?[] variables) {
        uint mask = 0U;
        ref object? pVar = ref MemoryMarshal.GetArrayDataReference(variables);
        for (int i = 0; i < variables.Length; i++)
            if (Unsafe.Add(ref pVar, i) is not null)
                mask |= 1U << i;
        return mask;
    }
    public static bool Equals(uint k1, uint k2) => k1 == k2;
}
public class Masker64 : IKeyMasker<ulong> {
    public static ulong ToMask(object?[] variables) {
        ulong mask = 0UL;
        ref object? pVar = ref MemoryMarshal.GetArrayDataReference(variables);
        for (int i = 0; i < variables.Length; i++)
            if (Unsafe.Add(ref pVar, i) is not null)
                mask |= 1UL << i;
        return mask;
    }
    public static bool Equals(ulong k1, ulong k2) => k1 == k2;
}
public class Masker128 : IKeyMasker<Int128> {
    private static readonly Int128 One = (Int128)1;
    public static Int128 ToMask(object?[] variables) {
        Int128 mask = Int128.Zero;
        ref object? pVar = ref MemoryMarshal.GetArrayDataReference(variables);
        for (int i = 0; i < variables.Length; i++)
            if (Unsafe.Add(ref pVar, i) is not null)
                mask |= One << i;
        return mask;
    }
    public static bool Equals(Int128 k1, Int128 k2) => k1 == k2;
}
public readonly struct Int256(Int128 low, Int128 high) {
    public readonly Int128 Low = low;
    public readonly Int128 High = high;
}
public class Masker256 : IKeyMasker<Int256> {
    private static readonly Int128 One = (Int128)1;
    public static Int256 ToMask(object?[] variables) {
        Int128 mask = Int128.Zero;
        ref object? pVar = ref MemoryMarshal.GetArrayDataReference(variables);
        var len = variables.Length - 128;
        for (int i = 0; i < 128; i++)
            if (Unsafe.Add(ref pVar, i) is not null)
                mask |= One << i;
        pVar = Unsafe.Add(ref pVar, 128);
        Int128 mask2 = Int128.Zero;
        for (int i = 0; i < len; i++)
            if (Unsafe.Add(ref pVar, i) is not null)
                mask2 |= One << i;
        return new(mask, mask2);
    }
    public static bool Equals(Int256 k1, Int256 k2) 
        => k1.Low == k2.Low && k1.High == k2.High;
}
public unsafe class MaskerInfinite : IKeyMasker<ulong[]> {
    public static bool Equals(ulong[] k1, ulong[] k2) 
        => k1.AsSpan().SequenceEqual(k2.AsSpan());
    public static ulong[] ToMask(object?[] variables) {
        int arraySize = (variables.Length + 63) >> 6;
        ulong[] data = new ulong[arraySize];

        ref object? pVar = ref MemoryMarshal.GetArrayDataReference(variables);

        // i >> 6 is index, i & 63 is bit position
        for (int i = 0; i < variables.Length; i++)
            if (Unsafe.Add(ref pVar, i) is not null)
                data[i >> 6] |= 1UL << (i & 63);
        return data;
    }
}