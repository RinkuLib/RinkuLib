using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using RinkuLib.Tools;

namespace RinkuLib.DbParsing;
/// <summary>Serializes a <see cref="DynaObject"/> to JSON as a plain object of its columns. Reading back from JSON is not supported.</summary>
public class DynaObjectConverter : JsonConverter<DynaObject> {
    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, DynaObject value, JsonSerializerOptions options) {
        writer.WriteStartObject();
        value.WriteJsonProperties(writer, options);
        writer.WriteEndObject();
    }

    /// <inheritdoc/>
    public override DynaObject Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        throw new NotImplementedException($"{typeof(DynaObject)} may not be built from JSON");
    }
}
/// <summary>
/// A row read without a type to map to, its columns reachable by name or index. Ask for a <c>DynaObject</c>
/// when the shape is not known ahead of time, it reads like a dictionary of the result's columns and
/// serializes to JSON as one. Values keep their column types, read them with <see cref="Get{T}(string)"/>.
/// </summary>
public abstract class DynaObject : IReadOnlyDictionary<string, object?>, IReadOnlyDictionary<string, int> {
    /// <summary>Writes the columns as JSON properties, used by the JSON converter.</summary>
    public abstract void WriteJsonProperties(Utf8JsonWriter writer, JsonSerializerOptions options);
    ///<inheritdoc/>
    internal DynaObject(Mapper mapper, int len) {
        if (mapper.Count != len)
            throw new Exception($"Expect length of {len} but {nameof(mapper)} is of length {mapper.Count}");
        Mapper = mapper;
    }
    /// <summary>The mapper to the fields</summary>
    public readonly Mapper Mapper;
    /// <inheritdoc/>
    public int Count => Mapper.Count;

    /// <summary>
    /// Get the value at the corresponding index
    /// </summary>
    public abstract bool TryGet<T>(int index, [MaybeNullWhen(false)] out T val);
    /// <summary>The miss road of <see cref="TryGet{T}(int, out T)"/>, shared by the generated shapes.</summary>
    private protected static bool Fail<T>([MaybeNullWhen(false)] out T val) {
        val = default;
        return false;
    }
    /// <summary>
    /// Get the value with the corresponding index
    /// </summary>
    public T Get<T>(int index) {
        if (!TryGet<T>(index, out var val))
            throw new Exception($"Unable to get value at index {index} of type {typeof(T)}");
        return val;
    }
    /// <summary>
    /// Get the value with the corresponding key
    /// </summary>
    public T Get<T>(string key) {
        var ind = Mapper.GetIndex(key);
        if (ind < 0)
            throw new KeyNotFoundException(key);
        if (!TryGet<T>(ind, out var val))
            throw new Exception($"Unable to get value for {key} of type {typeof(T)}");
        return val;
    }
    /// <summary>
    /// Get the value with the corresponding key
    /// </summary>
    public T Get<T>(ReadOnlySpan<char> key) {
        var ind = Mapper.GetIndex(key);
        if (ind < 0)
            throw new KeyNotFoundException(key.ToString());
        if (!TryGet<T>(ind, out var val))
            throw new Exception($"Unable to get value for {key} of type {typeof(T)}");
        return val;
    }

    /// <summary>
    /// Set the value at the corresponding index
    /// </summary>
    public abstract bool Set<T>(int index, T value);
    /// <summary>
    /// Get the value with the corresponding key
    /// </summary>
    public bool Set<T>(string key, T value) {
        var ind = Mapper.GetIndex(key);
        if (ind < 0)
            return false;
        return Set(ind, value);
    }
    /// <summary>
    /// Get the value with the corresponding key
    /// </summary>
    public bool Set<T>(ReadOnlySpan<char> key, T value) {
        var ind = Mapper.GetIndex(key);
        if (ind < 0)
            return false;
        return Set(ind, value);
    }

    /// <inheritdoc/>
    int IReadOnlyDictionary<string, int>.this[string key] => Mapper.GetIndex(key);
    /// <inheritdoc/>
    public object? this[int ind] {
        get {
            if (ind < 0 || ind >= Mapper.Count)
                throw new IndexOutOfRangeException();
            TryGet<object?>(ind, out var val);
            return val;
        }
        set {
            if (ind < 0 || ind >= Mapper.Count)
                throw new IndexOutOfRangeException();
            Set(ind, value);
        }
    }
    /// <inheritdoc/>
    public object? this[string key] {
        get {
            var ind = Mapper.GetIndex(key);
            if (ind < 0)
                throw new KeyNotFoundException(key);
            TryGet<object?>(ind, out var val);
            return val;
        }
        set {
            var ind = Mapper.GetIndex(key);
            if (ind < 0)
                throw new KeyNotFoundException(key);
            Set(ind, value);
        }
    }
    /// <inheritdoc/>
    public object? this[ReadOnlySpan<char> key] {
        get {
            var ind = Mapper.GetIndex(key);
            if (ind < 0)
                throw new KeyNotFoundException(key.ToString());
            TryGet<object?>(ind, out var val);
            return val;
        }
        set {
            var ind = Mapper.GetIndex(key);
            if (ind < 0)
                throw new KeyNotFoundException(key.ToString());
            Set(ind, value);
        }
    }
    /// <summary>
    /// Gets the unique keys held by this mapper as a <see cref="ReadOnlySpan{String}"/>.
    /// </summary>
    public ReadOnlySpan<string> Keys => Mapper.Keys;
    /// <inheritdoc/>
    IEnumerable<string> IReadOnlyDictionary<string, int>.Keys => ((IReadOnlyDictionary<string, int>)Mapper).Keys;
    /// <inheritdoc/>
    IEnumerable<string> IReadOnlyDictionary<string, object?>.Keys => ((IReadOnlyDictionary<string, int>)Mapper).Keys;

