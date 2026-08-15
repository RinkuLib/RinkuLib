using Rinku.Internal;

namespace Rinku.Mapping;
/// <summary>
/// Produces the type's default value, the parser used for a member that has no matching column.
/// </summary>
public class DefaultEmitter(Type targetType) : SimpleDbItemParser {
    private readonly Type targetType = targetType;
    /// <inheritdoc/>
    public override void Emit(ColumnInfo[] cols, Generator generator, NullSetPoint nullSetPoint) {
        EmitDefaultValue(targetType, generator);
    }
    /// <inheritdoc/>
    public override bool IsSequencial(ref int previousIndex) => true;

    /// <inheritdoc/>
    public override bool NeedNullSetPoint(ColumnInfo[] cols) => false;
}
/// <summary>Uses the type default when no result column matches a member.</summary>
public class DefaultValueFallback : IFallbackParserGetter {
    /// <summary>Singleton</summary>
    public static readonly DefaultValueFallback Instance = new();
    private DefaultValueFallback() { }
    /// <inheritdoc/>
    public DbItemPlan? FallbackTryGetParser(Type type) => new DefaultEmitter(type);
}
/// <summary>Applies column order and reuse settings to one slot or its complete nested value.</summary>
public class FlagUpdater(UsageFlags Flags, bool Subtree = false) : IColModifier {
    /// <summary>Gets the column usage setting added by this fallback.</summary>
    public UsageFlags Flags = Flags;
    /// <summary>Gets whether the settings apply to the complete nested value.</summary>
    public readonly bool Subtree = Subtree;
    /// <summary>Singleton</summary>
    public static readonly FlagUpdater CanReuse = new(UsageFlags.CanReuse);
    /// <summary>Singleton</summary>
    public static readonly FlagUpdater SequentialRead = new(UsageFlags.SequentialRead);
    /// <summary>Singleton</summary>
    public static readonly FlagUpdater RemoveSequentialRead = new(UsageFlags.RemoveSequentialRead);
    /// <summary>Singleton</summary>
    public static readonly FlagUpdater CanReuseAndSequential = new(UsageFlags.CanReuse | UsageFlags.SequentialRead);
    /// <summary>Singleton</summary>
    public static readonly FlagUpdater CanReuseAndRemoveSequential = new(UsageFlags.CanReuse | UsageFlags.RemoveSequentialRead);
    /// <inheritdoc/>
    public void UpdateColModifier(ref ColModifier mod) => mod.Flags |= Flags;
    /// <inheritdoc/>
    public void EnterSubtree(ref ColModifier mod, int nbClaims) {
        if (Subtree)
            mod.Flags |= Flags;
        else {
            mod.SwapFirstAt = nbClaims;
            mod.SwapFirstFlags = Flags;
        }
    }
}
/// <summary>
/// A member read plan that also carries a reading-order tweak and a fallback for when no column matches, the
/// plan the reading-order and default attributes assemble.
/// </summary>
public class ParamInfoPlus(Type Type, INullColHandler NullColHandler, INameComparer NameComparer, IColModifier colModifier, IFallbackParserGetter fallbackParserGetter) : ParamInfo(Type, NullColHandler, NameComparer) {
    /// <summary>When true, a scalar value accepts only a column with the exact type.</summary>
    public override bool RequireExactType {
        get => field;
        set => field = value;
    }
    /// <inheritdoc/>
    public IColModifier ColModifier {
        get => field;
        set => Interlocked.Exchange(ref field, value);
    } = colModifier;
    /// <inheritdoc/>
    public IFallbackParserGetter FallbackParserGetter {
        get => field;
        set => Interlocked.Exchange(ref field, value);
    } = fallbackParserGetter;
    /// <inheritdoc/>
    public override void UpdateColModifier(ref ColModifier mod)
        => ColModifier.UpdateColModifier(ref mod);
    /// <inheritdoc/>
    public override void EnterSubtree(ref ColModifier mod, int nbClaims)
        => ColModifier.EnterSubtree(ref mod, nbClaims);

    /// <inheritdoc/>
    public override DbItemPlan? FallbackTryGetParser(Type type)
        => FallbackParserGetter.FallbackTryGetParser(type);
}
/// <summary>Adjusts how a member claims its columns, its reading order and reuse, per slot or across a subtree.</summary>
public interface IColModifier {
    /// <summary>A modifier that changes nothing.</summary>
    public static readonly IColModifier Nothing = new NothingInst();
    private class NothingInst : IColModifier {
        public void UpdateColModifier(ref ColModifier mod) { }
    }
    /// <summary>Applies settings before a slot is mapped.</summary>
    public void UpdateColModifier(ref ColModifier mod);
    /// <summary>
    /// Applies settings when a nested value begins.
    /// </summary>
    public void EnterSubtree(ref ColModifier mod, int nbClaims) { }
}
/// <summary>Supplies a parser for a member when no column matches it, such as one that produces a default value.</summary>
public interface IFallbackParserGetter {
    /// <summary>A fallback that supplies nothing, leaving an unmatched member an error.</summary>
    public static readonly IFallbackParserGetter Nothing = new NothingInst();
    private class NothingInst : IFallbackParserGetter {
        public DbItemPlan? FallbackTryGetParser(Type type) => null;
    }
    /// <summary>
    /// A parser to use for <paramref name="type"/> when the normal column matching found none, or
    /// <see langword="null"/> to leave it unmatched.
    /// </summary>
    public DbItemPlan? FallbackTryGetParser(Type type);
}
