using System;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace Rinku.Tracking.Runtime;

// A generated CLR property/runtime slot. It says how the current interaction value is read/written;
// it does not imply that a same-named PropertyInfo exists on TOriginal.
/// <summary>Describes one generated tracking member.</summary>
public interface IRuntimeTrackingMember {
    /// <summary>Gets the generated member name.</summary>
    string Name { get; }
    /// <summary>Gets the generated member type.</summary>
    Type ValueType { get; }
    /// <summary>Gets whether the member can be written.</summary>
    bool CanWrite { get; }
    /// <summary>Gets whether the member supports runtime access.</summary>
    bool IncludeInRuntimeAccess { get; }
    /// <summary>Gets whether the member participates in parameter projection.</summary>
    bool IncludeInParameters { get; }
    /// <summary>Gets alternate parameter names.</summary>
    IReadOnlyList<string>? ParameterNames { get; }
    /// <summary>Gets whether a CLR property is exposed.</summary>
    bool ExposeProperty { get; }

    /// <summary>Emits member read behavior.</summary>
    void EmitGet(RuntimeTrackingMemberEmitContext context, ILGenerator il);
    /// <summary>Emits member write behavior.</summary>
    void EmitSet(RuntimeTrackingMemberEmitContext context, ILGenerator il);
    /// <summary>Applies member metadata to a generated property.</summary>
    void ApplyMetadata(PropertyBuilder property);
}

// Only this optional capability participates in the interaction edit snapshot/commit cycle.
// Unrelated runtime values can implement IRuntimeTrackingMember without becoming edits.
/// <summary>Describes a generated member that participates in edit snapshots.</summary>
public interface IRuntimeEditableTrackingMember : IRuntimeTrackingMember {
    /// <summary>Emits baseline capture behavior.</summary>
    void EmitReadBaseline(ILGenerator il, Action<ILGenerator> emitOriginal);
    /// <summary>Emits edit application behavior.</summary>
    void EmitApply(ILGenerator il, Action<ILGenerator> emitOriginal, Action<ILGenerator> emitValue);
}
