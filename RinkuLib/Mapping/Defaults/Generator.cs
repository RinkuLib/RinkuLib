using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using Rinku.Internal;

namespace Rinku.Mapping.Emission;
#if DEBUG
/// <summary>
/// Writes parser instructions and provides reusable local variables. Custom read plans receive this type in
/// their write methods.
/// </summary>
public class Generator(ILGenerator generator, ColumnInfo[] cols) : ILGenerator {
#pragma warning disable CA2211
    internal static Action<string> Write = Console.WriteLine;
#pragma warning restore CA2211
    internal readonly ILGenerator Il = generator;
    internal readonly ColumnInfo[] Columns = cols;
    private readonly Dictionary<Type, LocalBuilder> LocalCache = [];
    private readonly Dictionary<Label, string> LabelNames = [];
    private readonly Dictionary<Label, int> FingerprintLabels = [];
    private readonly EmissionFingerprintBuilder FingerprintBuilder = new();
    private readonly List<object> GeneratedTargets = [];

    private int labelCounter = 0;
    internal EmissionFingerprint Fingerprint => FingerprintBuilder.Value;

    internal void EmitTarget(object target) {
        int index = GeneratedTargets.Count;
        GeneratedTargets.Add(target);
        Emit(OpCodes.Ldarg_0);
        Emit(OpCodes.Ldc_I4, index);
        Emit(OpCodes.Ldelem_Ref);
        Emit(OpCodes.Castclass, target.GetType());
    }

    internal object[] GetTargets() => GeneratedTargets.ToArray();

    private int LabelId(Label label) {
        if (!FingerprintLabels.TryGetValue(label, out int id))
            FingerprintLabels[label] = id = FingerprintLabels.Count;
        return id;
    }

    private void Record(OpCode opcode, int kind) {
        FingerprintBuilder.Add(opcode);
        FingerprintBuilder.Add(kind);
    }

    /// <summary>Gets a local for the given type and reuses one when available.</summary>
    public LocalBuilder GetLocal(Type type) {
        if (LocalCache.TryGetValue(type, out var local)) {
            Write($"[IL] ReuseLocal type={type.ShortName()} index={local.LocalIndex}");
            return local;
        }

        local = DeclareLocal(type, false);
        LocalCache[type] = local;

        Write($"[IL] DeclareLocal type={type.ShortName()} index={local.LocalIndex}");
        return local;
    }
    /// <inheritdoc/>
    public override LocalBuilder DeclareLocal(Type localType, bool pinned) {
        var loc = Il.DeclareLocal(localType, pinned);
        FingerprintBuilder.Add(0x100);
        FingerprintBuilder.Add(localType);
        FingerprintBuilder.Add(pinned);
        Write($"[IL] DeclareLocal type={localType.ShortName()} pinned={pinned} index={loc.LocalIndex}");
        return loc;
    }


    /// <inheritdoc/>
    public override Label DefineLabel() {
        var label = Il.DefineLabel();
        var name = $"L{labelCounter++:000}";
        LabelNames[label] = name;
        LabelId(label);
        return label;
    }

    /// <inheritdoc/>
    public override void MarkLabel(Label loc) {
        var name = LabelNames.TryGetValue(loc, out var n) ? n : "(unknown)";
        Write($"[IL] MarkLabel {name}");
        FingerprintBuilder.Add(0x101);
        FingerprintBuilder.Add(LabelId(loc));
        Il.MarkLabel(loc);
    }


    /// <inheritdoc/>
    public override int ILOffset => Il.ILOffset;

    /// <inheritdoc/>
    public override void Emit(OpCode opcode) {
        Write($"[IL] Emit {opcode}");
        Record(opcode, 0);
        Il.Emit(opcode);
    }

    /// <inheritdoc/>
    public override void Emit(OpCode opcode, byte arg) {
        Write($"[IL] Emit {opcode} byte={arg}");
        Record(opcode, 1); FingerprintBuilder.Add(arg);
        Il.Emit(opcode, arg);
    }

    /// <inheritdoc/>
    public override void Emit(OpCode opcode, double arg) {
        Write($"[IL] Emit {opcode} double={arg}");
        Record(opcode, 2); FingerprintBuilder.Add(arg);
        Il.Emit(opcode, arg);
    }

