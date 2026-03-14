using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace RinkuLib.Tools;
/// <summary></summary>
public static class Caster {
    /// <summary>A reusable to safely return the value</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static bool TryCast<TFrom, TTo>(TFrom value, [MaybeNullWhen(false)] out TTo val) => Caster<TFrom, TTo>.TryCast(value, out val);
    static Caster() {
        AddTypeParser(v => Convert.ToSByte(v, CultureInfo.InvariantCulture));
        AddTypeParser(v => Convert.ToInt16(v, CultureInfo.InvariantCulture));
        AddTypeParser(v => Convert.ToInt32(v, CultureInfo.InvariantCulture));
        AddTypeParser(v => Convert.ToInt64(v, CultureInfo.InvariantCulture));
        AddTypeParser(v => Convert.ToByte(v, CultureInfo.InvariantCulture));
        AddTypeParser(v => Convert.ToUInt16(v, CultureInfo.InvariantCulture));
        AddTypeParser(v => Convert.ToUInt32(v, CultureInfo.InvariantCulture));
        AddTypeParser(v => Convert.ToUInt64(v, CultureInfo.InvariantCulture));
        AddTypeParser(v => Convert.ToSingle(v, CultureInfo.InvariantCulture));
        AddTypeParser(v => Convert.ToDouble(v, CultureInfo.InvariantCulture));
        AddTypeParser(v => Convert.ToDecimal(v, CultureInfo.InvariantCulture));
        AddTypeParser(v => Convert.ToChar(v, CultureInfo.InvariantCulture));
        AddTypeParser(v => v.ToString() ?? string.Empty);
        AddTypeParser(v => Convert.ToBoolean(v, CultureInfo.InvariantCulture));
        AddTypeParser(v => Convert.ToDateTime(v, CultureInfo.InvariantCulture));
        AddTypeParser(v => DateTimeOffset.Parse(v.ToString()!, CultureInfo.InvariantCulture));
        AddTypeParser(v => TimeSpan.Parse(v.ToString()!, CultureInfo.InvariantCulture));
        AddTypeParser(v => v switch {
            Guid g => g,
            string s => Guid.Parse(s),
            byte[] b => new Guid(b),
            _ => Guid.Parse(v.ToString()!)
        });
    }
    private static readonly Dictionary<Type, object> TypeParsers = [];
    /// <summary>Add a value to parse from object to a type</summary>
    public static void AddTypeParser<T>(Func<object, T> parser) => TypeParsers[typeof(T)] = parser;
    /// <summary>Parse an object value to <typeparamref name="T"/></summary>
    public static T Parse<T>(this object? value) {
        if (value is null || value is DBNull)
            return default!;
        if (value is T t)
            return t;
        var type = typeof(T);
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsEnum) {
            if (value is float || value is double || value is decimal) {
                value = Convert.ChangeType(value, Enum.GetUnderlyingType(type), CultureInfo.InvariantCulture);
            }
            return (T)Enum.ToObject(type, value);
        }
        if (TypeParsers.TryGetValue(type, out object? parser))
            return Unsafe.As<object, Func<object, T>>(ref parser)(value);
        return (T)Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
    }
    internal static IntPtr GetOpPtr(Type from, Type to) {
        TryGetOperator(from, to, out var m);
        return m!.MethodHandle.GetFunctionPointer();
    }
    internal static bool TryGetOperator(Type f, Type t, [NotNullWhen(true)] out MethodInfo? m) {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public;
        m = f.GetMethod("op_Explicit", flags, [f]) ?? f.GetMethod("op_Implicit", flags, [f])
         ?? t.GetMethod("op_Explicit", flags, [f]) ?? t.GetMethod("op_Implicit", flags, [f]);
        return m != null && m.ReturnType == t;
    }
    internal static bool IsNumeric(Type t) {
        var code = Type.GetTypeCode(t);
        switch (code) {
            case TypeCode.SByte:
            case TypeCode.Byte:
            case TypeCode.Int16:
            case TypeCode.UInt16:
            case TypeCode.Int32:
            case TypeCode.UInt32:
            case TypeCode.Int64:
            case TypeCode.UInt64:
            case TypeCode.Single:
            case TypeCode.Double:
            case TypeCode.Decimal:
                return true;
        }

        if (!t.IsValueType || t == typeof(DateTime) || t == typeof(Guid) || t == typeof(TimeSpan))
            return false;

        // Custom INumberBase check
        foreach (var i in t.GetInterfaces()) {
            if (i.IsGenericType &&
                i.GetGenericTypeDefinition() == typeof(INumberBase<>) &&
                i.GetGenericArguments()[0] == t) {
                return true;
            }
        }
        return false;
    }
    internal static bool IsParsable(Type t) => t.IsAssignableTo(typeof(IParsable<>).MakeGenericType(t));
}
/// <summary></summary>
public static class Caster<TFrom, TTo> {
    private static readonly unsafe delegate* managed<TFrom, out TTo, bool> _ptr;
    static unsafe Caster() {
        Type f = typeof(TFrom), t = typeof(TTo);
        Type? uF = Nullable.GetUnderlyingType(f), uT = Nullable.GetUnderlyingType(t);
        Type cF = uF ?? f, cT = uT ?? t;

        if (t.IsAssignableFrom(f)) {
            _ptr = (f.IsValueType && !t.IsValueType) ? &BoxedIdentity : &Identity;
        }
        else if (cF == cT) {
            var bridge = typeof(NullableBridge<>).MakeGenericType(cF);
            var name = uF == null ? nameof(NullableBridge<>.ToNullable) : nameof(NullableBridge<>.FromNullable);
            _ptr = (delegate* managed<TFrom, out TTo, bool>)bridge.GetMethod(name)!.MethodHandle.GetFunctionPointer();
        }
        else if (Caster.IsNumeric(cF) && Caster.IsNumeric(cT)) {
            var bridge = typeof(NumericBridge<,>).MakeGenericType(cF, cT);
            var name = (uF != null, uT != null) switch {
                (false, false) => nameof(NumericBridge<,>.Direct),
                (true, false) => nameof(NumericBridge<,>.FromNull),
                (false, true) => nameof(NumericBridge<,>.ToNull),
                (true, true) => nameof(NumericBridge<,>.BothNull)
            };
            _ptr = (delegate* managed<TFrom, out TTo, bool>)bridge.GetMethod(name)!.MethodHandle.GetFunctionPointer();
        }
        else if (f == typeof(string) && Caster.IsParsable(cT)) {
            var bridgeType = (uT == null) ? typeof(ParseBridge<>) : typeof(NullableParseBridge<>);
            _ptr = (delegate* managed<TFrom, out TTo, bool>)bridgeType
                .MakeGenericType(cT).GetMethod(nameof(ParseBridge<>.Execute))!
                .MethodHandle.GetFunctionPointer();
        }
        else if (Caster.TryGetOperator(cF, cT, out _)) {
            Type bridgeType = (uF != null, uT != null) switch {
                (false, false) => typeof(OpBridgeDirect<,>).MakeGenericType(cF, cT),
                (true, false) => typeof(OpBridgeFromNull<,>).MakeGenericType(cF, cT),
                (false, true) => typeof(OpBridgeToNull<,>).MakeGenericType(cF, cT),
                (true, true) => typeof(OpBridgeBothNull<,>).MakeGenericType(cF, cT)
            };

            _ptr = (delegate* managed<TFrom, out TTo, bool>)bridgeType
                .GetMethod(nameof(OpBridgeDirect<,>.Execute))!
                .MethodHandle.GetFunctionPointer();
        }
        else if (f == typeof(object)) {
            var bridgeType = (uT == null) ? typeof(ObjectBridge<>) : typeof(ObjectNullableBridge<>);
            _ptr = (delegate* managed<TFrom, out TTo, bool>)bridgeType
                .MakeGenericType(cT).GetMethod(nameof(ObjectBridge<>.Execute))!
                .MethodHandle.GetFunctionPointer();
        }
        else if (t == typeof(string)) {
            _ptr = (delegate* managed<TFrom, out TTo, bool>)typeof(ToStringBridge<>)
                .MakeGenericType(f).GetMethod(nameof(ToStringBridge<>.Execute))!
                .MethodHandle.GetFunctionPointer();
        }
        else {
            _ptr = &Fail;
        }
    }

