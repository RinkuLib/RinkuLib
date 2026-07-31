using System.Reflection;
using RinkuLib.Tools;

namespace RinkuLib.DbParsing;

/// <summary>
/// An attribute that makes a group boundary <see cref="IGroupingRule"/>. The registration scan finds every attribute
/// implementing this on one level (a type and its members, or a construction and its parameters) and lets it build
/// the rule, so a new grouping mechanism is its own attribute and <see cref="IGroupingRule"/> with no change to the
/// scan. An attribute either composes with others of its kind on the level into one rule (marked members, marked
/// parameters) or stands alone (a boundary method), and the scan refuses a standalone that shares its level.
/// </summary>
public interface IGroupingRuleMaker {
    /// <summary>Whether this attribute on <paramref name="carrier"/> composes one rule with others of its kind on the same level, or stands alone there.</summary>
    bool Composes(ICustomAttributeProvider carrier);
    /// <summary>Builds the rule from every carrier that declared this attribute on one level; a standalone attribute gets its single carrier.</summary>
    IGroupingRule MakeRule(IReadOnlyList<ICustomAttributeProvider> carriers);
}

/// <summary>
/// Finds the <see cref="IGroupingRuleMaker"/>s on one level and lets them build the rule. A level is a type and its
/// members or a construction and its parameters. A standalone key may not share its level with any other key, and one
/// level composes a single kind, so mixing kinds or a standalone with others throws <c>ConflictingGroupKey</c>.
/// </summary>
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
                $"{owner} composes group keys of more than one kind on one level; a level composes one kind");
        return found[0].Attr.MakeRule([.. found.Select(f => f.Carrier)]);
    }
}

/// <summary>
/// The shared negotiation a rule reuses: turn a name comparer and a type into a reused reader over the columns. A
/// key needs only how to match its column (the name comparer) and how to read it (the type); its null handling
/// follows the type, so no null rule crosses in. A custom <see cref="IGroupingRule"/> calls this to read each of
/// its key components.
/// </summary>
public static class GroupKeyNegotiation {
    /// <summary>Negotiates a fresh reader matching <paramref name="name"/> as <paramref name="type"/> against the columns, reusing what the slots claimed.</summary>
    public static DbItemPlan NegotiateReader(INameComparer name, Type type, ColumnInfo[] columns, ColModifier colModifier, string describe) {
        var param = new ParamInfo(type, type.IsNullable() ? NullableTypeHandle.Instance : NotNullHandle.Instance, name);
        var usage = new ColumnUsage(new bool[columns.Length]);
        return TypeParsingInfo.ForceGet(type).TryGetParser(type, new([], 0), param, columns, colModifier, ref usage, false)
            ?? throw new RinkuConfigurationException(ErrorCodes.GroupKeyUnmapped, $"the group key {describe} matched no column");
    }
}
