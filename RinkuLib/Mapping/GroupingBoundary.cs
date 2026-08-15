using System.Reflection;
using System.Reflection.Emit;

namespace Rinku.Mapping;

/// <summary>Creates the readers and fields needed by a custom grouping rule.</summary>
public interface IBoundaryBuild {
    /// <summary>Creates a boundary reader from a mapped value.</summary>
    IBoundaryReader Reader(DbItemPlan reader, Type type);
    /// <summary>Creates storage for one part of a key between rows.</summary>
    IBoundaryField Field(Type type);
}

/// <summary>Reads one part of a grouping key from the current row.</summary>
public interface IBoundaryReader {
    /// <summary>The value type this reader produces.</summary>
    Type Type { get; }
    /// <summary>Gets the column used to detect a missing joined value when one is available.</summary>
    int? Column { get; }
    /// <summary>Gets the bound values needed by this reader.</summary>
    (FieldInfo Field, object[] Targets) Targets { get; }
    /// <summary>Writes the instructions that read the value from the current row.</summary>
    void EmitRead(Generator g);
}

/// <summary>Stores one part of a grouping key between rows.</summary>
public interface IBoundaryField {
    /// <summary>Writes the instructions that load the value owner for <see cref="EmitStore"/>.</summary>
    void EmitThis(Generator g);
    /// <summary>Writes the instructions that load the stored value.</summary>
    void EmitLoad(Generator g);
    /// <summary>Writes the instructions that store a value after <see cref="EmitThis"/>.</summary>
    void EmitStore(Generator g);
}

/// <summary>
/// Decides when rows belong to the same object in a spanning mapping.
/// Derive from this type for a grouping rule that cannot use equality or a boundary method.
/// </summary>
public abstract class GroupingBoundary {
    /// <summary>The column whose <c>DBNull</c> marks the group absent for a left-joined sub level, or <see langword="null"/> when none.</summary>
    public virtual int? PresenceColumn => null;
    /// <summary>Whether a later row can ever start a new group. <see langword="false"/> folds every row into one instance.</summary>
    public abstract bool CanChange { get; }
    /// <summary>Whether the boundary stores state captured on the group's first row.</summary>
    public abstract bool Captures { get; }
    /// <summary>Gets the bound values used by this boundary.</summary>
    public virtual IEnumerable<(FieldInfo Field, object[] Targets)> Targets => [];
    /// <summary>Writes the instructions that capture a key from the first row.</summary>
    public abstract void EmitCapture(Generator g);
    /// <summary>Writes the instructions that branch to <paramref name="changed"/> when a new group starts.</summary>
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
    public override IEnumerable<(FieldInfo, object[])> Targets => Components.Select(c => c.Reader.Targets);
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
/// Uses a static method to decide whether the current row stays in the group.
/// The method receives the stored key followed by values from the current row.
/// It returns whether the group stays open and the key to store for the next row.
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
    public override IEnumerable<(FieldInfo, object[])> Targets => Parameters.Select(p => p.Targets);
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

/// <summary>Places every row in one group.</summary>
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
