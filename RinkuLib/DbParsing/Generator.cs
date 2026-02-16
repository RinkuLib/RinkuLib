using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using RinkuLib.Tools;

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
public class Generator(ILGenerator generator, ColumnInfo[] cols) : ILGenerator {
#pragma warning disable CA2211
    /// <summary></summary>
    public static Action<string> Write = Console.WriteLine;
#pragma warning restore CA2211
    /// <summary>The underlying <see cref="ILGenerator"/></summary>
    public readonly ILGenerator Il = generator;
    /// <summary>The currently using schema to write actual column names</summary>
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
    /// <summary>
    /// An wrapper to reuse locals instead of creating a new one each time
    /// </summary>
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
    /// <inheritdoc/>
    public override LocalBuilder DeclareLocal(Type localType, bool pinned) {
        var loc = Il.DeclareLocal(localType, pinned);
        Write($"[IL] DeclareLocal type={localType.ShortName()} pinned={pinned} index={loc.LocalIndex}");
        return loc;
    }


    // ----------------------------------------
    // LABELS
    // ----------------------------------------
    /// <inheritdoc/>
    public override Label DefineLabel() {
        var label = Il.DefineLabel();
        var name = $"L{labelCounter++:000}";
        LabelNames[label] = name;

        Write($"[IL] DefineLabel {name}");
        return label;
    }

    /// <inheritdoc/>
    public override void MarkLabel(Label loc) {
        var name = LabelNames.TryGetValue(loc, out var n) ? n : "(unknown)";
        Write($"[IL] MarkLabel {name}");
        Il.MarkLabel(loc);
    }


    // ----------------------------------------
    // BASIC EMITS
    // ----------------------------------------
    /// <inheritdoc/>
    public override int ILOffset => Il.ILOffset;

    /// <inheritdoc/>
    public override void Emit(OpCode opcode) {
        Write($"[IL] Emit {opcode}");
        Il.Emit(opcode);
    }

    /// <inheritdoc/>
    public override void Emit(OpCode opcode, byte arg) {
        Write($"[IL] Emit {opcode} byte={arg}");
        Il.Emit(opcode, arg);
    }

    /// <inheritdoc/>
    public override void Emit(OpCode opcode, double arg) {
        Write($"[IL] Emit {opcode} double={arg}");
        Il.Emit(opcode, arg);
    }

    /// <inheritdoc/>
    public override void Emit(OpCode opcode, short arg) {
        Write($"[IL] Emit {opcode} short={arg}");
        Il.Emit(opcode, arg);
    }

    /// <inheritdoc/>
    public override void Emit(OpCode opcode, int arg) {
        if (opcode == OpCodes.Ldc_I4 && (uint)arg < Columns.Length)
            Write($"[IL] Emit {opcode} int={arg} probable index for {Columns[arg].Name}");
        else
            Write($"[IL] Emit {opcode} int={arg}");
        Il.Emit(opcode, arg);
    }

    /// <inheritdoc/>
    public override void Emit(OpCode opcode, long arg) {
        Write($"[IL] Emit {opcode} long={arg}");
        Il.Emit(opcode, arg);
    }

    /// <inheritdoc/>
    public override void Emit(OpCode opcode, float arg) {
        Write($"[IL] Emit {opcode} float={arg}");
        Il.Emit(opcode, arg);
    }

    /// <inheritdoc/>
    public override void Emit(OpCode opcode, string str) {
        Write($"[IL] Emit {opcode} string=\"{str}\"");
        Il.Emit(opcode, str);
    }

    /// <inheritdoc/>
    public override void Emit(OpCode opcode, Type cls) {
        Write($"[IL] Emit {opcode} type={cls.ShortName()}");
        Il.Emit(opcode, cls);
    }


    // ----------------------------------------
    // COMPLEX EMITS
    // ----------------------------------------
    /// <inheritdoc/>
    public override void Emit(OpCode opcode, Label label) {
        var name = LabelNames.TryGetValue(label, out var n) ? n : "(unknown)";
        Write($"[IL] Emit {opcode} -> {name}");
        Il.Emit(opcode, label);
    }

    /// <inheritdoc/>
    public override void Emit(OpCode opcode, Label[] labels) {
        Write($"[IL] Emit {opcode} labels[{labels.Length}]");
        Il.Emit(opcode, labels);
    }