    /// <summary>A reusable to safely return the value</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static unsafe bool TryCast(TFrom value, [MaybeNullWhen(false)] out TTo result) => _ptr(value, out result);

    private static bool Identity(TFrom v, out TTo r) { r = Unsafe.As<TFrom, TTo>(ref v); return true; }
    private static bool BoxedIdentity(TFrom v, out TTo r) { r = (TTo)(object)v!; return true; }
    private static bool Fail(TFrom v, out TTo r) { r = default!; return false; }
}
internal static class NumericBridge<TIn, TOut>
    where TIn : struct, INumberBase<TIn>
    where TOut : struct, INumberBase<TOut> {
    public static bool Direct(TIn v, out TOut r) { r = TOut.CreateTruncating(v); return true; }
    public static bool FromNull(TIn? v, out TOut r) { if (v is TIn vv) { r = TOut.CreateTruncating(vv); return true; } r = default; return false; }
    public static bool ToNull(TIn v, out TOut? r) { r = TOut.CreateTruncating(v); return true; }
    public static bool BothNull(TIn? v, out TOut? r) { r = v is TIn vv ? TOut.CreateTruncating(vv) : null; return true; }
}

internal static class NullableBridge<T> where T : struct {
    public static bool ToNullable(T val, out T? v) { v = val; return true; }
    public static bool FromNullable(T? val, out T v) {
        if (val is T vv) {
            v = vv;
            return true;
        }
        v = default;
        return false;
    }
}
internal static class ObjectBridge<TTo> {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Execute(object input, out TTo? output) {
        if (input is null) { output = default; return true; }
        if (input is TTo direct) { output = direct; return true; }
        if (input is IConvertible conv) {
            try {
                output = Dispatch(conv);
                return true;
            }
            catch { }
        }
        output = default;
        return false;
    }
    internal static TTo Dispatch(IConvertible conv) {
        if (typeof(TTo) == typeof(bool)) { var v = conv.ToBoolean(null); return Unsafe.As<bool, TTo>(ref v); }
        if (typeof(TTo) == typeof(byte)) { var v = conv.ToByte(null); return Unsafe.As<byte, TTo>(ref v); }
        if (typeof(TTo) == typeof(char)) { var v = conv.ToChar(null); return Unsafe.As<char, TTo>(ref v); }
        if (typeof(TTo) == typeof(short)) { var v = conv.ToInt16(null); return Unsafe.As<short, TTo>(ref v); }
        if (typeof(TTo) == typeof(int)) { var v = conv.ToInt32(null); return Unsafe.As<int, TTo>(ref v); }
        if (typeof(TTo) == typeof(long)) { var v = conv.ToInt64(null); return Unsafe.As<long, TTo>(ref v); }
        if (typeof(TTo) == typeof(float)) { var v = conv.ToSingle(null); return Unsafe.As<float, TTo>(ref v); }
        if (typeof(TTo) == typeof(double)) { var v = conv.ToDouble(null); return Unsafe.As<double, TTo>(ref v); }
        if (typeof(TTo) == typeof(decimal)) { var v = conv.ToDecimal(null); return Unsafe.As<decimal, TTo>(ref v); }
        if (typeof(TTo) == typeof(DateTime)) { var v = conv.ToDateTime(null); return Unsafe.As<DateTime, TTo>(ref v); }
        if (typeof(TTo) == typeof(sbyte)) { var v = conv.ToSByte(null); return Unsafe.As<sbyte, TTo>(ref v); }
        if (typeof(TTo) == typeof(ushort)) { var v = conv.ToUInt16(null); return Unsafe.As<ushort, TTo>(ref v); }
        if (typeof(TTo) == typeof(uint)) { var v = conv.ToUInt32(null); return Unsafe.As<uint, TTo>(ref v); }
        if (typeof(TTo) == typeof(ulong)) { var v = conv.ToUInt64(null); return Unsafe.As<ulong, TTo>(ref v); }
        if (typeof(TTo) == typeof(string)) { var v = conv.ToString(null); return Unsafe.As<string, TTo>(ref v); }
        return (TTo)conv.ToType(typeof(TTo), null);
    }
}