    /// <inheritdoc/>
    IEnumerable<int> IReadOnlyDictionary<string, int>.Values => Mapper.Values;
    /// <inheritdoc/>
    public IEnumerable<object?> Values { get {
        var count = Mapper.Count;
        for (int i = 0; i < count; i++) {
            TryGet<object?>(i, out var val);
            yield return val;
        }
    } }

    /// <inheritdoc/>
    public bool ContainsKey(string key) => Mapper.GetIndex(key) >= 0;
    /// <inheritdoc/>
    public bool ContainsKey(ReadOnlySpan<char> key) => Mapper.GetIndex(key) >= 0;
    /// <inheritdoc/>
    public bool TryGetValue(string key, [MaybeNullWhen(false)] out int value) => Mapper.TryGetValue(key, out value);

    /// <inheritdoc/>
    public bool TryGetValue(ReadOnlySpan<char> key, [MaybeNullWhen(false)] out object? value) {
        int ind = Mapper.GetIndex(key);
        if (ind < 0) {
            value = default;
            return false;
        }
        return TryGet(ind, out value);
    }
    /// <inheritdoc/>
    public bool TryGetValue<T>(string key, [MaybeNullWhen(false)] out T value) {
        var ind = Mapper.GetIndex(key);
        if (ind < 0) {
            value = default;
            return false;
        }
        return TryGet(ind, out value);
    }
    /// <inheritdoc/>
    public bool TryGetValue<T>(ReadOnlySpan<char> key, [MaybeNullWhen(false)] out T value) {
        int ind = Mapper.GetIndex(key);
        if (ind < 0) {
            value = default;
            return false;
        }
        return TryGet(ind, out value);
    }
    /// <inheritdoc/>
    public bool TryGetValue(string key, [MaybeNullWhen(false)] out object? value) {
        var ind = Mapper.GetIndex(key);
        if (ind < 0) {
            value = default;
            return false;
        }
        return TryGet(ind, out value);
    }
    /// <inheritdoc/>
    IEnumerator<KeyValuePair<string, int>> IEnumerable<KeyValuePair<string, int>>.GetEnumerator() => Mapper.GetEnumerator();
    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() {
        var keys = Mapper.GetKeysArray();
        for (int i = 0; i < keys.Length; i++) {
            TryGet<object?>(i, out var val);
            yield return new(keys[i], val);
        }
    }
    /// <summary>Stores <paramref name="value"/> into <paramref name="field"/> when its type matches, used while filling a typed slot.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    protected static bool TrySet<T, TField>(T value, ref TField? field) {
        if (value is TField v) {
            field = v;
            return true;
        }
        return Caster.TryCast(value, out field);
    }
}
internal class DynaObject<T0>(T0 val0, Mapper mapper) : DynaObject(mapper, 1) {
    private T0? val0 = val0;
    public override bool TryGet<T>(int index, [MaybeNullWhen(false)] out T val) {
        if (index != 0) {
            val = default;
            return false;
        }
        return Caster.TryCast(val0, out val);
    }

    public override bool Set<T>(int index, T value)
        => index == 0 && TrySet(value, ref val0);
    public override void WriteJsonProperties(Utf8JsonWriter writer, JsonSerializerOptions options) {
        writer.WritePropertyName(Mapper.GetKey(0));
        JsonSerializer.Serialize(writer, val0, options);
    }
}
internal class DynaObject<T0, T1>(T0 val0, T1 val1, Mapper mapper) : DynaObject(mapper, 2) {
    private T0? val0 = val0;
    private T1? val1 = val1;
    public override bool TryGet<T>(int index, [MaybeNullWhen(false)] out T val) => index switch {
        0 => Caster.TryCast(val0, out val),
        1 => Caster.TryCast(val1, out val),
        _ => Fail(out val)
    };