    /// <inheritdoc/>
    public override void Emit(OpCode opcode, LocalBuilder local) {
        Write($"[IL] Emit {opcode} localIndex={local.LocalIndex} type={local.LocalType.ShortName()}");
        Il.Emit(opcode, local);
    }

    /// <inheritdoc/>
    public override void Emit(OpCode opcode, ConstructorInfo con) {
        Write($"[IL] Emit {opcode} ctor {con.DeclaringType.ShortName()}..ctor({ShortParams(con)})");
        Il.Emit(opcode, con);
    }

    /// <inheritdoc/>
    public override void Emit(OpCode opcode, MethodInfo meth) {
        Write($"[IL] Emit {opcode} {meth.ReturnType.ShortName()} {meth.DeclaringType.ShortName()}.{meth.Name}({ShortParams(meth)})");
        Il.Emit(opcode, meth);
    }

    /// <inheritdoc/>
    public override void Emit(OpCode opcode, FieldInfo field) {
        Write($"[IL] Emit {opcode} {field.FieldType.ShortName()} {field.DeclaringType.ShortName()}.{field.Name}");
        Il.Emit(opcode, field);
    }
    private static string ShortParams(MethodBase method) {
        return string.Join(", ", method.GetParameters().Select(p => p.ParameterType.ShortName()));
    }

    /// <inheritdoc/>
    public override void Emit(OpCode opcode, SignatureHelper signature) {
        Write($"[IL] Emit {opcode} signature");
        Il.Emit(opcode, signature);
    }


    // ----------------------------------------
    // CALL EMITS
    // ----------------------------------------
    /// <inheritdoc/>
    public override void EmitCall(OpCode opcode, MethodInfo methodInfo, Type[]? optionalParameterTypes) {
        Write($"[IL] EmitCall {opcode} method={methodInfo.DeclaringType.ShortName()}.{methodInfo.Name}({ShortParams(methodInfo)})");
        Il.EmitCall(opcode, methodInfo, optionalParameterTypes);
    }

    /// <inheritdoc/>
    public override void EmitCalli(OpCode opcode, CallingConventions callingConvention, Type? returnType, Type[]? parameterTypes, Type[]? optionalParameterTypes) {
        Write($"[IL] EmitCalli {opcode} conv={callingConvention}");
        Il.EmitCalli(opcode, callingConvention, returnType, parameterTypes, optionalParameterTypes);
    }

    /// <inheritdoc/>
    public override void EmitCalli(OpCode opcode, CallingConvention unmanagedCallConv, Type? returnType, Type[]? parameterTypes) {
        Write($"[IL] EmitCalli {opcode} unmanaged={unmanagedCallConv}");
        Il.EmitCalli(opcode, unmanagedCallConv, returnType, parameterTypes);
    }


    // ----------------------------------------
    // EXCEPTIONS + SCOPE
    // ----------------------------------------
    /// <inheritdoc/>
    public override Label BeginExceptionBlock() {
        Write("[IL] BeginExceptionBlock");
        return Il.BeginExceptionBlock();
    }

    /// <inheritdoc/>
    public override void EndExceptionBlock() {
        Write("[IL] EndExceptionBlock");
        Il.EndExceptionBlock();
    }

    /// <inheritdoc/>
    public override void BeginCatchBlock(Type? exceptionType) {
        Write($"[IL] BeginCatchBlock type={exceptionType.ShortName()}");
        Il.BeginCatchBlock(exceptionType);
    }

    /// <inheritdoc/>
    public override void BeginExceptFilterBlock() {
        Write("[IL] BeginExceptFilterBlock");
        Il.BeginExceptFilterBlock();
    }

    /// <inheritdoc/>
    public override void BeginFaultBlock() {
        Write("[IL] BeginFaultBlock");
        Il.BeginFaultBlock();
    }

    /// <inheritdoc/>
    public override void BeginFinallyBlock() {
        Write("[IL] BeginFinallyBlock");
        Il.BeginFinallyBlock();
    }

    /// <inheritdoc/>
    public override void BeginScope() {
        Write("[IL] BeginScope");
        Il.BeginScope();
    }

    /// <inheritdoc/>
    public override void EndScope() {
        Write("[IL] EndScope");
        Il.EndScope();
    }

