using System.Reflection;
using System.Reflection.Emit;
using Rinku.Tracking.Runtime.Emit;

namespace Rinku.Tracking.Runtime;

/// <summary>Cached generated type plus direct construction factories.</summary>
public sealed class RuntimeTrackingRegistration<TOriginal, TEdit>
{
    private readonly Func<TOriginal, TEdit> _create;
    private readonly Func<TEdit> _createNew;
    private readonly Action<TEdit>? _initializeNew;

    private RuntimeTrackingRegistration(Type generatedType, Func<TOriginal, TEdit> create, Func<TEdit> createNew, Action<TEdit>? initializeNew)
    {
        GeneratedType = generatedType;
        _create = create;
        _createNew = createNew;
        _initializeNew = initializeNew;
    }

    /// <summary>Gets the generated CLR type.</summary>
    public Type GeneratedType { get; }

    /// <summary>Creates an edit for an accepted original.</summary>
    public TEdit Create(TOriginal original) => _create(original);

    /// <summary>Creates a new edit without an accepted original.</summary>
    public TEdit CreateNew() => CreateNew(null);

    /// <summary>Creates a new edit and applies caller-supplied initial values.</summary>
    public TEdit CreateNew(Action<TEdit>? initialize) {
        TEdit item = _createNew();
        _initializeNew?.Invoke(item);
        initialize?.Invoke(item);
        return item;
    }

    internal static RuntimeTrackingRegistration<TOriginal, TEdit> Build(RuntimeTrackingTypeDefinition<TOriginal> definition)
    {
        RuntimeTrackingEmissionResult<TOriginal> emitted = RuntimeTrackingTypeEmitter<TOriginal, TEdit>.Build(definition);
        return new(emitted.Type, CreateExisting(emitted.ExistingCtor), CreateNewFactory(emitted.NewCtor), definition.NewItemInitializer is null ? null : definition.NewItemInitializer as Action<TEdit> ?? throw new InvalidOperationException($"The new-item initializer must accept {typeof(TEdit)}."));
    }

    private static Func<TOriginal, TEdit> CreateExisting(ConstructorInfo constructor)
    {
        var method = new DynamicMethod($"Create_{constructor.DeclaringType?.Name}", typeof(TEdit), [typeof(TOriginal)], typeof(RuntimeTrackingRegistration<TOriginal, TEdit>).Module, true);
        ILGenerator il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, constructor);
        il.Emit(OpCodes.Ret);
        return method.CreateDelegate<Func<TOriginal, TEdit>>();
    }

    private static Func<TEdit> CreateNewFactory(ConstructorInfo constructor)
    {
        var method = new DynamicMethod($"CreateNew_{constructor.DeclaringType?.Name}", typeof(TEdit), Type.EmptyTypes, typeof(RuntimeTrackingRegistration<TOriginal, TEdit>).Module, true);
        ILGenerator il = method.GetILGenerator();
        il.Emit(OpCodes.Newobj, constructor);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Callvirt, typeof(IEditable).GetMethod(nameof(IEditable.EnsureEditing))!);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);
        return method.CreateDelegate<Func<TEdit>>();
    }
}
