using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Dynamic;
using System.Runtime.CompilerServices;
using RinkuLib.Tools;

namespace RinkuLib.DbParsing;
/// <summary>
/// Represent an object that let dynamic acces to its members
/// </summary>
public abstract class DynaObject : IReadOnlyDictionary<string, object?>, IReadOnlyDictionary<string, int> {
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
    public abstract T Get<T>(int index);
    /// <summary>
    /// Get the value with the corresponding key
    /// </summary>
    public T Get<T>(string key) {
        var ind = Mapper.GetIndex(key);
        if (ind < 0)
            throw new KeyNotFoundException(key);
        return Get<T>(ind);
    }
    /// <summary>
    /// Get the value with the corresponding key
    /// </summary>
    public T Get<T>(ReadOnlySpan<char> key) {
        var ind = Mapper.GetIndex(key);
        if (ind < 0)
            throw new KeyNotFoundException(key.ToString());
        return Get<T>(ind);
    }

    /// <summary>
    /// Set the value at the corresponding index
    /// </summary>
    public abstract void Set<T>(int index, T value);
    /// <summary>
    /// Get the value with the corresponding key
    /// </summary>
    public void Set<T>(string key, T value) {
        var ind = Mapper.GetIndex(key);
        if (ind < 0)
            throw new KeyNotFoundException(key);
        Set(ind, value);
    }
    /// <summary>
    /// Get the value with the corresponding key
    /// </summary>
    public void Set<T>(ReadOnlySpan<char> key, T value) {
        var ind = Mapper.GetIndex(key);
        if (ind < 0)
            throw new KeyNotFoundException(key.ToString());
        Set(ind, value);
    }

    /// <inheritdoc/>
    int IReadOnlyDictionary<string, int>.this[string key] => Mapper.GetIndex(key);
    /// <inheritdoc/>
    public object? this[int ind] {
        get {
            if (ind < 0 || ind >= Mapper.Count)
                throw new IndexOutOfRangeException();
            return Get<object?>(ind);
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
            return Get<object?>(ind);
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
            return Get<object?>(ind);
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
            yield return Get<object?>(i);
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
        value = Get<object?>(ind);
        return true;
    }
    /// <inheritdoc/>
    public bool TryGetValue<T>(string key, [MaybeNullWhen(false)] out T value) {
        var ind = Mapper.GetIndex(key);
        if (ind < 0) {
            value = default;
            return false;
        }
        value = Get<T>(ind);
        return true;
    }
    /// <inheritdoc/>
    public bool TryGetValue<T>(ReadOnlySpan<char> key, [MaybeNullWhen(false)] out T value) {
        int ind = Mapper.GetIndex(key);
        if (ind < 0) {
            value = default;
            return false;
        }
        value = Get<T>(ind);
        return true;
    }
    /// <inheritdoc/>
    public bool TryGetValue(string key, [MaybeNullWhen(false)] out object? value) {
        var ind = Mapper.GetIndex(key);
        if (ind < 0) {
            value = default;
            return false;
        }
        value = Get<object?>(ind);
        return true;
    }
    /// <inheritdoc/>
    IEnumerator<KeyValuePair<string, int>> IEnumerable<KeyValuePair<string, int>>.GetEnumerator() => Mapper.GetEnumerator();
    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() {
        var keys = Mapper.GetKeysArray();
        for (int i = 0; i < keys.Length; i++)
            yield return new(keys[i], Get<object?>(i));
    }
    /// <summary>A reusable to safely return the value</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static TTo GetValue<TFrom, TTo>(TFrom value) {
        if (typeof(TFrom) == typeof(TTo))
            return Unsafe.As<TFrom, TTo>(ref value);

