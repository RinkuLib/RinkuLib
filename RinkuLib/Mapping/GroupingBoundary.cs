using System.Reflection;
using System.Reflection.Emit;

namespace Rinku.Mapping;

/// <summary>The surface a rule lowers its negotiated readers through, turning them into emit handles on the state struct.</summary>
public interface IBoundaryBuild {
    /// <summary>Compiles a negotiated reader into a handle whose <see cref="IBoundaryReader.EmitRead"/> reads the current row.</summary>
    IBoundaryReader Reader(DbItemPlan reader, Type type);
    /// <summary>Defines a per-instance state field of <paramref name="type"/> to store part of the key across rows.</summary>
    IBoundaryField Field(Type type);
}

/// <summary>A compiled key reader: emits reading its value from the current row.</summary>
public interface IBoundaryReader {
    /// <summary>The value type this reader produces.</summary>
    Type Type { get; }
    /// <summary>The column index when the reader is a single column, used to gate an absent left-joined sub level; otherwise <see langword="null"/>.</summary>
    int? Column { get; }
    /// <summary>The static field and its bound target when the reader carries one, wired after the state type is baked; otherwise <see langword="null"/>.</summary>
    (FieldInfo Field, object Target)? Target { get; }
    /// <summary>Emits reading the value from the current row and leaving it on the stack.</summary>
    void EmitRead(Generator g);
}

/// <summary>A per-instance state field storing part of the key between rows.</summary>
public interface IBoundaryField {
    /// <summary>Emits loading the state instance, so a value pushed after it can be stored with <see cref="EmitStore"/>.</summary>
    void EmitThis(Generator g);
    /// <summary>Emits loading the stored value onto the stack.</summary>
    void EmitLoad(Generator g);
    /// <summary>Emits storing the value on the stack into the field; expects the instance then the value beneath it.</summary>
    void EmitStore(Generator g);
}

/// <summary>
/// The negotiated group boundary of a spanning read, the IL emitter for the resolved key. It captures the group's
/// first row and, on each later row, decides same-group or not with its own comparison, so a boundary that is not
/// equality lives entirely here.
/// </summary>
public abstract class GroupingBoundary {
    /// <summary>The column whose <c>DBNull</c> marks the group absent for a left-joined sub level, or <see langword="null"/> when none.</summary>
    public virtual int? PresenceColumn => null;
    /// <summary>Whether a later row can ever start a new group; <see langword="false"/> folds every row into one instance.</summary>
    public abstract bool CanChange { get; }
    /// <summary>Whether the boundary stores state captured on the group's first row.</summary>
    public abstract bool Captures { get; }
    /// <summary>The static fields and bound targets the boundary's readers carry, wired after the state type is baked.</summary>
    public virtual IEnumerable<(FieldInfo Field, object Target)> Targets => [];
    /// <summary>Emits reading and storing the current row's key, the group's first row.</summary>
    public abstract void EmitCapture(Generator g);
    /// <summary>Emits reading this row's key and comparing it; branch to <paramref name="changed"/> for a new group, else advance the stored key.</summary>
    public abstract void EmitCompare(Generator g, Label changed);
}

/// <summary>
/// A key of one or more components, each read from its column with reuse and compared by its own equality. The
/// group changes when any component changes.
/// </summary>
public sealed class EqualityBoundary(IReadOnlyList<(IBoundaryReader Reader, IBoundaryField Field)> components) : GroupingBoundary {
    private readonly IReadOnlyList<(IBoundaryReader Reader, IBoundaryField Field)> Components = components;
    /// <inheritdoc/>
    public override bool CanChange => true;
    /// <inheritdoc/>
    public override bool Captures => true;
    /// <inheritdoc/>
    public override int? PresenceColumn => Components[0].Reader.Column;
    /// <inheritdoc/>
    public override IEnumerable<(FieldInfo, object)> Targets
        => Components.Where(c => c.Reader.Target is not null).Select(c => c.Reader.Target!.Value);
    /// <inheritdoc/>
    public override void EmitCapture(Generator g) {
        foreach (var (reader, field) in Components) {
            field.EmitThis(g);
            reader.EmitRead(g);
            field.EmitStore(g);
        }
    }
    /// <inheritdoc/>
    public override void EmitCompare(Generator g, Label changed) {
        foreach (var (reader, field) in Components) {
            EmitEquals(g, reader, field);
            g.Emit(OpCodes.Brfalse, changed);
        }
    }
    private static readonly MethodInfo StringEquals = typeof(string).GetMethod("op_Equality", [typeof(string), typeof(string)])!;
    /// <summary>
    /// Emits the stored-versus-current equality of one component, leaving 1 when it still matches. An integer,
    /// enum, or boolean key compares with a bare <c>ceq</c>, a string with the ordinal, null-safe <c>==</c>, and
    /// anything else with <see cref="EqualityComparer{T}"/>, so the common keys carry no comparer fetch or virtual call.
    /// </summary>
    private static void EmitEquals(Generator g, IBoundaryReader reader, IBoundaryField field) {
        var type = reader.Type;
        var underlying = type.IsEnum ? type.GetEnumUnderlyingType() : type;
        if (type == typeof(string)) {
            field.EmitLoad(g);
            reader.EmitRead(g);
            g.Emit(OpCodes.Call, StringEquals);
        }
        else if (IsCeqType(underlying)) {
            field.EmitLoad(g);
            reader.EmitRead(g);
            g.Emit(OpCodes.Ceq);
        }
        else {
            var eq = typeof(EqualityComparer<>).MakeGenericType(type);
            g.Emit(OpCodes.Call, eq.GetProperty("Default")!.GetGetMethod()!);
            field.EmitLoad(g);
            reader.EmitRead(g);
            g.Emit(OpCodes.Callvirt, eq.GetMethod("Equals", [type, type])!);
        }
    }
    private static bool IsCeqType(Type t) =>
        t == typeof(int) || t == typeof(uint) || t == typeof(long) || t == typeof(ulong) ||
        t == typeof(short) || t == typeof(ushort) || t == typeof(byte) || t == typeof(sbyte) ||
        t == typeof(char) || t == typeof(bool) || t == typeof(nint) || t == typeof(nuint);
}