    /// <inheritdoc/>
    public override void Emit(OpCode opcode, short arg) {
        Write($"[IL] Emit {opcode} short={arg}");
        Record(opcode, 3); FingerprintBuilder.Add(arg);
        Il.Emit(opcode, arg);
    }

    /// <inheritdoc/>
    public override void Emit(OpCode opcode, int arg) {
        if (opcode == OpCodes.Ldc_I4 && (uint)arg < Columns.Length)
            Write($"[IL] Emit {opcode} int={arg} probable index for {Columns[arg].Name}");
        else
            Write($"[IL] Emit {opcode} int={arg}");
        Record(opcode, 4); FingerprintBuilder.Add(arg); Il.Emit(opcode, arg);
    }

    /// <inheritdoc/>
    public override void Emit(OpCode opcode, long arg) {
        Write($"[IL] Emit {opcode} long={arg}");
        Record(opcode, 5); FingerprintBuilder.Add(arg);
        Il.Emit(opcode, arg);
    }

    /// <inheritdoc/>
    public override void Emit(OpCode opcode, float arg) {
        Write($"[IL] Emit {opcode} float={arg}");
        Record(opcode, 6); FingerprintBuilder.Add(arg);
        Il.Emit(opcode, arg);
    }

    /// <inheritdoc/>
    public override void Emit(OpCode opcode, string str) {
        Write($"[IL] Emit {opcode} string=\"{str}\"");
        Record(opcode, 7); FingerprintBuilder.Add(str);
        Il.Emit(opcode, str);
    }

    /// <inheritdoc/>
    public override void Emit(OpCode opcode, Type cls) {
        Write($"[IL] Emit {opcode} type={cls.ShortName()}");
        Record(opcode, 8); FingerprintBuilder.Add(cls);
        Il.Emit(opcode, cls);
    }


    /// <inheritdoc/>
    public override void Emit(OpCode opcode, Label label) {
        var name = LabelNames.TryGetValue(label, out var n) ? n : "(unknown)";
        Write($"[IL] Emit {opcode} -> {name}");
        Record(opcode, 9); FingerprintBuilder.Add(LabelId(label)); Il.Emit(opcode, label);
    }

    /// <inheritdoc/>
    public override void Emit(OpCode opcode, Label[] labels) {
        Write($"[IL] Emit {opcode} labels[{labels.Length}]");
        Record(opcode, 10); FingerprintBuilder.Add(labels.Length);
        foreach (var label in labels) FingerprintBuilder.Add(LabelId(label));
        Il.Emit(opcode, labels);
    }

    /// <inheritdoc/>
    public override void Emit(OpCode opcode, LocalBuilder local) {
        Write($"[IL] Emit {opcode} localIndex={local.LocalIndex} type={local.LocalType.ShortName()}");
        Record(opcode, 11); FingerprintBuilder.Add(local.LocalIndex);
        Il.Emit(opcode, local);
    }

    /// <inheritdoc/>
    public override void Emit(OpCode opcode, ConstructorInfo con) {
        Write(Describe($"[IL] Emit {opcode} ctor ", () => $"{con.DeclaringType.ShortName()}..ctor({ShortParams(con)})", con.Name));
        Record(opcode, 12); FingerprintBuilder.Add(con); Il.Emit(opcode, con);
    }

    /// <inheritdoc/>
    public override void Emit(OpCode opcode, MethodInfo meth) {
        Write(Describe($"[IL] Emit {opcode} ", () => $"{meth.ReturnType.ShortName()} {meth.DeclaringType.ShortName()}.{meth.Name}({ShortParams(meth)})", meth.Name));
        Record(opcode, 13); FingerprintBuilder.Add(meth); Il.Emit(opcode, meth);
    }

    /// <inheritdoc/>
    public override void Emit(OpCode opcode, FieldInfo field) {
        Write(Describe($"[IL] Emit {opcode} ", () => $"{field.FieldType.ShortName()} {field.DeclaringType.ShortName()}.{field.Name}", field.Name));
        Record(opcode, 14); FingerprintBuilder.Add(field); Il.Emit(opcode, field);
    }
    private static string ShortParams(MethodBase method) {
        return string.Join(", ", method.GetParameters().Select(p => p.ParameterType.ShortName()));
    }
    private static string Describe(string prefix, Func<string> full, string name) {
        try {
            return prefix + full();
        }
        catch (NotSupportedException) {
            return prefix + name;
        }
    }

