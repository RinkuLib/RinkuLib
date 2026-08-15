using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace RinkuLib.Analyzers;

internal static class ConstructorContract {
    public static ImmutableArray<IMethodSymbol> GetCandidates(ISymbol symbol) => symbol switch {
        INamedTypeSymbol type => type.InstanceConstructors,
        IMethodSymbol method => [method],
        _ => []
    };

    public static bool HasMatch(INamedTypeSymbol target, ISymbol reference) {
        foreach (var targetConstructor in target.InstanceConstructors) {
            foreach (var candidate in GetCandidates(reference)) {
                if (Matches(targetConstructor, candidate))
                    return true;
            }
        }
        return false;
    }

    public static bool MatchesAny(INamedTypeSymbol target, IMethodSymbol candidate) {
        foreach (var constructor in target.InstanceConstructors) {
            if (Matches(constructor, candidate))
                return true;
        }
        return false;
    }

    public static bool Matches(IMethodSymbol target, IMethodSymbol reference) {
        if (target.Parameters.Length != reference.Parameters.Length)
            return false;

        for (var i = 0; i < target.Parameters.Length; i++) {
            var left = target.Parameters[i];
            var right = reference.Parameters[i];
            if (!string.Equals(left.Name, right.Name, StringComparison.Ordinal)
                || !SymbolEqualityComparer.IncludeNullability.Equals(left.Type, right.Type)
                || left.RefKind != right.RefKind
                || left.IsParams != right.IsParams)
                return false;
        }
        return true;
    }

    public static bool HasConflictingSignature(INamedTypeSymbol target, IMethodSymbol candidate) {
        foreach (var constructor in target.InstanceConstructors) {
            if (constructor.Parameters.Length != candidate.Parameters.Length)
                continue;

            var matches = true;
            for (var i = 0; i < constructor.Parameters.Length; i++) {
                var left = constructor.Parameters[i];
                var right = candidate.Parameters[i];
                if (!SymbolEqualityComparer.Default.Equals(left.Type, right.Type)
                    || left.RefKind != right.RefKind) {
                    matches = false;
                    break;
                }
            }
            if (matches)
                return true;
        }
        return false;
    }
}