    public override bool Set<T>(int index, T value) => index switch {
        0 => TrySet(value, ref val0),
        1 => TrySet(value, ref val1),
        _ => false,
    };
    public override void WriteJsonProperties(Utf8JsonWriter writer, JsonSerializerOptions options) {
        writer.WritePropertyName(Mapper.GetKey(0));
        JsonSerializer.Serialize(writer, val0, options);
        writer.WritePropertyName(Mapper.GetKey(1));
        JsonSerializer.Serialize(writer, val1, options);
    }
}
internal class DynaObject<T0, T1, T2>(T0 val0, T1 val1, T2 val2, Mapper mapper) : DynaObject(mapper, 3) {
    private T0? val0 = val0;
    private T1? val1 = val1;
    private T2? val2 = val2;
    public override bool TryGet<T>(int index, [MaybeNullWhen(false)] out T val) => index switch {
        0 => Caster.TryCast(val0, out val),
        1 => Caster.TryCast(val1, out val),
        2 => Caster.TryCast(val2, out val),
        _ => Fail(out val)
    };

    public override bool Set<T>(int index, T value) => index switch {
        0 => TrySet(value, ref val0),
        1 => TrySet(value, ref val1),
        2 => TrySet(value, ref val2),
        _ => false,
    };
    public override void WriteJsonProperties(Utf8JsonWriter writer, JsonSerializerOptions options) {
        writer.WritePropertyName(Mapper.GetKey(0));
        JsonSerializer.Serialize(writer, val0, options);
        writer.WritePropertyName(Mapper.GetKey(1));
        JsonSerializer.Serialize(writer, val1, options);
        writer.WritePropertyName(Mapper.GetKey(2));
        JsonSerializer.Serialize(writer, val2, options);
    }
}
/// <summary>A <see cref="DynaObject"/> with four typed columns.</summary>
internal class DynaObject<T0, T1, T2, T3>(T0 val0, T1 val1, T2 val2, T3 val3, Mapper mapper) : DynaObject(mapper, 4) {
    private T0? val0 = val0; 
    private T1? val1 = val1; 
    private T2? val2 = val2; 
    private T3? val3 = val3;
    public override bool TryGet<T>(int index, [MaybeNullWhen(false)] out T val) => index switch {
        0 => Caster.TryCast(val0, out val), 
        1 => Caster.TryCast(val1, out val), 
        2 => Caster.TryCast(val2, out val), 
        3 => Caster.TryCast(val3, out val),
        _ => Fail(out val)
    };

    public override bool Set<T>(int index, T value) => index switch {
        0 => TrySet(value, ref val0),
        1 => TrySet(value, ref val1),
        2 => TrySet(value, ref val2),
        3 => TrySet(value, ref val3),
        _ => false,
    };
    public override void WriteJsonProperties(Utf8JsonWriter writer, JsonSerializerOptions options) {
        writer.WritePropertyName(Mapper.GetKey(0));
        JsonSerializer.Serialize(writer, val0, options);
        writer.WritePropertyName(Mapper.GetKey(1));
        JsonSerializer.Serialize(writer, val1, options);
        writer.WritePropertyName(Mapper.GetKey(2));
        JsonSerializer.Serialize(writer, val2, options);
        writer.WritePropertyName(Mapper.GetKey(3));
        JsonSerializer.Serialize(writer, val3, options);
    }
}

internal class DynaObject<T0, T1, T2, T3, T4>(T0 val0, T1 val1, T2 val2, T3 val3, T4 val4, Mapper mapper) : DynaObject(mapper, 5) {
    private T0? val0 = val0; 
    private T1? val1 = val1; 
    private T2? val2 = val2; 
    private T3? val3 = val3; 
    private T4? val4 = val4;
    public override bool TryGet<T>(int index, [MaybeNullWhen(false)] out T val) => index switch {
        0 => Caster.TryCast(val0, out val), 
        1 => Caster.TryCast(val1, out val), 
        2 => Caster.TryCast(val2, out val), 
        3 => Caster.TryCast(val3, out val),
        4 => Caster.TryCast(val4, out val), 
        _ => Fail(out val)
    };

    public override bool Set<T>(int index, T value) => index switch {
        0 => TrySet(value, ref val0),
        1 => TrySet(value, ref val1),
        2 => TrySet(value, ref val2),
        3 => TrySet(value, ref val3),
        4 => TrySet(value, ref val4),
        _ => false,
    };
    public override void WriteJsonProperties(Utf8JsonWriter writer, JsonSerializerOptions options) {
        writer.WritePropertyName(Mapper.GetKey(0));
        JsonSerializer.Serialize(writer, val0, options);
        writer.WritePropertyName(Mapper.GetKey(1));
        JsonSerializer.Serialize(writer, val1, options);
        writer.WritePropertyName(Mapper.GetKey(2));
        JsonSerializer.Serialize(writer, val2, options);
        writer.WritePropertyName(Mapper.GetKey(3));
        JsonSerializer.Serialize(writer, val3, options);
        writer.WritePropertyName(Mapper.GetKey(4));
        JsonSerializer.Serialize(writer, val4, options);
    }
}

