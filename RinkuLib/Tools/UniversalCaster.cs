using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace RinkuLib.Tools;
/// <summary>A conversion that writes its result through the out parameter and returns whether it succeeded.</summary>
public delegate bool CastFunc<TFrom, TTo>(TFrom value, out TTo result);
/// <summary>
/// Converts a value from one type to another at runtime, covering casts, numeric conversions, enum
/// conversions, and parsing, for when a value's type does not match the one a caller wants. Each type pair
/// resolves its conversion once, emitting a specialized method for it, and reuses it. A custom parser
/// registered with <see cref="AddParser"/> replaces that emitted conversion for the pair and takes effect
/// even after the pair was first used.
/// </summary>
public static class Caster {
    private static readonly BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;
    /// <summary>Register a custom conversion from <typeparamref name="TFrom"/> to <typeparamref name="TTo"/>. It takes priority over the built-in paths.</summary>
    public static void AddParser<TFrom, TTo>(CastFunc<TFrom, TTo> parser) => Caster<TFrom, TTo>.SetCustom(parser);
    /// <summary>Converts <paramref name="value"/> to <typeparamref name="TTo"/>, returning <see langword="false"/> when no conversion fits.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryCast<TFrom, TTo>(TFrom value, [MaybeNullWhen(false)] out TTo val) => Caster<TFrom, TTo>.TryCast(value, out val);
    static Caster() {
        AddParser((object v, out DateTimeOffset r) => { r = DateTimeOffset.Parse(v.ToString()!, CultureInfo.InvariantCulture); return true; });
        AddParser((object v, out TimeSpan r) => { r = TimeSpan.Parse(v.ToString()!, CultureInfo.InvariantCulture); return true; });
        AddParser((object v, out Guid r) => {
            r = v switch {
                string s => Guid.Parse(s),
                byte[] b => new Guid(b),
                _ => Guid.Parse(v.ToString()!)
            };
            return true;
        });
        _ = 1;
    }
    /// <summary>Parse an object value to <typeparamref name="T"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: NotNullIfNotNull(nameof(value))]
    public static T? Parse<T>(this object? value) {
        if (Caster<object?, T>.TryCast(value, out var val))
            return val!;
        throw new Exception($"Unable to parse from {value} (object : {value!.GetType()}) to {typeof(T)}");
    }
    internal static bool TryGetOperator(Type f, Type t, [NotNullWhen(true)] out MethodInfo? m) {
        m = f.GetMethod("op_Explicit", PublicStatic, [f]) ?? f.GetMethod("op_Implicit", PublicStatic, [f])
         ?? t.GetMethod("op_Explicit", PublicStatic, [f]) ?? t.GetMethod("op_Implicit", PublicStatic, [f]);
        return m != null && m.ReturnType == t;
    }
    /// <summary>
    /// Whether a value type is a plain number, so it can go through <see cref="INumberBase{TSelf}.CreateTruncating{TOther}"/>.
    /// Only ever asked about the non-enum value types the resolver reaches (see <c>Repr</c>), so it takes the
    /// built-in numeric type codes, rejects the value types that share a code but are not numbers
    /// (<see cref="DateTime"/>, <see cref="Guid"/>, <see cref="TimeSpan"/>), and finally accepts a user type
    /// that implements the generic-math interface (e.g. <see cref="Half"/>, <see cref="Int128"/>).
    /// </summary>
    internal static bool IsNumeric(Type t) {
        switch (Type.GetTypeCode(t)) {
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
        if (t == typeof(DateTime) || t == typeof(Guid) || t == typeof(TimeSpan))
            return false;
        foreach (var i in t.GetInterfaces())
            if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(INumberBase<>) && i.GetGenericArguments()[0] == t)
                return true;
        return false;
    }
    internal static bool IsParsable(Type t) {
        foreach (var i in t.GetInterfaces())
            if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IParsable<>))
                return true;
        return false;
    }
}
/// <summary>The cached converter from <typeparamref name="TFrom"/> to <typeparamref name="TTo"/>, resolved once per type pair and reused.</summary>
public static class Caster<TFrom, TTo> {
    private static volatile CastFunc<TFrom, TTo> _convert = CasterEmit.Build<TFrom, TTo>();

    /// <summary>Replaces the conversion with a custom parser. Rebuild through <see cref="CasterEmit"/> to return to the default.</summary>
    internal static void SetCustom(CastFunc<TFrom, TTo> parser) => _convert = parser;