        if (typeof(TFrom) == typeof(sbyte)) {
            sbyte v = Unsafe.As<TFrom, sbyte>(ref value);
            if (typeof(TTo) == typeof(byte))
                return (TTo)(object)(byte)v;
            if (typeof(TTo) == typeof(short))
                return (TTo)(object)(short)v;
            if (typeof(TTo) == typeof(ushort))
                return (TTo)(object)(ushort)v;
            if (typeof(TTo) == typeof(int))
                return (TTo)(object)(int)v;
            if (typeof(TTo) == typeof(uint))
                return (TTo)(object)(uint)v;
            if (typeof(TTo) == typeof(long))
                return (TTo)(object)(long)v;
            if (typeof(TTo) == typeof(ulong))
                return (TTo)(object)(ulong)v;
            if (typeof(TTo) == typeof(float))
                return (TTo)(object)(float)v;
            if (typeof(TTo) == typeof(double))
                return (TTo)(object)(double)v;
            if (typeof(TTo) == typeof(decimal))
                return (TTo)(object)(decimal)v;
        }
        else if (typeof(TFrom) == typeof(byte)) {
            byte v = Unsafe.As<TFrom, byte>(ref value);
            if (typeof(TTo) == typeof(sbyte))
                return (TTo)(object)(sbyte)v;
            if (typeof(TTo) == typeof(short))
                return (TTo)(object)(short)v;
            if (typeof(TTo) == typeof(ushort))
                return (TTo)(object)(ushort)v;
            if (typeof(TTo) == typeof(int))
                return (TTo)(object)(int)v;
            if (typeof(TTo) == typeof(uint))
                return (TTo)(object)(uint)v;
            if (typeof(TTo) == typeof(long))
                return (TTo)(object)(long)v;
            if (typeof(TTo) == typeof(ulong))
                return (TTo)(object)(ulong)v;
            if (typeof(TTo) == typeof(float))
                return (TTo)(object)(float)v;
            if (typeof(TTo) == typeof(double))
                return (TTo)(object)(double)v;
            if (typeof(TTo) == typeof(decimal))
                return (TTo)(object)(decimal)v;
        }
        else if (typeof(TFrom) == typeof(short)) {
            short v = Unsafe.As<TFrom, short>(ref value);
            if (typeof(TTo) == typeof(sbyte))
                return (TTo)(object)(sbyte)v;
            if (typeof(TTo) == typeof(byte))
                return (TTo)(object)(byte)v;
            if (typeof(TTo) == typeof(ushort))
                return (TTo)(object)(ushort)v;
            if (typeof(TTo) == typeof(int))
                return (TTo)(object)(int)v;
            if (typeof(TTo) == typeof(uint))
                return (TTo)(object)(uint)v;
            if (typeof(TTo) == typeof(long))
                return (TTo)(object)(long)v;
            if (typeof(TTo) == typeof(ulong))
                return (TTo)(object)(ulong)v;
            if (typeof(TTo) == typeof(float))
                return (TTo)(object)(float)v;
            if (typeof(TTo) == typeof(double))
                return (TTo)(object)(double)v;
            if (typeof(TTo) == typeof(decimal))
                return (TTo)(object)(decimal)v;
        }
        else if (typeof(TFrom) == typeof(ushort)) {
            ushort v = Unsafe.As<TFrom, ushort>(ref value);
            if (typeof(TTo) == typeof(sbyte))
                return (TTo)(object)(sbyte)v;
            if (typeof(TTo) == typeof(byte))
                return (TTo)(object)(byte)v;
            if (typeof(TTo) == typeof(short))
                return (TTo)(object)(short)v;
            if (typeof(TTo) == typeof(int))
                return (TTo)(object)(int)v;
            if (typeof(TTo) == typeof(uint))
                return (TTo)(object)(uint)v;
            if (typeof(TTo) == typeof(long))
                return (TTo)(object)(long)v;
            if (typeof(TTo) == typeof(ulong))
                return (TTo)(object)(ulong)v;
            if (typeof(TTo) == typeof(float))
                return (TTo)(object)(float)v;
            if (typeof(TTo) == typeof(double))
                return (TTo)(object)(double)v;
            if (typeof(TTo) == typeof(decimal))
                return (TTo)(object)(decimal)v;
        }
        else if (typeof(TFrom) == typeof(int)) {
            int v = Unsafe.As<TFrom, int>(ref value);
            if (typeof(TTo) == typeof(sbyte))
                return (TTo)(object)(sbyte)v;
            if (typeof(TTo) == typeof(byte))
                return (TTo)(object)(byte)v;
            if (typeof(TTo) == typeof(short))
                return (TTo)(object)(short)v;
            if (typeof(TTo) == typeof(ushort))
                return (TTo)(object)(ushort)v;
            if (typeof(TTo) == typeof(uint))
                return (TTo)(object)(uint)v;
            if (typeof(TTo) == typeof(long))
                return (TTo)(object)(long)v;
            if (typeof(TTo) == typeof(ulong))
                return (TTo)(object)(ulong)v;
            if (typeof(TTo) == typeof(float))
                return (TTo)(object)(float)v;
            if (typeof(TTo) == typeof(double))
                return (TTo)(object)(double)v;
            if (typeof(TTo) == typeof(decimal))
                return (TTo)(object)(decimal)v;
        }
        else if (typeof(TFrom) == typeof(uint)) {
            uint v = Unsafe.As<TFrom, uint>(ref value);
            if (typeof(TTo) == typeof(sbyte))
                return (TTo)(object)(sbyte)v;
            if (typeof(TTo) == typeof(byte))
                return (TTo)(object)(byte)v;
            if (typeof(TTo) == typeof(short))
                return (TTo)(object)(short)v;
            if (typeof(TTo) == typeof(ushort))
                return (TTo)(object)(ushort)v;
            if (typeof(TTo) == typeof(int))
                return (TTo)(object)(int)v;
            if (typeof(TTo) == typeof(long))
                return (TTo)(object)(long)v;
            if (typeof(TTo) == typeof(ulong))
                return (TTo)(object)(ulong)v;
            if (typeof(TTo) == typeof(float))
                return (TTo)(object)(float)v;
            if (typeof(TTo) == typeof(double))
                return (TTo)(object)(double)v;
            if (typeof(TTo) == typeof(decimal))
                return (TTo)(object)(decimal)v;
        }
        else if (typeof(TFrom) == typeof(long)) {
            long v = Unsafe.As<TFrom, long>(ref value);
            if (typeof(TTo) == typeof(sbyte))
                return (TTo)(object)(sbyte)v;
            if (typeof(TTo) == typeof(byte))
                return (TTo)(object)(byte)v;
            if (typeof(TTo) == typeof(short))
                return (TTo)(object)(short)v;
            if (typeof(TTo) == typeof(ushort))
                return (TTo)(object)(ushort)v;
            if (typeof(TTo) == typeof(int))
                return (TTo)(object)(int)v;
            if (typeof(TTo) == typeof(uint))
                return (TTo)(object)(uint)v;
            if (typeof(TTo) == typeof(ulong))
                return (TTo)(object)(ulong)v;
            if (typeof(TTo) == typeof(float))
                return (TTo)(object)(float)v;
            if (typeof(TTo) == typeof(double))
                return (TTo)(object)(double)v;
            if (typeof(TTo) == typeof(decimal))
                return (TTo)(object)(decimal)v;
        }
        else if (typeof(TFrom) == typeof(ulong)) {
            ulong v = Unsafe.As<TFrom, ulong>(ref value);
            if (typeof(TTo) == typeof(sbyte))
                return (TTo)(object)(sbyte)v;
            if (typeof(TTo) == typeof(byte))
                return (TTo)(object)(byte)v;
            if (typeof(TTo) == typeof(short))
                return (TTo)(object)(short)v;
            if (typeof(TTo) == typeof(ushort))
                return (TTo)(object)(ushort)v;
            if (typeof(TTo) == typeof(int))
                return (TTo)(object)(int)v;
            if (typeof(TTo) == typeof(uint))
                return (TTo)(object)(uint)v;
            if (typeof(TTo) == typeof(long))
                return (TTo)(object)(long)v;
            if (typeof(TTo) == typeof(float))
                return (TTo)(object)(float)v;
            if (typeof(TTo) == typeof(double))
                return (TTo)(object)(double)v;
            if (typeof(TTo) == typeof(decimal))
                return (TTo)(object)(decimal)v;
        }
        else if (typeof(TFrom) == typeof(float)) {
            float v = Unsafe.As<TFrom, float>(ref value);
            if (typeof(TTo) == typeof(sbyte))
                return (TTo)(object)(sbyte)v;
            if (typeof(TTo) == typeof(byte))
                return (TTo)(object)(byte)v;
            if (typeof(TTo) == typeof(short))
                return (TTo)(object)(short)v;
            if (typeof(TTo) == typeof(ushort))
                return (TTo)(object)(ushort)v;
            if (typeof(TTo) == typeof(int))
                return (TTo)(object)(int)v;
            if (typeof(TTo) == typeof(uint))
                return (TTo)(object)(uint)v;
            if (typeof(TTo) == typeof(long))
                return (TTo)(object)(long)v;
            if (typeof(TTo) == typeof(ulong))
                return (TTo)(object)(ulong)v;
            if (typeof(TTo) == typeof(double))
                return (TTo)(object)(double)v;
            if (typeof(TTo) == typeof(decimal))
                return (TTo)(object)(decimal)v;
        }
        else if (typeof(TFrom) == typeof(double)) {
            double v = Unsafe.As<TFrom, double>(ref value);
            if (typeof(TTo) == typeof(sbyte))
                return (TTo)(object)(sbyte)v;
            if (typeof(TTo) == typeof(byte))
                return (TTo)(object)(byte)v;
            if (typeof(TTo) == typeof(short))
                return (TTo)(object)(short)v;
            if (typeof(TTo) == typeof(ushort))
                return (TTo)(object)(ushort)v;
            if (typeof(TTo) == typeof(int))
                return (TTo)(object)(int)v;
            if (typeof(TTo) == typeof(uint))
                return (TTo)(object)(uint)v;
            if (typeof(TTo) == typeof(long))
                return (TTo)(object)(long)v;
            if (typeof(TTo) == typeof(ulong))
                return (TTo)(object)(ulong)v;
            if (typeof(TTo) == typeof(float))
                return (TTo)(object)(float)v;
            if (typeof(TTo) == typeof(decimal))
                return (TTo)(object)(decimal)v;
        }
        else if (typeof(TFrom) == typeof(decimal)) {
            decimal v = Unsafe.As<TFrom, decimal>(ref value);
            if (typeof(TTo) == typeof(sbyte))
                return (TTo)(object)(sbyte)v;
            if (typeof(TTo) == typeof(byte))
                return (TTo)(object)(byte)v;
            if (typeof(TTo) == typeof(short))
                return (TTo)(object)(short)v;
            if (typeof(TTo) == typeof(ushort))
                return (TTo)(object)(ushort)v;
            if (typeof(TTo) == typeof(int))
                return (TTo)(object)(int)v;
            if (typeof(TTo) == typeof(uint))
                return (TTo)(object)(uint)v;
            if (typeof(TTo) == typeof(long))
                return (TTo)(object)(long)v;
            if (typeof(TTo) == typeof(ulong))
                return (TTo)(object)(ulong)v;
            if (typeof(TTo) == typeof(float))
                return (TTo)(object)(float)v;
            if (typeof(TTo) == typeof(double))
                return (TTo)(object)(double)v;
        }
        return (TTo)(object)value!;
    }