internal class DynaObject<T0, T1, T2, T3, T4, T5>(T0 val0, T1 val1, T2 val2, T3 val3, T4 val4, T5 val5, Mapper mapper) : DynaObject(mapper, 6) {
    private T0? val0 = val0;
    private T1? val1 = val1;
    private T2? val2 = val2;
    private T3? val3 = val3;
    private T4? val4 = val4;
    private T5? val5 = val5;
    public override bool TryGet<T>(int index, [MaybeNullWhen(false)] out T val) => index switch {
        0 => Caster.TryCast(val0, out val), 
        1 => Caster.TryCast(val1, out val), 
        2 => Caster.TryCast(val2, out val), 
        3 => Caster.TryCast(val3, out val),
        4 => Caster.TryCast(val4, out val), 
        5 => Caster.TryCast(val5, out val), 
        _ => Fail(out val)
    };

    public override bool Set<T>(int index, T value) => index switch {
        0 => TrySet(value, ref val0),
        1 => TrySet(value, ref val1),
        2 => TrySet(value, ref val2),
        3 => TrySet(value, ref val3),
        4 => TrySet(value, ref val4),
        5 => TrySet(value, ref val5),
        _ => false,
    };
    public override void WriteJsonProperties(Utf8JsonWriter writer, JsonSerializerOptions options) {
        writer.WritePropertyName(Mapper.GetKey(0));
        JsonSerializer.Serialize(writer, val0, options);
        writer.WritePropertyName(Mapper.GetKey(1));
        JsonSerializer.Serialize(writer, val1, options);
        writer.WritePropertyName(Mapper.GetKey(2));
        JsonSerializer.Serialize(writer, val2, options);
        writer.WritePropertyName(Mapper.GetKey(3));
        JsonSerializer.Serialize(writer, val3, options);
        writer.WritePropertyName(Mapper.GetKey(4));
        JsonSerializer.Serialize(writer, val4, options);
        writer.WritePropertyName(Mapper.GetKey(5));
        JsonSerializer.Serialize(writer, val5, options);
    }
}

internal class DynaObject<T0, T1, T2, T3, T4, T5, T6>(T0 val0, T1 val1, T2 val2, T3 val3, T4 val4, T5 val5, T6 val6, Mapper mapper) : DynaObject(mapper, 7) {
    private T0? val0 = val0;
    private T1? val1 = val1;
    private T2? val2 = val2;
    private T3? val3 = val3;
    private T4? val4 = val4;
    private T5? val5 = val5;
    private T6? val6 = val6;
    public override bool TryGet<T>(int index, [MaybeNullWhen(false)] out T val) => index switch {
        0 => Caster.TryCast(val0, out val), 
        1 => Caster.TryCast(val1, out val), 
        2 => Caster.TryCast(val2, out val), 
        3 => Caster.TryCast(val3, out val),
        4 => Caster.TryCast(val4, out val), 
        5 => Caster.TryCast(val5, out val), 
        6 => Caster.TryCast(val6, out val), 
        _ => Fail(out val)
    };

    public override bool Set<T>(int index, T value) => index switch {
        0 => TrySet(value, ref val0),
        1 => TrySet(value, ref val1),
        2 => TrySet(value, ref val2),
        3 => TrySet(value, ref val3),
        4 => TrySet(value, ref val4),
        5 => TrySet(value, ref val5),
        6 => TrySet(value, ref val6),
        _ => false,
    };
    public override void WriteJsonProperties(Utf8JsonWriter writer, JsonSerializerOptions options) {
        writer.WritePropertyName(Mapper.GetKey(0));
        JsonSerializer.Serialize(writer, val0, options);
        writer.WritePropertyName(Mapper.GetKey(1));
        JsonSerializer.Serialize(writer, val1, options);
        writer.WritePropertyName(Mapper.GetKey(2));
        JsonSerializer.Serialize(writer, val2, options);
        writer.WritePropertyName(Mapper.GetKey(3));
        JsonSerializer.Serialize(writer, val3, options);
        writer.WritePropertyName(Mapper.GetKey(4));
        JsonSerializer.Serialize(writer, val4, options);
        writer.WritePropertyName(Mapper.GetKey(5));
        JsonSerializer.Serialize(writer, val5, options);
        writer.WritePropertyName(Mapper.GetKey(6));
        JsonSerializer.Serialize(writer, val6, options);
    }
}

