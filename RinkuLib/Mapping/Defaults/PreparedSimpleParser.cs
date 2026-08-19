using System.Data;
using System.Data.Common;
using System.Reflection.Emit;
using Rinku.Mapping.Parsers.Defaults;

namespace Rinku.Mapping.Defaults;

internal sealed class PreparedSimpleParser<T>(DynamicMethod method, CommandBehavior behavior, object[] targets, INullColHandler nullColHandler, EmissionFingerprint fingerprint) {
    internal CommandBehavior Behavior { get; } = behavior;
    internal object[] Targets { get; } = targets;
    internal EmissionFingerprint Fingerprint { get; } = fingerprint;

    internal bool Matches(ITypeParser<T> parser) => parser is SimpleTypeParser<T> simple
        && simple.MatchesGenerated(Behavior, Fingerprint, Targets);

    internal ITypeParser<T> Complete() => new SimpleTypeParser<T>(Behavior, method.CreateDelegate<Func<DbDataReader, T>>(Targets), Fingerprint, nullColHandler);

    internal void Discard() => DisposeTargets(Targets);

    internal static bool TargetsEqual(object[] left, object[] right) {
        if (ReferenceEquals(left, right))
            return true;
        if (left.Length != right.Length)
            return false;
        for (int i = 0; i < left.Length; i++)
            if (!ReferenceEquals(left[i], right[i])
                && (left[i] is not IGeneratedParserTarget target || !target.Matches(right[i])))
                return false;
        return true;
    }

    internal static void DisposeTargets(object[] targets) {
        for (int i = 0; i < targets.Length; i++)
            if (targets[i] is IGeneratedParserTarget target)
                target.Dispose();
    }
}
