using System.Runtime.CompilerServices;
using Rinku.Mapping.Parsers.Defaults;

namespace Rinku.Mapping.Defaults;

/// <summary>Connects the shipped mapping implementations to the implementation-neutral registries.</summary>
public static class DefaultMappingBootstrap {
    private static int Initialized;

    /// <summary>Installs the shipped mapping defaults. Calling this more than once has no effect.</summary>
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize() {
        if (Interlocked.Exchange(ref Initialized, 1) != 0)
            return;

        TypeParsingInfo.TryInstallDefaultFactory(new DefaultTypeParsingInfoFactory());
        var listFallback = new ReusingBaseTypeParserMaker([typeof(List<>)],
            (definition, itemType, ref _) => typeof(ListTypeParser<>).MakeGenericType(itemType),
            (definition, itemType, ref _) => typeof(FastListTypeParser<>).MakeGenericType(itemType));
        TypeParser.TryInstallDefaults(new DefaultTypeParserMaker(listFallback),
            new EnumerableTypeParserMaker(),
            new ReusingBaseTypeParserMaker([typeof(Optional<>), typeof(OptionalStruct<>), typeof(OptionalNullable<>)],
                (definition, itemType, ref _) => typeof(OptionalTypeParser<,>).MakeGenericType(definition.MakeGenericType(itemType), itemType),
                (definition, itemType, ref _) => typeof(FastOptionalTypeParser<,>).MakeGenericType(definition.MakeGenericType(itemType), itemType)),
            new ReusingBaseTypeParserMaker([typeof(Single<>)],
                (definition, itemType, ref _) => typeof(SingleTypeParser<,>).MakeGenericType(definition.MakeGenericType(itemType), itemType),
                (definition, itemType, ref _) => typeof(FastSingleTypeParser<,>).MakeGenericType(definition.MakeGenericType(itemType), itemType)));

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