internal class DynaObject<T0, T1, T2, T3, T4, T5, T6, T7>(T0 val0, T1 val1, T2 val2, T3 val3, T4 val4, T5 val5, T6 val6, T7 val7, Mapper mapper) : DynaObject(mapper, 8) {
    private T0? val0 = val0; 
    private T1? val1 = val1;
    private T2? val2 = val2;
    private T3? val3 = val3;
    private T4? val4 = val4;
    private T5? val5 = val5;
    private T6? val6 = val6;
    private T7? val7 = val7;
    public override bool TryGet<T>(int index, [MaybeNullWhen(false)] out T val) => index switch {
        0 => Caster.TryCast(val0, out val), 
        1 => Caster.TryCast(val1, out val), 
        2 => Caster.TryCast(val2, out val), 
        3 => Caster.TryCast(val3, out val),
        4 => Caster.TryCast(val4, out val), 
        5 => Caster.TryCast(val5, out val), 
        6 => Caster.TryCast(val6, out val), 
        7 => Caster.TryCast(val7, out val),
        _ => Fail(out val)
    };

    public override bool Set<T>(int index, T value) => index switch {
        0 => TrySet(value, ref val0),
        1 => TrySet(value, ref val1),
        2 => TrySet(value, ref val2),
        3 => TrySet(value, ref val3),
        4 => TrySet(value, ref val4),
        5 => TrySet(value, ref val5),
        6 => TrySet(value, ref val6),
        7 => TrySet(value, ref val7),
        _ => false,
    };
    public override void WriteJsonProperties(Utf8JsonWriter writer, JsonSerializerOptions options) {
        writer.WritePropertyName(Mapper.GetKey(0));
        JsonSerializer.Serialize(writer, val0, options);
        writer.WritePropertyName(Mapper.GetKey(1));
        JsonSerializer.Serialize(writer, val1, options);
        writer.WritePropertyName(Mapper.GetKey(2));
        JsonSerializer.Serialize(writer, val2, options);
        writer.WritePropertyName(Mapper.GetKey(3));
        JsonSerializer.Serialize(writer, val3, options);
        writer.WritePropertyName(Mapper.GetKey(4));
        JsonSerializer.Serialize(writer, val4, options);
        writer.WritePropertyName(Mapper.GetKey(5));
        JsonSerializer.Serialize(writer, val5, options);
        writer.WritePropertyName(Mapper.GetKey(6));
        JsonSerializer.Serialize(writer, val6, options);
        writer.WritePropertyName(Mapper.GetKey(7));
        JsonSerializer.Serialize(writer, val7, options);
    }
}

internal class DynaObject<T0, T1, T2, T3, T4, T5, T6, T7, T8>(T0 val0, T1 val1, T2 val2, T3 val3, T4 val4, T5 val5, T6 val6, T7 val7, T8 val8, Mapper mapper) : DynaObject(mapper, 9) {
    private T0? val0 = val0;
    private T1? val1 = val1;
    private T2? val2 = val2;
    private T3? val3 = val3;
    private T4? val4 = val4;
    private T5? val5 = val5;
    private T6? val6 = val6; 
    private T7? val7 = val7; 
    private T8? val8 = val8;
    public override bool TryGet<T>(int index, [MaybeNullWhen(false)] out T val) => index switch {
        0 => Caster.TryCast(val0, out val), 
        1 => Caster.TryCast(val1, out val), 
        2 => Caster.TryCast(val2, out val), 
        3 => Caster.TryCast(val3, out val),
        4 => Caster.TryCast(val4, out val), 
        5 => Caster.TryCast(val5, out val), 
        6 => Caster.TryCast(val6, out val), 
        7 => Caster.TryCast(val7, out val),
        8 => Caster.TryCast(val8, out val), 
        _ => Fail(out val)
    };

    public override bool Set<T>(int index, T value) => index switch {
        0 => TrySet(value, ref val0),
        1 => TrySet(value, ref val1),
        2 => TrySet(value, ref val2),
        3 => TrySet(value, ref val3),
        4 => TrySet(value, ref val4),
        5 => TrySet(value, ref val5),
        6 => TrySet(value, ref val6),
        7 => TrySet(value, ref val7),
        8 => TrySet(value, ref val8),
        _ => false,
    };
    public override void WriteJsonProperties(Utf8JsonWriter writer, JsonSerializerOptions options) {
        writer.WritePropertyName(Mapper.GetKey(0));
        JsonSerializer.Serialize(writer, val0, options);
        writer.WritePropertyName(Mapper.GetKey(1));
        JsonSerializer.Serialize(writer, val1, options);
        writer.WritePropertyName(Mapper.GetKey(2));
        JsonSerializer.Serialize(writer, val2, options);
        writer.WritePropertyName(Mapper.GetKey(3));
        JsonSerializer.Serialize(writer, val3, options);
        writer.WritePropertyName(Mapper.GetKey(4));
        JsonSerializer.Serialize(writer, val4, options);
        writer.WritePropertyName(Mapper.GetKey(5));
        JsonSerializer.Serialize(writer, val5, options);
        writer.WritePropertyName(Mapper.GetKey(6));
        JsonSerializer.Serialize(writer, val6, options);
        writer.WritePropertyName(Mapper.GetKey(7));
        JsonSerializer.Serialize(writer, val7, options);
        writer.WritePropertyName(Mapper.GetKey(8));
        JsonSerializer.Serialize(writer, val8, options);
    }
}

