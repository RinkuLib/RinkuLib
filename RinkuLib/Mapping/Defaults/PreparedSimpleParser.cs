using System.Data;
using System.Data.Common;
using System.Reflection.Emit;
using Rinku.Mapping.Emission;
using Rinku.Mapping.Parsers;
using Rinku.Mapping.Parsers.Defaults;
using Rinku.Querying;

namespace Rinku.Mapping.Defaults;

internal sealed class PreparedSimpleParser<T>(DynamicMethod method, CommandBehavior behavior, object? target, INullColHandler nullColHandler, EmissionFingerprint fingerprint) {
    internal CommandBehavior Behavior { get; } = behavior;
    internal object? Target { get; } = target;
    internal EmissionFingerprint Fingerprint { get; } = fingerprint;

    internal bool Matches(ITypeParser<T> parser) => parser is SimpleTypeParser<T> simple
        && simple.MatchesGenerated(Behavior, Fingerprint, Target);

    internal ITypeParser<T> Complete() => new SimpleTypeParser<T>(Behavior, method.CreateDelegate<Func<DbDataReader, T>>(Target), Fingerprint, nullColHandler);

    internal void Discard() {
        if (Target is IGeneratedParserTarget generatedTarget)
            generatedTarget.Dispose();
    }

    internal static bool TargetsEqual(object? left, object? right) {
        if (ReferenceEquals(left, right))
            return true;
        return left is IGeneratedParserTarget generatedTarget && generatedTarget.Matches(right);
    }
}
