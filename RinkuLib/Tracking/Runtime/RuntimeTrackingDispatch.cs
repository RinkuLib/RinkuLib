using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using Rinku.Mapping;

namespace Rinku.Tracking.Runtime;

internal sealed class RuntimeTrackingRegistration<TOriginal, TEdit> where TEdit : class, IRuntimeTrackingItem<TOriginal> {
    private readonly Func<TOriginal, TEdit> _fromOriginal;
    private readonly Func<TEdit>? _new;
    private readonly Lazy<PropertyDescriptorCollection> _properties;

    private RuntimeTrackingRegistration(Type interactionType, ConstructorInfo existingCtor, ConstructorInfo? newCtor) {
        InteractionType = interactionType;
        _fromOriginal = BuildExisting(existingCtor);
        if (newCtor is not null) _new = BuildNew(newCtor);
        _properties = new(() => typeof(TEdit) == typeof(IRuntimeDynamicTrackingItem<TOriginal>)
            ? TypeDescriptor.GetProperties(InteractionType)
            : RuntimeContractPropertyDescriptors<TOriginal, TEdit>.Create(InteractionType), true);
    }

    public Type InteractionType { get; }
    public bool CanCreateNew => _new is not null;
    public TEdit Create(TOriginal original) => _fromOriginal(original);
    public TEdit CreateNew() => _new?.Invoke() ?? throw new NotSupportedException($"{InteractionType} cannot create a new tracking item.");
    public PropertyDescriptorCollection GetProperties() => _properties.Value;

    public static RuntimeTrackingRegistration<TOriginal, TEdit> Build(RuntimeTrackingOptions<TOriginal> options) {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.IsResolvedFor(typeof(TEdit)))
            throw new InvalidOperationException($"Runtime options must be resolved for {typeof(TEdit)} before emission.");
        options.Freeze();
        if (!typeof(TOriginal).IsVisible)
            throw new NotSupportedException($"Runtime-generated tracking requires a publicly visible original type; {typeof(TOriginal)} is not visible outside its assembly.");
        if (!typeof(TEdit).IsInterface || !typeof(TEdit).IsVisible)
            throw new NotSupportedException($"The exposed runtime contract {typeof(TEdit)} must be a publicly visible interface.");

        IRuntimeTrackingMember[] baseMembers = options.BuildMembers();
        var definition = new RuntimeTrackingTypeDefinition<TOriginal>(typeof(TEdit), baseMembers, options.Capabilities,
            options.ResolveDynamicAccess(typeof(TEdit)), options.ResolveNotifications(typeof(TEdit)));
        foreach (IRuntimeTrackingTypeContributor<TOriginal> contributor in options.Contributors) contributor.Configure(definition);

        bool requiredDynamicAccess = typeof(IRuntimeMemberAccess).IsAssignableFrom(typeof(TEdit));
        bool requiredNotifications = typeof(INotifyPropertyChanged).IsAssignableFrom(typeof(TEdit));
        if (requiredDynamicAccess && !definition.DynamicAccess)
            throw new InvalidOperationException($"Runtime contract {typeof(TEdit)} requires {nameof(IRuntimeMemberAccess)}.");
        if (requiredNotifications && !definition.Notifications)
            throw new InvalidOperationException($"Runtime contract {typeof(TEdit)} requires {nameof(INotifyPropertyChanged)}.");

        IRuntimeTrackingMember[] members = definition.Members.ToArray();
        bool dynamicAccess = definition.DynamicAccess;
        IRuntimeTrackingMember[] runtimeMembers = dynamicAccess
            ? members.Where(static x => x.IncludeInRuntimeAccess).ToArray()
            : [];

        Mapper? mapper = null;
        if (dynamicAccess) {
            mapper = Mapper.GetMapper(runtimeMembers.Select(static x => x.Name).ToArray());
            if (mapper.Count != runtimeMembers.Length)
                throw new InvalidOperationException($"Runtime tracking member names for {typeof(TOriginal)} are not unique for the configured Mapper.");
        }

        var editStorage = new RuntimeDynaEditStorage<TOriginal>(members);
        (Type type, ConstructorInfo existingCtor, ConstructorInfo? newCtor) = RuntimeTrackingTypeEmitter<TOriginal, TEdit>.Build(
            members, runtimeMembers, mapper, editStorage, options.ResolveNewOriginalCall(), definition.Capabilities,
            definition.DynamicAccess, definition.Notifications);
        return new(type, existingCtor, newCtor);
    }

    private static Func<TOriginal, TEdit> BuildExisting(ConstructorInfo ctor) {
        var dm = new DynamicMethod($"TrackingFromOriginal_{typeof(TEdit).Name}", typeof(TEdit), [typeof(TOriginal)],
            typeof(RuntimeTrackingRegistration<TOriginal, TEdit>).Module, true);
        ILGenerator il = dm.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, ctor);
        il.Emit(OpCodes.Castclass, typeof(TEdit));
        il.Emit(OpCodes.Ret);
        return dm.CreateDelegate<Func<TOriginal, TEdit>>();
    }

    private static Func<TEdit> BuildNew(ConstructorInfo ctor) {
        var dm = new DynamicMethod($"TrackingNew_{typeof(TEdit).Name}", typeof(TEdit), Type.EmptyTypes,
            typeof(RuntimeTrackingRegistration<TOriginal, TEdit>).Module, true);
        ILGenerator il = dm.GetILGenerator();
        il.Emit(OpCodes.Newobj, ctor);
        il.Emit(OpCodes.Castclass, typeof(TEdit));
        il.Emit(OpCodes.Ret);
        return dm.CreateDelegate<Func<TEdit>>();
    }
}

internal static class RuntimeTrackingDefaultShapeCache<TOriginal> {
    private static readonly Lazy<RuntimeTrackingRegistration<TOriginal, IRuntimeDynamicTrackingItem<TOriginal>>> Default =
        new(() => RuntimeTrackingRegistration<TOriginal, IRuntimeDynamicTrackingItem<TOriginal>>.Build(
            RuntimeTrackingContract<TOriginal, IRuntimeDynamicTrackingItem<TOriginal>>.BuildOptions(includeDefaultMembers: true)), true);
    internal static RuntimeTrackingRegistration<TOriginal, IRuntimeDynamicTrackingItem<TOriginal>> Registration => Default.Value;
}

internal static class RuntimeTrackingContractCache<TOriginal, TEdit> where TEdit : class, IRuntimeTrackingItem<TOriginal> {
    private static readonly Lazy<RuntimeTrackingRegistration<TOriginal, TEdit>> Default =
        new(() => RuntimeTrackingRegistration<TOriginal, TEdit>.Build(RuntimeTrackingContract<TOriginal, TEdit>.BuildOptions()), true);
    internal static RuntimeTrackingRegistration<TOriginal, TEdit> Registration => Default.Value;
}

internal static class RuntimeTrackingModule {
    internal static readonly AssemblyBuilder Assembly = AssemblyBuilder.DefineDynamicAssembly(
        new AssemblyName("Rinku.Tracking.Runtime.Generated"), AssemblyBuilderAccess.Run);
    internal static readonly ModuleBuilder Module = Assembly.DefineDynamicModule("Rinku.Tracking.Runtime.Generated");
    private static int _counter;
    internal static int NextId() => Interlocked.Increment(ref _counter);
}