internal class DynaObject<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9>(T0 val0, T1 val1, T2 val2, T3 val3, T4 val4, T5 val5, T6 val6, T7 val7, T8 val8, T9 val9, Mapper mapper) : DynaObject(mapper, 10) {
    private T0? val0 = val0;
    private T1? val1 = val1;
    private T2? val2 = val2;
    private T3? val3 = val3;
    private T4? val4 = val4;
    private T5? val5 = val5;
    private T6? val6 = val6;
    private T7? val7 = val7; 
    private T8? val8 = val8; 
    private T9? val9 = val9;
    public override bool TryGet<T>(int index, [MaybeNullWhen(false)] out T val) => index switch {
        0 => Caster.TryCast(val0, out val), 
        1 => Caster.TryCast(val1, out val), 
        2 => Caster.TryCast(val2, out val), 
        3 => Caster.TryCast(val3, out val),
        4 => Caster.TryCast(val4, out val), 
        5 => Caster.TryCast(val5, out val), 
        6 => Caster.TryCast(val6, out val), 
        7 => Caster.TryCast(val7, out val),
        8 => Caster.TryCast(val8, out val), 
        9 => Caster.TryCast(val9, out val), 
        _ => Fail(out val)
    };

    public override bool Set<T>(int index, T value) => index switch {
        0 => TrySet(value, ref val0),
        1 => TrySet(value, ref val1),
        2 => TrySet(value, ref val2),
        3 => TrySet(value, ref val3),
        4 => TrySet(value, ref val4),
        5 => TrySet(value, ref val5),
        6 => TrySet(value, ref val6),
        7 => TrySet(value, ref val7),
        8 => TrySet(value, ref val8),
        9 => TrySet(value, ref val9),
        _ => false,
    };
    public override void WriteJsonProperties(Utf8JsonWriter writer, JsonSerializerOptions options) {
        writer.WritePropertyName(Mapper.GetKey(0));
        JsonSerializer.Serialize(writer, val0, options);
        writer.WritePropertyName(Mapper.GetKey(1));
        JsonSerializer.Serialize(writer, val1, options);
        writer.WritePropertyName(Mapper.GetKey(2));
        JsonSerializer.Serialize(writer, val2, options);
        writer.WritePropertyName(Mapper.GetKey(3));
        JsonSerializer.Serialize(writer, val3, options);
        writer.WritePropertyName(Mapper.GetKey(4));
        JsonSerializer.Serialize(writer, val4, options);
        writer.WritePropertyName(Mapper.GetKey(5));
        JsonSerializer.Serialize(writer, val5, options);
        writer.WritePropertyName(Mapper.GetKey(6));
        JsonSerializer.Serialize(writer, val6, options);
        writer.WritePropertyName(Mapper.GetKey(7));
        JsonSerializer.Serialize(writer, val7, options);
        writer.WritePropertyName(Mapper.GetKey(8));
        JsonSerializer.Serialize(writer, val8, options);
        writer.WritePropertyName(Mapper.GetKey(9));
        JsonSerializer.Serialize(writer, val9, options);
    }
}

internal class DynaObject<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(T0 val0, T1 val1, T2 val2, T3 val3, T4 val4, T5 val5, T6 val6, T7 val7, T8 val8, T9 val9, T10 val10, Mapper mapper) : DynaObject(mapper, 11) {
    private T0? val0 = val0;
    private T1? val1 = val1;
    private T2? val2 = val2;
    private T3? val3 = val3;
    private T4? val4 = val4;
    private T5? val5 = val5;
    private T6? val6 = val6;
    private T7? val7 = val7; 
    private T8? val8 = val8; 
    private T9? val9 = val9; 
    private T10? val10 = val10;
    public override bool TryGet<T>(int index, [MaybeNullWhen(false)] out T val) => index switch {
        0 => Caster.TryCast(val0, out val), 
        1 => Caster.TryCast(val1, out val), 
        2 => Caster.TryCast(val2, out val), 
        3 => Caster.TryCast(val3, out val),
        4 => Caster.TryCast(val4, out val), 
        5 => Caster.TryCast(val5, out val), 
        6 => Caster.TryCast(val6, out val), 
        7 => Caster.TryCast(val7, out val),
        8 => Caster.TryCast(val8, out val), 
        9 => Caster.TryCast(val9, out val), 
        10 => Caster.TryCast(val10, out val), 
        _ => Fail(out val)
    };