    /// <summary>A reusable to safely set the value</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static void SetValue<TField, T>(ref TField field, T value) {
        if (typeof(T) == typeof(TField)) {
            field = Unsafe.As<T, TField>(ref value);
            return;
        }
        if (value is TField converted) {
            field = converted;
            return;
        }
        field = GetValue<T, TField>(value);
    }
}
internal class DynaObject<T0>(T0 val0, Mapper mapper) : DynaObject(mapper, 1) {
    private T0 val0 = val0;
    public override T Get<T>(int index) {
        if (index != 0)
            throw new IndexOutOfRangeException();
        return GetValue<T0, T>(val0);
    }

    public override void Set<T>(int index, T value) {
        if (index != 0)
            throw new IndexOutOfRangeException();
        SetValue(ref val0, value);
    }
}
internal class DynaObject<T0, T1>(T0 val0, T1 val1, Mapper mapper) : DynaObject(mapper, 2) {
    private T0 val0 = val0;
    private T1 val1 = val1;
    public override T Get<T>(int index) => index switch {
        0 => GetValue<T0, T>(val0),
        1 => GetValue<T1, T>(val1),
        _ => throw new IndexOutOfRangeException()
    };

    public override void Set<T>(int index, T value) {
        switch (index) {
            case 0:
                SetValue(ref val0, value);
                break;
            case 1:
                SetValue(ref val1, value);
                break;
            default:
                throw new IndexOutOfRangeException();
        }
    }
}
internal class DynaObject<T0, T1, T2>(T0 val0, T1 val1, T2 val2, Mapper mapper) : DynaObject(mapper, 3) {
    private T0 val0 = val0;
    private T1 val1 = val1;
    private T2 val2 = val2;
    public override T Get<T>(int index) => index switch {
        0 => GetValue<T0, T>(val0),
        1 => GetValue<T1, T>(val1),
        2 => GetValue<T2, T>(val2),
        _ => throw new IndexOutOfRangeException()
    };