internal static class ObjectNullableBridge<TTo> where TTo : struct {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Execute(object input, out TTo? output) {
        if (input is null) { output = null; return true; }
        if (input is TTo direct) { output = direct; return true; }
        if (input is IConvertible conv) {
            try {
                output = ObjectBridge<TTo>.Dispatch(conv);
                return true;
            }
            catch { }
        }
        output = null;
        return false;
    }
}
internal static class ParseBridge<TTo> where TTo : IParsable<TTo> {
    public static bool Execute(string input, out TTo output)
        => TTo.TryParse(input, null, out output!);
}

internal static class NullableParseBridge<TTo> where TTo : IParsable<TTo> {
    public static bool Execute(string input, out TTo? output) {
        if (TTo.TryParse(input, null, out TTo? res)) {
            output = res;
            return true;
        }
        output = default;
        return false;
    }
}

internal static class ToStringBridge<TIn> {
    public static bool Execute(TIn input, out string output) {
        output = input?.ToString()!;
        return output != null;
    }
}
internal static class OpBridgeDirect<TIn, TOut> {
    private static readonly unsafe delegate* managed<TIn, TOut> _op;
    static unsafe OpBridgeDirect() => _op = (delegate* managed<TIn, TOut>)Caster.GetOpPtr(typeof(TIn), typeof(TOut));

