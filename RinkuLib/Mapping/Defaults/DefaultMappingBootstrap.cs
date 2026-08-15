using System.Runtime.CompilerServices;
using Rinku.Mapping.Parsers.Defaults;

namespace Rinku.Mapping.Defaults;

/// <summary>Registers the mapping defaults supplied by Rinku.</summary>
public static class DefaultMappingBootstrap {
    private static int Initialized;

    /// <summary>Installs the shipped mapping defaults. Calling this more than once has no effect.</summary>
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize() {
        if (Interlocked.Exchange(ref Initialized, 1) != 0)
            return;

        TypeParser.TryInstallDefaults(new DefaultTypeParserMaker(),
            new EnumerableTypeParserMaker(),
            new ReusingBaseTypeParserMaker([typeof(Optional<>), typeof(OptionalStruct<>), typeof(OptionalNullable<>)],
                (definition, itemType, ref _) => typeof(OptionalTypeParser<,>).MakeGenericType(definition.MakeGenericType(itemType), itemType),
                (definition, itemType, ref _) => typeof(FastOptionalTypeParser<,>).MakeGenericType(definition.MakeGenericType(itemType), itemType)),
            new ReusingBaseTypeParserMaker([typeof(OptionalNullableStruct<>)],
                (definition, itemType, ref _) => typeof(OptionalTypeParser<,>).MakeGenericType(definition.MakeGenericType(itemType.GetGenericArguments()[0]), itemType),
                (definition, itemType, ref _) => typeof(FastOptionalTypeParser<,>).MakeGenericType(definition.MakeGenericType(itemType.GetGenericArguments()[0]), itemType),
                static type => typeof(Nullable<>).MakeGenericType(type)),
            new ReusingBaseTypeParserMaker([typeof(Single<>)],
                (definition, itemType, ref _) => typeof(SingleTypeParser<,>).MakeGenericType(definition.MakeGenericType(itemType), itemType),
                (definition, itemType, ref _) => typeof(FastSingleTypeParser<,>).MakeGenericType(definition.MakeGenericType(itemType), itemType)),
            new ReusingBaseTypeParserMaker([typeof(SingleOrDefault<>), typeof(SingleOrDefaultStruct<>), typeof(SingleOrDefaultNullable<>)],
                (definition, itemType, ref _) => typeof(SingleOrDefaultTypeParser<,>).MakeGenericType(definition.MakeGenericType(itemType), itemType),
                (definition, itemType, ref _) => typeof(FastSingleOrDefaultTypeParser<,>).MakeGenericType(definition.MakeGenericType(itemType), itemType)),
            new ReusingBaseTypeParserMaker([typeof(SingleOrDefaultNullableStruct<>)],
                (definition, itemType, ref _) => typeof(SingleOrDefaultTypeParser<,>).MakeGenericType(definition.MakeGenericType(itemType.GetGenericArguments()[0]), itemType),
                (definition, itemType, ref _) => typeof(FastSingleOrDefaultTypeParser<,>).MakeGenericType(definition.MakeGenericType(itemType.GetGenericArguments()[0]), itemType),
                static type => typeof(Nullable<>).MakeGenericType(type)));

        var tuples = CtorTypeInfo.Instance;
        TypeParsingInfo.AddOrSet(typeof(ValueTuple<>), tuples);
        TypeParsingInfo.AddOrSet(typeof(ValueTuple<,>), tuples);
        TypeParsingInfo.AddOrSet(typeof(ValueTuple<,,>), tuples);
        TypeParsingInfo.AddOrSet(typeof(ValueTuple<,,,>), tuples);
        TypeParsingInfo.AddOrSet(typeof(ValueTuple<,,,,>), tuples);
        TypeParsingInfo.AddOrSet(typeof(ValueTuple<,,,,,>), tuples);
        TypeParsingInfo.AddOrSet(typeof(ValueTuple<,,,,,,>), tuples);
        TypeParsingInfo.AddOrSet(typeof(ValueTuple<,,,,,,,>), tuples);
        TypeParsingInfo.AddOrSet<DynaObject>(DynaObjectTypeInfo.Instance);
        TypeParsingInfo.AddOrSet<Dictionary<string, object>>(DictionaryTypeParsingInfo.Instance,
            saveAsGenericDefinitionWhenGeneric: false);
        TypeParsingInfo.AddOrSet(typeof(List<>), MultiRowTypeParsingInfo.ForList);
        TypeParsingInfo.AddOrSet(typeof(IEnumerable<>), MultiRowTypeParsingInfo.ForList);
    }
}