    public override void Set<T>(int index, T value) {
        switch (index) {
            case 0: SetValue(ref val0, value); break;
            case 1: SetValue(ref val1, value); break;
            case 2: SetValue(ref val2, value); break;
            default: throw new IndexOutOfRangeException();
        }
    }
}
internal class DynaObject<T0, T1, T2, T3>(T0 val0, T1 val1, T2 val2, T3 val3, Mapper mapper) : DynaObject(mapper, 4) {
    private T0 val0 = val0; private T1 val1 = val1; private T2 val2 = val2; private T3 val3 = val3;
    public override T Get<T>(int index) => index switch {
        0 => GetValue<T0, T>(val0), 
        1 => GetValue<T1, T>(val1), 
        2 => GetValue<T2, T>(val2), 
        3 => GetValue<T3, T>(val3),
        _ => throw new IndexOutOfRangeException()
    };
    public override void Set<T>(int index, T value) {
        switch (index) {
            case 0: SetValue(ref val0, value); break; 
            case 1: SetValue(ref val1, value); break;
            case 2: SetValue(ref val2, value); break; 
            case 3: SetValue(ref val3, value); break;
            default: throw new IndexOutOfRangeException();
        }
    }
}

internal class DynaObject<T0, T1, T2, T3, T4>(T0 val0, T1 val1, T2 val2, T3 val3, T4 val4, Mapper mapper) : DynaObject(mapper, 5) {
    private T0 val0 = val0; private T1 val1 = val1; private T2 val2 = val2; private T3 val3 = val3; private T4 val4 = val4;
    public override T Get<T>(int index) => index switch {
        0 => GetValue<T0, T>(val0), 
        1 => GetValue<T1, T>(val1), 
        2 => GetValue<T2, T>(val2), 
        3 => GetValue<T3, T>(val3),
        4 => GetValue<T4, T>(val4), 
        _ => throw new IndexOutOfRangeException()
    };
    public override void Set<T>(int index, T value) {
        switch (index) {
            case 0: SetValue(ref val0, value); break; 
            case 1: SetValue(ref val1, value); break;
            case 2: SetValue(ref val2, value); break; 
            case 3: SetValue(ref val3, value); break;
            case 4: SetValue(ref val4, value); break; 
            default: throw new IndexOutOfRangeException();
        }
    }
}

