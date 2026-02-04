using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace RinkuLib.DbParsing;
#if DEBUG
/// <summary>
/// A wrapper for <see cref="ILGenerator"/> that provides instruction logging 
/// and automatic local variable management.
/// </summary>
/// <remarks>
/// This class can be compiled in two modes:
/// <list type="bullet">
/// <item><b>Standard:</b> A high-performance passthrough to the underlying IL generator.</item>
/// <item><b>Verbose (<c>USE_VERBOSE_GENERATOR</c>):</b> Logs every emitted instruction to the Console, 
/// providing a readable trace of the dynamic code being generated.</item>
/// </list>
/// </remarks>
public class Generator(ILGenerator generator, ColumnInfo[] cols) {
#pragma warning disable CA2211
    public static Action<string> Write = Console.WriteLine;
#pragma warning restore CA2211
    public string? TargetName;
    public readonly ILGenerator Il = generator;
    public readonly ColumnInfo[] Columns = cols;
    /// <summary> 
    /// Tracks declared locals to allow for slot reuse and cleaner generated code. 
    /// </summary>

    private readonly Dictionary<Type, LocalBuilder> LocalCache = [];
    private readonly Dictionary<Label, string> LabelNames = [];

    private int labelCounter = 0;

    // ----------------------------------------
    // LOCALS
    // ----------------------------------------
    public LocalBuilder GetLocal(Type type) {
        if (LocalCache.TryGetValue(type, out var local)) {
            Write($"[IL] ReuseLocal type={type.ShortName()} index={local.LocalIndex}");
            return local;
        }

        local = Il.DeclareLocal(type);
        LocalCache[type] = local;

        Write($"[IL] DeclareLocal type={type.ShortName()} index={local.LocalIndex}");
        return local;
    }

    public LocalBuilder DeclareLocal(Type localType, bool pinned) {
        var loc = Il.DeclareLocal(localType, pinned);
        Write($"[IL] DeclareLocal type={localType.ShortName()} pinned={pinned} index={loc.LocalIndex}");
        return loc;
    }


    // ----------------------------------------
    // LABELS
    // ----------------------------------------
    public Label DefineLabel() {
        var label = Il.DefineLabel();
        var name = $"L{labelCounter++:000}";
        LabelNames[label] = name;

        Write($"[IL] DefineLabel {name}");
        return label;
    }

    public void MarkLabel(Label loc) {
        var name = LabelNames.TryGetValue(loc, out var n) ? n : "(unknown)";
        Write($"[IL] MarkLabel {name}");
        Il.MarkLabel(loc);
    }


    // ----------------------------------------
    // BASIC EMITS
    // ----------------------------------------
    public int ILOffset => Il.ILOffset;

    public void Emit(OpCode opcode) {
        Write($"[IL] Emit {opcode}");
        Il.Emit(opcode);
    }

    public void Emit(OpCode opcode, byte arg) {
        Write($"[IL] Emit {opcode} byte={arg}");
        Il.Emit(opcode, arg);
    }

    public void Emit(OpCode opcode, double arg) {
        Write($"[IL] Emit {opcode} double={arg}");
        Il.Emit(opcode, arg);
    }

    public void Emit(OpCode opcode, short arg) {
        Write($"[IL] Emit {opcode} short={arg}");
        Il.Emit(opcode, arg);
    }

    public void Emit(OpCode opcode, int arg) {
        if (opcode == OpCodes.Ldc_I4 && (uint)arg < Columns.Length)
            Write($"[IL] Emit {opcode} int={arg} probable index for {Columns[arg].Name}");
        else
            Write($"[IL] Emit {opcode} int={arg}");
        Il.Emit(opcode, arg);
    }

    public void Emit(OpCode opcode, long arg) {
        Write($"[IL] Emit {opcode} long={arg}");
        Il.Emit(opcode, arg);
    }

    public void Emit(OpCode opcode, float arg) {
        Write($"[IL] Emit {opcode} float={arg}");
        Il.Emit(opcode, arg);
    }

    public void Emit(OpCode opcode, string str) {
        Write($"[IL] Emit {opcode} string=\"{str}\"");
        Il.Emit(opcode, str);
    }