    public unsafe static bool Execute(TIn input, out TOut output) {
        output = _op(input);
        return true;
    }
}

// 2. TIn? -> TOut
internal static class OpBridgeFromNull<TIn, TOut> where TIn : struct {
    private static readonly unsafe delegate* managed<TIn, TOut> _op;
    static unsafe OpBridgeFromNull() => _op = (delegate* managed<TIn, TOut>)Caster.GetOpPtr(typeof(TIn), typeof(TOut));

    public unsafe static bool Execute(TIn? input, out TOut output) {
        if (input is TIn inp) {
            output = _op(inp);
            return true;
        }
        output = default!;
        return false;
    }
}

// 3. TIn -> TOut?
internal static class OpBridgeToNull<TIn, TOut> where TOut : struct {
    private static readonly unsafe delegate* managed<TIn, TOut> _op;
    static unsafe OpBridgeToNull() => _op = (delegate* managed<TIn, TOut>)Caster.GetOpPtr(typeof(TIn), typeof(TOut));

    public unsafe static bool Execute(TIn input, out TOut? output) {
        output = _op(input);
        return true;
    }
}

// 4. TIn? -> TOut?
internal static class OpBridgeBothNull<TIn, TOut> where TIn : struct where TOut : struct {
    private static readonly unsafe delegate* managed<TIn, TOut> _op;
    static unsafe OpBridgeBothNull() => _op = (delegate* managed<TIn, TOut>)Caster.GetOpPtr(typeof(TIn), typeof(TOut));