    public override bool Set<T>(int index, T value) => index switch {
        0 => TrySet(value, ref val0),
        1 => TrySet(value, ref val1),
        2 => TrySet(value, ref val2),
        3 => TrySet(value, ref val3),
        4 => TrySet(value, ref val4),
        5 => TrySet(value, ref val5),
        6 => TrySet(value, ref val6),
        7 => TrySet(value, ref val7),
        8 => TrySet(value, ref val8),
        9 => TrySet(value, ref val9),
        10 => TrySet(value, ref val10),
        _ => false,
    };
    public override void WriteJsonProperties(Utf8JsonWriter writer, JsonSerializerOptions options) {
        writer.WritePropertyName(Mapper.GetKey(0));
        JsonSerializer.Serialize(writer, val0, options);
        writer.WritePropertyName(Mapper.GetKey(1));
        JsonSerializer.Serialize(writer, val1, options);
        writer.WritePropertyName(Mapper.GetKey(2));
        JsonSerializer.Serialize(writer, val2, options);
        writer.WritePropertyName(Mapper.GetKey(3));
        JsonSerializer.Serialize(writer, val3, options);
        writer.WritePropertyName(Mapper.GetKey(4));
        JsonSerializer.Serialize(writer, val4, options);
        writer.WritePropertyName(Mapper.GetKey(5));
        JsonSerializer.Serialize(writer, val5, options);
        writer.WritePropertyName(Mapper.GetKey(6));
        JsonSerializer.Serialize(writer, val6, options);
        writer.WritePropertyName(Mapper.GetKey(7));
        JsonSerializer.Serialize(writer, val7, options);
        writer.WritePropertyName(Mapper.GetKey(8));
        JsonSerializer.Serialize(writer, val8, options);
        writer.WritePropertyName(Mapper.GetKey(9));
        JsonSerializer.Serialize(writer, val9, options);
        writer.WritePropertyName(Mapper.GetKey(10));
        JsonSerializer.Serialize(writer, val10, options);
    }
}

internal class DynaObject<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(T0 val0, T1 val1, T2 val2, T3 val3, T4 val4, T5 val5, T6 val6, T7 val7, T8 val8, T9 val9, T10 val10, T11 val11, Mapper mapper) : DynaObject(mapper, 12) {
    private T0? val0 = val0;
    private T1? val1 = val1;
    private T2? val2 = val2;
    private T3? val3 = val3;
    private T4? val4 = val4;
    private T5? val5 = val5;
    private T6? val6 = val6;
    private T7? val7 = val7;
    private T8? val8 = val8;
    private T9? val9 = val9;
    private T10? val10 = val10;
    private T11? val11 = val11;
    public override bool TryGet<T>(int index, [MaybeNullWhen(false)] out T val) => index switch {
        0 => Caster.TryCast(val0, out val),
        1 => Caster.TryCast(val1, out val),
        2 => Caster.TryCast(val2, out val),
        3 => Caster.TryCast(val3, out val),
        4 => Caster.TryCast(val4, out val),
        5 => Caster.TryCast(val5, out val),
        6 => Caster.TryCast(val6, out val),
        7 => Caster.TryCast(val7, out val),
        8 => Caster.TryCast(val8, out val),
        9 => Caster.TryCast(val9, out val),
        10 => Caster.TryCast(val10, out val),
        11 => Caster.TryCast(val11, out val),
        _ => Fail(out val)
    };

