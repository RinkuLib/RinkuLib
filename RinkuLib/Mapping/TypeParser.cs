using System.Reflection;

namespace Rinku.Mapping;

/// <summary>
/// Builds the parser that reads a result into a given <c>T</c>. Cached parsers decide which schemas they can
/// accept. When none can, the makers in <see cref="TypeParserMakers"/> are tried in order and the first to
/// claim <c>T</c> builds one. Add a maker to that list to register a custom result type.
/// </summary>
public static class TypeParser {
    internal static (INullColHandler NullColHandler, object Parser)[] ReadingInfos = [];
    /// <summary>
    /// Raised after a parser leaves the global cache and before it is disposed. A cache retaining the exact
    /// parser either cancels <see cref="ParserInvalidationMode.CheckUsage"/> or releases its reference for
    /// <see cref="ParserInvalidationMode.InvalidateReferences"/>.
    /// </summary>
    public static event EventHandler<ParserDisposingEventArgs>? ParserDisposing;
    private static ITypeParserMaker? _defaultTypeParserMaker;
    /// <summary>The fallback maker used when no registered shape maker claims a type.</summary>
    public static ITypeParserMaker DefaultTypeParserMaker {
        get => _defaultTypeParserMaker ?? throw new RinkuInternalException(ErrorCodes.InternalInvariant, "No default type-parser maker has been installed");
        set {
            lock (TypeParserMakers) {
                _defaultTypeParserMaker = value;
            }
        }
    }
    /// <summary>
    /// The makers tried in order to build a parser for a <c>T</c>, the built-in result shapes among them.
    /// Insert your own ahead of the defaults to add a shape (see the parsers guide).
    /// </summary>
    public static readonly TypeParserMakerCollection TypeParserMakers = [];
    /// <summary>
    /// Installs the initial fallback and parser makers.
    /// Call this once during startup when replacing all supplied defaults.
    /// </summary>
    public static bool TryInstallDefaults(ITypeParserMaker fallback, params ITypeParserMaker[] makers) {
        lock (TypeParserMakers) {
            if (_defaultTypeParserMaker is not null)
                return false;
            _defaultTypeParserMaker = fallback;
            for (int i = 0; i < makers.Length; i++)
                TypeParserMakers.Add(makers[i]);
            return true;
        }
    }
    /// <summary>Removes every global parser for which <paramref name="predicate"/> returns <see langword="true"/>.</summary>
    /// <param name="predicate">Selects the globally cached parser instances to remove.</param>
    /// <param name="mode">Controls whether caches retaining a selected parser must release it or may keep it alive.</param>
    /// <returns>The number of distinct parsers removed.</returns>
    public static int Invalidate(Func<ITypeParser, bool> predicate, ParserInvalidationMode mode) {
        ValidateMode(mode);
        List<ITypeParser> removed = [];
        lock (TypeParserMakers) {
            var current = ReadingInfos;
            for (int i = 0; i < current.Length; i++) {
                var parser = (ITypeParser)current[i].Parser;
                if (predicate(parser) && !ContainsReference(removed, parser))
                    removed.Add(parser);
            }
            if (removed.Count != 0)
                ReadingInfos = WithoutParsers(current, removed);
            for (int i = 0; i < removed.Count; i++)
                TryDisposeParser(removed[i], mode);
        }
        return removed.Count;
    }
    /// <summary>
    /// Releases an owner's reference to <paramref name="parser"/> and disposes it only when neither the
    /// global cache nor another subscribed cache reports that it still retains the same instance.
    /// </summary>
    /// <returns><see langword="true"/> when the parser was disposed. Otherwise <see langword="false"/>.</returns>
    public static bool Release(ITypeParser parser) => TryDisposeParser(parser, ParserInvalidationMode.CheckUsage);
    internal static bool TryInvalidateIfUnreferenced(ITypeParser parser) {
        lock (TypeParserMakers) {
            var current = ReadingInfos;
            if (!IsGloballyCached(parser))
                return false;
            ReadingInfos = WithoutParser(current, parser);
            var args = new ParserDisposingEventArgs(parser, ParserInvalidationMode.CheckUsage);
            ParserDisposing?.Invoke(null, args);
            if (args.Cancel) {
                ReadingInfos = current;
                return false;
            }
            parser.Dispose();
            return true;
        }
    }
    internal static bool TryDisposeParser(ITypeParser parser, ParserInvalidationMode mode) {
        ValidateMode(mode);
        lock (TypeParserMakers) {
            if (mode == ParserInvalidationMode.CheckUsage && IsGloballyCached(parser))
                return false;
            var args = new ParserDisposingEventArgs(parser, mode);
            ParserDisposing?.Invoke(null, args);
            if (mode == ParserInvalidationMode.CheckUsage && args.Cancel)
                return false;
            parser.Dispose();
            return true;
        }
    }
    internal static bool IsGloballyCached(ITypeParser parser) {
        var current = ReadingInfos;
        for (int i = 0; i < current.Length; i++)
            if (ReferenceEquals(current[i].Parser, parser))
                return true;
        return false;
    }
    private static (INullColHandler NullColHandler, object Parser)[] WithoutParser(
        (INullColHandler NullColHandler, object Parser)[] current, ITypeParser parser) {
        int kept = 0;
        for (int i = 0; i < current.Length; i++)
            if (!ReferenceEquals(current[i].Parser, parser))
                kept++;
        var updated = new (INullColHandler NullColHandler, object Parser)[kept];
        int destination = 0;
        for (int i = 0; i < current.Length; i++)
            if (!ReferenceEquals(current[i].Parser, parser))
                updated[destination++] = current[i];
        return updated;
    }
    private static (INullColHandler NullColHandler, object Parser)[] WithoutParsers(
        (INullColHandler NullColHandler, object Parser)[] current, List<ITypeParser> parsers) {
        int kept = 0;
        for (int i = 0; i < current.Length; i++)
            if (!ContainsReference(parsers, (ITypeParser)current[i].Parser))
                kept++;
        var updated = new (INullColHandler NullColHandler, object Parser)[kept];
        int destination = 0;
        for (int i = 0; i < current.Length; i++)
            if (!ContainsReference(parsers, (ITypeParser)current[i].Parser))
                updated[destination++] = current[i];
        return updated;
    }
    private static bool ContainsReference(List<ITypeParser> parsers, ITypeParser parser) {
        for (int i = 0; i < parsers.Count; i++)
            if (ReferenceEquals(parsers[i], parser))
                return true;
        return false;
    }
    private static void ValidateMode(ParserInvalidationMode mode) {
        if (mode != ParserInvalidationMode.CheckUsage && mode != ParserInvalidationMode.InvalidateReferences)
            throw new ArgumentOutOfRangeException(nameof(mode));
    }
    /// <summary>The root nullability implied by <paramref name="type"/> itself.</summary>
    public static INullColHandler GetDefaultNullColHandler(Type type) => Nullable.GetUnderlyingType(type) is not null
        ? NullableTypeHandle.Instance : NotNullHandle.Instance;
    /// <summary>The root nullability implied by <typeparamref name="T"/> itself.</summary>
    public static INullColHandler GetDefaultNullColHandler<T>() => GetDefaultNullColHandler(typeof(T));
    /// <summary>
    /// Gets a parser for <typeparamref name="T"/> that accepts the supplied columns.
    /// Reuse the returned parser or a <see cref="QueryCommand"/> instead of calling this for every execution.
    /// </summary>
    /// <param name="cols">The columns the result carries.</param>
    /// <param name="nullColHandler">
    /// A custom root null rule.
    /// When omitted or equal to <see cref="GetDefaultNullColHandler"/>, the type's own nullability applies
    /// </param>
    public static ITypeParser<T> GetTypeParser<T>(ColumnInfo[] cols, INullColHandler? nullColHandler = null) {
        nullColHandler ??= GetDefaultNullColHandler<T>();
        var readingInfos = ReadingInfos;
        foreach (var (nullCol, p) in readingInfos) {
            if (p is ITypeParser<T> parser && nullCol == nullColHandler && parser.CanParse(cols))
                return parser;
        }
        lock (TypeParserMakers) {
            var current = ReadingInfos;
            for (int i = 0; i < current.Length; i++) {
                var (nullCol, p) = current[i];
                if (p is ITypeParser<T> parser && nullCol == nullColHandler && parser.CanParse(cols))
                    return parser;
            }
            var unusual = MakeParser<T>(cols, nullColHandler);
            current = ReadingInfos;
            var updated = new (INullColHandler, object)[current.Length + 1];
            current.CopyTo(updated, 0);
            updated[^1] = (nullColHandler, unusual);
            ReadingInfos = updated;
            return unusual;
        }
    }
    /// <summary>Gets the cached parser for a runtime result type and schema.</summary>
    public static ITypeParser GetTypeParser(Type type, ColumnInfo[] cols, INullColHandler? nullColHandler = null) {
        nullColHandler ??= GetDefaultNullColHandler(type);
        var readingInfos = ReadingInfos;
        for (int i = 0; i < readingInfos.Length; i++) {
            var (nullCol, parser) = readingInfos[i];
            if (parser is ITypeParser p && p.Type == type && nullCol == nullColHandler && p.CanParse(cols))
                return p;
        }
        lock (TypeParserMakers) {
            var current = ReadingInfos;
            for (int i = 0; i < current.Length; i++) {
                var (nullCol, parser) = current[i];
                if (parser is ITypeParser p && p.Type == type && nullCol == nullColHandler && p.CanParse(cols))
                    return p;
            }
            var maker = typeof(TypeParser).GetMethod(nameof(MakeParser), BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new RinkuInternalException(ErrorCodes.InternalInvariant, $"{nameof(MakeParser)} was not found.");
            ITypeParser created;
            try {
                created = maker.MakeGenericMethod(type).Invoke(null, [cols, nullColHandler]) as ITypeParser
                    ?? throw new RinkuInternalException(ErrorCodes.InternalInvariant, $"{nameof(MakeParser)} returned no parser.");
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null) {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
            var updated = new (INullColHandler, object)[current.Length + 1];
            current.CopyTo(updated, 0);
            updated[^1] = (nullColHandler, created);
            ReadingInfos = updated;
            return created;
        }
    }
    private static ITypeParser<T> MakeParser<T>(ColumnInfo[] cols, INullColHandler nullColHandler) {
        ITypeParserMaker typeParserMaker = DefaultTypeParserMaker;
        foreach (var tpm in TypeParserMakers)
            if (tpm.CanHandle<T>()) {
                typeParserMaker = tpm;
                break;
            }
        if (!typeParserMaker.TryMakeParser<T>(nullColHandler, cols, out var info))
            Refuse.NoParser(typeof(T), cols);
        return info;
    }
    /// <summary>The parser for <typeparamref name="T"/> over the columns of <typeparamref name="TSchema"/>.</summary>
    public static ITypeParser<T> GetTypeParser<TSchema, T>(INullColHandler? nullColHandler = null)
        => GetTypeParser<T>(TypeSchema<TSchema>._schema, nullColHandler);

    /// <summary>The parser for <typeparamref name="T"/> over the columns derived from <paramref name="schemaType"/>.</summary>
    public static ITypeParser<T> GetTypeParser<T>(Type schemaType, INullColHandler? nullColHandler = null) => GetTypeParser<T>(SchemaExtractor.FromType(schemaType), nullColHandler);

    /// <summary>The parser for <typeparamref name="T"/> over the columns derived from <paramref name="schemaMethod"/>.</summary>
    public static ITypeParser<T> GetTypeParser<T>(MethodBase schemaMethod, INullColHandler? nullColHandler = null) => GetTypeParser<T>(SchemaExtractor.FromMethod(schemaMethod), nullColHandler);

    /// <summary>The parser for <typeparamref name="T"/> over the columns derived from <paramref name="schemaConstructor"/>.</summary>
    public static ITypeParser<T> GetTypeParser<T>(ConstructorInfo schemaConstructor, INullColHandler? nullColHandler = null) => GetTypeParser<T>(SchemaExtractor.FromConstructor(schemaConstructor), nullColHandler);

    /// <summary>The parser for <typeparamref name="T"/> over the columns derived from <paramref name="schemaFactory"/>.</summary>
    public static ITypeParser<T> GetTypeParser<T>(Delegate schemaFactory, INullColHandler? nullColHandler = null) => GetTypeParser<T>(SchemaExtractor.FromMethod(schemaFactory.Method), nullColHandler);

    /// <summary>The parser for <typeparamref name="T"/> over a reflected type, method, or constructor.</summary>
    public static ITypeParser<T> GetTypeParser<T>(MemberInfo schemaMember, INullColHandler? nullColHandler = null) {
        return schemaMember switch {
            Type schemaType => GetTypeParser<T>(schemaType, nullColHandler),
            ConstructorInfo schemaConstructor => GetTypeParser<T>(schemaConstructor, nullColHandler),
            MethodBase schemaMethod => GetTypeParser<T>(schemaMethod, nullColHandler),
            _ => throw new ArgumentException("The schema member must be a type, method, or constructor.", nameof(schemaMember))
        };
    }

    /// <summary>Gets a parser for a runtime result type over the columns derived from <paramref name="schemaType"/>.</summary>
    public static ITypeParser GetTypeParser(Type resultType, Type schemaType, INullColHandler? nullColHandler = null) => GetTypeParser(resultType, SchemaExtractor.FromType(schemaType), nullColHandler);

    /// <summary>Gets a parser for a runtime result type over the columns derived from <paramref name="schemaMethod"/>.</summary>
    public static ITypeParser GetTypeParser(Type resultType, MethodBase schemaMethod, INullColHandler? nullColHandler = null) => GetTypeParser(resultType, SchemaExtractor.FromMethod(schemaMethod), nullColHandler);

    /// <summary>Gets a parser for a runtime result type over the columns derived from <paramref name="schemaConstructor"/>.</summary>
    public static ITypeParser GetTypeParser(Type resultType, ConstructorInfo schemaConstructor, INullColHandler? nullColHandler = null) => GetTypeParser(resultType, SchemaExtractor.FromConstructor(schemaConstructor), nullColHandler);

    /// <summary>Gets a parser for a runtime result type over the columns derived from <paramref name="schemaFactory"/>.</summary>
    public static ITypeParser GetTypeParser(Type resultType, Delegate schemaFactory, INullColHandler? nullColHandler = null) => GetTypeParser(resultType, SchemaExtractor.FromMethod(schemaFactory.Method), nullColHandler);

    /// <summary>Gets a parser for a runtime result type over a reflected type, method, or constructor.</summary>
    public static ITypeParser GetTypeParser(Type resultType, MemberInfo schemaMember, INullColHandler? nullColHandler = null) {
        return schemaMember switch {
            Type schemaType => GetTypeParser(resultType, schemaType, nullColHandler),
            ConstructorInfo schemaConstructor => GetTypeParser(resultType, schemaConstructor, nullColHandler),
            MethodBase schemaMethod => GetTypeParser(resultType, schemaMethod, nullColHandler),
            _ => throw new ArgumentException("The schema member must be a type, method, or constructor.", nameof(schemaMember))
        };
    }
}
