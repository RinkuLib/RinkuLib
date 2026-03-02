using System.Reflection.Emit;
using RinkuLib.Tools;

namespace RinkuLib.DbParsing;
/// <summary>
/// A terminal parser that emits IL to read a single column from a data reader.
/// Handles null checks, type conversions, and nullable wrapper instantiation.
/// </summary>
public class BasicParser(Type ParentType, ITypeConverter TypeConverter, string ParamName, INullColHandler NullColHandler, int Index) : DbItemParser {
    private readonly Type ParentType = ParentType;
    private readonly ITypeConverter TypeConverter = TypeConverter;
    private readonly string ParamName = ParamName;
    private readonly INullColHandler NullColHandler = NullColHandler;
    private readonly int Index = Index;
    /// <summary>
    /// Determines if the specific column/handler combination requires a jump target for null values.
    /// </summary>
    public override bool NeedNullSetPoint(ColumnInfo[] cols) => cols[Index].IsNullable && NullColHandler.NeedNullJumpSetPoint(TypeConverter.OutputType);
    /// <inheritdoc/>
    public override bool IsSequencial(ref int previousIndex) {
        if (previousIndex >= Index)
            return false;
        previousIndex = Index;
        return true;
    }
    /// <inheritdoc/>
    public override void Emit(ColumnInfo[] cols, Generator generator, NullSetPoint nullSetPoint, out object? targetObject) {
        targetObject = null;
        var col = cols[Index];
        var meth = col.Type.GetDbMethod();
        if (!col.IsNullable) {
            generator.Emit(OpCodes.Ldarg_1);
            generator.Emit(OpCodes.Ldc_I4, Index);
            generator.Emit(OpCodes.Callvirt, meth);
            TypeConverter.EmitConversion(generator, col.Type);
            return;
        }
        Label notNull = generator.DefineLabel();
        generator.Emit(OpCodes.Ldarg_1);
        generator.Emit(OpCodes.Ldc_I4, Index);
        generator.Emit(OpCodes.Callvirt, TypeExtensions.IsNull);
        var op = OpCodes.Brfalse_S;
        if (!NullColHandler.IsBr_S(TypeConverter.OutputType) || nullSetPoint.NbOfPopToMake + 5 > 127)
            op = OpCodes.Brfalse;
        generator.Emit(op, notNull);
        Label? endLabel = NullColHandler.HandleNull(ParentType, TypeConverter.OutputType, ParamName, generator, nullSetPoint);
        generator.MarkLabel(notNull);
        generator.Emit(OpCodes.Ldarg_1);
        generator.Emit(OpCodes.Ldc_I4, Index);
        generator.Emit(OpCodes.Callvirt, meth);
        TypeConverter.EmitConversion(generator, col.Type);
        if (endLabel.HasValue)
            generator.MarkLabel(endLabel.Value);
    }
}