    /// <inheritdoc/>
    public override void Emit(OpCode opcode, SignatureHelper signature) {
        Write($"[IL] Emit {opcode} signature");
        Record(opcode, 15); FingerprintBuilder.Add(signature);
        Il.Emit(opcode, signature);
    }


    /// <inheritdoc/>
    public override void EmitCall(OpCode opcode, MethodInfo methodInfo, Type[]? optionalParameterTypes) {
        Write($"[IL] EmitCall {opcode} method={methodInfo.DeclaringType.ShortName()}.{methodInfo.Name}({ShortParams(methodInfo)})");
        Record(opcode, 16); FingerprintBuilder.Add(methodInfo); FingerprintBuilder.Add(optionalParameterTypes); Il.EmitCall(opcode, methodInfo, optionalParameterTypes);
    }

    /// <inheritdoc/>
    public override void EmitCalli(OpCode opcode, CallingConventions callingConvention, Type? returnType, Type[]? parameterTypes, Type[]? optionalParameterTypes) {
        Write($"[IL] EmitCalli {opcode} conv={callingConvention}");
        Record(opcode, 17); FingerprintBuilder.Add((int)callingConvention); FingerprintBuilder.Add(returnType); FingerprintBuilder.Add(parameterTypes); FingerprintBuilder.Add(optionalParameterTypes); Il.EmitCalli(opcode, callingConvention, returnType, parameterTypes, optionalParameterTypes);
    }

    /// <inheritdoc/>
    public override void EmitCalli(OpCode opcode, CallingConvention unmanagedCallConv, Type? returnType, Type[]? parameterTypes) {
        Write($"[IL] EmitCalli {opcode} unmanaged={unmanagedCallConv}");
        Record(opcode, 18); FingerprintBuilder.Add((int)unmanagedCallConv); FingerprintBuilder.Add(returnType); FingerprintBuilder.Add(parameterTypes); Il.EmitCalli(opcode, unmanagedCallConv, returnType, parameterTypes);
    }


    /// <inheritdoc/>
    public override Label BeginExceptionBlock() {
        Write("[IL] BeginExceptionBlock");
        FingerprintBuilder.Add(0x112);
        var label = Il.BeginExceptionBlock();
        LabelId(label);
        return label;
    }

    /// <inheritdoc/>
    public override void EndExceptionBlock() {
        Write("[IL] EndExceptionBlock");
        FingerprintBuilder.Add(0x116);
        Il.EndExceptionBlock();
    }

    /// <inheritdoc/>
    public override void BeginCatchBlock(Type? exceptionType) {
        Write($"[IL] BeginCatchBlock type={exceptionType.ShortName()}");
        FingerprintBuilder.Add(0x110); FingerprintBuilder.Add(exceptionType);
        Il.BeginCatchBlock(exceptionType);
    }

    /// <inheritdoc/>
    public override void BeginExceptFilterBlock() {
        Write("[IL] BeginExceptFilterBlock");
        FingerprintBuilder.Add(0x111);
        Il.BeginExceptFilterBlock();
    }

    /// <inheritdoc/>
    public override void BeginFaultBlock() {
        Write("[IL] BeginFaultBlock");
        FingerprintBuilder.Add(0x113);
        Il.BeginFaultBlock();
    }

    /// <inheritdoc/>
    public override void BeginFinallyBlock() {
        Write("[IL] BeginFinallyBlock");
        FingerprintBuilder.Add(0x114);
        Il.BeginFinallyBlock();
    }

    /// <inheritdoc/>
    public override void BeginScope() {
        Write("[IL] BeginScope");
        FingerprintBuilder.Add(0x115);
        Il.BeginScope();
    }

    /// <inheritdoc/>
    public override void EndScope() {
        Write("[IL] EndScope");
        FingerprintBuilder.Add(0x117);
        Il.EndScope();
    }

