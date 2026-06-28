using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using RinkuLib.Tools;
using RinkuLib.TypeAccessing;

namespace RinkuLib.DbParsing;

/// <summary></summary>
public class DefaultTypeParserMaker : ITypeParserMaker {
    /// <inheritdoc/>
    public bool CanHandle<T>() => true;
    private static readonly Type[] TReaderArg = [typeof(object), typeof(DbDataReader)];
    internal static readonly Module Module = typeof(DbDataReader).Module;
    internal static readonly ParamInfo InfoNullable = new(ParamInfo.NoType, NullableTypeHandle.Instance, NoNameComparer.Instance);
    internal static readonly ParamInfo InfoNotNullable = new(ParamInfo.NoType, NotNullHandle.Instance, NoNameComparer.Instance);
    /// <summary>
    /// The compilation core. Orchestrates the transition from metadata to IL.
    /// </summary>
    /// <remarks>
    /// <b>Process:</b>
    /// <list type="number">
    /// <item>Determines the appropriate <see cref="INullColHandler"/> based on the nullability of <typeparamref name="T"/>.</item>
    /// <item>Requests a <see cref="DbItemParser"/> (emission tree) from the <see cref="TypeParsingInfo"/> registry.</item>
    /// <item>Initializes a <see cref="DynamicMethod"/> and uses the emission tree to generate IL via <see cref="Generator"/>.</item>
    /// <item>Evaluates if the generated logic allows for <see cref="CommandBehavior.SequentialAccess"/> optimization.</item>
    /// </list>
    /// </remarks>
    public bool TryMakeParser<T>(INullColHandler nullColHandler, ColumnInfo[] cols, [MaybeNullWhen(false)] out ITypeParser<T> parser) {
        var t = Nullable.GetUnderlyingType(typeof(T));
        bool isNullable = t is not null;
        var closedType = t ?? typeof(T);
        var paramInfo = nullColHandler == NullableTypeHandle.Instance
            ? InfoNullable 
            : nullColHandler == NullableTypeHandle.Instance
                ? InfoNotNullable
                : new(ParamInfo.NoType, nullColHandler, NoNameComparer.Instance);
        var colUsage = new ColumnUsage(stackalloc bool[cols.Length]);
        var rd = TypeParsingInfo.ForceGet(closedType).TryGetParser(typeof(T), new([], 0), paramInfo, cols, new(), ref colUsage);
        if (rd is null) {
            parser = default;
            return false;
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
        rd.Emit(cols, gen, rd.NeedNullSetPoint(cols) ? new(gen.DefineLabel(), 0) : default, out var targetObj);
        gen.Emit(OpCodes.Ret);
        dm.DefineParameter(1, ParameterAttributes.In, "reader");
        var prevIndex = -1;
        var defaultBehavior = CommandBehavior.SingleRow | CommandBehavior.SingleResult;
        if (rd.IsSequencial(ref prevIndex))
            defaultBehavior |= CommandBehavior.SequentialAccess;
        parser = new SimpleTypeParser<T>(defaultBehavior, dm.CreateDelegate<Func<DbDataReader, T>>(targetObj));
        return true;
    }
}