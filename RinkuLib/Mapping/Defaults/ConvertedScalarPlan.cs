using System.Reflection.Emit;
using Rinku.Internal;

namespace Rinku.Mapping.Defaults;

/// <summary>A terminal plan that reads a column through the standard typed reader API and emits a conversion.</summary>
public sealed class ConvertedScalarPlan(Type parentType, ITypeConverter converter, string parameterName,
    INullColHandler nullHandler, int ordinal)
    : ScalarDbItemPlan(parentType, converter.OutputType, parameterName, nullHandler, ordinal) {
    /// <summary>The conversion emitted after the standard column read.</summary>
    public ITypeConverter Converter { get; } = converter;

    /// <inheritdoc/>
    protected override void EmitValue(ColumnInfo column, Generator generator, out object? targetObject) {
        targetObject = null;
        generator.Emit(OpCodes.Ldarg_1);
        generator.Emit(OpCodes.Ldc_I4, ColumnOrdinal);
        var method = column.Type.GetDbMethod();
        generator.Emit(OpCodes.Callvirt, method);
        EmitUnwrap(generator, method, column.Type);
        Converter.EmitConversion(generator, column.Type);
    }

    /// <summary>
    /// A column type outside the reader's typed getters is fetched as <see cref="Nullable{T}"/>; the value is
    /// known non-null here, so it is unwrapped to leave the column type itself on the stack.
    /// </summary>
    private static void EmitUnwrap(Generator generator, System.Reflection.MethodInfo method, Type columnType) {
        if (method.ReturnType == columnType)
            return;
        var local = generator.DeclareLocal(method.ReturnType);
        generator.Emit(OpCodes.Stloc, local);
        generator.Emit(OpCodes.Ldloca, local);
        generator.Emit(OpCodes.Call, method.ReturnType.GetMethod(nameof(Nullable<int>.GetValueOrDefault), Type.EmptyTypes)!);
    }
}
