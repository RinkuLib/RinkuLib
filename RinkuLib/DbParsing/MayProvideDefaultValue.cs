using RinkuLib.Tools;

namespace RinkuLib.DbParsing;
/// <summary>
/// Produces the type's default value, the parser used for a member that has no matching column.
/// </summary>
public class DefaultEmiter(Type targetType) : DbItemParser {
    private readonly Type targetType = targetType;
    /// <inheritdoc/>
    public override void Emit(ColumnInfo[] cols, Generator generator, NullSetPoint nullSetPoint, out object? targetObject) {
        targetObject = null;
        EmitDefaultValue(targetType, generator);
    }
    /// <inheritdoc/>
    public override bool IsSequencial(ref int previousIndex) => true;

    /// <inheritdoc/>
    public override bool NeedNullSetPoint(ColumnInfo[] cols) => false;
}
/// <summary>Fallbabk to emit the default value of the type</summary>
public class DefaultValueFallback : IFallbackParserGetter {
    /// <summary>Singleton</summary>
    public static readonly DefaultValueFallback Instance = new();
    private DefaultValueFallback() { }
    /// <inheritdoc/>
    public DbItemParser? FallbackTryGetParser(Type type) => new DefaultEmiter(type);
}
/// <summary>Applies reading-order flags to a <see cref="ColModifier"/>, per slot or across a subtree.</summary>
public class FlagUpdater(UsageFlags Flags, bool Subtree = false) : IColModifier {
    /// <summary>The flag that will be added to the current modifyer</summary>
    public UsageFlags Flags = Flags;
    /// <summary>When true the flags govern a complex slot's whole subtree; otherwise only its first column.</summary>
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
    public void EnterSubtree(ref ColModifier mod, int nbUsed) {
        if (Subtree)
            mod.Flags |= Flags;
        else {
            mod.SwapFirstAt = nbUsed;
            mod.SwapFirstFlags = Flags;
        }
    }
}
/// <summary>
/// A member read plan that also carries a reading-order tweak and a fallback for when no column matches, the
/// plan the reading-order and default attributes assemble.
/// </summary>
public class ParamInfoPlus(Type Type, INullColHandler NullColHandler, INameComparer NameComparer, IColModifier colModifier, IFallbackParserGetter fallbackParserGetter) : ParamInfo(Type, NullColHandler, NameComparer) {
    /// <inheritdoc/>
    public IColModifier ColModifier { get => field; set => Interlocked.Exchange(ref field, value); } = colModifier;
    /// <inheritdoc/>
    public IFallbackParserGetter FallbackParserGetter { get => field; set => Interlocked.Exchange(ref field, value); } = fallbackParserGetter;
    /// <inheritdoc/>
    public override void UpdateColModifier(ref ColModifier mod)
        => ColModifier.UpdateColModifier(ref mod);
    /// <inheritdoc/>
    public override void EnterSubtree(ref ColModifier mod, int nbUsed)
        => ColModifier.EnterSubtree(ref mod, nbUsed);

    /// <inheritdoc/>
    public override DbItemParser? FallbackTryGetParser(Type type)
        => FallbackParserGetter.FallbackTryGetParser(type);
}
/// <summary>Adjusts how a member claims its columns, its reading order and reuse, per slot or across a subtree.</summary>
public interface IColModifier {
    /// <summary>A modifier that changes nothing.</summary>
    public static readonly IColModifier Nothing = new NothingInst();
    private class NothingInst : IColModifier {
        /// <inheritdoc/>
        public void UpdateColModifier(ref ColModifier mod) { }
    }
    /// <summary>Provide a way to modify the col modifier based on the param info state</summary>
    public void UpdateColModifier(ref ColModifier mod);
    /// <summary>
    /// Set up the modifier as a complex slot's subtree is entered: a subtree-scope flag applies across
    /// the subtree, a slot-scope one arms the swap for the subtree's first consumed column.
    /// </summary>
    public void EnterSubtree(ref ColModifier mod, int nbUsed) { }
}
/// <summary>Supplies a parser for a member when no column matches it, such as one that produces a default value.</summary>
public interface IFallbackParserGetter {
    /// <summary>A fallback that supplies nothing, leaving an unmatched member an error.</summary>
    public static readonly IFallbackParserGetter Nothing = new NothingInst();
    private class NothingInst : IFallbackParserGetter {
        /// <inheritdoc/>
        public DbItemParser? FallbackTryGetParser(Type type) => null;
    }
    /// <summary>
    /// A parser to use for <paramref name="type"/> when the normal column matching found none, or
    /// <see langword="null"/> to leave it unmatched.
    /// </summary>
    public DbItemParser? FallbackTryGetParser(Type type);
}