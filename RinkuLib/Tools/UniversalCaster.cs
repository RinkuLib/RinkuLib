using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace RinkuLib.Tools;
/// <summary></summary>
public static class Caster {
    /// <summary>A reusable to safely return the value</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static bool TryCast<TFrom, TTo>(TFrom value, [MaybeNullWhen(false)] out TTo val) => Caster<TFrom, TTo>.TryCast(value, out val);
}
/// <summary></summary>
public static class Caster<TFrom, TTo> {
    private static readonly unsafe delegate* managed<TFrom, out TTo, bool> _castPtr;
    static unsafe Caster() {
        Type fromT = typeof(TFrom);
        Type toT = typeof(TTo);
        Type? uFrom = Nullable.GetUnderlyingType(fromT);
        Type? uTo = Nullable.GetUnderlyingType(toT);

        if (IsNumeric(uFrom ?? fromT) && IsNumeric(uTo ?? toT)) {
            Type bridgeType = typeof(NumericBridge<,>).MakeGenericType(uFrom ?? fromT, uTo ?? toT);
            string method = (uFrom != null, uTo != null) switch {
                (false, false) => nameof(NumericBridge<,>.Direct),
                (true, false) => nameof(NumericBridge<,>.FromNullable),
                (false, true) => nameof(NumericBridge<,>.ToNullable),
                (true, true) => nameof(NumericBridge<,>.BothNullable)
            };

            _castPtr = (delegate* managed<TFrom, out TTo, bool>)bridgeType.GetMethod(method)!.MethodHandle.GetFunctionPointer();
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
}
/*
/// <summary></summary>
public class UniversalCaster {

    /// <summary>A reusable to safely return the value</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static bool TryCast<TFrom, TTo>(TFrom value, [MaybeNullWhen(false)] out TTo val) {
        if (value is TTo va) {
            val = va;
            return true;
        }
        if (typeof(TFrom).IsPrimitive && typeof(TTo).IsPrimitive
         || (Nullable.GetUnderlyingType(typeof(TTo)) is not null && Nullable.GetUnderlyingType(typeof(TTo))!.IsPrimitive)) {
            val = (TTo)(object)value!;
            return true;
        }
        val = default;
        return false;
    }
    /// <summary>A reusable to safely return the value</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static bool TryCastt<TFrom, TTo>(TFrom value, [MaybeNullWhen(false)] out TTo val) {

        if (typeof(TTo).IsAssignableFrom(typeof(TFrom))) {
            val = Unsafe.As<TFrom, TTo>(ref value);
            return true;
        }
        else if (typeof(TFrom).IsGenericType)
            return NullableCast(value, out val);
        return NotNullableCast(value, out val);
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool NullableCast<TTo, TFrom>(TFrom value, [MaybeNullWhen(false)] out TTo val) {
        if (typeof(TFrom) == typeof(bool?)) return NullableBoolCast(Unsafe.As<TFrom, bool?>(ref value), out val);
        else if (typeof(TFrom) == typeof(sbyte?)) return NullableSByteCast(Unsafe.As<TFrom, sbyte?>(ref value), out val);
        else if (typeof(TFrom) == typeof(byte?)) return NullableByteCast(Unsafe.As<TFrom, byte?>(ref value), out val);
        else if (typeof(TFrom) == typeof(short?)) return NullableShortCast(Unsafe.As<TFrom, short?>(ref value), out val);
        else if (typeof(TFrom) == typeof(ushort?)) return NullableUShortCast(Unsafe.As<TFrom, ushort?>(ref value), out val);
        else if (typeof(TFrom) == typeof(int?)) return NullableIntCast(Unsafe.As<TFrom, int?>(ref value), out val);
        else if (typeof(TFrom) == typeof(uint?)) return NullableUIntCast(Unsafe.As<TFrom, uint?>(ref value), out val);
        else if (typeof(TFrom) == typeof(long?)) return NullableLongCast(Unsafe.As<TFrom, long?>(ref value), out val);
        else if (typeof(TFrom) == typeof(ulong?)) return NullableULongCast(Unsafe.As<TFrom, ulong?>(ref value), out val);
        else if (typeof(TFrom) == typeof(float?)) return NullableFloatCast(Unsafe.As<TFrom, float?>(ref value), out val);
        else if (typeof(TFrom) == typeof(double?)) return NullableDoubleCast(Unsafe.As<TFrom, double?>(ref value), out val);
        else if (typeof(TFrom) == typeof(decimal?)) return NullableDecimalCast(Unsafe.As<TFrom, decimal?>(ref value), out val);
        else if (typeof(TFrom) == typeof(char?)) return NullableCharCast(Unsafe.As<TFrom, char?>(ref value), out val);
        else return TryGetValueFallback(value, out val);
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool NotNullableCast<TTo, TFrom>(TFrom value, [MaybeNullWhen(false)] out TTo val) {
        if (typeof(TFrom) == typeof(bool)) return BoolCast(Unsafe.As<TFrom, bool>(ref value), out val);
        else if (typeof(TFrom) == typeof(sbyte)) return SByteCast(Unsafe.As<TFrom, sbyte>(ref value), out val);
        else if (typeof(TFrom) == typeof(byte)) return ByteCast(Unsafe.As<TFrom, byte>(ref value), out val);
        else if (typeof(TFrom) == typeof(short)) return ShortCast(Unsafe.As<TFrom, short>(ref value), out val);
        else if (typeof(TFrom) == typeof(ushort)) return UShortCast(Unsafe.As<TFrom, ushort>(ref value), out val);
        else if (typeof(TFrom) == typeof(int)) return IntCast(Unsafe.As<TFrom, int>(ref value), out val);
        else if (typeof(TFrom) == typeof(uint)) return UIntCast(Unsafe.As<TFrom, uint>(ref value), out val);
        else if (typeof(TFrom) == typeof(long)) return LongCast(Unsafe.As<TFrom, long>(ref value), out val);
        else if (typeof(TFrom) == typeof(ulong)) return ULongCast(Unsafe.As<TFrom, ulong>(ref value), out val);
        else if (typeof(TFrom) == typeof(float)) return FloatCast(Unsafe.As<TFrom, float>(ref value), out val);
        else if (typeof(TFrom) == typeof(double)) return DoubleCast(Unsafe.As<TFrom, double>(ref value), out val);
        else if (typeof(TFrom) == typeof(decimal)) return DecimalCast(Unsafe.As<TFrom, decimal>(ref value), out val);
        else if (typeof(TFrom) == typeof(char)) return CharCast(Unsafe.As<TFrom, char>(ref value), out val);
        else return TryGetValueFallback(value, out val);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool NullableBoolCast<TTo>(bool? value, [MaybeNullWhen(false)] out TTo val) {
        if (typeof(TTo).IsGenericType)
            return NullableBoolNullCast(value, out val);
        var v = Unsafe.As<bool?, sbyte>(ref value);
        if (typeof(TTo) == typeof(bool)) val = (TTo)(object)value!.Value;
        else if (typeof(TTo) == typeof(sbyte)) val = (TTo)(object)(sbyte)v;
        else if (typeof(TTo) == typeof(byte)) val = (TTo)(object)(byte)v;
        else if (typeof(TTo) == typeof(short)) val = (TTo)(object)(short)v;
        else if (typeof(TTo) == typeof(ushort)) val = (TTo)(object)(ushort)v;
        else if (typeof(TTo) == typeof(int)) val = (TTo)(object)(int)v;
        else if (typeof(TTo) == typeof(uint)) val = (TTo)(object)(uint)v;
        else if (typeof(TTo) == typeof(long)) val = (TTo)(object)(long)v;
        else if (typeof(TTo) == typeof(ulong)) val = (TTo)(object)(ulong)v;
        else if (typeof(TTo) == typeof(float)) val = (TTo)(object)(float)v;
        else if (typeof(TTo) == typeof(double)) val = (TTo)(object)(double)v;
        else if (typeof(TTo) == typeof(decimal)) val = (TTo)(object)(decimal)v;
        else if (typeof(TTo) == typeof(char)) val = (TTo)(object)(char)v;
        else return TryGetValueFallback(value, out val);
        return value.HasValue;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool NullableBoolNullCast<TTo>(bool? value, [MaybeNullWhen(false)] out TTo val) {
        if (typeof(TTo) == typeof(bool?)) { val = (TTo)(object)value!; return true; }
        else if (typeof(TTo) == typeof(sbyte?)) val = (TTo)(object)(sbyte?)(!value.HasValue ? null : (sbyte)(value.Value ? 1 : 0))!;
        else if (typeof(TTo) == typeof(byte?)) val = (TTo)(object)(byte?)(!value.HasValue ? null : (byte)(value.Value ? 1 : 0))!;
        else if (typeof(TTo) == typeof(short?)) val = (TTo)(object)(short?)(!value.HasValue ? null : (short)(value.Value ? 1 : 0))!;
        else if (typeof(TTo) == typeof(ushort?)) val = (TTo)(object)(ushort?)(!value.HasValue ? null : (ushort)(value.Value ? 1 : 0))!;
        else if (typeof(TTo) == typeof(int?)) val = (TTo)(object)(int?)(!value.HasValue ? null : (int)(value.Value ? 1 : 0))!;
        else if (typeof(TTo) == typeof(uint?)) val = (TTo)(object)(uint?)(!value.HasValue ? null : (uint)(value.Value ? 1 : 0))!;
        else if (typeof(TTo) == typeof(long?)) val = (TTo)(object)(long?)(!value.HasValue ? null : (long)(value.Value ? 1 : 0))!;
        else if (typeof(TTo) == typeof(ulong?)) val = (TTo)(object)(ulong?)(!value.HasValue ? null : (ulong)(value.Value ? 1 : 0))!;
        else if (typeof(TTo) == typeof(float?)) val = (TTo)(object)(float?)(!value.HasValue ? null : (float)(value.Value ? 1 : 0))!;
        else if (typeof(TTo) == typeof(double?)) val = (TTo)(object)(double?)(!value.HasValue ? null : (double)(value.Value ? 1 : 0))!;
        else if (typeof(TTo) == typeof(decimal?)) val = (TTo)(object)(decimal?)(!value.HasValue ? null : (decimal)(value.Value ? 1 : 0))!;
        else if (typeof(TTo) == typeof(char?)) val = (TTo)(object)(char?)(!value.HasValue ? null : (char)(value.Value ? 1 : 0))!;
        else return TryGetValueFallback(value, out val);
        return true;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool NullableSByteCast<TTo>(sbyte? value, [MaybeNullWhen(false)] out TTo val) {
        if (typeof(TTo).IsGenericType)
            return NullableSByteNullCast(value, out val);
        var v = Unsafe.As<sbyte?, sbyte>(ref value);
        if (typeof(TTo) == typeof(bool)) val = (TTo)(object)(v != 0);
        else if (typeof(TTo) == typeof(sbyte)) val = (TTo)(object)(sbyte)v;
        else if (typeof(TTo) == typeof(byte)) val = (TTo)(object)(byte)v;
        else if (typeof(TTo) == typeof(short)) val = (TTo)(object)(short)v;
        else if (typeof(TTo) == typeof(ushort)) val = (TTo)(object)(ushort)v;
        else if (typeof(TTo) == typeof(int)) val = (TTo)(object)(int)v;
        else if (typeof(TTo) == typeof(uint)) val = (TTo)(object)(uint)v;
        else if (typeof(TTo) == typeof(long)) val = (TTo)(object)(long)v;
        else if (typeof(TTo) == typeof(ulong)) val = (TTo)(object)(ulong)v;
        else if (typeof(TTo) == typeof(float)) val = (TTo)(object)(float)v;
        else if (typeof(TTo) == typeof(double)) val = (TTo)(object)(double)v;
        else if (typeof(TTo) == typeof(decimal)) val = (TTo)(object)(decimal)v;
        else if (typeof(TTo) == typeof(char)) val = (TTo)(object)(char)v;
        else return TryGetValueFallback(value, out val);
        return value.HasValue;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool NullableSByteNullCast<TTo>(sbyte? value, [MaybeNullWhen(false)] out TTo val) {
        if (typeof(TTo) == typeof(bool?)) val = (TTo)(object)(value.HasValue && value.Value != 0);
        else if (typeof(TTo) == typeof(sbyte?)) val = (TTo)(object)(sbyte?)value!;
        else if (typeof(TTo) == typeof(byte?)) val = (TTo)(object)(byte?)value!;
        else if (typeof(TTo) == typeof(short?)) val = (TTo)(object)(short?)value!;
        else if (typeof(TTo) == typeof(ushort?)) val = (TTo)(object)(ushort?)value!;
        else if (typeof(TTo) == typeof(int?)) val = (TTo)(object)(int?)value!;
        else if (typeof(TTo) == typeof(uint?)) val = (TTo)(object)(uint?)value!;
        else if (typeof(TTo) == typeof(long?)) val = (TTo)(object)(long?)value!;
        else if (typeof(TTo) == typeof(ulong?)) val = (TTo)(object)(ulong?)value!;
        else if (typeof(TTo) == typeof(float?)) val = (TTo)(object)(float?)value!;
        else if (typeof(TTo) == typeof(double?)) val = (TTo)(object)(double?)value!;
        else if (typeof(TTo) == typeof(decimal?)) val = (TTo)(object)(decimal?)value!;
        else if (typeof(TTo) == typeof(char?)) val = (TTo)(object)(char?)value!;
        else return TryGetValueFallback(value, out val);
        return true;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool NullableByteCast<TTo>(byte? value, [MaybeNullWhen(false)] out TTo val) {
        if (typeof(TTo).IsGenericType)
            return NullableByteNullCast(value, out val);
        var v = Unsafe.As<byte?, byte>(ref value);
        if (typeof(TTo) == typeof(bool)) val = (TTo)(object)(v != 0);
        else if (typeof(TTo) == typeof(sbyte)) val = (TTo)(object)(sbyte)v;
        else if (typeof(TTo) == typeof(byte)) val = (TTo)(object)(byte)v;
        else if (typeof(TTo) == typeof(short)) val = (TTo)(object)(short)v;
        else if (typeof(TTo) == typeof(ushort)) val = (TTo)(object)(ushort)v;
        else if (typeof(TTo) == typeof(int)) val = (TTo)(object)(int)v;
        else if (typeof(TTo) == typeof(uint)) val = (TTo)(object)(uint)v;
        else if (typeof(TTo) == typeof(long)) val = (TTo)(object)(long)v;
        else if (typeof(TTo) == typeof(ulong)) val = (TTo)(object)(ulong)v;
        else if (typeof(TTo) == typeof(float)) val = (TTo)(object)(float)v;
        else if (typeof(TTo) == typeof(double)) val = (TTo)(object)(double)v;
        else if (typeof(TTo) == typeof(decimal)) val = (TTo)(object)(decimal)v;
        else if (typeof(TTo) == typeof(char)) val = (TTo)(object)(char)v;
        else return TryGetValueFallback(value, out val);
        return value.HasValue;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool NullableByteNullCast<TTo>(byte? value, [MaybeNullWhen(false)] out TTo val) {
        if (typeof(TTo) == typeof(bool?)) val = (TTo)(object)(value.HasValue && value.Value != 0);
        else if (typeof(TTo) == typeof(sbyte?)) val = (TTo)(object)(sbyte?)value!;
        else if (typeof(TTo) == typeof(byte?)) val = (TTo)(object)(byte?)value!;
        else if (typeof(TTo) == typeof(short?)) val = (TTo)(object)(short?)value!;
        else if (typeof(TTo) == typeof(ushort?)) val = (TTo)(object)(ushort?)value!;
        else if (typeof(TTo) == typeof(int?)) val = (TTo)(object)(int?)value!;
        else if (typeof(TTo) == typeof(uint?)) val = (TTo)(object)(uint?)value!;
        else if (typeof(TTo) == typeof(long?)) val = (TTo)(object)(long?)value!;
        else if (typeof(TTo) == typeof(ulong?)) val = (TTo)(object)(ulong?)value!;
        else if (typeof(TTo) == typeof(float?)) val = (TTo)(object)(float?)value!;
        else if (typeof(TTo) == typeof(double?)) val = (TTo)(object)(double?)value!;
        else if (typeof(TTo) == typeof(decimal?)) val = (TTo)(object)(decimal?)value!;
        else if (typeof(TTo) == typeof(char?)) val = (TTo)(object)(char?)value!;
        else return TryGetValueFallback(value, out val);
        return true;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool NullableShortCast<TTo>(short? value, [MaybeNullWhen(false)] out TTo val) {
        if (typeof(TTo).IsGenericType)
            return NullableShortNullCast(value, out val);
        var v = Unsafe.As<short?, short>(ref value);
        if (typeof(TTo) == typeof(bool)) val = (TTo)(object)(v != 0);
        else if (typeof(TTo) == typeof(sbyte)) val = (TTo)(object)(sbyte)v;
        else if (typeof(TTo) == typeof(byte)) val = (TTo)(object)(byte)v;
        else if (typeof(TTo) == typeof(short)) val = (TTo)(object)(short)v;
        else if (typeof(TTo) == typeof(ushort)) val = (TTo)(object)(ushort)v;
        else if (typeof(TTo) == typeof(int)) val = (TTo)(object)(int)v;
        else if (typeof(TTo) == typeof(uint)) val = (TTo)(object)(uint)v;
        else if (typeof(TTo) == typeof(long)) val = (TTo)(object)(long)v;
        else if (typeof(TTo) == typeof(ulong)) val = (TTo)(object)(ulong)v;
        else if (typeof(TTo) == typeof(float)) val = (TTo)(object)(float)v;
        else if (typeof(TTo) == typeof(double)) val = (TTo)(object)(double)v;
        else if (typeof(TTo) == typeof(decimal)) val = (TTo)(object)(decimal)v;
        else if (typeof(TTo) == typeof(char)) val = (TTo)(object)(char)v;
        else return TryGetValueFallback(value, out val);
        return value.HasValue;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool NullableShortNullCast<TTo>(short? value, [MaybeNullWhen(false)] out TTo val) {
        if (typeof(TTo) == typeof(bool?)) val = (TTo)(object)(value.HasValue && value.Value != 0);
        else if (typeof(TTo) == typeof(sbyte?)) val = (TTo)(object)(sbyte?)value!;
        else if (typeof(TTo) == typeof(byte?)) val = (TTo)(object)(byte?)value!;
        else if (typeof(TTo) == typeof(short?)) val = (TTo)(object)(short?)value!;
        else if (typeof(TTo) == typeof(ushort?)) val = (TTo)(object)(ushort?)value!;
        else if (typeof(TTo) == typeof(int?)) val = (TTo)(object)(int?)value!;
        else if (typeof(TTo) == typeof(uint?)) val = (TTo)(object)(uint?)value!;
        else if (typeof(TTo) == typeof(long?)) val = (TTo)(object)(long?)value!;
        else if (typeof(TTo) == typeof(ulong?)) val = (TTo)(object)(ulong?)value!;
        else if (typeof(TTo) == typeof(float?)) val = (TTo)(object)(float?)value!;
        else if (typeof(TTo) == typeof(double?)) val = (TTo)(object)(double?)value!;
        else if (typeof(TTo) == typeof(decimal?)) val = (TTo)(object)(decimal?)value!;
        else if (typeof(TTo) == typeof(char?)) val = (TTo)(object)(char?)value!;
        else return TryGetValueFallback(value, out val);
        return true;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool NullableUShortCast<TTo>(ushort? value, [MaybeNullWhen(false)] out TTo val) {
        if (typeof(TTo).IsGenericType)
            return NullableUShortNullCast(value, out val);
        var v = Unsafe.As<ushort?, ushort>(ref value);
        if (typeof(TTo) == typeof(bool)) val = (TTo)(object)(v != 0);
        else if (typeof(TTo) == typeof(sbyte)) val = (TTo)(object)(sbyte)v;
        else if (typeof(TTo) == typeof(byte)) val = (TTo)(object)(byte)v;
        else if (typeof(TTo) == typeof(short)) val = (TTo)(object)(short)v;
        else if (typeof(TTo) == typeof(ushort)) val = (TTo)(object)(ushort)v;
        else if (typeof(TTo) == typeof(int)) val = (TTo)(object)(int)v;
        else if (typeof(TTo) == typeof(uint)) val = (TTo)(object)(uint)v;
        else if (typeof(TTo) == typeof(long)) val = (TTo)(object)(long)v;
        else if (typeof(TTo) == typeof(ulong)) val = (TTo)(object)(ulong)v;
        else if (typeof(TTo) == typeof(float)) val = (TTo)(object)(float)v;
        else if (typeof(TTo) == typeof(double)) val = (TTo)(object)(double)v;
        else if (typeof(TTo) == typeof(decimal)) val = (TTo)(object)(decimal)v;
        else if (typeof(TTo) == typeof(char)) val = (TTo)(object)(char)v;
        else return TryGetValueFallback(value, out val);
        return value.HasValue;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool NullableUShortNullCast<TTo>(ushort? value, [MaybeNullWhen(false)] out TTo val) {
        if (typeof(TTo) == typeof(bool?)) val = (TTo)(object)(value.HasValue && value.Value != 0);
        else if (typeof(TTo) == typeof(sbyte?)) val = (TTo)(object)(sbyte?)value!;
        else if (typeof(TTo) == typeof(byte?)) val = (TTo)(object)(byte?)value!;
        else if (typeof(TTo) == typeof(short?)) val = (TTo)(object)(short?)value!;
        else if (typeof(TTo) == typeof(ushort?)) val = (TTo)(object)(ushort?)value!;
        else if (typeof(TTo) == typeof(int?)) val = (TTo)(object)(int?)value!;
        else if (typeof(TTo) == typeof(uint?)) val = (TTo)(object)(uint?)value!;
        else if (typeof(TTo) == typeof(long?)) val = (TTo)(object)(long?)value!;
        else if (typeof(TTo) == typeof(ulong?)) val = (TTo)(object)(ulong?)value!;
        else if (typeof(TTo) == typeof(float?)) val = (TTo)(object)(float?)value!;
        else if (typeof(TTo) == typeof(double?)) val = (TTo)(object)(double?)value!;
        else if (typeof(TTo) == typeof(decimal?)) val = (TTo)(object)(decimal?)value!;
        else if (typeof(TTo) == typeof(char?)) val = (TTo)(object)(char?)value!;
        else return TryGetValueFallback(value, out val);
        return true;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool NullableIntCast<TTo>(int? value, [MaybeNullWhen(false)] out TTo val) {
        if (typeof(TTo).IsGenericType)
            return NullableIntNullCast(value, out val);
        var v = Unsafe.As<int?, int>(ref value);
        if (typeof(TTo) == typeof(bool)) val = (TTo)(object)(v != 0);
        else if (typeof(TTo) == typeof(sbyte)) val = (TTo)(object)(sbyte)v;
        else if (typeof(TTo) == typeof(byte)) val = (TTo)(object)(byte)v;
        else if (typeof(TTo) == typeof(short)) val = (TTo)(object)(short)v;
        else if (typeof(TTo) == typeof(ushort)) val = (TTo)(object)(ushort)v;
        else if (typeof(TTo) == typeof(int)) val = (TTo)(object)(int)v;
        else if (typeof(TTo) == typeof(uint)) val = (TTo)(object)(uint)v;
        else if (typeof(TTo) == typeof(long)) val = (TTo)(object)(long)v;
        else if (typeof(TTo) == typeof(ulong)) val = (TTo)(object)(ulong)v;
        else if (typeof(TTo) == typeof(float)) val = (TTo)(object)(float)v;
        else if (typeof(TTo) == typeof(double)) val = (TTo)(object)(double)v;
        else if (typeof(TTo) == typeof(decimal)) val = (TTo)(object)(decimal)v;
        else if (typeof(TTo) == typeof(char)) val = (TTo)(object)(char)v;
        else return TryGetValueFallback(value, out val);
        return value.HasValue;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool NullableIntNullCast<TTo>(int? value, [MaybeNullWhen(false)] out TTo val) {
        if (typeof(TTo) == typeof(bool?)) val = (TTo)(object)(value.HasValue && value.Value != 0);
        else if (typeof(TTo) == typeof(sbyte?)) val = (TTo)(object)(sbyte?)value!;
        else if (typeof(TTo) == typeof(byte?)) val = (TTo)(object)(byte?)value!;
        else if (typeof(TTo) == typeof(short?)) val = (TTo)(object)(short?)value!;
        else if (typeof(TTo) == typeof(ushort?)) val = (TTo)(object)(ushort?)value!;
        else if (typeof(TTo) == typeof(int?)) val = (TTo)(object)(int?)value!;
        else if (typeof(TTo) == typeof(uint?)) val = (TTo)(object)(uint?)value!;
        else if (typeof(TTo) == typeof(long?)) val = (TTo)(object)(long?)value!;
        else if (typeof(TTo) == typeof(ulong?)) val = (TTo)(object)(ulong?)value!;
        else if (typeof(TTo) == typeof(float?)) val = (TTo)(object)(float?)value!;
        else if (typeof(TTo) == typeof(double?)) val = (TTo)(object)(double?)value!;
        else if (typeof(TTo) == typeof(decimal?)) val = (TTo)(object)(decimal?)value!;
        else if (typeof(TTo) == typeof(char?)) val = (TTo)(object)(char?)value!;
        else return TryGetValueFallback(value, out val);
        return true;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool NullableUIntCast<TTo>(uint? value, [MaybeNullWhen(false)] out TTo val) {
        if (typeof(TTo).IsGenericType)
            return NullableUIntNullCast(value, out val);
        var v = Unsafe.As<uint?, uint>(ref value);
        if (typeof(TTo) == typeof(bool)) val = (TTo)(object)(v != 0);
        else if (typeof(TTo) == typeof(sbyte)) val = (TTo)(object)(sbyte)v;
        else if (typeof(TTo) == typeof(byte)) val = (TTo)(object)(byte)v;
        else if (typeof(TTo) == typeof(short)) val = (TTo)(object)(short)v;
        else if (typeof(TTo) == typeof(ushort)) val = (TTo)(object)(ushort)v;
        else if (typeof(TTo) == typeof(int)) val = (TTo)(object)(int)v;
        else if (typeof(TTo) == typeof(uint)) val = (TTo)(object)(uint)v;
        else if (typeof(TTo) == typeof(long)) val = (TTo)(object)(long)v;
        else if (typeof(TTo) == typeof(ulong)) val = (TTo)(object)(ulong)v;
        else if (typeof(TTo) == typeof(float)) val = (TTo)(object)(float)v;
        else if (typeof(TTo) == typeof(double)) val = (TTo)(object)(double)v;
        else if (typeof(TTo) == typeof(decimal)) val = (TTo)(object)(decimal)v;
        else if (typeof(TTo) == typeof(char)) val = (TTo)(object)(char)v;
        else return TryGetValueFallback(value, out val);
        return value.HasValue;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool NullableUIntNullCast<TTo>(uint? value, [MaybeNullWhen(false)] out TTo val) {
        if (typeof(TTo) == typeof(bool?)) val = (TTo)(object)(value.HasValue && value.Value != 0);
        else if (typeof(TTo) == typeof(sbyte?)) val = (TTo)(object)(sbyte?)value!;
        else if (typeof(TTo) == typeof(byte?)) val = (TTo)(object)(byte?)value!;
        else if (typeof(TTo) == typeof(short?)) val = (TTo)(object)(short?)value!;
        else if (typeof(TTo) == typeof(ushort?)) val = (TTo)(object)(ushort?)value!;
        else if (typeof(TTo) == typeof(int?)) val = (TTo)(object)(int?)value!;
        else if (typeof(TTo) == typeof(uint?)) val = (TTo)(object)(uint?)value!;
        else if (typeof(TTo) == typeof(long?)) val = (TTo)(object)(long?)value!;
        else if (typeof(TTo) == typeof(ulong?)) val = (TTo)(object)(ulong?)value!;
        else if (typeof(TTo) == typeof(float?)) val = (TTo)(object)(float?)value!;
        else if (typeof(TTo) == typeof(double?)) val = (TTo)(object)(double?)value!;
        else if (typeof(TTo) == typeof(decimal?)) val = (TTo)(object)(decimal?)value!;
        else if (typeof(TTo) == typeof(char?)) val = (TTo)(object)(char?)value!;
        else return TryGetValueFallback(value, out val);
        return true;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool NullableLongCast<TTo>(long? value, [MaybeNullWhen(false)] out TTo val) {
        if (typeof(TTo).IsGenericType)
            return NullableLongNullCast(value, out val);
        var v = Unsafe.As<long?, long>(ref value);
        if (typeof(TTo) == typeof(bool)) val = (TTo)(object)(v != 0);
        else if (typeof(TTo) == typeof(sbyte)) val = (TTo)(object)(sbyte)v;
        else if (typeof(TTo) == typeof(byte)) val = (TTo)(object)(byte)v;
        else if (typeof(TTo) == typeof(short)) val = (TTo)(object)(short)v;
        else if (typeof(TTo) == typeof(ushort)) val = (TTo)(object)(ushort)v;
        else if (typeof(TTo) == typeof(int)) val = (TTo)(object)(int)v;
        else if (typeof(TTo) == typeof(uint)) val = (TTo)(object)(uint)v;
        else if (typeof(TTo) == typeof(long)) val = (TTo)(object)(long)v;
        else if (typeof(TTo) == typeof(ulong)) val = (TTo)(object)(ulong)v;
        else if (typeof(TTo) == typeof(float)) val = (TTo)(object)(float)v;
        else if (typeof(TTo) == typeof(double)) val = (TTo)(object)(double)v;
        else if (typeof(TTo) == typeof(decimal)) val = (TTo)(object)(decimal)v;
        else if (typeof(TTo) == typeof(char)) val = (TTo)(object)(char)v;
        else return TryGetValueFallback(value, out val);
        return value.HasValue;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool NullableLongNullCast<TTo>(long? value, [MaybeNullWhen(false)] out TTo val) {
        if (typeof(TTo) == typeof(bool?)) val = (TTo)(object)(value.HasValue && value.Value != 0);
        else if (typeof(TTo) == typeof(sbyte?)) val = (TTo)(object)(sbyte?)value!;
        else if (typeof(TTo) == typeof(byte?)) val = (TTo)(object)(byte?)value!;
        else if (typeof(TTo) == typeof(short?)) val = (TTo)(object)(short?)value!;
        else if (typeof(TTo) == typeof(ushort?)) val = (TTo)(object)(ushort?)value!;
        else if (typeof(TTo) == typeof(int?)) val = (TTo)(object)(int?)value!;
        else if (typeof(TTo) == typeof(uint?)) val = (TTo)(object)(uint?)value!;
        else if (typeof(TTo) == typeof(long?)) val = (TTo)(object)(long?)value!;
        else if (typeof(TTo) == typeof(ulong?)) val = (TTo)(object)(ulong?)value!;
        else if (typeof(TTo) == typeof(float?)) val = (TTo)(object)(float?)value!;
        else if (typeof(TTo) == typeof(double?)) val = (TTo)(object)(double?)value!;
        else if (typeof(TTo) == typeof(decimal?)) val = (TTo)(object)(decimal?)value!;
        else if (typeof(TTo) == typeof(char?)) val = (TTo)(object)(char?)value!;
        else return TryGetValueFallback(value, out val);
        return true;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool NullableULongCast<TTo>(ulong? value, [MaybeNullWhen(false)] out TTo val) {
        if (typeof(TTo).IsGenericType)
            return NullableULongNullCast(value, out val);
        var v = Unsafe.As<ulong?, ulong>(ref value);
        if (typeof(TTo) == typeof(bool)) val = (TTo)(object)(v != 0);
        else if (typeof(TTo) == typeof(sbyte)) val = (TTo)(object)(sbyte)v;
        else if (typeof(TTo) == typeof(byte)) val = (TTo)(object)(byte)v;
        else if (typeof(TTo) == typeof(short)) val = (TTo)(object)(short)v;
        else if (typeof(TTo) == typeof(ushort)) val = (TTo)(object)(ushort)v;
        else if (typeof(TTo) == typeof(int)) val = (TTo)(object)(int)v;
        else if (typeof(TTo) == typeof(uint)) val = (TTo)(object)(uint)v;
        else if (typeof(TTo) == typeof(long)) val = (TTo)(object)(long)v;
        else if (typeof(TTo) == typeof(ulong)) val = (TTo)(object)(ulong)v;
        else if (typeof(TTo) == typeof(float)) val = (TTo)(object)(float)v;
        else if (typeof(TTo) == typeof(double)) val = (TTo)(object)(double)v;
        else if (typeof(TTo) == typeof(decimal)) val = (TTo)(object)(decimal)v;
        else if (typeof(TTo) == typeof(char)) val = (TTo)(object)(char)v;
        else return TryGetValueFallback(value, out val);
        return value.HasValue;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool NullableULongNullCast<TTo>(ulong? value, [MaybeNullWhen(false)] out TTo val) {
        if (typeof(TTo) == typeof(bool?)) val = (TTo)(object)(value.HasValue && value.Value != 0);
        else if (typeof(TTo) == typeof(sbyte?)) val = (TTo)(object)(sbyte?)value!;
        else if (typeof(TTo) == typeof(byte?)) val = (TTo)(object)(byte?)value!;
        else if (typeof(TTo) == typeof(short?)) val = (TTo)(object)(short?)value!;
        else if (typeof(TTo) == typeof(ushort?)) val = (TTo)(object)(ushort?)value!;
        else if (typeof(TTo) == typeof(int?)) val = (TTo)(object)(int?)value!;
        else if (typeof(TTo) == typeof(uint?)) val = (TTo)(object)(uint?)value!;
        else if (typeof(TTo) == typeof(long?)) val = (TTo)(object)(long?)value!;
        else if (typeof(TTo) == typeof(ulong?)) val = (TTo)(object)(ulong?)value!;
        else if (typeof(TTo) == typeof(float?)) val = (TTo)(object)(float?)value!;
        else if (typeof(TTo) == typeof(double?)) val = (TTo)(object)(double?)value!;
        else if (typeof(TTo) == typeof(decimal?)) val = (TTo)(object)(decimal?)value!;
        else if (typeof(TTo) == typeof(char?)) val = (TTo)(object)(char?)value!;
        else return TryGetValueFallback(value, out val);
        return true;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool NullableFloatCast<TTo>(float? value, [MaybeNullWhen(false)] out TTo val) {
        if (typeof(TTo).IsGenericType)
            return NullableFloatNullCast(value, out val);
        var v = Unsafe.As<float?, float>(ref value);
        if (typeof(TTo) == typeof(bool)) val = (TTo)(object)(v != 0);
        else if (typeof(TTo) == typeof(sbyte)) val = (TTo)(object)(sbyte)v;
        else if (typeof(TTo) == typeof(byte)) val = (TTo)(object)(byte)v;
        else if (typeof(TTo) == typeof(short)) val = (TTo)(object)(short)v;
        else if (typeof(TTo) == typeof(ushort)) val = (TTo)(object)(ushort)v;
        else if (typeof(TTo) == typeof(int)) val = (TTo)(object)(int)v;
        else if (typeof(TTo) == typeof(uint)) val = (TTo)(object)(uint)v;
        else if (typeof(TTo) == typeof(long)) val = (TTo)(object)(long)v;
        else if (typeof(TTo) == typeof(ulong)) val = (TTo)(object)(ulong)v;
        else if (typeof(TTo) == typeof(float)) val = (TTo)(object)(float)v;
        else if (typeof(TTo) == typeof(double)) val = (TTo)(object)(double)v;
        else if (typeof(TTo) == typeof(decimal)) val = (TTo)(object)(decimal)v;
        else if (typeof(TTo) == typeof(char)) val = (TTo)(object)(char)v;
        else return TryGetValueFallback(value, out val);
        return value.HasValue;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool NullableFloatNullCast<TTo>(float? value, [MaybeNullWhen(false)] out TTo val) {
        if (typeof(TTo) == typeof(bool?)) val = (TTo)(object)(value.HasValue && value.Value != 0);
        else if (typeof(TTo) == typeof(sbyte?)) val = (TTo)(object)(sbyte?)value!;
        else if (typeof(TTo) == typeof(byte?)) val = (TTo)(object)(byte?)value!;
        else if (typeof(TTo) == typeof(short?)) val = (TTo)(object)(short?)value!;
        else if (typeof(TTo) == typeof(ushort?)) val = (TTo)(object)(ushort?)value!;
        else if (typeof(TTo) == typeof(int?)) val = (TTo)(object)(int?)value!;
        else if (typeof(TTo) == typeof(uint?)) val = (TTo)(object)(uint?)value!;
        else if (typeof(TTo) == typeof(long?)) val = (TTo)(object)(long?)value!;
        else if (typeof(TTo) == typeof(ulong?)) val = (TTo)(object)(ulong?)value!;
        else if (typeof(TTo) == typeof(float?)) val = (TTo)(object)(float?)value!;
        else if (typeof(TTo) == typeof(double?)) val = (TTo)(object)(double?)value!;
        else if (typeof(TTo) == typeof(decimal?)) val = (TTo)(object)(decimal?)value!;
        else if (typeof(TTo) == typeof(char?)) val = (TTo)(object)(char?)value!;
        else return TryGetValueFallback(value, out val);
        return true;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool NullableDoubleCast<TTo>(double? value, [MaybeNullWhen(false)] out TTo val) {
        if (typeof(TTo).IsGenericType)
            return NullableDoubleNullCast(value, out val);
        var v = Unsafe.As<double?, double>(ref value);
        if (typeof(TTo) == typeof(bool)) val = (TTo)(object)(v != 0);
        else if (typeof(TTo) == typeof(sbyte)) val = (TTo)(object)(sbyte)v;
        else if (typeof(TTo) == typeof(byte)) val = (TTo)(object)(byte)v;
        else if (typeof(TTo) == typeof(short)) val = (TTo)(object)(short)v;
        else if (typeof(TTo) == typeof(ushort)) val = (TTo)(object)(ushort)v;
        else if (typeof(TTo) == typeof(int)) val = (TTo)(object)(int)v;
        else if (typeof(TTo) == typeof(uint)) val = (TTo)(object)(uint)v;
        else if (typeof(TTo) == typeof(long)) val = (TTo)(object)(long)v;
        else if (typeof(TTo) == typeof(ulong)) val = (TTo)(object)(ulong)v;
        else if (typeof(TTo) == typeof(float)) val = (TTo)(object)(float)v;
        else if (typeof(TTo) == typeof(double)) val = (TTo)(object)(double)v;
        else if (typeof(TTo) == typeof(decimal)) val = (TTo)(object)(decimal)v;
        else if (typeof(TTo) == typeof(char)) val = (TTo)(object)(char)v;
        else return TryGetValueFallback(value, out val);
        return value.HasValue;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool NullableDoubleNullCast<TTo>(double? value, [MaybeNullWhen(false)] out TTo val) {
        if (typeof(TTo) == typeof(bool?)) val = (TTo)(object)(value.HasValue && value.Value != 0);
        else if (typeof(TTo) == typeof(sbyte?)) val = (TTo)(object)(sbyte?)value!;
        else if (typeof(TTo) == typeof(byte?)) val = (TTo)(object)(byte?)value!;
        else if (typeof(TTo) == typeof(short?)) val = (TTo)(object)(short?)value!;
        else if (typeof(TTo) == typeof(ushort?)) val = (TTo)(object)(ushort?)value!;
        else if (typeof(TTo) == typeof(int?)) val = (TTo)(object)(int?)value!;
        else if (typeof(TTo) == typeof(uint?)) val = (TTo)(object)(uint?)value!;
        else if (typeof(TTo) == typeof(long?)) val = (TTo)(object)(long?)value!;
        else if (typeof(TTo) == typeof(ulong?)) val = (TTo)(object)(ulong?)value!;
        else if (typeof(TTo) == typeof(float?)) val = (TTo)(object)(float?)value!;
        else if (typeof(TTo) == typeof(double?)) val = (TTo)(object)(double?)value!;
        else if (typeof(TTo) == typeof(decimal?)) val = (TTo)(object)(decimal?)value!;
        else if (typeof(TTo) == typeof(char?)) val = (TTo)(object)(char?)value!;
        else return TryGetValueFallback(value, out val);
        return true;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool NullableDecimalCast<TTo>(decimal? value, [MaybeNullWhen(false)] out TTo val) {
        if (typeof(TTo).IsGenericType)
            return NullableDecimalNullCast(value, out val);
        var v = Unsafe.As<decimal?, decimal>(ref value);
        if (typeof(TTo) == typeof(bool)) val = (TTo)(object)(v != 0);
        else if (typeof(TTo) == typeof(sbyte)) val = (TTo)(object)(sbyte)v;
        else if (typeof(TTo) == typeof(byte)) val = (TTo)(object)(byte)v;
        else if (typeof(TTo) == typeof(short)) val = (TTo)(object)(short)v;
        else if (typeof(TTo) == typeof(ushort)) val = (TTo)(object)(ushort)v;
        else if (typeof(TTo) == typeof(int)) val = (TTo)(object)(int)v;
        else if (typeof(TTo) == typeof(uint)) val = (TTo)(object)(uint)v;
        else if (typeof(TTo) == typeof(long)) val = (TTo)(object)(long)v;
        else if (typeof(TTo) == typeof(ulong)) val = (TTo)(object)(ulong)v;
        else if (typeof(TTo) == typeof(float)) val = (TTo)(object)(float)v;
        else if (typeof(TTo) == typeof(double)) val = (TTo)(object)(double)v;
        else if (typeof(TTo) == typeof(decimal)) val = (TTo)(object)(decimal)v;
        else if (typeof(TTo) == typeof(char)) val = (TTo)(object)(char)v;
        else return TryGetValueFallback(value, out val);
        return value.HasValue;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool NullableDecimalNullCast<TTo>(decimal? value, [MaybeNullWhen(false)] out TTo val) {
        if (typeof(TTo) == typeof(bool?)) val = (TTo)(object)(value.HasValue && value.Value != 0);
        else if (typeof(TTo) == typeof(sbyte?)) val = (TTo)(object)(sbyte?)value!;
        else if (typeof(TTo) == typeof(byte?)) val = (TTo)(object)(byte?)value!;
        else if (typeof(TTo) == typeof(short?)) val = (TTo)(object)(short?)value!;
        else if (typeof(TTo) == typeof(ushort?)) val = (TTo)(object)(ushort?)value!;
        else if (typeof(TTo) == typeof(int?)) val = (TTo)(object)(int?)value!;
        else if (typeof(TTo) == typeof(uint?)) val = (TTo)(object)(uint?)value!;
        else if (typeof(TTo) == typeof(long?)) val = (TTo)(object)(long?)value!;
        else if (typeof(TTo) == typeof(ulong?)) val = (TTo)(object)(ulong?)value!;
        else if (typeof(TTo) == typeof(float?)) val = (TTo)(object)(float?)value!;
        else if (typeof(TTo) == typeof(double?)) val = (TTo)(object)(double?)value!;
        else if (typeof(TTo) == typeof(decimal?)) val = (TTo)(object)(decimal?)value!;
        else if (typeof(TTo) == typeof(char?)) val = (TTo)(object)(char?)value!;
        else return TryGetValueFallback(value, out val);
        return true;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool NullableCharCast<TTo>(char? value, [MaybeNullWhen(false)] out TTo val) {
        if (typeof(TTo).IsGenericType)
            return NullableCharNullCast(value, out val);
        var v = Unsafe.As<char?, char>(ref value);
        if (typeof(TTo) == typeof(bool)) val = (TTo)(object)(v != 0);
        else if (typeof(TTo) == typeof(sbyte)) val = (TTo)(object)(sbyte)v;
        else if (typeof(TTo) == typeof(byte)) val = (TTo)(object)(byte)v;
        else if (typeof(TTo) == typeof(short)) val = (TTo)(object)(short)v;
        else if (typeof(TTo) == typeof(ushort)) val = (TTo)(object)(ushort)v;
        else if (typeof(TTo) == typeof(int)) val = (TTo)(object)(int)v;
        else if (typeof(TTo) == typeof(uint)) val = (TTo)(object)(uint)v;
        else if (typeof(TTo) == typeof(long)) val = (TTo)(object)(long)v;
        else if (typeof(TTo) == typeof(ulong)) val = (TTo)(object)(ulong)v;
        else if (typeof(TTo) == typeof(float)) val = (TTo)(object)(float)v;
        else if (typeof(TTo) == typeof(double)) val = (TTo)(object)(double)v;
        else if (typeof(TTo) == typeof(decimal)) val = (TTo)(object)(decimal)v;
        else if (typeof(TTo) == typeof(char)) val = (TTo)(object)(char)v;
        else return TryGetValueFallback(value, out val);
        return value.HasValue;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool NullableCharNullCast<TTo>(char? value, [MaybeNullWhen(false)] out TTo val) {
        if (typeof(TTo) == typeof(bool?)) val = (TTo)(object)(value.HasValue && value.Value != 0);
        else if (typeof(TTo) == typeof(sbyte?)) val = (TTo)(object)(sbyte?)value!;
        else if (typeof(TTo) == typeof(byte?)) val = (TTo)(object)(byte?)value!;
        else if (typeof(TTo) == typeof(short?)) val = (TTo)(object)(short?)value!;
        else if (typeof(TTo) == typeof(ushort?)) val = (TTo)(object)(ushort?)value!;
        else if (typeof(TTo) == typeof(int?)) val = (TTo)(object)(int?)value!;
        else if (typeof(TTo) == typeof(uint?)) val = (TTo)(object)(uint?)value!;
        else if (typeof(TTo) == typeof(long?)) val = (TTo)(object)(long?)value!;
        else if (typeof(TTo) == typeof(ulong?)) val = (TTo)(object)(ulong?)value!;
        else if (typeof(TTo) == typeof(float?)) val = (TTo)(object)(float?)value!;
        else if (typeof(TTo) == typeof(double?)) val = (TTo)(object)(double?)value!;
        else if (typeof(TTo) == typeof(decimal?)) val = (TTo)(object)(decimal?)value!;
        else if (typeof(TTo) == typeof(char?)) val = (TTo)(object)(char?)value!;
        else return TryGetValueFallback(value, out val);
        return true;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool BoolCast<TTo>(bool value, [MaybeNullWhen(false)] out TTo val) {
        var v = (bool)(object)value!;
        if (typeof(TTo) == typeof(bool) || typeof(TTo) == typeof(bool?)) val = (TTo)(object)(bool)v;
        else if (typeof(TTo) == typeof(sbyte) || typeof(TTo) == typeof(sbyte?)) val = (TTo)(object)(sbyte)(v ? 1 : 0);
        else if (typeof(TTo) == typeof(byte) || typeof(TTo) == typeof(byte?)) val = (TTo)(object)(byte)(v ? 1 : 0);
        else if (typeof(TTo) == typeof(short) || typeof(TTo) == typeof(short?)) val = (TTo)(object)(short)(v ? 1 : 0);
        else if (typeof(TTo) == typeof(ushort) || typeof(TTo) == typeof(ushort?)) val = (TTo)(object)(ushort)(v ? 1 : 0);
        else if (typeof(TTo) == typeof(int) || typeof(TTo) == typeof(int?)) val = (TTo)(object)(int)(v ? 1 : 0);
        else if (typeof(TTo) == typeof(uint) || typeof(TTo) == typeof(uint?)) val = (TTo)(object)(uint)(v ? 1 : 0);
        else if (typeof(TTo) == typeof(long) || typeof(TTo) == typeof(long?)) val = (TTo)(object)(long)(v ? 1 : 0);
        else if (typeof(TTo) == typeof(ulong) || typeof(TTo) == typeof(ulong?)) val = (TTo)(object)(ulong)(v ? 1 : 0);
        else if (typeof(TTo) == typeof(float) || typeof(TTo) == typeof(float?)) val = (TTo)(object)(float)(v ? 1 : 0);
        else if (typeof(TTo) == typeof(double) || typeof(TTo) == typeof(double?)) val = (TTo)(object)(double)(v ? 1 : 0);
        else if (typeof(TTo) == typeof(decimal) || typeof(TTo) == typeof(decimal?)) val = (TTo)(object)(decimal)(v ? 1 : 0);
        else if (typeof(TTo) == typeof(char) || typeof(TTo) == typeof(char?)) val = (TTo)(object)(char)(v ? 1 : 0);
        else return TryGetValueFallback(value, out val);
        return true;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool SByteCast<TTo>(sbyte value, [MaybeNullWhen(false)] out TTo val) {
        var v = (sbyte)(object)value!;
        if (typeof(TTo) == typeof(bool) || typeof(TTo) == typeof(bool?)) val = (TTo)(object)(bool)(v != 0);
        else if (typeof(TTo) == typeof(sbyte) || typeof(TTo) == typeof(sbyte?)) val = (TTo)(object)(sbyte)v;
        else if (typeof(TTo) == typeof(byte) || typeof(TTo) == typeof(byte?)) val = (TTo)(object)(byte)v;
        else if (typeof(TTo) == typeof(short) || typeof(TTo) == typeof(short?)) val = (TTo)(object)(short)v;
        else if (typeof(TTo) == typeof(ushort) || typeof(TTo) == typeof(ushort?)) val = (TTo)(object)(ushort)v;
        else if (typeof(TTo) == typeof(int) || typeof(TTo) == typeof(int?)) val = (TTo)(object)(int)v;
        else if (typeof(TTo) == typeof(uint) || typeof(TTo) == typeof(uint?)) val = (TTo)(object)(uint)v;
        else if (typeof(TTo) == typeof(long) || typeof(TTo) == typeof(long?)) val = (TTo)(object)(long)v;
        else if (typeof(TTo) == typeof(ulong) || typeof(TTo) == typeof(ulong?)) val = (TTo)(object)(ulong)v;
        else if (typeof(TTo) == typeof(float) || typeof(TTo) == typeof(float?)) val = (TTo)(object)(float)v;
        else if (typeof(TTo) == typeof(double) || typeof(TTo) == typeof(double?)) val = (TTo)(object)(double)v;
        else if (typeof(TTo) == typeof(decimal) || typeof(TTo) == typeof(decimal?)) val = (TTo)(object)(decimal)v;
        else if (typeof(TTo) == typeof(char) || typeof(TTo) == typeof(char?)) val = (TTo)(object)(char)v;
        else return TryGetValueFallback(value, out val);
        return true;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool ByteCast<TTo>(byte value, [MaybeNullWhen(false)] out TTo val) {
        var v = (byte)(object)value!;
        if (typeof(TTo) == typeof(bool) || typeof(TTo) == typeof(bool?)) val = (TTo)(object)(bool)(v != 0);
        else if (typeof(TTo) == typeof(sbyte) || typeof(TTo) == typeof(sbyte?)) val = (TTo)(object)(sbyte)v;
        else if (typeof(TTo) == typeof(byte) || typeof(TTo) == typeof(byte?)) val = (TTo)(object)(byte)v;
        else if (typeof(TTo) == typeof(short) || typeof(TTo) == typeof(short?)) val = (TTo)(object)(short)v;
        else if (typeof(TTo) == typeof(ushort) || typeof(TTo) == typeof(ushort?)) val = (TTo)(object)(ushort)v;
        else if (typeof(TTo) == typeof(int) || typeof(TTo) == typeof(int?)) val = (TTo)(object)(int)v;
        else if (typeof(TTo) == typeof(uint) || typeof(TTo) == typeof(uint?)) val = (TTo)(object)(uint)v;
        else if (typeof(TTo) == typeof(long) || typeof(TTo) == typeof(long?)) val = (TTo)(object)(long)v;
        else if (typeof(TTo) == typeof(ulong) || typeof(TTo) == typeof(ulong?)) val = (TTo)(object)(ulong)v;
        else if (typeof(TTo) == typeof(float) || typeof(TTo) == typeof(float?)) val = (TTo)(object)(float)v;
        else if (typeof(TTo) == typeof(double) || typeof(TTo) == typeof(double?)) val = (TTo)(object)(double)v;
        else if (typeof(TTo) == typeof(decimal) || typeof(TTo) == typeof(decimal?)) val = (TTo)(object)(decimal)v;
        else if (typeof(TTo) == typeof(char) || typeof(TTo) == typeof(char?)) val = (TTo)(object)(char)v;
        else return TryGetValueFallback(value, out val);
        return true;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool ShortCast<TTo>(short value, [MaybeNullWhen(false)] out TTo val) {
        var v = (short)(object)value!;
        if (typeof(TTo) == typeof(bool) || typeof(TTo) == typeof(bool?)) val = (TTo)(object)(bool)(v != 0);
        else if (typeof(TTo) == typeof(sbyte) || typeof(TTo) == typeof(sbyte?)) val = (TTo)(object)(sbyte)v;
        else if (typeof(TTo) == typeof(byte) || typeof(TTo) == typeof(byte?)) val = (TTo)(object)(byte)v;
        else if (typeof(TTo) == typeof(short) || typeof(TTo) == typeof(short?)) val = (TTo)(object)(short)v;
        else if (typeof(TTo) == typeof(ushort) || typeof(TTo) == typeof(ushort?)) val = (TTo)(object)(ushort)v;
        else if (typeof(TTo) == typeof(int) || typeof(TTo) == typeof(int?)) val = (TTo)(object)(int)v;
        else if (typeof(TTo) == typeof(uint) || typeof(TTo) == typeof(uint?)) val = (TTo)(object)(uint)v;
        else if (typeof(TTo) == typeof(long) || typeof(TTo) == typeof(long?)) val = (TTo)(object)(long)v;
        else if (typeof(TTo) == typeof(ulong) || typeof(TTo) == typeof(ulong?)) val = (TTo)(object)(ulong)v;
        else if (typeof(TTo) == typeof(float) || typeof(TTo) == typeof(float?)) val = (TTo)(object)(float)v;
        else if (typeof(TTo) == typeof(double) || typeof(TTo) == typeof(double?)) val = (TTo)(object)(double)v;
        else if (typeof(TTo) == typeof(decimal) || typeof(TTo) == typeof(decimal?)) val = (TTo)(object)(decimal)v;
        else if (typeof(TTo) == typeof(char) || typeof(TTo) == typeof(char?)) val = (TTo)(object)(char)v;
        else return TryGetValueFallback(value, out val);
        return true;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool UShortCast<TTo>(ushort value, [MaybeNullWhen(false)] out TTo val) {
        var v = (ushort)(object)value!;
        if (typeof(TTo) == typeof(bool) || typeof(TTo) == typeof(bool?)) val = (TTo)(object)(bool)(v != 0);
        else if (typeof(TTo) == typeof(sbyte) || typeof(TTo) == typeof(sbyte?)) val = (TTo)(object)(sbyte)v;
        else if (typeof(TTo) == typeof(byte) || typeof(TTo) == typeof(byte?)) val = (TTo)(object)(byte)v;
        else if (typeof(TTo) == typeof(short) || typeof(TTo) == typeof(short?)) val = (TTo)(object)(short)v;
        else if (typeof(TTo) == typeof(ushort) || typeof(TTo) == typeof(ushort?)) val = (TTo)(object)(ushort)v;
        else if (typeof(TTo) == typeof(int) || typeof(TTo) == typeof(int?)) val = (TTo)(object)(int)v;
        else if (typeof(TTo) == typeof(uint) || typeof(TTo) == typeof(uint?)) val = (TTo)(object)(uint)v;
        else if (typeof(TTo) == typeof(long) || typeof(TTo) == typeof(long?)) val = (TTo)(object)(long)v;
        else if (typeof(TTo) == typeof(ulong) || typeof(TTo) == typeof(ulong?)) val = (TTo)(object)(ulong)v;
        else if (typeof(TTo) == typeof(float) || typeof(TTo) == typeof(float?)) val = (TTo)(object)(float)v;
        else if (typeof(TTo) == typeof(double) || typeof(TTo) == typeof(double?)) val = (TTo)(object)(double)v;
        else if (typeof(TTo) == typeof(decimal) || typeof(TTo) == typeof(decimal?)) val = (TTo)(object)(decimal)v;
        else if (typeof(TTo) == typeof(char) || typeof(TTo) == typeof(char?)) val = (TTo)(object)(char)v;
        else return TryGetValueFallback(value, out val);
        return true;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool IntCast<TTo>(int value, [MaybeNullWhen(false)] out TTo val) {
        var v = (int)(object)value!;
        if (typeof(TTo) == typeof(bool) || typeof(TTo) == typeof(bool?)) val = (TTo)(object)(bool)(v != 0);
        else if (typeof(TTo) == typeof(sbyte) || typeof(TTo) == typeof(sbyte?)) val = (TTo)(object)(sbyte)v;
        else if (typeof(TTo) == typeof(byte) || typeof(TTo) == typeof(byte?)) val = (TTo)(object)(byte)v;
        else if (typeof(TTo) == typeof(short) || typeof(TTo) == typeof(short?)) val = (TTo)(object)(short)v;
        else if (typeof(TTo) == typeof(ushort) || typeof(TTo) == typeof(ushort?)) val = (TTo)(object)(ushort)v;
        else if (typeof(TTo) == typeof(int) || typeof(TTo) == typeof(int?)) val = (TTo)(object)(int)v;
        else if (typeof(TTo) == typeof(uint) || typeof(TTo) == typeof(uint?)) val = (TTo)(object)(uint)v;
        else if (typeof(TTo) == typeof(long) || typeof(TTo) == typeof(long?)) val = (TTo)(object)(long)v;
        else if (typeof(TTo) == typeof(ulong) || typeof(TTo) == typeof(ulong?)) val = (TTo)(object)(ulong)v;
        else if (typeof(TTo) == typeof(float) || typeof(TTo) == typeof(float?)) val = (TTo)(object)(float)v;
        else if (typeof(TTo) == typeof(double) || typeof(TTo) == typeof(double?)) val = (TTo)(object)(double)v;
        else if (typeof(TTo) == typeof(decimal) || typeof(TTo) == typeof(decimal?)) val = (TTo)(object)(decimal)v;
        else if (typeof(TTo) == typeof(char) || typeof(TTo) == typeof(char?)) val = (TTo)(object)(char)v;
        else return TryGetValueFallback(value, out val);
        return true;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool UIntCast<TTo>(uint value, [MaybeNullWhen(false)] out TTo val) {
        var v = (uint)(object)value!;
        if (typeof(TTo) == typeof(bool) || typeof(TTo) == typeof(bool?)) val = (TTo)(object)(bool)(v != 0);
        else if (typeof(TTo) == typeof(sbyte) || typeof(TTo) == typeof(sbyte?)) val = (TTo)(object)(sbyte)v;
        else if (typeof(TTo) == typeof(byte) || typeof(TTo) == typeof(byte?)) val = (TTo)(object)(byte)v;
        else if (typeof(TTo) == typeof(short) || typeof(TTo) == typeof(short?)) val = (TTo)(object)(short)v;
        else if (typeof(TTo) == typeof(ushort) || typeof(TTo) == typeof(ushort?)) val = (TTo)(object)(ushort)v;
        else if (typeof(TTo) == typeof(int) || typeof(TTo) == typeof(int?)) val = (TTo)(object)(int)v;
        else if (typeof(TTo) == typeof(uint) || typeof(TTo) == typeof(uint?)) val = (TTo)(object)(uint)v;
        else if (typeof(TTo) == typeof(long) || typeof(TTo) == typeof(long?)) val = (TTo)(object)(long)v;
        else if (typeof(TTo) == typeof(ulong) || typeof(TTo) == typeof(ulong?)) val = (TTo)(object)(ulong)v;
        else if (typeof(TTo) == typeof(float) || typeof(TTo) == typeof(float?)) val = (TTo)(object)(float)v;
        else if (typeof(TTo) == typeof(double) || typeof(TTo) == typeof(double?)) val = (TTo)(object)(double)v;
        else if (typeof(TTo) == typeof(decimal) || typeof(TTo) == typeof(decimal?)) val = (TTo)(object)(decimal)v;
        else if (typeof(TTo) == typeof(char) || typeof(TTo) == typeof(char?)) val = (TTo)(object)(char)v;
        else return TryGetValueFallback(value, out val);
        return true;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool LongCast<TTo>(long value, [MaybeNullWhen(false)] out TTo val) {
        var v = (long)(object)value!;
        if (typeof(TTo) == typeof(bool) || typeof(TTo) == typeof(bool?)) val = (TTo)(object)(bool)(v != 0);
        else if (typeof(TTo) == typeof(sbyte) || typeof(TTo) == typeof(sbyte?)) val = (TTo)(object)(sbyte)v;
        else if (typeof(TTo) == typeof(byte) || typeof(TTo) == typeof(byte?)) val = (TTo)(object)(byte)v;
        else if (typeof(TTo) == typeof(short) || typeof(TTo) == typeof(short?)) val = (TTo)(object)(short)v;
        else if (typeof(TTo) == typeof(ushort) || typeof(TTo) == typeof(ushort?)) val = (TTo)(object)(ushort)v;
        else if (typeof(TTo) == typeof(int) || typeof(TTo) == typeof(int?)) val = (TTo)(object)(int)v;
        else if (typeof(TTo) == typeof(uint) || typeof(TTo) == typeof(uint?)) val = (TTo)(object)(uint)v;
        else if (typeof(TTo) == typeof(long) || typeof(TTo) == typeof(long?)) val = (TTo)(object)(long)v;
        else if (typeof(TTo) == typeof(ulong) || typeof(TTo) == typeof(ulong?)) val = (TTo)(object)(ulong)v;
        else if (typeof(TTo) == typeof(float) || typeof(TTo) == typeof(float?)) val = (TTo)(object)(float)v;
        else if (typeof(TTo) == typeof(double) || typeof(TTo) == typeof(double?)) val = (TTo)(object)(double)v;
        else if (typeof(TTo) == typeof(decimal) || typeof(TTo) == typeof(decimal?)) val = (TTo)(object)(decimal)v;
        else if (typeof(TTo) == typeof(char) || typeof(TTo) == typeof(char?)) val = (TTo)(object)(char)v;
        else return TryGetValueFallback(value, out val);
        return true;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool ULongCast<TTo>(ulong value, [MaybeNullWhen(false)] out TTo val) {
        var v = (ulong)(object)value!;
        if (typeof(TTo) == typeof(bool) || typeof(TTo) == typeof(bool?)) val = (TTo)(object)(bool)(v != 0);
        else if (typeof(TTo) == typeof(sbyte) || typeof(TTo) == typeof(sbyte?)) val = (TTo)(object)(sbyte)v;
        else if (typeof(TTo) == typeof(byte) || typeof(TTo) == typeof(byte?)) val = (TTo)(object)(byte)v;
        else if (typeof(TTo) == typeof(short) || typeof(TTo) == typeof(short?)) val = (TTo)(object)(short)v;
        else if (typeof(TTo) == typeof(ushort) || typeof(TTo) == typeof(ushort?)) val = (TTo)(object)(ushort)v;
        else if (typeof(TTo) == typeof(int) || typeof(TTo) == typeof(int?)) val = (TTo)(object)(int)v;
        else if (typeof(TTo) == typeof(uint) || typeof(TTo) == typeof(uint?)) val = (TTo)(object)(uint)v;
        else if (typeof(TTo) == typeof(long) || typeof(TTo) == typeof(long?)) val = (TTo)(object)(long)v;
        else if (typeof(TTo) == typeof(ulong) || typeof(TTo) == typeof(ulong?)) val = (TTo)(object)(ulong)v;
        else if (typeof(TTo) == typeof(float) || typeof(TTo) == typeof(float?)) val = (TTo)(object)(float)v;
        else if (typeof(TTo) == typeof(double) || typeof(TTo) == typeof(double?)) val = (TTo)(object)(double)v;
        else if (typeof(TTo) == typeof(decimal) || typeof(TTo) == typeof(decimal?)) val = (TTo)(object)(decimal)v;
        else if (typeof(TTo) == typeof(char) || typeof(TTo) == typeof(char?)) val = (TTo)(object)(char)v;
        else return TryGetValueFallback(value, out val);
        return true;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool FloatCast<TTo>(float value, [MaybeNullWhen(false)] out TTo val) {
        var v = (float)(object)value!;
        if (typeof(TTo) == typeof(bool) || typeof(TTo) == typeof(bool?)) val = (TTo)(object)(bool)(v != 0);
        else if (typeof(TTo) == typeof(sbyte) || typeof(TTo) == typeof(sbyte?)) val = (TTo)(object)(sbyte)v;
        else if (typeof(TTo) == typeof(byte) || typeof(TTo) == typeof(byte?)) val = (TTo)(object)(byte)v;
        else if (typeof(TTo) == typeof(short) || typeof(TTo) == typeof(short?)) val = (TTo)(object)(short)v;
        else if (typeof(TTo) == typeof(ushort) || typeof(TTo) == typeof(ushort?)) val = (TTo)(object)(ushort)v;
        else if (typeof(TTo) == typeof(int) || typeof(TTo) == typeof(int?)) val = (TTo)(object)(int)v;
        else if (typeof(TTo) == typeof(uint) || typeof(TTo) == typeof(uint?)) val = (TTo)(object)(uint)v;
        else if (typeof(TTo) == typeof(long) || typeof(TTo) == typeof(long?)) val = (TTo)(object)(long)v;
        else if (typeof(TTo) == typeof(ulong) || typeof(TTo) == typeof(ulong?)) val = (TTo)(object)(ulong)v;
        else if (typeof(TTo) == typeof(float) || typeof(TTo) == typeof(float?)) val = (TTo)(object)(float)v;
        else if (typeof(TTo) == typeof(double) || typeof(TTo) == typeof(double?)) val = (TTo)(object)(double)v;
        else if (typeof(TTo) == typeof(decimal) || typeof(TTo) == typeof(decimal?)) val = (TTo)(object)(decimal)v;
        else if (typeof(TTo) == typeof(char) || typeof(TTo) == typeof(char?)) val = (TTo)(object)(char)v;
        else return TryGetValueFallback(value, out val);
        return true;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool DoubleCast<TTo>(double value, [MaybeNullWhen(false)] out TTo val) {
        var v = (double)(object)value!;
        if (typeof(TTo) == typeof(bool) || typeof(TTo) == typeof(bool?)) val = (TTo)(object)(bool)(v != 0);
        else if (typeof(TTo) == typeof(sbyte) || typeof(TTo) == typeof(sbyte?)) val = (TTo)(object)(sbyte)v;
        else if (typeof(TTo) == typeof(byte) || typeof(TTo) == typeof(byte?)) val = (TTo)(object)(byte)v;
        else if (typeof(TTo) == typeof(short) || typeof(TTo) == typeof(short?)) val = (TTo)(object)(short)v;
        else if (typeof(TTo) == typeof(ushort) || typeof(TTo) == typeof(ushort?)) val = (TTo)(object)(ushort)v;
        else if (typeof(TTo) == typeof(int) || typeof(TTo) == typeof(int?)) val = (TTo)(object)(int)v;
        else if (typeof(TTo) == typeof(uint) || typeof(TTo) == typeof(uint?)) val = (TTo)(object)(uint)v;
        else if (typeof(TTo) == typeof(long) || typeof(TTo) == typeof(long?)) val = (TTo)(object)(long)v;
        else if (typeof(TTo) == typeof(ulong) || typeof(TTo) == typeof(ulong?)) val = (TTo)(object)(ulong)v;
        else if (typeof(TTo) == typeof(float) || typeof(TTo) == typeof(float?)) val = (TTo)(object)(float)v;
        else if (typeof(TTo) == typeof(double) || typeof(TTo) == typeof(double?)) val = (TTo)(object)(double)v;
        else if (typeof(TTo) == typeof(decimal) || typeof(TTo) == typeof(decimal?)) val = (TTo)(object)(decimal)v;
        else if (typeof(TTo) == typeof(char) || typeof(TTo) == typeof(char?)) val = (TTo)(object)(char)v;
        else return TryGetValueFallback(value, out val);
        return true;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool DecimalCast<TTo>(decimal value, [MaybeNullWhen(false)] out TTo val) {
        var v = (decimal)(object)value!;
        if (typeof(TTo) == typeof(bool) || typeof(TTo) == typeof(bool?)) val = (TTo)(object)(bool)(v != 0);
        else if (typeof(TTo) == typeof(sbyte) || typeof(TTo) == typeof(sbyte?)) val = (TTo)(object)(sbyte)v;
        else if (typeof(TTo) == typeof(byte) || typeof(TTo) == typeof(byte?)) val = (TTo)(object)(byte)v;
        else if (typeof(TTo) == typeof(short) || typeof(TTo) == typeof(short?)) val = (TTo)(object)(short)v;
        else if (typeof(TTo) == typeof(ushort) || typeof(TTo) == typeof(ushort?)) val = (TTo)(object)(ushort)v;
        else if (typeof(TTo) == typeof(int) || typeof(TTo) == typeof(int?)) val = (TTo)(object)(int)v;
        else if (typeof(TTo) == typeof(uint) || typeof(TTo) == typeof(uint?)) val = (TTo)(object)(uint)v;
        else if (typeof(TTo) == typeof(long) || typeof(TTo) == typeof(long?)) val = (TTo)(object)(long)v;
        else if (typeof(TTo) == typeof(ulong) || typeof(TTo) == typeof(ulong?)) val = (TTo)(object)(ulong)v;
        else if (typeof(TTo) == typeof(float) || typeof(TTo) == typeof(float?)) val = (TTo)(object)(float)v;
        else if (typeof(TTo) == typeof(double) || typeof(TTo) == typeof(double?)) val = (TTo)(object)(double)v;
        else if (typeof(TTo) == typeof(decimal) || typeof(TTo) == typeof(decimal?)) val = (TTo)(object)(decimal)v;
        else if (typeof(TTo) == typeof(char) || typeof(TTo) == typeof(char?)) val = (TTo)(object)(char)v;
        else return TryGetValueFallback(value, out val);
        return true;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool CharCast<TTo>(char value, [MaybeNullWhen(false)] out TTo val) {
        var v = (char)(object)value!;
        if (typeof(TTo) == typeof(bool) || typeof(TTo) == typeof(bool?)) val = (TTo)(object)(bool)(v != 0);
        else if (typeof(TTo) == typeof(sbyte) || typeof(TTo) == typeof(sbyte?)) val = (TTo)(object)(sbyte)v;
        else if (typeof(TTo) == typeof(byte) || typeof(TTo) == typeof(byte?)) val = (TTo)(object)(byte)v;
        else if (typeof(TTo) == typeof(short) || typeof(TTo) == typeof(short?)) val = (TTo)(object)(short)v;
        else if (typeof(TTo) == typeof(ushort) || typeof(TTo) == typeof(ushort?)) val = (TTo)(object)(ushort)v;
        else if (typeof(TTo) == typeof(int) || typeof(TTo) == typeof(int?)) val = (TTo)(object)(int)v;
        else if (typeof(TTo) == typeof(uint) || typeof(TTo) == typeof(uint?)) val = (TTo)(object)(uint)v;
        else if (typeof(TTo) == typeof(long) || typeof(TTo) == typeof(long?)) val = (TTo)(object)(long)v;
        else if (typeof(TTo) == typeof(ulong) || typeof(TTo) == typeof(ulong?)) val = (TTo)(object)(ulong)v;
        else if (typeof(TTo) == typeof(float) || typeof(TTo) == typeof(float?)) val = (TTo)(object)(float)v;
        else if (typeof(TTo) == typeof(double) || typeof(TTo) == typeof(double?)) val = (TTo)(object)(double)v;
        else if (typeof(TTo) == typeof(decimal) || typeof(TTo) == typeof(decimal?)) val = (TTo)(object)(decimal)v;
        else if (typeof(TTo) == typeof(char) || typeof(TTo) == typeof(char?)) val = (TTo)(object)(char)v;
        else return TryGetValueFallback(value, out val);
        return true;
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static bool TryGetValueFallback<TFrom, TTo>(TFrom value, [MaybeNullWhen(false)] out TTo val) {
        if (value is TTo matchedValue) {
            val = matchedValue;
            return true;
        }
        val = default;
        return false;
    }
}
*/