    /// <summary>Converts <paramref name="value"/>, returning <see langword="false"/> when no conversion fits.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryCast(TFrom value, [MaybeNullWhen(false)] out TTo result) {
        if (typeof(TFrom) == typeof(TTo)) {
            result = Unsafe.As<TFrom, TTo>(ref value);
            return true;
        }
        if (!typeof(TFrom).IsValueType && value is TTo cast) {
            result = cast;
            return true;
        }
        return _convert(value, out result);
    }
}
/// <summary>
/// Emits, once per type pair, a specialized method with the signature <c>bool(TFrom, out TTo)</c> that
/// performs the conversion directly. Value conversions (numbers, enums, nullables, operators) compile to
/// straight-line IL with no boxing; the reference and parsing paths call the small bridge helpers below.
/// </summary>
internal static class CasterEmit {
    internal static CastFunc<TFrom, TTo> Build<TFrom, TTo>() {
        var dm = new DynamicMethod(
            $"Cast_{typeof(TFrom).Name}_{typeof(TTo).Name}",
            typeof(bool),
            [typeof(TFrom), typeof(TTo).MakeByRefType()],
            typeof(Caster).Module,
            skipVisibility: true);
        Emit(dm.GetILGenerator(), typeof(TFrom), typeof(TTo));
        return dm.CreateDelegate<CastFunc<TFrom, TTo>>();
    }