    /// <inheritdoc/>
    public override void UsingNamespace(string usingNamespace) {
        Write($"[IL] UsingNamespace {usingNamespace}");
        Il.UsingNamespace(usingNamespace);
    }
}
#else
/// <summary>
/// A wrapper for <see cref="ILGenerator"/> that provides automatic local variable management.
/// </summary>
public class Generator(ILGenerator generator) : ILGenerator {
    /// <summary>The underlying <see cref="ILGenerator"/></summary>
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
    /// <inheritdoc/>
    public override int ILOffset => Il.ILOffset;
    /// <inheritdoc/>
    public override void BeginCatchBlock(Type? exceptionType) => Il.BeginCatchBlock(exceptionType);
    /// <inheritdoc/>
    public override void BeginExceptFilterBlock() => Il.BeginExceptFilterBlock();
    /// <inheritdoc/>
    public override Label BeginExceptionBlock() => Il.BeginExceptionBlock();
    /// <inheritdoc/>
    public override void BeginFaultBlock() => Il.BeginFaultBlock();
    /// <inheritdoc/>
    public override void BeginFinallyBlock() => Il.BeginFinallyBlock();
    /// <inheritdoc/>
    public override void BeginScope() => Il.BeginScope();
    /// <inheritdoc/>
    public override LocalBuilder DeclareLocal(Type localType, bool pinned) => Il.DeclareLocal(localType, pinned);
    /// <inheritdoc/>
    public override Label DefineLabel() => Il.DefineLabel();
    /// <inheritdoc/>
    public override void Emit(OpCode opcode) => Il.Emit(opcode);

    /// <inheritdoc/>
    public override void Emit(OpCode opcode, byte arg) => Il.Emit(opcode, arg);
    /// <inheritdoc/>
    public override void Emit(OpCode opcode, double arg) => Il.Emit(opcode, arg);
    /// <inheritdoc/>
    public override void Emit(OpCode opcode, short arg) => Il.Emit(opcode, arg);
    /// <inheritdoc/>
    public override void Emit(OpCode opcode, int arg) => Il.Emit(opcode, arg);
    /// <inheritdoc/>
    public override void Emit(OpCode opcode, long arg) => Il.Emit(opcode, arg);
    /// <inheritdoc/>
    public override void Emit(OpCode opcode, ConstructorInfo con) => Il.Emit(opcode, con);
    /// <inheritdoc/>
    public override void Emit(OpCode opcode, Label label) => Il.Emit(opcode, label);
    /// <inheritdoc/>
    public override void Emit(OpCode opcode, Label[] labels) => Il.Emit(opcode, labels);
    /// <inheritdoc/>
    public override void Emit(OpCode opcode, LocalBuilder local) => Il.Emit(opcode, local);
    /// <inheritdoc/>
    public override void Emit(OpCode opcode, SignatureHelper signature) => Il.Emit(opcode, signature);
    /// <inheritdoc/>
    public override void Emit(OpCode opcode, FieldInfo field) => Il.Emit(opcode, field);
    /// <inheritdoc/>
    public override void Emit(OpCode opcode, MethodInfo meth) => Il.Emit(opcode, meth);
    /// <inheritdoc/>
    public override void Emit(OpCode opcode, float arg) => Il.Emit(opcode, arg);
    /// <inheritdoc/>
    public override void Emit(OpCode opcode, string str) => Il.Emit(opcode, str);
    /// <inheritdoc/>
    public override void Emit(OpCode opcode, Type cls) => Il.Emit(opcode, cls);
    /// <inheritdoc/>
    public override void EmitCall(OpCode opcode, MethodInfo methodInfo, Type[]? optionalParameterTypes) => Il.EmitCall(opcode, methodInfo, optionalParameterTypes);
    /// <inheritdoc/>
    public override void EmitCalli(OpCode opcode, CallingConventions callingConvention, Type? returnType, Type[]? parameterTypes, Type[]? optionalParameterTypes) => Il.EmitCalli(opcode, callingConvention, returnType, parameterTypes, optionalParameterTypes);
    /// <inheritdoc/>
    public override void EmitCalli(OpCode opcode, CallingConvention unmanagedCallConv, Type? returnType, Type[]? parameterTypes) => Il.EmitCalli(opcode, unmanagedCallConv, returnType, parameterTypes);
    /// <inheritdoc/>
    public override void EndExceptionBlock() => Il.EndExceptionBlock();
    /// <inheritdoc/>
    public override void EndScope() => Il.EndScope();
    /// <inheritdoc/>
    public override void MarkLabel(Label loc) => Il.MarkLabel(loc);
    /// <inheritdoc/>
    public override void UsingNamespace(string usingNamespace) => Il.UsingNamespace(usingNamespace);
}
#endif