internal class DynaObject<T0, T1, T2, T3, T4, T5>(T0 val0, T1 val1, T2 val2, T3 val3, T4 val4, T5 val5, Mapper mapper) : DynaObject(mapper, 6) {
    private T0 val0 = val0; private T1 val1 = val1; private T2 val2 = val2; private T3 val3 = val3; private T4 val4 = val4; private T5 val5 = val5;
    public override T Get<T>(int index) => index switch {
        0 => GetValue<T0, T>(val0), 
        1 => GetValue<T1, T>(val1), 
        2 => GetValue<T2, T>(val2), 
        3 => GetValue<T3, T>(val3),
        4 => GetValue<T4, T>(val4), 
        5 => GetValue<T5, T>(val5), 
        _ => throw new IndexOutOfRangeException()
    };
    public override void Set<T>(int index, T value) {
        switch (index) {
            case 0: SetValue(ref val0, value); break; 
            case 1: SetValue(ref val1, value); break;
            case 2: SetValue(ref val2, value); break; 
            case 3: SetValue(ref val3, value); break;
            case 4: SetValue(ref val4, value); break; 
            case 5: SetValue(ref val5, value); break;
            default: throw new IndexOutOfRangeException();
        }
    }
}

internal class DynaObject<T0, T1, T2, T3, T4, T5, T6>(T0 val0, T1 val1, T2 val2, T3 val3, T4 val4, T5 val5, T6 val6, Mapper mapper) : DynaObject(mapper, 7) {
    private T0 val0 = val0; private T1 val1 = val1; private T2 val2 = val2; private T3 val3 = val3; private T4 val4 = val4; private T5 val5 = val5; private T6 val6 = val6;
    public override T Get<T>(int index) => index switch {
        0 => GetValue<T0, T>(val0), 
        1 => GetValue<T1, T>(val1), 
        2 => GetValue<T2, T>(val2), 
        3 => GetValue<T3, T>(val3),
        4 => GetValue<T4, T>(val4), 
        5 => GetValue<T5, T>(val5), 
        6 => GetValue<T6, T>(val6), 
        _ => throw new IndexOutOfRangeException()
    };
    public override void Set<T>(int index, T value) {
        switch (index) {
            case 0: SetValue(ref val0, value); break; 
            case 1: SetValue(ref val1, value); break;
            case 2: SetValue(ref val2, value); break; 
            case 3: SetValue(ref val3, value); break;
            case 4: SetValue(ref val4, value); break; 
            case 5: SetValue(ref val5, value); break;
            case 6: SetValue(ref val6, value); break; 
            default: throw new IndexOutOfRangeException();
        }
    }
}