    public override bool Set<T>(int index, T value) => index switch {
        0 => TrySet(value, ref val0),
        1 => TrySet(value, ref val1),
        2 => TrySet(value, ref val2),
        3 => TrySet(value, ref val3),
        4 => TrySet(value, ref val4),
        5 => TrySet(value, ref val5),
        6 => TrySet(value, ref val6),
        7 => TrySet(value, ref val7),
        8 => TrySet(value, ref val8),
        9 => TrySet(value, ref val9),
        10 => TrySet(value, ref val10),
        11 => TrySet(value, ref val11),
        _ => false,
    };
    public override void WriteJsonProperties(Utf8JsonWriter writer, JsonSerializerOptions options) {
        writer.WritePropertyName(Mapper.GetKey(0));
        JsonSerializer.Serialize(writer, val0, options);
        writer.WritePropertyName(Mapper.GetKey(1));
        JsonSerializer.Serialize(writer, val1, options);
        writer.WritePropertyName(Mapper.GetKey(2));
        JsonSerializer.Serialize(writer, val2, options);
        writer.WritePropertyName(Mapper.GetKey(3));
        JsonSerializer.Serialize(writer, val3, options);
        writer.WritePropertyName(Mapper.GetKey(4));
        JsonSerializer.Serialize(writer, val4, options);
        writer.WritePropertyName(Mapper.GetKey(5));
        JsonSerializer.Serialize(writer, val5, options);
        writer.WritePropertyName(Mapper.GetKey(6));
        JsonSerializer.Serialize(writer, val6, options);
        writer.WritePropertyName(Mapper.GetKey(7));
        JsonSerializer.Serialize(writer, val7, options);
        writer.WritePropertyName(Mapper.GetKey(8));
        JsonSerializer.Serialize(writer, val8, options);
        writer.WritePropertyName(Mapper.GetKey(9));
        JsonSerializer.Serialize(writer, val9, options);
        writer.WritePropertyName(Mapper.GetKey(10));
        JsonSerializer.Serialize(writer, val10, options);
        writer.WritePropertyName(Mapper.GetKey(11));
        JsonSerializer.Serialize(writer, val11, options);
    }
}
internal class DynaObjectInfinite<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(T0 val0, T1 val1, T2 val2, T3 val3, T4 val4, T5 val5, T6 val6, T7 val7, T8 val8, T9 val9, T10 val10, T11 val11, object?[] additionalValues, Mapper mapper) : DynaObject(mapper, 12 + additionalValues.Length) {
    private T0? val0 = val0;
    private T1? val1 = val1;
    private T2? val2 = val2;
    private T3? val3 = val3;
    private T4? val4 = val4;
    private T5? val5 = val5;
    private T6? val6 = val6;
    private T7? val7 = val7;
    private T8? val8 = val8;
    private T9? val9 = val9;
    private T10? val10 = val10;
    private T11? val11 = val11;
    private readonly object?[] additionalValues = additionalValues;
    public override bool TryGet<T>(int index, [MaybeNullWhen(false)] out T val) => index switch {
        0 => Caster.TryCast(val0, out val),
        1 => Caster.TryCast(val1, out val),
        2 => Caster.TryCast(val2, out val),
        3 => Caster.TryCast(val3, out val),
        4 => Caster.TryCast(val4, out val),
        5 => Caster.TryCast(val5, out val),
        6 => Caster.TryCast(val6, out val),
        7 => Caster.TryCast(val7, out val),
        8 => Caster.TryCast(val8, out val),
        9 => Caster.TryCast(val9, out val),
        10 => Caster.TryCast(val10, out val),
        11 => Caster.TryCast(val11, out val),
        _ => (uint)(index - 12) < (uint)additionalValues.Length
            ? Caster.TryCast(additionalValues[index - 12], out val)
            : Fail(out val)
    };

    public override bool Set<T>(int index, T value) => index switch {
        0 => TrySet(value, ref val0),
        1 => TrySet(value, ref val1),
        2 => TrySet(value, ref val2),
        3 => TrySet(value, ref val3),
        4 => TrySet(value, ref val4),
        5 => TrySet(value, ref val5),
        6 => TrySet(value, ref val6),
        7 => TrySet(value, ref val7),
        8 => TrySet(value, ref val8),
        9 => TrySet(value, ref val9),
        10 => TrySet(value, ref val10),
        11 => TrySet(value, ref val11),
        _ => (uint)(index - 12) < (uint)additionalValues.Length && TrySet(value, ref additionalValues[index - 12])
    };
    public override void WriteJsonProperties(Utf8JsonWriter writer, JsonSerializerOptions options) {
        writer.WritePropertyName(Mapper.GetKey(0));
        JsonSerializer.Serialize(writer, val0, options);
        writer.WritePropertyName(Mapper.GetKey(1));
        JsonSerializer.Serialize(writer, val1, options);
        writer.WritePropertyName(Mapper.GetKey(2));
        JsonSerializer.Serialize(writer, val2, options);
        writer.WritePropertyName(Mapper.GetKey(3));
        JsonSerializer.Serialize(writer, val3, options);
        writer.WritePropertyName(Mapper.GetKey(4));
        JsonSerializer.Serialize(writer, val4, options);
        writer.WritePropertyName(Mapper.GetKey(5));
        JsonSerializer.Serialize(writer, val5, options);
        writer.WritePropertyName(Mapper.GetKey(6));
        JsonSerializer.Serialize(writer, val6, options);
        writer.WritePropertyName(Mapper.GetKey(7));
        JsonSerializer.Serialize(writer, val7, options);
        writer.WritePropertyName(Mapper.GetKey(8));
        JsonSerializer.Serialize(writer, val8, options);
        writer.WritePropertyName(Mapper.GetKey(9));
        JsonSerializer.Serialize(writer, val9, options);
        writer.WritePropertyName(Mapper.GetKey(10));
        JsonSerializer.Serialize(writer, val10, options);
        writer.WritePropertyName(Mapper.GetKey(11));
        JsonSerializer.Serialize(writer, val11, options);
        for (int i = 0; i < additionalValues.Length; i++) {
            writer.WritePropertyName(Mapper.GetKey(i + 12));
            JsonSerializer.Serialize(writer, additionalValues[i], options);
        }
    }
}