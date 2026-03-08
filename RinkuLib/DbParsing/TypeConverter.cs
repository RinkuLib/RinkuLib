using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;

namespace RinkuLib.DbParsing; 
/// <summary>Emitting the conversion code to get from one type to another</summary>
public interface ITypeConverter {
#pragma warning disable CA2211
    /// <summary>The culture to use for the parsing</summary>
    public static IFormatProvider FormatProvider = CultureInfo.InvariantCulture;
#pragma warning restore CA2211
    /// <summary>The field for emit to use for the parsing</summary>
    public static readonly FieldInfo _providerField = typeof(ITypeConverter).GetField(nameof(FormatProvider))!;
    /// <summary>The final type after conversion</summary>
    public Type OutputType { get; }
    /// <summary>Emit the conversion of the item currently on the stack</summary>
    public void EmitConversion(Generator generator, Type sourceType);
    private const BindingFlags staticFlags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
    /// <summary>
    /// Try to get the conversion emiter to convert from source  to output type
    /// </summary>
    public static bool TryGetConverter(Type sourceType, Type outputType, [MaybeNullWhen(false)] out ITypeConverter converter) {
        converter = null;
        var t = Nullable.GetUnderlyingType(outputType);
        if (t is not null) {
            if (TryGetConverter(sourceType, t, out var baseConverter)) {
                converter = new NullableWrapperConverter(baseConverter);
                return true;
            }
            return false;
        }
        outputType = t ?? outputType;
        if (sourceType.IsEnum)
            sourceType = Enum.GetUnderlyingType(sourceType);
        if (outputType.IsEnum)
            outputType = Enum.GetUnderlyingType(outputType);
        if (outputType.IsAssignableFrom(sourceType)) {
            converter = sourceType.IsValueType && !outputType.IsValueType
                ? new BoxConverter(outputType)
                : new IdentityConverter(outputType);
            return true;
        }

        if (!sourceType.IsValueType && !outputType.IsValueType) {
            if (sourceType.IsAssignableFrom(outputType)) {
                converter = new CastClassConverter(outputType);
                return true;
            }
        }

        if (sourceType.IsPrimitive && outputType.IsPrimitive) {
            if (outputType == typeof(IntPtr)) { converter = new OpCodeConverter(outputType, OpCodes.Conv_I); return true; }
            if (outputType == typeof(UIntPtr)) { converter = new OpCodeConverter(outputType, OpCodes.Conv_U); return true; }

            var opCode = Type.GetTypeCode(outputType) switch {
                TypeCode.SByte => OpCodes.Conv_I1,
                TypeCode.Int16 => OpCodes.Conv_I2,
                TypeCode.Int32 => OpCodes.Conv_I4,
                TypeCode.Int64 => OpCodes.Conv_I8,
                TypeCode.Byte => OpCodes.Conv_U1,
                TypeCode.UInt16 => OpCodes.Conv_U2,
                TypeCode.UInt32 => OpCodes.Conv_U4,
                TypeCode.UInt64 => OpCodes.Conv_U8,
                TypeCode.Single => OpCodes.Conv_R4,
                TypeCode.Double => OpCodes.Conv_R8,
                TypeCode.Boolean => OpCodes.Conv_U1,
                TypeCode.Char => OpCodes.Conv_U2,
                _ => (OpCode?)null
            };

            if (opCode.HasValue) {
                converter = new OpCodeConverter(outputType, opCode.Value);
                return true;
            }
        }

        if (sourceType == typeof(string)) {
            Type parsable = typeof(IParsable<>).MakeGenericType(outputType);
            if (parsable.IsAssignableFrom(outputType)) {
                converter = new ParsableConverter(outputType);
                return true;
            }
            var parse = outputType.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, null, [typeof(string)], null);
            if (parse != null) {
                converter = new MethodCallConverter(parse);
                return true;
            }
        }

        if (outputType == typeof(string)) {
            converter = new ToStringConverter();
            return true;
        }

        Type[] pArr = [sourceType];

        var ctor = outputType.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, pArr, null);
        if (ctor is not null) {
            converter = new ConstructorConverter(ctor);
            return true;
        }

        var method = outputType.GetMethod("op_Implicit", staticFlags, null, pArr, null)
                ?? outputType.GetMethod("op_Explicit", staticFlags, null, pArr, null)
                ?? sourceType.GetMethod("op_Implicit", staticFlags, null, pArr, null)
                ?? sourceType.GetMethod("op_Explicit", staticFlags, null, pArr, null);

        if (method != null && method.ReturnType == outputType) {
            converter = new MethodCallConverter(method);
            return true;
        }
        return false;
    }
}
/// <summary>No-op: Source and Target are already compatible.</summary>
public class IdentityConverter(Type type) : ITypeConverter {
    /// <inheritdoc/>
    public Type OutputType { get; } = type;
    /// <inheritdoc/>
    public void EmitConversion(Generator generator, Type sourceType) { }
}

