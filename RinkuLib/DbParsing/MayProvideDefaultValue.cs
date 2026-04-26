using RinkuLib.Tools;

namespace RinkuLib.DbParsing;
/// <summary>
/// Generate the IL of the default value of the type
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
/// <summary>Fallbabk to emit the default value of the type</summary>
public class FlagUpdater(UsageFlags Flags) : IColModifier {
    /// <summary>The flag that will be added to the current modifyer</summary>
    public UsageFlags Flags = Flags;
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
}
/// <summary>
/// Emmit the default value of the type when no match with the schema
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
    public override DbItemParser? FallbackTryGetParser(Type type)
        => FallbackParserGetter.FallbackTryGetParser(type);
}
/// <summary></summary>
public interface IColModifier {
    /// <summary>An instance that does nothing</summary>
    public static readonly IColModifier Nothing = new NothingInst();
    /// <summary></summary>
    private class NothingInst : IColModifier {
        /// <inheritdoc/>
        public void UpdateColModifier(ref ColModifier mod) { }
    }
    /// <summary>Provide a way to modify the col modifier based on the param info state</summary>
    public void UpdateColModifier(ref ColModifier mod);
}
/// <summary></summary>
public interface IFallbackParserGetter {
    /// <summary>An instance that does nothing</summary>
    public static readonly IFallbackParserGetter Nothing = new NothingInst();
    /// <summary></summary>
    private class NothingInst : IFallbackParserGetter {
        /// <inheritdoc/>
        public DbItemParser? FallbackTryGetParser(Type type) => null;
    }
    /// <summary>
    /// Provide a way to retrieve a <see cref="DbItemParser"/> when the normal way fails
    /// </summary>
    /// <returns></returns>
    public DbItemParser? FallbackTryGetParser(Type type);
}