internal class DynaObject<T0, T1, T2, T3, T4, T5, T6, T7>(T0 val0, T1 val1, T2 val2, T3 val3, T4 val4, T5 val5, T6 val6, T7 val7, Mapper mapper) : DynaObject(mapper, 8) {
    private T0 val0 = val0; private T1 val1 = val1; private T2 val2 = val2; private T3 val3 = val3; private T4 val4 = val4; private T5 val5 = val5; private T6 val6 = val6; private T7 val7 = val7;
    public override T Get<T>(int index) => index switch {
        0 => GetValue<T0, T>(val0), 
        1 => GetValue<T1, T>(val1), 
        2 => GetValue<T2, T>(val2), 
        3 => GetValue<T3, T>(val3),
        4 => GetValue<T4, T>(val4), 
        5 => GetValue<T5, T>(val5), 
        6 => GetValue<T6, T>(val6), 
        7 => GetValue<T7, T>(val7),
        _ => throw new IndexOutOfRangeException()
    };
    public override void Set<T>(int index, T value) {
        switch (index) {
            case 0: SetValue(ref val0, value); break; 
            case 1: SetValue(ref val1, value); break;
            case 2: SetValue(ref val2, value); break; 
            case 3: SetValue(ref val3, value); break;
            case 4: SetValue(ref val4, value); break; 
            case 5: SetValue(ref val5, value); break;
            case 6: SetValue(ref val6, value); break; 
            case 7: SetValue(ref val7, value); break;
            default: throw new IndexOutOfRangeException();
        }
    }
}

