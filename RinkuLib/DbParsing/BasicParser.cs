using System.Reflection.Emit;
using RinkuLib.Tools;

namespace RinkuLib.DbParsing;
/// <summary>
/// A terminal parser that emits IL to read a single column from a data reader.
/// Handles null checks, type conversions, and nullable wrapper instantiation.
/// </summary>
public class BasicParser(Type Type, string ParamName, INullColHandler NullColHandler, int Index) : DbItemParser {
    private readonly Type Type = Type;
    private readonly string ParamName = ParamName;
    private readonly INullColHandler NullColHandler = NullColHandler;
    private readonly int Index = Index;
    /// <summary>
    /// Determines if the specific column/handler combination requires a jump target for null values.
    /// </summary>
    public override bool NeedNullSetPoint(ColumnInfo[] cols) => cols[Index].IsNullable && NullColHandler.NeedNullJumpSetPoint(Type);
    /// <inheritdoc/>
    public override bool IsSequencial(ref int previousIndex) {
        if (previousIndex >= Index)
            return false;
        previousIndex = Index;
        return true;
    }
    /// <inheritdoc/>
    public override void Emit(ColumnInfo[] cols, Generator generator, NullSetPoint nullSetPoint) {
        var col = cols[Index];
        var under = Nullable.GetUnderlyingType(Type);
        var meth = col.Type.GetDbMethod();
        var t = under ?? Type;
        if (!col.IsNullable) {
            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Ldc_I4, Index);
            generator.Emit(OpCodes.Callvirt, meth);
            EmitConversion(col.Type, t, generator);
            if (under is not null)
                generator.Emit(OpCodes.Newobj, under.GetNullableConstructor());
            return;
        }
        Label notNull = generator.DefineLabel();
        generator.Emit(OpCodes.Ldarg_0);
        generator.Emit(OpCodes.Ldc_I4, Index);
        generator.Emit(OpCodes.Callvirt, TypeExtensions.IsNull);
        var op = OpCodes.Brfalse_S;
        if (!NullColHandler.IsBr_S(Type) || nullSetPoint.NbOfPopToMake + 5 > 127)
            op = OpCodes.Brfalse;
        generator.Emit(op, notNull);
        Label? endLabel = NullColHandler.HandleNull(Type, ParamName, generator, nullSetPoint);
        generator.MarkLabel(notNull);
        generator.Emit(OpCodes.Ldarg_0);
        generator.Emit(OpCodes.Ldc_I4, Index);
        generator.Emit(OpCodes.Callvirt, meth);
        EmitConversion(col.Type, t, generator);
        if (under is not null)
            generator.Emit(OpCodes.Newobj, under.GetNullableConstructor());
        if (endLabel.HasValue)
            generator.MarkLabel(endLabel.Value);
    }
    /// <summary>
    /// Emits IL to convert values between database types and C# target types.
    /// </summary>
    /// <param name="dbType">The type returned by the DataReader method.</param>
    /// <param name="targetType">The type required by the object property or constructor parameter.</param>
    /// <param name="generator">The IL stream wrapper.</param>
    /// <exception cref="NotSupportedException">Thrown if no valid IL conversion exists.</exception>
    private static void EmitConversion(Type dbType, Type targetType, Generator generator) {
        if (dbType.IsStackEquivalent(targetType))
            return;
        if (!targetType.IsValueType) {
            if (dbType.IsValueType)
                generator.Emit(OpCodes.Box, dbType);
            if (targetType != typeof(object))
                generator.Emit(OpCodes.Castclass, targetType);
            return;
        }
        Type effectiveTarget = targetType.IsEnum ? Enum.GetUnderlyingType(targetType) : targetType;

        if (effectiveTarget.IsPrimitive) {
            switch (Type.GetTypeCode(effectiveTarget)) {
                case TypeCode.Int32:
                    generator.Emit(OpCodes.Conv_I4);
                    break;
                case TypeCode.Int64:
                    generator.Emit(OpCodes.Conv_I8);
                    break;
                case TypeCode.Single:
                    generator.Emit(OpCodes.Conv_R4);
                    break;
                case TypeCode.Double:
                    generator.Emit(OpCodes.Conv_R8);
                    break;
                case TypeCode.Int16:
                    generator.Emit(OpCodes.Conv_I2);
                    break;
                case TypeCode.Byte:
                    generator.Emit(OpCodes.Conv_U1);
                    break;
                case TypeCode.SByte:
                    generator.Emit(OpCodes.Conv_I1);
                    break;
                case TypeCode.UInt32:
                    generator.Emit(OpCodes.Conv_U4);
                    break;
                case TypeCode.UInt64:
                    generator.Emit(OpCodes.Conv_U8);
                    break;
                case TypeCode.Char:
                    generator.Emit(OpCodes.Conv_U2);
                    break;
                case TypeCode.Boolean:
                    generator.Emit(OpCodes.Conv_I1);
                    break;
                default:
                    throw new NotSupportedException($"Unsupported primitive conversion to {effectiveTarget.Name}");
            }
            return;
        }
        throw new NotSupportedException($"No IL conversion exists between {dbType.Name} and {targetType.Name}.");
    }
}