    /// <inheritdoc/>
    public override void UsingNamespace(string usingNamespace) {
        Write($"[IL] UsingNamespace {usingNamespace}");
        FingerprintBuilder.Add(0x118); FingerprintBuilder.Add(usingNamespace);
        Il.UsingNamespace(usingNamespace);
    }
}
#else
/// <summary>
/// Writes parser instructions and provides reusable local variables. Custom read plans receive this type in
/// their write methods.
/// </summary>
public class Generator(ILGenerator generator) : ILGenerator {
    internal readonly ILGenerator Il = generator;
    private readonly Dictionary<Type, LocalBuilder> LocalCache = [];
    private readonly Dictionary<Label, int> FingerprintLabels = [];
    private readonly EmissionFingerprintBuilder FingerprintBuilder = new();
    private readonly List<object> GeneratedTargets = [];
    internal EmissionFingerprint Fingerprint => FingerprintBuilder.Value;
    internal void EmitTarget(object target) {
        int index = GeneratedTargets.Count;
        GeneratedTargets.Add(target);
        Emit(OpCodes.Ldarg_0);
        Emit(OpCodes.Ldc_I4, index);
        Emit(OpCodes.Ldelem_Ref);
        Emit(OpCodes.Castclass, target.GetType());
    }
    internal object[] GetTargets() => GeneratedTargets.ToArray();
    private int LabelId(Label label) {
        if (!FingerprintLabels.TryGetValue(label, out int id))
            FingerprintLabels[label] = id = FingerprintLabels.Count;
        return id;
    }
    private void Record(OpCode opcode, int kind) { FingerprintBuilder.Add(opcode); FingerprintBuilder.Add(kind); }
    /// <summary>Gets a local for the given type and reuses one when available.</summary>
    public LocalBuilder GetLocal(Type type) {
        if (LocalCache.TryGetValue(type, out var local))
            return local;
        local = DeclareLocal(type, false);
        LocalCache[type] = local;
        return local;
    }
    /// <inheritdoc/>
    public override int ILOffset => Il.ILOffset;
    /// <inheritdoc/>
    public override void BeginCatchBlock(Type? exceptionType) { FingerprintBuilder.Add(0x110); FingerprintBuilder.Add(exceptionType); Il.BeginCatchBlock(exceptionType); }
    /// <inheritdoc/>
    public override void BeginExceptFilterBlock() { FingerprintBuilder.Add(0x111); Il.BeginExceptFilterBlock(); }
    /// <inheritdoc/>
    public override Label BeginExceptionBlock() { FingerprintBuilder.Add(0x112); var label = Il.BeginExceptionBlock(); LabelId(label); return label; }
    /// <inheritdoc/>
    public override void BeginFaultBlock() { FingerprintBuilder.Add(0x113); Il.BeginFaultBlock(); }
    /// <inheritdoc/>
    public override void BeginFinallyBlock() { FingerprintBuilder.Add(0x114); Il.BeginFinallyBlock(); }
    /// <inheritdoc/>
    public override void BeginScope() { FingerprintBuilder.Add(0x115); Il.BeginScope(); }
    /// <inheritdoc/>
    public override LocalBuilder DeclareLocal(Type localType, bool pinned) { FingerprintBuilder.Add(0x100); FingerprintBuilder.Add(localType); FingerprintBuilder.Add(pinned); return Il.DeclareLocal(localType, pinned); }
    /// <inheritdoc/>
    public override Label DefineLabel() { var label = Il.DefineLabel(); LabelId(label); return label; }
    /// <inheritdoc/>
    public override void Emit(OpCode opcode) { Record(opcode, 0); Il.Emit(opcode); }