internal class DynaObject<T0, T1, T2, T3, T4, T5, T6, T7, T8>(T0 val0, T1 val1, T2 val2, T3 val3, T4 val4, T5 val5, T6 val6, T7 val7, T8 val8, Mapper mapper) : DynaObject(mapper, 9) {
    private T0 val0 = val0; private T1 val1 = val1; private T2 val2 = val2; private T3 val3 = val3; private T4 val4 = val4; private T5 val5 = val5; private T6 val6 = val6; private T7 val7 = val7; private T8 val8 = val8;
    public override T Get<T>(int index) => index switch {
        0 => GetValue<T0, T>(val0), 
        1 => GetValue<T1, T>(val1), 
        2 => GetValue<T2, T>(val2), 
        3 => GetValue<T3, T>(val3),
        4 => GetValue<T4, T>(val4), 
        5 => GetValue<T5, T>(val5), 
        6 => GetValue<T6, T>(val6), 
        7 => GetValue<T7, T>(val7),
        8 => GetValue<T8, T>(val8), 
        _ => throw new IndexOutOfRangeException()
    };
    public override void Set<T>(int index, T value) {
        switch (index) {
            case 0: SetValue(ref val0, value); break; 
            case 1: SetValue(ref val1, value); break;
            case 2: SetValue(ref val2, value); break; 
            case 3: SetValue(ref val3, value); break;
            case 4: SetValue(ref val4, value); break; 
            case 5: SetValue(ref val5, value); break;
            case 6: SetValue(ref val6, value); break; 
            case 7: SetValue(ref val7, value); break;
            case 8: SetValue(ref val8, value); break; 
            default: throw new IndexOutOfRangeException();
        }
    }
}

internal class DynaObject<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9>(T0 val0, T1 val1, T2 val2, T3 val3, T4 val4, T5 val5, T6 val6, T7 val7, T8 val8, T9 val9, Mapper mapper) : DynaObject(mapper, 10) {
    private T0 val0 = val0; private T1 val1 = val1; private T2 val2 = val2; private T3 val3 = val3; private T4 val4 = val4; private T5 val5 = val5; private T6 val6 = val6; private T7 val7 = val7; private T8 val8 = val8; private T9 val9 = val9;
    public override T Get<T>(int index) => index switch {
        0 => GetValue<T0, T>(val0), 
        1 => GetValue<T1, T>(val1), 
        2 => GetValue<T2, T>(val2), 
        3 => GetValue<T3, T>(val3),
        4 => GetValue<T4, T>(val4), 
        5 => GetValue<T5, T>(val5), 
        6 => GetValue<T6, T>(val6), 
        7 => GetValue<T7, T>(val7),
        8 => GetValue<T8, T>(val8), 
        9 => GetValue<T9, T>(val9), 
        _ => throw new IndexOutOfRangeException()
    };
    public override void Set<T>(int index, T value) {
        switch (index) {
            case 0: SetValue(ref val0, value); break; 
            case 1: SetValue(ref val1, value); break;
            case 2: SetValue(ref val2, value); break; 
            case 3: SetValue(ref val3, value); break;
            case 4: SetValue(ref val4, value); break; 
            case 5: SetValue(ref val5, value); break;
            case 6: SetValue(ref val6, value); break; 
            case 7: SetValue(ref val7, value); break;
            case 8: SetValue(ref val8, value); break; 
            case 9: SetValue(ref val9, value); break;
            default: throw new IndexOutOfRangeException();
        }
    }
}