    public unsafe static bool Execute(TIn? input, out TOut? output) {
        if (input is TIn inp) {
            output = _op(inp);
            return true;
        }
        output = null;
        return true;
    }
}
/*
/// <summary></summary>
public static class Caster<TFrom, TTo> {
    private static readonly unsafe delegate* managed<TFrom, out TTo, bool> _castPtr;
    static unsafe Caster() {
        Type fromT = typeof(TFrom);
        Type toT = typeof(TTo);
        Type? uFrom = Nullable.GetUnderlyingType(fromT);
        Type? uTo = Nullable.GetUnderlyingType(toT);

        if (IsNumeric(uFrom ?? fromT) && IsNumeric(uTo ?? toT)) {
            _castPtr = GetBridgePointer(typeof(NumericBridge<,>), uFrom ?? fromT, uTo ?? toT, uFrom != null, uTo != null);
        }
        else if (toT.IsAssignableFrom(fromT))
            _castPtr = (fromT.IsValueType && !toT.IsValueType) ? &BoxedCast : &RawReinterpret;
        else if (uFrom == toT && uFrom is not null) {
            var bridge = typeof(NullableBridge<>).MakeGenericType(toT);
            _castPtr = (delegate* managed<TFrom, out TTo, bool>)bridge.GetMethod(nameof(NullableBridge<>.FromNullable))!.MethodHandle.GetFunctionPointer();
        }
        else if (uTo == fromT && uTo is not null) {
            var bridge = typeof(NullableBridge<>).MakeGenericType(fromT);
            _castPtr = (delegate* managed<TFrom, out TTo, bool>)bridge.GetMethod(nameof(NullableBridge<>.ToNullable))!.MethodHandle.GetFunctionPointer();
        }
        else 
            _castPtr = &ReturnDefault;
    }

    #region Helpers
    private static unsafe delegate* managed<TFrom, out TTo, bool> GetBridgePointer(Type bridgeOpenType, Type? t1, Type t2, bool fromNull, bool toNull, MethodInfo? customMethod = null) {
        Type closed = t1 != null ? bridgeOpenType.MakeGenericType(t1, t2) : bridgeOpenType.MakeGenericType(t2);
    string name = (fromNull, toNull) switch {
        (false, false) => "Direct",
        (true, false) => "FromNullable",
        (false, true) => "ToNullable",
        _ => "BothNullable"
    };

    var method = customMethod ?? closed.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        return (delegate* managed<TFrom, out TTo, bool>) method!.MethodHandle.GetFunctionPointer();
    }
    private static bool IsNumeric(Type t) => t.IsPrimitive || t == typeof(decimal) || t == typeof(Int128) || t == typeof(UInt128);
    private static bool RawReinterpret(TFrom val, out TTo v) { v = Unsafe.As<TFrom, TTo>(ref val); return true; }
    private static bool BoxedCast(TFrom val, out TTo v) { v = (TTo)(object)val!; return true; }
    private static bool ReturnDefault(TFrom val, out TTo v) { v = default!; return false; }
    #endregion

    /// <summary>A reusable to safely return the value</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static unsafe bool TryCast(TFrom value, [MaybeNullWhen(false)] out TTo val) => _castPtr(value, out val);
}
internal static class NumericBridge<TIn, TOut>
    where TIn : struct, INumber<TIn>
    where TOut : struct, INumber<TOut> {
    public static bool Direct(TIn val, out TOut v) { v = TOut.CreateTruncating(val); return true; }

    public static bool FromNullable(TIn? val, out TOut v) { 
        if (val is TIn vv) { v = TOut.CreateTruncating(vv); return true; }
        v = default; return false;
    }

    public static bool ToNullable(TIn val, out TOut? v) { v = TOut.CreateTruncating(val); return true; }

    public static bool BothNullable(TIn? val, out TOut? v) {
        v = val is TIn vv ? TOut.CreateTruncating(vv) : default;
        return true;
    }
}
internal static class NullableBridge<T> where T : struct {
    public static bool ToNullable(T val, out T? v) {
        v = val;
        return true;
    }
    public static bool FromNullable(T? val, out T v) {
        if (val is T vv) {
            v = vv;
            return true;
        }
        v = default;
        return false;
    }
}*/