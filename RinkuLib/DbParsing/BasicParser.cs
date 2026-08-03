using System.Reflection.Emit;
using RinkuLib.Tools;

namespace RinkuLib.DbParsing;
/// <summary>
/// A terminal parser that emits IL to read a single column from a data reader.
/// Handles null checks, type conversions, and nullable wrapper instantiation.
/// </summary>
internal sealed class BasicParser(Type ParentType, ITypeConverter TypeConverter, string ParamName, INullColHandler NullColHandler, int Index, IColumnReader? ColumnReader = null) : SimpleDbItemParser {
    private readonly Type ParentType = ParentType;
    private readonly ITypeConverter TypeConverter = TypeConverter;
    private readonly string ParamName = ParamName;
    private readonly INullColHandler NullColHandler = NullColHandler;
    private readonly int Index = Index;
    private readonly IColumnReader? ColumnReader = ColumnReader;
    /// <summary>The column ordinal this node reads, used by the multi-row emit to null-check a sub-level's key.</summary>
    internal int ColumnIndex => Index;
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
        var valueType = ColumnReader?.ValueType ?? col.Type;
        if (!col.IsNullable) {
            generator.Emit(OpCodes.Ldarg_1);
            generator.Emit(OpCodes.Ldc_I4, Index);
            EmitRead(generator, col, meth, valueType);
            TypeConverter.EmitConversion(generator, valueType);
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
        EmitRead(generator, col, meth, valueType);
        TypeConverter.EmitConversion(generator, valueType);
        if (endLabel.HasValue)
            generator.MarkLabel(endLabel.Value);
    }
    private void EmitRead(Generator generator, ColumnInfo col, System.Reflection.MethodInfo defaultMethod, Type valueType) {
        if (ColumnReader is null) {
            generator.Emit(OpCodes.Callvirt, defaultMethod);
            EmitUnwrap(generator, defaultMethod, valueType);
            return;
        }
        generator.Emit(OpCodes.Ldtoken, col.Type);
        generator.Emit(OpCodes.Call, typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle), [typeof(RuntimeTypeHandle)])!);
        generator.Emit(OpCodes.Call, DbColumnReaderRegistry.ReadRegisteredMethod);
        if (valueType.IsValueType)
            generator.Emit(OpCodes.Unbox_Any, valueType);
        else
            generator.Emit(OpCodes.Castclass, valueType);
    }
    /// <summary>
    /// A column type outside the reader's typed getters is fetched as <see cref="Nullable{T}"/>; the value is
    /// known non-null here, so it is unwrapped to leave the column type itself on the stack.
    /// </summary>
    private static void EmitUnwrap(Generator generator, System.Reflection.MethodInfo meth, Type colType) {
        if (meth.ReturnType == colType)
            return;
        var local = generator.DeclareLocal(meth.ReturnType);
        generator.Emit(OpCodes.Stloc, local);
        generator.Emit(OpCodes.Ldloca, local);
        generator.Emit(OpCodes.Call, meth.ReturnType.GetMethod(nameof(Nullable<int>.GetValueOrDefault), Type.EmptyTypes)!);
    }
}
