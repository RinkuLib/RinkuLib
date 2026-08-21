using System.Reflection;
using System.Reflection.Emit;

namespace Rinku.Tracking.Runtime;

/// <summary>Cached generated type plus direct construction/context factories.</summary>
public sealed class RuntimeTrackingRegistration<TOriginal, TEdit>
{
    private readonly Func<TOriginal, TEdit> _create;
    private readonly Func<TOriginal, bool, TEdit> _createWithState;
    private readonly Func<TOriginal>? _newOriginal;

    private RuntimeTrackingRegistration(
        Type generatedType,
        Func<TOriginal, TEdit> create,
        Func<TOriginal, bool, TEdit> createWithState,
        Func<TOriginal>? newOriginal)
    {
        GeneratedType = generatedType;
        _create = create;
        _createWithState = createWithState;
        _newOriginal = newOriginal;
    }

    /// <summary>Gets the generated CLR type.</summary>
    public Type GeneratedType { get; }
    /// <summary>Gets whether a new item can be created.</summary>
    public bool CanCreateNew => _newOriginal is not null;

    /// <summary>Creates an edit for an accepted original.</summary>
    public TEdit Create(TOriginal original) => _create(original);

    /// <summary>Creates a new edit with a provisional original.</summary>
    public TEdit CreateNew()
    {
        Func<TOriginal> factory = _newOriginal
            ?? throw new NotSupportedException($"{typeof(TOriginal)} has no configured new-original factory or public parameterless constructor.");
        return _createWithState(factory(), true);
    }

    /// <summary>Creates a generated item context without a source list.</summary>
    public ITrackingListContext<TEdit> CreateContext() => new RuntimeGeneratedTrackingListContext<TOriginal, TEdit>(this);

    /// <summary>Creates a context suited to the supplied source.</summary>
    public ITrackingListContext<TEdit> CreateContext(IEnumerable<TOriginal> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source is IList<TOriginal> list
            ? new RuntimeIndexedTrackingListContext<TOriginal, TEdit>(list, this)
            : CreateContext();
    }

    internal static RuntimeTrackingRegistration<TOriginal, TEdit> Build(RuntimeTrackingTypeDefinition<TOriginal> definition)
    {
        RuntimeTrackingEmissionResult<TOriginal> emitted = RuntimeTrackingTypeEmitter<TOriginal, TEdit>.Build(definition);
        return new(
            emitted.Type,
            CreateExisting(emitted.ExistingCtor),
            CreateWithState(emitted.StateCtor),
            definition.NewOriginalFactory);
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

    private static Func<TOriginal, bool, TEdit> CreateWithState(ConstructorInfo constructor)
    {
        var method = new DynamicMethod($"CreateState_{constructor.DeclaringType?.Name}", typeof(TEdit), [typeof(TOriginal), typeof(bool)], typeof(RuntimeTrackingRegistration<TOriginal, TEdit>).Module, true);
        ILGenerator il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Newobj, constructor);
        il.Emit(OpCodes.Ret);
        return method.CreateDelegate<Func<TOriginal, bool, TEdit>>();
    }
}
