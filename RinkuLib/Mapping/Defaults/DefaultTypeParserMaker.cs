using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using Rinku.Internal;
using Rinku.Mapping.Parsers;
using Rinku.Mapping.Parsers.Defaults;

namespace Rinku.Mapping.Defaults;

/// <summary>
/// The object parser, the maker that claims any type no other maker did. It maps a result's columns onto a
/// type's members and constructor by name, the default behind mapping a plain class, record, or struct.
/// </summary>
public class DefaultTypeParserMaker : ITypeParserMaker, ITypeParserSchemaMatcher {
    /// <inheritdoc/>
    [ExcludeFromCodeCoverage]
    public bool CanHandle<T>() => true;
    private static readonly Type[] TReaderArg = [typeof(object[]), typeof(DbDataReader)];
    internal static readonly Module Module = typeof(DbDataReader).Module;
    internal static readonly ParamInfo InfoNullable = new(ParamInfo.NoType, NullableTypeHandle.Instance, NoNameComparer.Instance);
    internal static readonly ParamInfo InfoNotNullable = new(ParamInfo.NoType, NotNullHandle.Instance, NoNameComparer.Instance);
    /// <summary>
    /// Builds the object parser for <typeparamref name="T"/> over the given columns, matching them to the
    /// type's members and constructor. It opts the result into sequential reading when the mapping's column
    /// order allows it.
    /// </summary>
    public bool TryMakeParser<T>(INullColHandler nullColHandler, ColumnInfo[] cols, [MaybeNullWhen(false)] out ITypeParser<T> parser) {
        if (!TryPrepareParser<T>(nullColHandler, cols, out var prepared, out parser))
            return false;
        parser ??= prepared!.Complete();
        return true;
    }

    internal bool TryPrepareParser<T>(INullColHandler nullColHandler, ColumnInfo[] cols, out PreparedSimpleParser<T>? prepared, out ITypeParser<T>? parser) {
        var rd = Negotiate<T>(nullColHandler, cols);
        if (rd is null) {
            prepared = null;
            parser = default;
            return false;
        }
        if (!DbItemPlan.AllSimple(rd)) {
            prepared = null;
            parser = MultiRowEmitter.Build<T>(rd, cols);
            return true;
        }
        var dm = new DynamicMethod(
            $"Map_{typeof(T).Name}_{Guid.NewGuid():N}",
            typeof(T), TReaderArg, Module,
            skipVisibility: true
        );
        Generator gen =
#if DEBUG
            new(dm.GetILGenerator(), cols);
#else
            new(dm.GetILGenerator());
#endif
        Label? nullJump = rd.NeedNullSetPoint(cols) ? gen.DefineLabel() : null;
        ((ISimpleDbItemPlan)rd).Emit(cols, gen, nullJump.HasValue ? new(nullJump.Value, 0) : default);
        object[] targets = gen.GetTargets();
        if (nullJump.HasValue) {
            var parsed = gen.DefineLabel();
            gen.Emit(OpCodes.Br, parsed);
            gen.MarkLabel(nullJump.Value);
            DbItemPlan.EmitDefaultValue(typeof(T), gen);
            gen.MarkLabel(parsed);
        }
        gen.Emit(OpCodes.Ret);
        dm.DefineParameter(1, ParameterAttributes.In, "reader");
        var prevIndex = -1;
        var defaultBehavior = CommandBehavior.SingleRow | CommandBehavior.SingleResult;
        if (rd.IsSequencial(ref prevIndex))
            defaultBehavior |= CommandBehavior.SequentialAccess;
        prepared = new(dm, defaultBehavior, targets, nullColHandler, gen.Fingerprint);
        parser = null;
        return true;
    }

    bool ITypeParserSchemaMatcher.CanParse<T>(ITypeParser<T> parser, ColumnInfo[] schema) {
        if (!TryPrepareParser<T>(parser is SimpleTypeParser<T> simple ? simple.GeneratedNullability : TypeParser.GetDefaultNullColHandler<T>(), schema, out var prepared, out _)
            || prepared is null)
            return false;
        bool matches = prepared.Matches(parser);
        prepared.Discard();
        return matches;
    }

    private static DbItemPlan? Negotiate<T>(INullColHandler nullColHandler, ColumnInfo[] cols) {
        var closedType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        var paramInfo = nullColHandler == NullableTypeHandle.Instance
            ? InfoNullable
            : nullColHandler == NotNullHandle.Instance
                ? InfoNotNullable
                : new(ParamInfo.NoType, nullColHandler, NoNameComparer.Instance);
        var colUsage = new ColumnUsage(stackalloc bool[cols.Length]);
        return TypeParsingInfo.ForceGet(closedType).TryGetParser(typeof(T), new([], 0), paramInfo, cols, new(), ref colUsage);
    }

    internal ITypeParser<T> ForceMultiRow<T>(INullColHandler nullColHandler, ColumnInfo[] cols) {
        var rd = Negotiate<T>(nullColHandler, cols)
            ?? throw new RinkuInternalException(ErrorCodes.InternalInvariant, $"no read plan for {typeof(T)} over the given columns");
        return MultiRowEmitter.Build<T>(rd, cols);
    }
}
