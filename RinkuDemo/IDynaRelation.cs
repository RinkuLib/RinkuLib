using System.Data;
using System.Data.Common;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using RinkuLib.Commands;
using RinkuLib.DBActions;
using RinkuLib.DbParsing;
using RinkuLib.Queries;
using RinkuLib.Tools;

namespace RinkuDemo;
public interface IDynaRelation {
    public ValueTask HandleAsync<TParserGetter>(DynaListWrapper parents, TParserGetter parserGetter, CancellationToken ct = default) where TParserGetter : IParserGetter;
    public ValueTask HandleAsync<TParserGetter>(DynaWrapper parent, TParserGetter parserGetter, CancellationToken ct = default) where TParserGetter : IParserGetter;
    public ValueTask InitHandleAsync(DynaListWrapper parents, DbDataReader reader, QueryCommand query, object?[] variables, DbCommand cmd, CancellationToken ct = default);
    public ValueTask InitHandleAsync(DynaWrapper parent, DbDataReader reader, QueryCommand query, object?[] variables, DbCommand cmd, CancellationToken ct = default);
}
public struct Pair(string Key, object? Value) {
    public string Key = Key;
    public object? Value = Value;
}
[JsonConverter(typeof(DynaWrapperConverter))]
public sealed class DynaWrapper(DynaObject item, List<Pair> additionalProps) {
    public DynaObject Item = item;
    public List<Pair> AdditionalProps = additionalProps;
    public ref object? GetOrAdd(string key) {
        var span = CollectionsMarshal.AsSpan(AdditionalProps);
        for (int i = 0; i < span.Length; i++)
            if (span[i].Key == key)
                return ref span[i].Value;
        AdditionalProps.Add(new(key, null));
        return ref CollectionsMarshal.AsSpan(AdditionalProps)[^1].Value;
    }
}
public sealed class DynaWrapperConverter : JsonConverter<DynaWrapper> {
    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, DynaWrapper value, JsonSerializerOptions options) {
        writer.WriteStartObject();
        value.Item.WriteJsonProperties(writer, options!);
        for (int j = 0; j < value.AdditionalProps.Count; j++) {
            var kvp = value.AdditionalProps[j];
            writer.WritePropertyName(kvp.Key);
            JsonSerializer.Serialize(writer, kvp.Value, options);
        }
        writer.WriteEndObject();
    }

    /// <inheritdoc/>
    public override DynaWrapper Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => throw new NotSupportedException();
}

[JsonConverter(typeof(DynaCollectionConverter<DynaListWrapper, List<DynaObject>>))]
public sealed class DynaListWrapper(List<DynaObject> items, List<KeyValuePair<string, object[]>> additionalProps = null!) : DynaCollectionWrapper<List<DynaObject>>(items, additionalProps ?? []) {
    public override DynaObject this[int index] => Items[index];
}
[JsonConverter(typeof(DynaCollectionConverter<DynaArrayWrapper, DynaObject[]>))]
public sealed class DynaArrayWrapper(DynaObject[] items, List<KeyValuePair<string, object[]>> additionalProps = null!) : DynaCollectionWrapper<DynaObject[]>(items, additionalProps ?? []) {
    public override DynaObject this[int index] => Items[index];
}

