using System.Reflection;
using System.Reflection.Emit;

namespace Rinku.Tracking.Runtime;

internal interface IRuntimeMetadataEmitterConfiguration
{
    Type MetadataType { get; }
    void RequireReader();
    void RequireWriter();
}

internal sealed class RuntimeMetadataEmitter<TOriginal, TMetadata> : IRuntimeTrackingTypeEmitter<TOriginal>, IRuntimeMetadataEmitterConfiguration
{
    internal bool Reader { get; private set; }
    internal bool Writer { get; private set; }
    public Type MetadataType => typeof(TMetadata);

    public void RequireReader() => Reader = true;
    public void RequireWriter() => Writer = true;

    public void Emit(RuntimeTrackingEmitContext<TOriginal> context)
    {
        FieldBuilder metadata = context.TypeBuilder.DefineField($"_metadata_{Sanitize(typeof(TMetadata).Name)}", typeof(TMetadata), FieldAttributes.Private);

        if (Reader)
        {
            Type contractType = typeof(IMetadataReader<TMetadata>);
            context.TypeBuilder.AddInterfaceImplementation(contractType);
            MethodInfo contract = contractType.GetProperty(nameof(IMetadataReader<TMetadata>.Metadata))?.GetMethod
                ?? throw new MissingMethodException(contractType.FullName, $"get_{nameof(IMetadataReader<TMetadata>.Metadata)}");
            MethodBuilder method = DefineExplicit(context.TypeBuilder, contract);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, metadata);
            il.Emit(OpCodes.Ret);
            context.TypeBuilder.DefineMethodOverride(method, contract);
        }

        if (Writer)
        {
            Type contractType = typeof(IMetadataWriter<TMetadata>);
            context.TypeBuilder.AddInterfaceImplementation(contractType);
            MethodInfo contract = contractType.GetMethod(nameof(IMetadataWriter<TMetadata>.SetMetadata))
                ?? throw new MissingMethodException(contractType.FullName, nameof(IMetadataWriter<TMetadata>.SetMetadata));
            MethodBuilder method = DefineExplicit(context.TypeBuilder, contract);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, metadata);
            il.Emit(OpCodes.Ret);
            context.TypeBuilder.DefineMethodOverride(method, contract);
        }
    }

    private static MethodBuilder DefineExplicit(TypeBuilder type, MethodInfo contract)
        => type.DefineMethod(
            $"__metadata_{Sanitize(contract.Name)}_{Sanitize(typeof(TMetadata).Name)}",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            contract.ReturnType,
            contract.GetParameters().Select(static parameter => parameter.ParameterType).ToArray());

    private static string Sanitize(string value)
    {
        char[] chars = value.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_') chars[i] = '_';
        return new string(chars);
    }
}