/// <summary>
/// A boundary whose same-group decision is a method, <c>static (bool same, TKey next) Method(TKey stored, ...)</c>.
/// The first parameter is the stored key; the rest are negotiated readers, so a boundary that is not equality (a
/// bucket, a tolerance, a running key) lives in the method and its inputs come through the same negotiation as any
/// slot. The stored key advances to <c>next</c> on every row that stays in the group.
/// </summary>
public sealed class MethodBoundary(MethodInfo method, Type keyType, IBoundaryField key, IReadOnlyList<IBoundaryReader> parameters) : GroupingBoundary {
    private readonly MethodInfo Method = method;
    private readonly Type KeyType = keyType;
    private readonly IBoundaryField Key = key;
    private readonly IReadOnlyList<IBoundaryReader> Parameters = parameters;
    private FieldInfo Same => Method.ReturnType.GetField("Item1")!;
    private FieldInfo Next => Method.ReturnType.GetField("Item2")!;
    /// <inheritdoc/>
    public override int? PresenceColumn => Parameters.Count > 0 ? Parameters[0].Column : null;
    /// <inheritdoc/>
    public override bool CanChange => true;
    /// <inheritdoc/>
    public override bool Captures => true;
    /// <inheritdoc/>
    public override IEnumerable<(FieldInfo, object)> Targets
        => Parameters.Where(p => p.Target is not null).Select(p => p.Target!.Value);
    /// <inheritdoc/>
    public override void EmitCapture(Generator g) {
        var tuple = g.DeclareLocal(Method.ReturnType);
        Key.EmitThis(g);
        DbItemPlan.EmitDefaultValue(KeyType, g);
        foreach (var p in Parameters)
            p.EmitRead(g);
        g.Emit(OpCodes.Call, Method);
        g.Emit(OpCodes.Stloc, tuple);
        g.Emit(OpCodes.Ldloca, tuple);
        g.Emit(OpCodes.Ldfld, Next);
        Key.EmitStore(g);
    }
    /// <inheritdoc/>
    public override void EmitCompare(Generator g, Label changed) {
        var tuple = g.DeclareLocal(Method.ReturnType);
        Key.EmitLoad(g);
        foreach (var p in Parameters)
            p.EmitRead(g);
        g.Emit(OpCodes.Call, Method);
        g.Emit(OpCodes.Stloc, tuple);
        g.Emit(OpCodes.Ldloca, tuple);
        g.Emit(OpCodes.Ldfld, Same);
        g.Emit(OpCodes.Brfalse, changed);
        Key.EmitThis(g);
        g.Emit(OpCodes.Ldloca, tuple);
        g.Emit(OpCodes.Ldfld, Next);
        Key.EmitStore(g);
    }
}

/// <summary>A boundary that never changes: every row folds into the one instance, so the read runs until the rows end.</summary>
public sealed class AlwaysGroupedBoundary : GroupingBoundary {
    /// <summary>The single instance.</summary>
    public static readonly AlwaysGroupedBoundary Instance = new();
    private AlwaysGroupedBoundary() { }
    /// <inheritdoc/>
    public override bool CanChange => false;
    /// <inheritdoc/>
    public override bool Captures => false;
    /// <inheritdoc/>
    public override void EmitCapture(Generator g) { }
    /// <inheritdoc/>
    public override void EmitCompare(Generator g, Label changed) { }
}