public abstract class DynaCollectionWrapper<T>(T items, List<KeyValuePair<string, object[]>> additionalProps) where T : ICollection<DynaObject> {
    public T Items = items;
    public List<KeyValuePair<string, object[]>> AdditionalProps = additionalProps; 
    public object[] GetOrAdd(string key) {
        var span = CollectionsMarshal.AsSpan(AdditionalProps);
        for (int i = 0; i < span.Length; i++)
            if (span[i].Key == key)
                return span[i].Value;
        var newValue = new object[Items.Count];
        AdditionalProps.Add(new(key, newValue));
        return newValue;
    }
    public abstract DynaObject this[int index] { get; }
}
/// <summary></summary>
public class DynaCollectionConverter<TWrapper, T> : JsonConverter<TWrapper> where TWrapper : DynaCollectionWrapper<T> where T : ICollection<DynaObject> {
    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, TWrapper value, JsonSerializerOptions options) {
        writer.WriteStartArray();
        for (int i = 0; i < value.Items.Count; i++) {
            writer.WriteStartObject();
            value[i].WriteJsonProperties(writer, options!);
            for (int j = 0; j < value.AdditionalProps.Count; j++) {
                var kvp = value.AdditionalProps[j];
                writer.WritePropertyName(kvp.Key);
                JsonSerializer.Serialize(writer, kvp.Value[i], options);
            }
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    /// <inheritdoc/>
    public override TWrapper Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => throw new NotSupportedException();
}
/// <summary></summary>
public class DynaRelation<TID>(string PropName, string CompareColumn) : IDynaRelation {
    public readonly string PropName = PropName;
    public readonly string CompareColumn = CompareColumn;
    public async ValueTask InitHandleAsync(DynaListWrapper parents, DbDataReader reader, QueryCommand query, object?[] variables, DbCommand cmd, CancellationToken ct = default) {
        if (query.TryGetCache<DBPair<TID, DynaObject>>(variables, out var parser)) { }
        else if (parser.parser is not null)
            query.UpdateCache(cmd);
        else 
            parser = QueryCommandUsingObjectParam.UpdateCache<DBPair<TID, DynaObject>>(query, reader.GetColumns(), variables.ToBoolArray());
        await HandleAsync(parents, reader, parser, ct).ConfigureAwait(false);
    }
    /// <inheritdoc/>
    public ValueTask HandleAsync<TParserGetter>(DynaListWrapper parents, TParserGetter parserGetter, CancellationToken ct = default) where TParserGetter : IParserGetter {
        var reader = parserGetter.GetParserAndReader<DBPair<TID, DynaObject>>(CommandBehavior.SingleResult, out var parser);
        return HandleAsync(parents, reader, parser, ct);
    }
    public async ValueTask HandleAsync(DynaListWrapper parents, DbDataReader reader, SchemaParser<DBPair<TID, DynaObject>> parser, CancellationToken ct) {
        using (reader) {
            var comparer = EqualityComparer<TID>.Default;
            using PooledArray<DynaObject> sharedArray = new(0);
            bool hasData = await reader.ReadAsync(ct).ConfigureAwait(false);
            var currentPair = hasData ? parser.Parse(reader) : default;
            var count = parents.Items.Count;
            var items = parents.GetOrAdd(PropName);
            for (int i = 0; i < count; i++) {
                var parentId = parents.Items[i].Get<TID>(CompareColumn);
                while (hasData && comparer.Equals(currentPair.ID, parentId)) {
                    sharedArray.Add(currentPair.Object);
                    hasData = await reader.ReadAsync(ct).ConfigureAwait(false);
                    if (hasData) {
                        currentPair = parser.Parse(reader);
                    }
                }
                items[i] = sharedArray.ToArray();
                sharedArray.Clear();
            }
            if (hasData)
                throw new Exception("Should have consumed all the rows");
        }
    }

    public async ValueTask InitHandleAsync(DynaWrapper parent, DbDataReader reader, QueryCommand query, object?[] variables, DbCommand cmd, CancellationToken ct = default) {
        if (query.TryGetCache<DBPair<TID, DynaObject>>(variables, out var parser)) { }
        else if (parser.parser is not null)
            query.UpdateCache(cmd);
        else
            parser = QueryCommandUsingObjectParam.UpdateCache<DBPair<TID, DynaObject>>(query, reader.GetColumns(), variables.ToBoolArray());
        await HandleAsync(parent, reader, parser, ct).ConfigureAwait(false);
    }
    /// <inheritdoc/>
    public async ValueTask HandleAsync<TParserGetter>(DynaWrapper parent, TParserGetter parserGetter, CancellationToken ct = default) where TParserGetter : IParserGetter {
        var reader = parserGetter.GetParserAndReader<DBPair<TID, DynaObject>>(CommandBehavior.SingleResult, out var parser);
        await HandleAsync(parent, reader, parser, ct).ConfigureAwait(false);
    }

    public async ValueTask HandleAsync(DynaWrapper parent, DbDataReader reader, SchemaParser<DBPair<TID, DynaObject>> parser, CancellationToken ct) {
        using (reader) {
            var comparer = EqualityComparer<TID>.Default;
            using PooledArray<DynaObject> sharedArray = new(0);
            bool hasData = await reader.ReadAsync(ct).ConfigureAwait(false);
            var currentPair = hasData ? parser.Parse(reader) : default;

            var parentId = parent.Item.Get<TID>(CompareColumn);
            while (hasData && comparer.Equals(currentPair.ID, parentId)) {
                sharedArray.Add(currentPair.Object);
                hasData = await reader.ReadAsync(ct).ConfigureAwait(false);
                if (hasData) {
                    currentPair = parser.Parse(reader);
                }
            }
            if (hasData)
                throw new Exception("Should have consumed all the rows");
            parent.GetOrAdd(PropName) = sharedArray.ToArray();
        }
    }
}

/// <summary></summary>
public class DynaRelationTwoLevel<TParentID, TTransientID>(string ParentPropName, string ParentIDColumn, string TransientProp, string TransientIDColumn) : IDynaRelation {
    public readonly string ParentPropName = ParentPropName;
    public readonly string ParentIDColumn = ParentIDColumn;
    public readonly string TransientProp = TransientProp;
    public readonly string TransientIDColumn = TransientIDColumn;
    public async ValueTask InitHandleAsync(DynaListWrapper parents, DbDataReader reader, QueryCommand query, object?[] variables, DbCommand cmd, CancellationToken ct = default) {
        if (query.TryGetCache<DBTrio<TParentID, TTransientID, DynaObject>>(variables, out var parser)) { }
        else if (parser.parser is not null)
            query.UpdateCache(cmd);
        else
            parser = QueryCommandUsingObjectParam.UpdateCache<DBTrio<TParentID, TTransientID, DynaObject>>(query, reader.GetColumns(), variables.ToBoolArray());
        await HandleAsync(parents, reader, parser, ct).ConfigureAwait(false);
    }
    /// <inheritdoc/>
    public ValueTask HandleAsync<TParserGetter>(DynaListWrapper parents, TParserGetter parserGetter, CancellationToken ct = default) where TParserGetter : IParserGetter {
        var reader = parserGetter.GetParserAndReader<DBTrio<TParentID, TTransientID, DynaObject>>(CommandBehavior.SingleResult, out var parser);
        return HandleAsync(parents, reader, parser, ct);
    }
    public async ValueTask HandleAsync(DynaListWrapper parents, DbDataReader reader, SchemaParser<DBTrio<TParentID, TTransientID, DynaObject>> parser, CancellationToken ct) {
        using (reader) {
            var comparerParent = EqualityComparer<TParentID>.Default;
            var comparerTransient = EqualityComparer<TTransientID>.Default;
            using PooledArray<DynaObject> sharedArray = new(0);
            bool hasData = await reader.ReadAsync(ct).ConfigureAwait(false);
            var currentPair = hasData ? parser.Parse(reader) : default;
            var count = parents.Items.Count;
            var items = parents.GetOrAdd(ParentPropName);
            for (int i = 0; i < count; i++) {
                var parentId = parents.Items[i].Get<TParentID>(ParentIDColumn);
                var (transients, props) = GetTransient(ref items[i]);
                for (int j = 0; j < transients.Length; j++) {
                    var transientId = transients[j].Get<TTransientID>(TransientIDColumn);
                    while (hasData && comparerParent.Equals(currentPair.ID1, parentId)
                            && comparerTransient.Equals(currentPair.ID2, transientId)) {
                        sharedArray.Add(currentPair.Object);
                        hasData = await reader.ReadAsync(ct).ConfigureAwait(false);
                        if (hasData) {
                            currentPair = parser.Parse(reader);
                        }
                    }
                    props[j] = sharedArray.ToArray();
                    sharedArray.Clear();
                }
            }
            if (hasData)
                throw new Exception("Should have consumed all the rows");
        }
    }

    private (DynaObject[], object[]) GetTransient(ref object? item) {
        var tr = item;
        if (tr is not DynaArrayWrapper wrap) {
            wrap = new DynaArrayWrapper(tr as DynaObject[] ?? [], []);
            item = wrap;
        }
        var transients = wrap.Items;
        object[]? additionsParams = null;
        foreach (var prop in wrap.AdditionalProps) {
            if (string.Equals(prop.Key, TransientProp, StringComparison.OrdinalIgnoreCase)) {
                additionsParams = prop.Value;
                break;
            }
        }
        if (additionsParams is null) {
            additionsParams = new object[transients.Length];
            wrap.AdditionalProps.Add(new(TransientProp, additionsParams));
        }
        return (transients, additionsParams);
    }
    public async ValueTask InitHandleAsync(DynaWrapper parent, DbDataReader reader, QueryCommand query, object?[] variables, DbCommand cmd, CancellationToken ct = default) {
        if (query.TryGetCache<DBTrio<TParentID, TTransientID, DynaObject>>(variables, out var parser)) { }
        else if (parser.parser is not null)
            query.UpdateCache(cmd);
        else
            parser = QueryCommandUsingObjectParam.UpdateCache<DBTrio<TParentID, TTransientID, DynaObject>>(query, reader.GetColumns(), variables.ToBoolArray());
        await HandleAsync(parent, reader, parser, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask HandleAsync<TParserGetter>(DynaWrapper parent, TParserGetter parserGetter, CancellationToken ct = default) where TParserGetter : IParserGetter {
        var reader = parserGetter.GetParserAndReader<DBTrio<TParentID, TTransientID, DynaObject>>(CommandBehavior.SingleResult, out var parser);
        await HandleAsync(parent, reader, parser, ct).ConfigureAwait(false);
    }

    public async ValueTask HandleAsync(DynaWrapper parent, DbDataReader reader, SchemaParser<DBTrio<TParentID, TTransientID, DynaObject>> parser, CancellationToken ct) {
        using (reader) {
            var comparerParent = EqualityComparer<TParentID>.Default;
            var comparerTransient = EqualityComparer<TTransientID>.Default;
            using PooledArray<DynaObject> sharedArray = new(0);
            bool hasData = await reader.ReadAsync(ct).ConfigureAwait(false);
            var currentPair = hasData ? parser.Parse(reader) : default;

            var parentId = parent.Item.Get<TParentID>(ParentIDColumn);
            var (transients, props) = GetTransient(ref parent.GetOrAdd(ParentPropName));
            for (int j = 0; j < transients.Length; j++) {
                var transientId = transients[j].Get<TTransientID>(TransientIDColumn);
                while (hasData && comparerParent.Equals(currentPair.ID1, parentId)
                        && comparerTransient.Equals(currentPair.ID2, transientId)) {
                    sharedArray.Add(currentPair.Object);
                    hasData = await reader.ReadAsync(ct).ConfigureAwait(false);
                    if (hasData) {
                        currentPair = parser.Parse(reader);
                    }
                }
                props[j] = sharedArray.ToArray();
                sharedArray.Clear();
            }
            if (hasData)
                throw new Exception("Should have consumed all the rows");
        }
    }
}
