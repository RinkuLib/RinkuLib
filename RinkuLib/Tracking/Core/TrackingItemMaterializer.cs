using System;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Rinku.Tracking.Runtime;

namespace Rinku.Tracking;

// Cached TOriginal -> TEdit plan. TrackingList<T> is not part of materialization and never knows the generated CLR type.
internal static class TrackingItemMaterializer<TOriginal, TEdit> {
    private static readonly Lazy<MaterializationPlan> Cached = new(Build, true);
    private static MaterializationPlan Plan => Cached.Value;

    internal static Func<TOriginal, TEdit> Creator => Plan.Create;
    internal static TEdit Create(TOriginal original) => EnsureCreated(Plan.Create(original));

    internal static void ConfigureList(TrackingList<TEdit> list) {
        MaterializationPlan plan = Plan;
        if (plan.InteractionType is null) return;
        list.ConfigureBinding(plan.CreateNew, plan.Properties, typeof(TEdit).Name);
    }

    private static MaterializationPlan Build() {
        Type editType = typeof(TEdit);
        bool fromOriginal = typeof(IFromOriginal<TOriginal, TEdit>).IsAssignableFrom(editType);

        // Concrete user types are authoritative even when they also implement runtime contracts.
        if (!editType.IsInterface && !editType.IsAbstract && fromOriginal) return BuildFromOriginal(editType);

        if (editType.IsInterface && typeof(IRuntimeTrackingItem<TOriginal>).IsAssignableFrom(editType))
            return BuildRuntime(editType);

        if (fromOriginal)
            throw new NotSupportedException($"{editType} exposes {typeof(IFromOriginal<TOriginal, TEdit>)} but is not a concrete interaction type. Use a concrete TEdit or the selector overload.");

        throw new NotSupportedException(
            $"{editType} cannot be materialized from {typeof(TOriginal)}. " +
            $"Implement {typeof(IFromOriginal<TOriginal, TEdit>)} on a concrete type, request a runtime contract, or use the selector overload.");
    }

    private static MaterializationPlan BuildFromOriginal(Type editType) {
        MethodInfo method = typeof(TrackingItemMaterializer<TOriginal, TEdit>)
            .GetMethod(nameof(CreateFromOriginal), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(editType);
        var create = (Func<TOriginal, TEdit>)method.CreateDelegate(typeof(Func<TOriginal, TEdit>));
        return new(original => EnsureCreated(create(original)), null, null, null);
    }

    private static TCreated CreateFromOriginal<TCreated>(TOriginal original)
        where TCreated : IFromOriginal<TOriginal, TCreated>
        => TCreated.Create(original);

    private static MaterializationPlan BuildRuntime(Type editType) {
        Type cacheType = typeof(RuntimeTrackingContractCache<,>).MakeGenericType(typeof(TOriginal), editType);
        object registration;
        try { registration = cacheType.GetProperty("Registration", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!; }
        catch (TargetInvocationException ex) when (ex.InnerException is not null) {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
        Type registrationType = registration.GetType();

        MethodInfo createMethod = registrationType.GetMethod("Create", [typeof(TOriginal)])!;
        var create = (Func<TOriginal, TEdit>)createMethod.CreateDelegate(typeof(Func<TOriginal, TEdit>), registration);

        Func<TEdit>? createNew = null;
        if ((bool)registrationType.GetProperty("CanCreateNew")!.GetValue(registration)!) {
            MethodInfo newMethod = registrationType.GetMethod("CreateNew", Type.EmptyTypes)!;
            createNew = (Func<TEdit>)newMethod.CreateDelegate(typeof(Func<TEdit>), registration);
        }

        Type interactionType = (Type)registrationType.GetProperty("InteractionType")!.GetValue(registration)!;
        MethodInfo propertiesMethod = registrationType.GetMethod("GetProperties", Type.EmptyTypes)!;
        var properties = (Func<PropertyDescriptorCollection>)propertiesMethod.CreateDelegate(typeof(Func<PropertyDescriptorCollection>), registration);
        return new(original => EnsureCreated(create(original)), createNew is null ? null : () => EnsureCreated(createNew()), interactionType, properties);
    }

    private static TValue EnsureCreated<TValue>(TValue value) {
        if (value is null) throw new InvalidOperationException("The tracking-item materializer returned null.");
        return value;
    }

    private readonly struct MaterializationPlan(Func<TOriginal, TEdit> create, Func<TEdit>? createNew, Type? interactionType,
        Func<PropertyDescriptorCollection>? properties) {
        internal readonly Func<TOriginal, TEdit> Create = create;
        internal readonly Func<TEdit>? CreateNew = createNew;
        internal readonly Type? InteractionType = interactionType;
        internal readonly Func<PropertyDescriptorCollection>? Properties = properties;
    }
}