    /// <inheritdoc/>
    public override void Emit(OpCode opcode, byte arg) { Record(opcode, 1); FingerprintBuilder.Add(arg); Il.Emit(opcode, arg); }
    /// <inheritdoc/>
    public override void Emit(OpCode opcode, double arg) { Record(opcode, 2); FingerprintBuilder.Add(arg); Il.Emit(opcode, arg); }
    /// <inheritdoc/>
    public override void Emit(OpCode opcode, short arg) { Record(opcode, 3); FingerprintBuilder.Add(arg); Il.Emit(opcode, arg); }
    /// <inheritdoc/>
    public override void Emit(OpCode opcode, int arg) { Record(opcode, 4); FingerprintBuilder.Add(arg); Il.Emit(opcode, arg); }
    /// <inheritdoc/>
    public override void Emit(OpCode opcode, long arg) { Record(opcode, 5); FingerprintBuilder.Add(arg); Il.Emit(opcode, arg); }
    /// <inheritdoc/>
    public override void Emit(OpCode opcode, ConstructorInfo con) { Record(opcode, 12); FingerprintBuilder.Add(con); Il.Emit(opcode, con); }
    /// <inheritdoc/>
    public override void Emit(OpCode opcode, Label label) { Record(opcode, 9); FingerprintBuilder.Add(LabelId(label)); Il.Emit(opcode, label); }
    /// <inheritdoc/>
    public override void Emit(OpCode opcode, Label[] labels) { Record(opcode, 10); FingerprintBuilder.Add(labels.Length); foreach (var label in labels) FingerprintBuilder.Add(LabelId(label)); Il.Emit(opcode, labels); }
    /// <inheritdoc/>
    public override void Emit(OpCode opcode, LocalBuilder local) { Record(opcode, 11); FingerprintBuilder.Add(local.LocalIndex); Il.Emit(opcode, local); }
    /// <inheritdoc/>
    public override void Emit(OpCode opcode, SignatureHelper signature) { Record(opcode, 15); FingerprintBuilder.Add(signature); Il.Emit(opcode, signature); }
    /// <inheritdoc/>
    public override void Emit(OpCode opcode, FieldInfo field) { Record(opcode, 14); FingerprintBuilder.Add(field); Il.Emit(opcode, field); }
    /// <inheritdoc/>
    public override void Emit(OpCode opcode, MethodInfo meth) { Record(opcode, 13); FingerprintBuilder.Add(meth); Il.Emit(opcode, meth); }
    /// <inheritdoc/>
    public override void Emit(OpCode opcode, float arg) { Record(opcode, 6); FingerprintBuilder.Add(arg); Il.Emit(opcode, arg); }
    /// <inheritdoc/>
    public override void Emit(OpCode opcode, string str) { Record(opcode, 7); FingerprintBuilder.Add(str); Il.Emit(opcode, str); }
    /// <inheritdoc/>
    public override void Emit(OpCode opcode, Type cls) { Record(opcode, 8); FingerprintBuilder.Add(cls); Il.Emit(opcode, cls); }
    /// <inheritdoc/>
    public override void EmitCall(OpCode opcode, MethodInfo methodInfo, Type[]? optionalParameterTypes) { Record(opcode, 16); FingerprintBuilder.Add(methodInfo); FingerprintBuilder.Add(optionalParameterTypes); Il.EmitCall(opcode, methodInfo, optionalParameterTypes); }
    /// <inheritdoc/>
    public override void EmitCalli(OpCode opcode, CallingConventions callingConvention, Type? returnType, Type[]? parameterTypes, Type[]? optionalParameterTypes) { Record(opcode, 17); FingerprintBuilder.Add((int)callingConvention); FingerprintBuilder.Add(returnType); FingerprintBuilder.Add(parameterTypes); FingerprintBuilder.Add(optionalParameterTypes); Il.EmitCalli(opcode, callingConvention, returnType, parameterTypes, optionalParameterTypes); }
    /// <inheritdoc/>
    public override void EmitCalli(OpCode opcode, CallingConvention unmanagedCallConv, Type? returnType, Type[]? parameterTypes) { Record(opcode, 18); FingerprintBuilder.Add((int)unmanagedCallConv); FingerprintBuilder.Add(returnType); FingerprintBuilder.Add(parameterTypes); Il.EmitCalli(opcode, unmanagedCallConv, returnType, parameterTypes); }
    /// <inheritdoc/>
    public override void EndExceptionBlock() { FingerprintBuilder.Add(0x116); Il.EndExceptionBlock(); }
    /// <inheritdoc/>
    public override void EndScope() { FingerprintBuilder.Add(0x117); Il.EndScope(); }
    /// <inheritdoc/>
    public override void MarkLabel(Label loc) { FingerprintBuilder.Add(0x101); FingerprintBuilder.Add(LabelId(loc)); Il.MarkLabel(loc); }
    /// <inheritdoc/>
    public override void UsingNamespace(string usingNamespace) { FingerprintBuilder.Add(0x118); FingerprintBuilder.Add(usingNamespace); Il.UsingNamespace(usingNamespace); }
}
#endif