    private static void Emit(ILGenerator il, Type f, Type t) {
        Type? uF = Nullable.GetUnderlyingType(f), uT = Nullable.GetUnderlyingType(t);
        Type cF = uF ?? f, cT = uT ?? t;
        bool fromNull = uF is not null, toNull = uT is not null;

        if (!f.IsValueType && !t.IsValueType && t.IsAssignableFrom(f)) {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_0);
            Store(il, t);
            Return(il, true);
            return;
        }
        if (f.IsValueType && !t.IsValueType && t.IsAssignableFrom(f)) {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Box, f);
            il.Emit(OpCodes.Stind_Ref);
            Return(il, true);
            return;
        }
        if (cF == cT && cF.IsValueType) {
            EmitLifted(il, cF, cT, fromNull, toNull, static _ => { });
            return;
        }
        if (cF.IsValueType && cT.IsValueType) {
            Type? reprF = Repr(cF), reprT = Repr(cT);
            if (reprF is not null && reprT is not null) {
                EmitLifted(il, cF, cT, fromNull, toNull, l => EmitNumeric(l, reprF, reprT));
                return;
            }
        }
        if (f == typeof(string)) {
            if (cT.IsEnum) {
                EmitStringToEnum(il, cT, toNull);
                return;
            }
            if (Caster.IsParsable(cT)) {
                EmitParse(il, cT, toNull);
                return;
            }
        }
        if (Caster.TryGetOperator(cF, cT, out var op)) {
            EmitLifted(il, cF, cT, fromNull, toNull, l => l.Emit(OpCodes.Call, op));
            return;
        }
        if (f == typeof(object)) {
            EmitObject(il, cT, toNull);
            return;
        }
        if (typeof(IConvertible).IsAssignableFrom(f)) {
            var bridge = typeof(ConvertibleBridge<,>).MakeGenericType(f, t);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, bridge.GetMethod("Execute")!);
            il.Emit(OpCodes.Ret);
            return;
        }

        if (t == typeof(string)) {
            EmitToString(il, f);
            return;
        }
        StoreDefault(il, t);
        Return(il, false);
    }

    /// <summary>
    /// Wraps a value-to-value conversion into the four nullability combinations. <paramref name="convert"/>
    /// assumes the present <paramref name="cF"/> value is on the stack and leaves a <paramref name="cT"/>
    /// value; the wrapper adds the missing-input checks and the result wrap or unwrap around it.
    /// </summary>
    private static void EmitLifted(ILGenerator il, Type cF, Type cT, bool fromNull, bool toNull, Action<ILGenerator> convert) {
        Type? nullT = toNull ? typeof(Nullable<>).MakeGenericType(cT) : null;

        if (!fromNull) {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_0);
            convert(il);
            StoreResult(il, cT, nullT);
            Return(il, true);
            return;
        }

        Type nullF = typeof(Nullable<>).MakeGenericType(cF);
        Label absent = il.DefineLabel();
        il.Emit(OpCodes.Ldarga_S, (byte)0);
        il.Emit(OpCodes.Call, nullF.GetMethod("get_HasValue")!);
        il.Emit(OpCodes.Brfalse, absent);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarga_S, (byte)0);
        il.Emit(OpCodes.Call, nullF.GetMethod("get_Value")!);
        convert(il);
        StoreResult(il, cT, nullT);
        Return(il, true);

        il.MarkLabel(absent);
        if (toNull) {
            StoreDefault(il, nullT!);
            Return(il, true);
        }
        else {
            StoreDefault(il, cT);
            Return(il, false);
        }
    }

    /// <summary>Stores a <paramref name="cT"/> value already on the stack into the result, wrapping it when the target is nullable.</summary>
    private static void StoreResult(ILGenerator il, Type cT, Type? nullT) {
        if (nullT is not null) {
            il.Emit(OpCodes.Newobj, nullT.GetConstructor([cT])!);
            il.Emit(OpCodes.Stobj, nullT);
        }
        else {
            Store(il, cT);
        }
    }

    /// <summary>Reinterprets a numeric or enum representation to another, truncating the value. Same representations need no work.</summary>
    private static void EmitNumeric(ILGenerator il, Type reprF, Type reprT) {
        if (reprF == reprT)
            return;
        var create = typeof(INumberBase<>).MakeGenericType(reprT).GetMethod("CreateTruncating")!.MakeGenericMethod(reprF);
        il.Emit(OpCodes.Constrained, reprT);
        il.Emit(OpCodes.Call, create);
    }

    private static void EmitStringToEnum(ILGenerator il, Type cT, bool toNull) {
        var tryParse = typeof(Enum).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == "TryParse" && m.IsGenericMethodDefinition
                && m.GetParameters() is [var p0, var p1, _] && p0.ParameterType == typeof(string) && p1.ParameterType == typeof(bool))
            .MakeGenericMethod(cT);
        if (!toNull) {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, tryParse);
            il.Emit(OpCodes.Ret);
            return;
        }
        EmitParseIntoNullable(il, cT, l => {
            l.Emit(OpCodes.Ldarg_0);
            l.Emit(OpCodes.Ldc_I4_1);
        }, tryParse, constrainedOn: null);
    }

    private static void EmitParse(ILGenerator il, Type cT, bool toNull) {
        var tryParse = typeof(IParsable<>).MakeGenericType(cT).GetMethod("TryParse")!;
        var invariant = typeof(CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!;
        if (!toNull) {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, invariant);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Constrained, cT);
            il.Emit(OpCodes.Call, tryParse);
            il.Emit(OpCodes.Ret);
            return;
        }
        EmitParseIntoNullable(il, cT, l => {
            l.Emit(OpCodes.Ldarg_0);
            l.Emit(OpCodes.Call, invariant);
        }, tryParse, constrainedOn: cT);
    }

    /// <summary>
    /// Runs a <c>bool TryParse(..., out cT)</c> into a temporary and, on success, wraps it into the nullable
    /// result. <paramref name="pushArgs"/> pushes every argument before the out parameter.
    /// </summary>
    private static void EmitParseIntoNullable(ILGenerator il, Type cT, Action<ILGenerator> pushArgs, MethodInfo tryParse, Type? constrainedOn) {
        Type nullT = typeof(Nullable<>).MakeGenericType(cT);
        var tmp = il.DeclareLocal(cT);
        var fail = il.DefineLabel();
        pushArgs(il);
        il.Emit(OpCodes.Ldloca_S, (byte)tmp.LocalIndex);
        if (constrainedOn is not null)
            il.Emit(OpCodes.Constrained, constrainedOn);
        il.Emit(OpCodes.Call, tryParse);
        il.Emit(OpCodes.Brfalse, fail);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, tmp);
        il.Emit(OpCodes.Newobj, nullT.GetConstructor([cT])!);
        il.Emit(OpCodes.Stobj, nullT);
        Return(il, true);
        il.MarkLabel(fail);
        StoreDefault(il, nullT);
        Return(il, false);
    }

    private static void EmitObject(ILGenerator il, Type cT, bool toNull) {
        if (cT == typeof(string)) {
            EmitToString(il, typeof(object));
            return;
        }
        var bridge = (toNull ? typeof(ObjectNullableBridge<>) : typeof(ObjectBridge<>)).MakeGenericType(cT);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, bridge.GetMethod("Execute")!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitToString(ILGenerator il, Type f) {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(ToStringBridge<>).MakeGenericType(f).GetMethod("Execute")!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>The numeric or enum underlying type used to reinterpret a value, or null when it is neither.</summary>
    private static Type? Repr(Type t)
        => t.IsEnum ? Enum.GetUnderlyingType(t) : Caster.IsNumeric(t) ? t : null;

    private static void Store(ILGenerator il, Type type) {
        if (type.IsValueType)
            il.Emit(OpCodes.Stobj, type);
        else
            il.Emit(OpCodes.Stind_Ref);
    }

    private static void StoreDefault(ILGenerator il, Type type) {
        il.Emit(OpCodes.Ldarg_1);
        if (type.IsValueType) {
            il.Emit(OpCodes.Initobj, type);
        }
        else {
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Stind_Ref);
        }
    }

    private static void Return(ILGenerator il, bool value) {
        il.Emit(value ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }
}
internal static class ObjectBridge<TTo> {
    private static readonly bool IsEnum = typeof(TTo).IsEnum;
    private static readonly Type EnumUnderlying = IsEnum ? Enum.GetUnderlyingType(typeof(TTo)) : typeof(TTo);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Execute(object? input, out TTo output) {
        if (input is null || input is DBNull) { output = default!; return true; }
        if (IsEnum && TryReadEnum(input, out var asEnum)) {
            output = asEnum;
            return true;
        }
        if (input is IConvertible conv) {
            try {
                output = Dispatch(conv);
                return true;
            }
            catch { }
        }
        output = default!;
        return false;
    }
    internal static bool TryReadEnum(object input, [MaybeNullWhen(false)] out TTo output) {
        try {
            if (input is string s) {
                if (Enum.TryParse(typeof(TTo), s, true, out var parsed)) {
                    output = (TTo)parsed;
                    return true;
                }
                output = default;
                return false;
            }
            object val = input;
            if (val is float || val is double || val is decimal)
                val = Convert.ChangeType(val, EnumUnderlying, CultureInfo.InvariantCulture);
            output = (TTo)Enum.ToObject(typeof(TTo), val);
            return true;
        }
        catch {
            output = default;
            return false;
        }
    }
    internal static TTo Dispatch(IConvertible conv) {
        if (typeof(TTo) == typeof(bool)) { var v = conv.ToBoolean(CultureInfo.InvariantCulture); return Unsafe.As<bool, TTo>(ref v); }
        if (typeof(TTo) == typeof(byte)) { var v = conv.ToByte(CultureInfo.InvariantCulture); return Unsafe.As<byte, TTo>(ref v); }
        if (typeof(TTo) == typeof(char)) { var v = conv.ToChar(CultureInfo.InvariantCulture); return Unsafe.As<char, TTo>(ref v); }
        if (typeof(TTo) == typeof(short)) { var v = conv.ToInt16(CultureInfo.InvariantCulture); return Unsafe.As<short, TTo>(ref v); }
        if (typeof(TTo) == typeof(int)) { var v = conv.ToInt32(CultureInfo.InvariantCulture); return Unsafe.As<int, TTo>(ref v); }
        if (typeof(TTo) == typeof(long)) { var v = conv.ToInt64(CultureInfo.InvariantCulture); return Unsafe.As<long, TTo>(ref v); }
        if (typeof(TTo) == typeof(float)) { var v = conv.ToSingle(CultureInfo.InvariantCulture); return Unsafe.As<float, TTo>(ref v); }
        if (typeof(TTo) == typeof(double)) { var v = conv.ToDouble(CultureInfo.InvariantCulture); return Unsafe.As<double, TTo>(ref v); }
        if (typeof(TTo) == typeof(decimal)) { var v = conv.ToDecimal(CultureInfo.InvariantCulture); return Unsafe.As<decimal, TTo>(ref v); }
        if (typeof(TTo) == typeof(DateTime)) { var v = conv.ToDateTime(CultureInfo.InvariantCulture); return Unsafe.As<DateTime, TTo>(ref v); }
        if (typeof(TTo) == typeof(sbyte)) { var v = conv.ToSByte(CultureInfo.InvariantCulture); return Unsafe.As<sbyte, TTo>(ref v); }
        if (typeof(TTo) == typeof(ushort)) { var v = conv.ToUInt16(CultureInfo.InvariantCulture); return Unsafe.As<ushort, TTo>(ref v); }
        if (typeof(TTo) == typeof(uint)) { var v = conv.ToUInt32(CultureInfo.InvariantCulture); return Unsafe.As<uint, TTo>(ref v); }
        if (typeof(TTo) == typeof(ulong)) { var v = conv.ToUInt64(CultureInfo.InvariantCulture); return Unsafe.As<ulong, TTo>(ref v); }
        return (TTo)conv.ToType(typeof(TTo), CultureInfo.InvariantCulture);
    }
}
internal static class ObjectNullableBridge<TTo> where TTo : struct {
    private static readonly bool IsEnum = typeof(TTo).IsEnum;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Execute(object? input, out TTo? output) {
        if (input is null || input is DBNull) { output = null; return true; }
        if (IsEnum && ObjectBridge<TTo>.TryReadEnum(input, out var enumValue)) {
            output = enumValue;
            return true;
        }
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

internal static class ConvertibleBridge<TFrom, TTo> where TFrom : IConvertible {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Execute(TFrom input, out TTo output) {
        try {
            output = ObjectBridge<TTo>.Dispatch(input);
            return true;
        }
        catch {
            output = default!;
            return false;
        }
    }
}

internal static class ToStringBridge<TIn> {
    public static bool Execute(TIn input, out string? output) {
        if (input is null || input is DBNull) { output = null; return true; }
        output = input.ToString();
        return true;
    }
}