    public void Emit(OpCode opcode, Type cls) {
        Write($"[IL] Emit {opcode} type={cls.ShortName()}");
        Il.Emit(opcode, cls);
    }


    // ----------------------------------------
    // COMPLEX EMITS
    // ----------------------------------------
    public void Emit(OpCode opcode, Label label) {
        var name = LabelNames.TryGetValue(label, out var n) ? n : "(unknown)";
        Write($"[IL] Emit {opcode} -> {name}");
        Il.Emit(opcode, label);
    }

    public void Emit(OpCode opcode, Label[] labels) {
        Write($"[IL] Emit {opcode} labels[{labels.Length}]");
        Il.Emit(opcode, labels);
    }

    public void Emit(OpCode opcode, LocalBuilder local) {
        Write($"[IL] Emit {opcode} localIndex={local.LocalIndex} type={local.LocalType.ShortName()}");
        Il.Emit(opcode, local);
    }

    public void Emit(OpCode opcode, ConstructorInfo con) {
        Write($"[IL] Emit {opcode} ctor {con.DeclaringType.ShortName()}..ctor({ShortParams(con)})");
        Il.Emit(opcode, con);
    }

    public void Emit(OpCode opcode, MethodInfo meth) {
        Write($"[IL] Emit {opcode} {meth.ReturnType.ShortName()} {meth.DeclaringType.ShortName()}.{meth.Name}({ShortParams(meth)})");
        Il.Emit(opcode, meth);
    }

    public void Emit(OpCode opcode, FieldInfo field) {
        Write($"[IL] Emit {opcode} {field.FieldType.ShortName()} {field.DeclaringType.ShortName()}.{field.Name}");
        Il.Emit(opcode, field);
    }
    private static string ShortParams(MethodBase method) {
        return string.Join(", ", method.GetParameters().Select(p => p.ParameterType.ShortName()));
    }

    public void Emit(OpCode opcode, SignatureHelper signature) {
        Write($"[IL] Emit {opcode} signature");
        Il.Emit(opcode, signature);
    }


    // ----------------------------------------
    // CALL EMITS
    // ----------------------------------------
    public void EmitCall(OpCode opcode, MethodInfo methodInfo, Type[]? optionalParameterTypes) {
        Write($"[IL] EmitCall {opcode} method={methodInfo.DeclaringType.ShortName()}.{methodInfo.Name}({ShortParams(methodInfo)})");
        Il.EmitCall(opcode, methodInfo, optionalParameterTypes);
    }

    public void EmitCalli(OpCode opcode, CallingConventions callingConvention, Type? returnType, Type[]? parameterTypes, Type[]? optionalParameterTypes) {
        Write($"[IL] EmitCalli {opcode} conv={callingConvention}");
        Il.EmitCalli(opcode, callingConvention, returnType, parameterTypes, optionalParameterTypes);
    }

    public void EmitCalli(OpCode opcode, CallingConvention unmanagedCallConv, Type? returnType, Type[]? parameterTypes) {
        Write($"[IL] EmitCalli {opcode} unmanaged={unmanagedCallConv}");
        Il.EmitCalli(opcode, unmanagedCallConv, returnType, parameterTypes);
    }


    // ----------------------------------------
    // EXCEPTIONS + SCOPE
    // ----------------------------------------
    public Label BeginExceptionBlock() {
        Write("[IL] BeginExceptionBlock");
        return Il.BeginExceptionBlock();
    }

    public void EndExceptionBlock() {
        Write("[IL] EndExceptionBlock");
        Il.EndExceptionBlock();
    }

    public void BeginCatchBlock(Type? exceptionType) {
        Write($"[IL] BeginCatchBlock type={exceptionType.ShortName()}");
        Il.BeginCatchBlock(exceptionType);
    }

    public void BeginExceptFilterBlock() {
        Write("[IL] BeginExceptFilterBlock");
        Il.BeginExceptFilterBlock();
    }

    public void BeginFaultBlock() {
        Write("[IL] BeginFaultBlock");
        Il.BeginFaultBlock();
    }

    public void BeginFinallyBlock() {
        Write("[IL] BeginFinallyBlock");
        Il.BeginFinallyBlock();
    }

    public void BeginScope() {
        Write("[IL] BeginScope");
        Il.BeginScope();
    }