internal class DynaObject<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(T0 val0, T1 val1, T2 val2, T3 val3, T4 val4, T5 val5, T6 val6, T7 val7, T8 val8, T9 val9, T10 val10, Mapper mapper) : DynaObject(mapper, 11) {
    private T0 val0 = val0; private T1 val1 = val1; private T2 val2 = val2; private T3 val3 = val3; private T4 val4 = val4; private T5 val5 = val5; private T6 val6 = val6; private T7 val7 = val7; private T8 val8 = val8; private T9 val9 = val9; private T10 val10 = val10;
    public override T Get<T>(int index) => index switch {
        0 => GetValue<T0, T>(val0), 
        1 => GetValue<T1, T>(val1), 
        2 => GetValue<T2, T>(val2), 
        3 => GetValue<T3, T>(val3),
        4 => GetValue<T4, T>(val4), 
        5 => GetValue<T5, T>(val5), 
        6 => GetValue<T6, T>(val6), 
        7 => GetValue<T7, T>(val7),
        8 => GetValue<T8, T>(val8), 
        9 => GetValue<T9, T>(val9), 
        10 => GetValue<T10, T>(val10), 
        _ => throw new IndexOutOfRangeException()
    };
    public override void Set<T>(int index, T value) {
        switch (index) {
            case 0: SetValue(ref val0, value); break; 
            case 1: SetValue(ref val1, value); break;
            case 2: SetValue(ref val2, value); break; 
            case 3: SetValue(ref val3, value); break;
            case 4: SetValue(ref val4, value); break; 
            case 5: SetValue(ref val5, value); break;
            case 6: SetValue(ref val6, value); break; 
            case 7: SetValue(ref val7, value); break;
            case 8: SetValue(ref val8, value); break; 
            case 9: SetValue(ref val9, value); break;
            case 10: SetValue(ref val10, value); break; 
            default: throw new IndexOutOfRangeException();
        }
    }
}

internal class DynaObject<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(T0 val0, T1 val1, T2 val2, T3 val3, T4 val4, T5 val5, T6 val6, T7 val7, T8 val8, T9 val9, T10 val10, T11 val11, Mapper mapper) : DynaObject(mapper, 12) {
    private T0 val0 = val0; private T1 val1 = val1; private T2 val2 = val2; private T3 val3 = val3; private T4 val4 = val4; private T5 val5 = val5; private T6 val6 = val6; private T7 val7 = val7; private T8 val8 = val8; private T9 val9 = val9; private T10 val10 = val10; private T11 val11 = val11;
    public override T Get<T>(int index) => index switch {
        0 => GetValue<T0, T>(val0), 
        1 => GetValue<T1, T>(val1), 
        2 => GetValue<T2, T>(val2), 
        3 => GetValue<T3, T>(val3),
        4 => GetValue<T4, T>(val4), 
        5 => GetValue<T5, T>(val5), 
        6 => GetValue<T6, T>(val6), 
        7 => GetValue<T7, T>(val7),
        8 => GetValue<T8, T>(val8), 
        9 => GetValue<T9, T>(val9), 
        10 => GetValue<T10, T>(val10), 
        11 => GetValue<T11, T>(val11),
        _ => throw new IndexOutOfRangeException()
    };
    public override void Set<T>(int index, T value) {
        switch (index) {
            case 0: SetValue(ref val0, value); break; 
            case 1: SetValue(ref val1, value); break;
            case 2: SetValue(ref val2, value); break; 
            case 3: SetValue(ref val3, value); break;
            case 4: SetValue(ref val4, value); break; 
            case 5: SetValue(ref val5, value); break;
            case 6: SetValue(ref val6, value); break; 
            case 7: SetValue(ref val7, value); break;
            case 8: SetValue(ref val8, value); break; 
            case 9: SetValue(ref val9, value); break;
            case 10: SetValue(ref val10, value); break; 
            case 11: SetValue(ref val11, value); break;
            default: throw new IndexOutOfRangeException();
        }
    }
}