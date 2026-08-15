using System.Reflection;
using Rinku.Internal;

namespace Rinku.Mapping;

/// <summary>
/// Lets an attribute provide a custom grouping rule.
/// Implement this interface on an attribute used by types, members, or construction parameters.
/// </summary>
public interface IGroupingRuleMaker {
    /// <summary>Returns whether this declaration may be combined with matching declarations.</summary>
    bool Composes(ICustomAttributeProvider carrier);
    /// <summary>Creates a rule from the declarations found on one type or construction.</summary>
    IGroupingRule MakeRule(IReadOnlyList<ICustomAttributeProvider> carriers);
}

internal static class GroupKeyScan {
    public static IGroupingRule? Resolve(Type owner, IEnumerable<ICustomAttributeProvider> carriers) {
        List<(IGroupingRuleMaker Attr, ICustomAttributeProvider Carrier)> found = [];
        foreach (var carrier in carriers)
            foreach (var attribute in carrier.GetCustomAttributes(inherit: true))
                if (attribute is IGroupingRuleMaker key)
                    found.Add((key, carrier));
        if (found.Count == 0)
            return null;
        if (found.Count > 1 && found.Any(f => !f.Attr.Composes(f.Carrier)))
            throw new RinkuConfigurationException(ErrorCodes.ConflictingGroupKey,
                $"{owner} has a group key that stands alone next to other group keys on one level; that key is the only one on its level");
        if (found.Select(f => f.Attr.GetType()).Distinct().Count() > 1)
            throw new RinkuConfigurationException(ErrorCodes.ConflictingGroupKey,
                $"{owner} composes declarations from more than one grouping rule family on one level");
        return found[0].Attr.MakeRule([.. found.Select(f => f.Carrier)]);
    }
}

/// <summary>Builds readers used by custom grouping rules.</summary>
public static class GroupKeyNegotiation {
    /// <summary>Creates a reusable key reader for a named column.</summary>
    public static DbItemPlan NegotiateReader(INameComparer name, Type type, ColumnInfo[] columns, ColModifier colModifier, string describe) {
        var param = new ParamInfo(type, type.IsNullable() ? NullableTypeHandle.Instance : NotNullHandle.Instance, name);
        var usage = new ColumnUsage(new bool[columns.Length]);
        return TypeParsingInfo.ForceGet(type).TryGetParser(type, new([], 0), param, columns, colModifier, ref usage)
            ?? throw new RinkuConfigurationException(ErrorCodes.GroupKeyUnmapped, $"the group key {describe} matched no column");
    }
}
