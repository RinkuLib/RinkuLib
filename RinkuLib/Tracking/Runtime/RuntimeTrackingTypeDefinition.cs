using System;
using System.Collections.Generic;

namespace Rinku.Tracking.Runtime;

/// <summary>Contributes members and capabilities to a generated type.</summary>
public interface IRuntimeTrackingTypeContributor<TOriginal> {
    /// <summary>Configures the generated type.</summary>
    void Configure(RuntimeTrackingTypeDefinition<TOriginal> type);
}

// Final mutable shape between the canonical option tree and IL emission. Advanced contributors may
// work here, but normal compile-time/runtime configuration should target RuntimeTrackingOptions<TOriginal>.
/// <summary>Describes a generated tracking type during configuration.</summary>
public sealed class RuntimeTrackingTypeDefinition<TOriginal> {
    private readonly List<IRuntimeTrackingMember> _members = [];
    private readonly List<IRuntimeTrackingCapability<TOriginal>> _capabilities = [];

    internal RuntimeTrackingTypeDefinition(Type exposedContract, IEnumerable<IRuntimeTrackingMember> members, IEnumerable<IRuntimeTrackingCapability<TOriginal>> capabilities, bool dynamicAccess, bool notifications) {
        ExposedContract = exposedContract;
        DynamicAccess = dynamicAccess;
        Notifications = notifications;
        foreach (IRuntimeTrackingMember member in members) AddMember(member);
        _capabilities.AddRange(capabilities);
    }

    /// <summary>Gets the original source type.</summary>
    public Type OriginalType => typeof(TOriginal);
    /// <summary>Gets the generated contract type.</summary>
    public Type ExposedContract { get; }
    /// <summary>Gets or sets whether dynamic access is emitted.</summary>
    public bool DynamicAccess { get; set; }
    /// <summary>Gets or sets whether notifications are emitted.</summary>
    public bool Notifications { get; set; }
    /// <summary>Gets generated members.</summary>
    public IReadOnlyList<IRuntimeTrackingMember> Members => _members;
    /// <summary>Gets generated capabilities.</summary>
    public IReadOnlyList<IRuntimeTrackingCapability<TOriginal>> Capabilities => _capabilities;

    /// <summary>Finds a generated member.</summary>
    public IRuntimeTrackingMember? FindMember(string name) {
        for (int i = 0; i < _members.Count; i++)
            if (string.Equals(_members[i].Name, name, StringComparison.OrdinalIgnoreCase)) return _members[i];
        return null;
    }

    /// <summary>Adds a generated member.</summary>
    public void AddMember(IRuntimeTrackingMember member) {
        ArgumentNullException.ThrowIfNull(member);
        if (FindMember(member.Name) is not null) throw new InvalidOperationException($"Runtime member '{member.Name}' already exists.");
        _members.Add(member);
    }

    /// <summary>Replaces or adds a generated member.</summary>
    public void ReplaceMember(IRuntimeTrackingMember member) {
        ArgumentNullException.ThrowIfNull(member);
        for (int i = 0; i < _members.Count; i++)
            if (string.Equals(_members[i].Name, member.Name, StringComparison.OrdinalIgnoreCase)) {
                _members[i] = member;
                return;
            }
        _members.Add(member);
    }

    /// <summary>Removes a generated member.</summary>
    public bool RemoveMember(string name) {
        for (int i = 0; i < _members.Count; i++)
            if (string.Equals(_members[i].Name, name, StringComparison.OrdinalIgnoreCase)) {
                _members.RemoveAt(i);
                return true;
            }
        return false;
    }

    /// <summary>Adds a generated capability.</summary>
    public void AddCapability(IRuntimeTrackingCapability<TOriginal> capability) {
        ArgumentNullException.ThrowIfNull(capability);
        _capabilities.Add(capability);
    }
}