/// <summary>Primitive IL-level casts (e.g., Conv_I4, Conv_R8).</summary>
public class OpCodeConverter(Type target, OpCode op) : ITypeConverter {
    /// <inheritdoc/>
    public Type OutputType { get; } = target; private readonly OpCode _op = op;
    /// <inheritdoc/>
    public void EmitConversion(Generator generator, Type sourceType) => generator.Emit(_op);
}
/// <summary>Boxes a value type into an object/interface reference.</summary>
public class BoxConverter(Type targetType) : ITypeConverter {
    /// <inheritdoc/>
    public Type OutputType { get; } = targetType;
    /// <inheritdoc/>
    public void EmitConversion(Generator generator, Type sourceType) {
        generator.Emit(OpCodes.Box, sourceType);
        if (OutputType != typeof(object) && OutputType != sourceType)
            generator.Emit(OpCodes.Castclass, OutputType);
    }
}

/// <summary>Reference type casting (object -> Class/Interface).</summary>
public class CastClassConverter(Type targetType) : ITypeConverter {
    /// <inheritdoc/>
    public Type OutputType { get; } = targetType;

    /// <inheritdoc/>
    public void EmitConversion(Generator generator, Type sourceType) => generator.Emit(OpCodes.Castclass, OutputType);
}

/// <summary>Calls static op_Implicit or op_Explicit methods.</summary>
public class MethodCallConverter(MethodInfo method) : ITypeConverter {
    /// <inheritdoc/>
    public Type OutputType { get; } = method.ReturnType;
    private readonly MethodInfo _method = method;
    /// <inheritdoc/>
    public void EmitConversion(Generator generator, Type sourceType) => generator.Emit(OpCodes.Call, _method);
}

/// <summary>String to T via IParsable. Uses static Call.</summary>
public class ParsableConverter(Type targetType) : ITypeConverter {
    /// <inheritdoc/>
    public Type OutputType { get; } = targetType;

    /// <inheritdoc/>
    public void EmitConversion(Generator generator, Type sourceType) {
        var method = OutputType.GetMethod(nameof(IParsable<>.Parse), BindingFlags.Public | BindingFlags.Static, [typeof(string), typeof(IFormatProvider)]);
        if (method is not null && method.ReturnType == OutputType) {
            generator.Emit(OpCodes.Ldsfld, ITypeConverter._providerField);
            generator.Emit(OpCodes.Call, method);
            return;
        }
        method = OutputType.GetMethod(nameof(IParsable<>.Parse), BindingFlags.Public | BindingFlags.Static, [typeof(string)]);
        if (method is not null && method.ReturnType == OutputType) {
            generator.Emit(OpCodes.Call, method);
            return;
        }
        throw new NotImplementedException();
    }
}

/// <summary>T to String using 'constrained' to avoid boxing structs.</summary>
public class ToStringConverter : ITypeConverter {
    /// <inheritdoc/>
    public Type OutputType => typeof(string);
    /// <inheritdoc/>
    public void EmitConversion(Generator generator, Type sourceType) {
        var method = sourceType.GetMethod(nameof(ToString), [typeof(IFormatProvider)]);
        if (method is not null && method.ReturnType == typeof(string)) {
            generator.Emit(OpCodes.Ldsfld, ITypeConverter._providerField);
            generator.Emit(OpCodes.Call, method);
            return;
        }
        method = sourceType.GetMethod(nameof(ToString), Type.EmptyTypes);
        if (method is not null && method.ReturnType == typeof(string)) {
            generator.Emit(OpCodes.Call, method);
            return;
        }
        method = typeof(object).GetMethod(nameof(ToString), Type.EmptyTypes);
        if (method is not null && method.ReturnType == typeof(string)) {
            generator.Emit(OpCodes.Box);
            generator.Emit(OpCodes.Callvirt, method);
            return;
        }
        throw new NotImplementedException();
    }
}
/// <summary>Calls a constructor that accepts the source type as its single argument.</summary>
public class ConstructorConverter(ConstructorInfo ctor) : ITypeConverter {
    /// <inheritdoc/>
    public Type OutputType { get; } = ctor.DeclaringType!;
    private readonly ConstructorInfo _ctor = ctor;

    /// <inheritdoc/>
    public void EmitConversion(Generator generator, Type sourceType) {
        generator.Emit(OpCodes.Newobj, _ctor);
    }
}

/// <summary>Wraps an existing value into <see cref="Nullable{T}"/>.</summary>
public class NullableWrapperConverter(ITypeConverter inner) : ITypeConverter {
    /// <inheritdoc/>
    public Type OutputType { get; } = typeof(Nullable<>).MakeGenericType(inner.OutputType);
    private readonly ITypeConverter _inner = inner;

    /// <inheritdoc/>
    public void EmitConversion(Generator generator, Type sourceType) {
        _inner.EmitConversion(generator, sourceType);
        generator.Emit(OpCodes.Newobj, _inner.OutputType.GetNullableConstructor());
    }
}