    public void EndScope() {
        Write("[IL] EndScope");
        Il.EndScope();
    }

    public void UsingNamespace(string usingNamespace) {
        Write($"[IL] UsingNamespace {usingNamespace}");
        Il.UsingNamespace(usingNamespace);
    }
}
#else
public class Generator(ILGenerator generator) : ILGenerator {
    public string? TargetName;
    public string? LastColRead;
    public readonly ILGenerator Il = generator;
    /// <summary> 
    /// Tracks declared locals to allow for slot reuse and cleaner generated code. 
    /// </summary>
    public readonly Dictionary<Type, LocalBuilder> LocalCache = [];
    /// <summary>
    /// Gets or declares a local variable of the specified type.
    /// Reuses an existing slot if one was already declared for this type.
    /// </summary>
    public LocalBuilder GetLocal(Type type) {
        if (LocalCache.TryGetValue(type, out var local))
            return local;
        local = Il.DeclareLocal(type);
        LocalCache[type] = local;
        return local;
    }
    public override int ILOffset => Il.ILOffset;
    public override void BeginCatchBlock(Type? exceptionType) => Il.BeginCatchBlock(exceptionType);
    public override void BeginExceptFilterBlock() => Il.BeginExceptFilterBlock();
    public override Label BeginExceptionBlock() => Il.BeginExceptionBlock();
    public override void BeginFaultBlock() => Il.BeginFaultBlock();
    public override void BeginFinallyBlock() => Il.BeginFinallyBlock();
    public override void BeginScope() => Il.BeginScope();
    public override LocalBuilder DeclareLocal(Type localType, bool pinned) => Il.DeclareLocal(localType, pinned);
    public override Label DefineLabel() => Il.DefineLabel();
    public override void Emit(OpCode opcode) => Il.Emit(opcode);

    public override void Emit(OpCode opcode, byte arg) => Il.Emit(opcode, arg);
    public override void Emit(OpCode opcode, double arg) => Il.Emit(opcode, arg);
    public override void Emit(OpCode opcode, short arg) => Il.Emit(opcode, arg);
    public override void Emit(OpCode opcode, int arg) => Il.Emit(opcode, arg);
    public override void Emit(OpCode opcode, long arg) => Il.Emit(opcode, arg);
    public override void Emit(OpCode opcode, ConstructorInfo con) => Il.Emit(opcode, con);
    public override void Emit(OpCode opcode, Label label) => Il.Emit(opcode, label);
    public override void Emit(OpCode opcode, Label[] labels) => Il.Emit(opcode, labels);
    public override void Emit(OpCode opcode, LocalBuilder local) => Il.Emit(opcode, local);
    public override void Emit(OpCode opcode, SignatureHelper signature) => Il.Emit(opcode, signature);
    public override void Emit(OpCode opcode, FieldInfo field) => Il.Emit(opcode, field);
    public override void Emit(OpCode opcode, MethodInfo meth) => Il.Emit(opcode, meth);
    public override void Emit(OpCode opcode, float arg) => Il.Emit(opcode, arg);
    public override void Emit(OpCode opcode, string str) => Il.Emit(opcode, str);
    public override void Emit(OpCode opcode, Type cls) => Il.Emit(opcode, cls);
    public override void EmitCall(OpCode opcode, MethodInfo methodInfo, Type[]? optionalParameterTypes) => Il.EmitCall(opcode, methodInfo, optionalParameterTypes);
    public override void EmitCalli(OpCode opcode, CallingConventions callingConvention, Type? returnType, Type[]? parameterTypes, Type[]? optionalParameterTypes) => Il.EmitCalli(opcode, callingConvention, returnType, parameterTypes, optionalParameterTypes);
    public override void EmitCalli(OpCode opcode, CallingConvention unmanagedCallConv, Type? returnType, Type[]? parameterTypes) => Il.EmitCalli(opcode, unmanagedCallConv, returnType, parameterTypes);
    public override void EndExceptionBlock() => Il.EndExceptionBlock();
    public override void EndScope() => Il.EndScope();
    public override void MarkLabel(Label loc) => Il.MarkLabel(loc);
    public override void UsingNamespace(string usingNamespace) => Il.UsingNamespace(usingNamespace);
}
#endif