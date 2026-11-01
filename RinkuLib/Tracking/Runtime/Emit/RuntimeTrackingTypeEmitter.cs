using System.Reflection;
using System.Reflection.Emit;
using Rinku.Mapping;
using Rinku.Mapping.Conversion;

namespace Rinku.Tracking.Runtime.Emit;

internal static class RuntimeTrackingTypeEmitter<TOriginal, TEdit>
{
    private readonly record struct RuntimeAccessEntry(string Name, Type ValueType, MethodBuilder Getter, MethodBuilder? Setter);

    private static readonly MethodInfo CasterTryCast = typeof(Caster).GetMethods(BindingFlags.Static | BindingFlags.Public)
        .Single(static method => method.Name == nameof(Caster.TryCast) && method.IsGenericMethodDefinition && method.GetGenericArguments().Length == 2);

    internal static RuntimeTrackingEmissionResult<TOriginal> Build(RuntimeTrackingTypeDefinition<TOriginal> definition)
    {
        definition.Validate();
        int id = RuntimeTrackingModule.NextId();
        string originalName = Sanitize(typeof(TOriginal).Name);
        TypeBuilder snapshot = RuntimeTrackingModule.Module.DefineType(
            $"RinkuTrackingSnapshot_{originalName}_{id}",
            TypeAttributes.NotPublic | TypeAttributes.Sealed | TypeAttributes.Class);
        TypeBuilder type = RuntimeTrackingModule.Module.DefineType(
            $"RinkuTracking_{originalName}_{id}",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class);

        FieldBuilder original = definition.OriginalStorage.DefineField(type);
        FieldBuilder edit = type.DefineField("_edit", snapshot, FieldAttributes.Private);
        FieldBuilder isNew = type.DefineField("_isNew", typeof(bool), FieldAttributes.Private);
        FieldBuilder mapper = type.DefineField("s_mapper", typeof(Mapper), FieldAttributes.Private | FieldAttributes.Static);

        var context = new RuntimeTrackingEmitContext<TOriginal>(definition, type, snapshot, original, edit, isNew, mapper);
        AddInterfaces(type, definition);

        for (int i = 0; i < definition.Members.Count; i++)
        {
            RuntimeTrackingMemberDefinition<TOriginal> member = definition.Members[i];
            var memberContext = new RuntimeTrackingMemberEmitContext<TOriginal>(context, member);
            context.Members.Add(member, memberContext);
            RuntimeTrackingMemberEmitter<TOriginal> emitter = member.Emitter
                ?? throw new InvalidOperationException($"Runtime member '{member.Name}' has no emitter.");
            emitter.DefineStorage(memberContext);
        }

        ConstructorBuilder snapshotCtor = snapshot.DefineDefaultConstructor(MethodAttributes.Public);

        for (int i = 0; i < definition.TypeEmitters.Count; i++)
            definition.TypeEmitters[i].Emit(context);
        context.CompleteEmission();

        EmitExistingConstructor(context);
        EmitNewConstructor(context);
        MethodBuilder ensureEdit = DefineEnsureEdit(context);
        context.EnsureEditMethod = ensureEdit;
        EmitProperties(context);
        EmitEnsureEditBody(context, ensureEdit, snapshotCtor);
        RuntimeAccessEntry[] runtimeAccess = BuildRuntimeAccess(context);
        EmitRuntimeAccess(context, runtimeAccess);
        EmitConfirmationEvent(context);
        EmitLifecycle(context);
        EmitMethods(context);
        BindInterfaceProperties(context);

        Type snapshotType = snapshot.CreateType()
            ?? throw new InvalidOperationException("Unable to create runtime tracking snapshot type.");
        Type created;
        try
        {
            created = type.CreateType()
                ?? throw new InvalidOperationException($"Unable to create runtime tracking type for {typeof(TOriginal)}.");
        }
        catch (TypeLoadException exception)
        {
            throw new InvalidOperationException($"Unable to generate tracking contract {definition.RequestedContract}.", exception);
        }

        string[] names = new string[runtimeAccess.Length];
        for (int i = 0; i < runtimeAccess.Length; i++) names[i] = runtimeAccess[i].Name;
        Mapper runtimeMapper = Mapper.GetMapper(names);
        FieldInfo mapperField = created.GetField(mapper.Name, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(created.FullName, mapper.Name);
        mapperField.SetValue(null, runtimeMapper);

        context.Initialize(created);
        var generated = new RuntimeTrackingGeneratedType<TOriginal>(created, definition);
        for (int i = 0; i < definition.TypeEmitters.Count; i++) definition.TypeEmitters[i].Complete(generated);

        ConstructorInfo existing = created.GetConstructor([typeof(TOriginal)])
            ?? throw new MissingMethodException(created.FullName, ".ctor(TOriginal)");
        ConstructorInfo newItem = created.GetConstructor(Type.EmptyTypes)
            ?? throw new MissingMethodException(created.FullName, ".ctor()");
        return new(created, snapshotType, existing, newItem, runtimeMapper);
    }

    private static void AddInterfaces(TypeBuilder type, RuntimeTrackingTypeDefinition<TOriginal> definition)
    {
        var interfaces = new HashSet<Type>
        {
            typeof(ITrackingItem<TOriginal>),
            typeof(ICanInsert)
        };
        if (definition.RequestedContract.IsInterface) interfaces.Add(definition.RequestedContract);
        for (int i = 0; i < definition.Interfaces.Count; i++) interfaces.Add(definition.Interfaces[i]);
        foreach (Type current in interfaces) type.AddInterfaceImplementation(current);
    }

    private static ConstructorBuilder EmitExistingConstructor(RuntimeTrackingEmitContext<TOriginal> context)
    {
        ConstructorBuilder ctor = context.TypeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [typeof(TOriginal)]);
        ILGenerator il = ctor.GetILGenerator();
        EmitObjectConstructor(il);
        context.Definition.OriginalStorage.EmitStoreFromArgument(il, context.OriginalField, 1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, context.HasOriginalField);
        il.Emit(OpCodes.Ret);
        return ctor;
    }

    private static ConstructorBuilder EmitNewConstructor(RuntimeTrackingEmitContext<TOriginal> context) {
        ConstructorBuilder ctor = context.TypeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
        ILGenerator il = ctor.GetILGenerator();
        EmitObjectConstructor(il);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, context.IsNewField);
        il.Emit(OpCodes.Ret);
        return ctor;
    }

    private static void EmitObjectConstructor(ILGenerator il)
    {
        ConstructorInfo constructor = typeof(object).GetConstructor(Type.EmptyTypes)
            ?? throw new MissingMethodException(typeof(object).FullName, ".ctor()");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, constructor);
    }

    private static MethodBuilder DefineEnsureEdit(RuntimeTrackingEmitContext<TOriginal> context)
        => context.TypeBuilder.DefineMethod("EnsureEdit", MethodAttributes.Private | MethodAttributes.HideBySig, context.SnapshotBuilder, Type.EmptyTypes);

    private static void EmitEnsureEditBody(RuntimeTrackingEmitContext<TOriginal> context, MethodBuilder method, ConstructorBuilder snapshotCtor)
    {
        ILGenerator il = method.GetILGenerator();
        Label create = il.DefineLabel();
        context.EmitLoadEdit(il);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, create);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(create);
        il.Emit(OpCodes.Pop);

        LocalBuilder snapshot = il.DeclareLocal(context.SnapshotBuilder);
        il.Emit(OpCodes.Newobj, snapshotCtor);
        il.Emit(OpCodes.Stloc, snapshot);

        for (int i = 0; i < context.Definition.Members.Count; i++)
        {
            RuntimeTrackingMemberDefinition<TOriginal> member = context.Definition.Members[i];
            RuntimeTrackingMemberEmitter<TOriginal> emitter = member.Emitter
                ?? throw new InvalidOperationException($"Runtime member '{member.Name}' has no emitter.");
            if (!emitter.UsesSnapshot) continue;
            il.Emit(OpCodes.Ldloc, snapshot);
            emitter.EmitInitializeSnapshot(context.MemberContext(member), il);
        }

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, snapshot);
        il.Emit(OpCodes.Stfld, context.EditField);
        foreach (var member in context.Definition.Members) {
            if (member.CreateOnEdit is not { } factory) continue;
            Type factoryType = factory.GetType();
            string name = "s_createMember_" + context.Properties[member].Property.Name;
            FieldBuilder factoryField = context.TypeBuilder.DefineField(name, factoryType, FieldAttributes.Private | FieldAttributes.Static);
            context.AddInitializer(generated => generated.GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, factory));
            Label existing = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, context.Properties[member].Getter);
            il.Emit(OpCodes.Brtrue, existing);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldsfld, factoryField);
            il.Emit(OpCodes.Callvirt, factoryType.GetMethod("Invoke")!);
            il.Emit(OpCodes.Call, context.Properties[member].Setter!);
            il.MarkLabel(existing);
        }
        context.EmitChanged(il, null);
        il.Emit(OpCodes.Ldloc, snapshot);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitProperties(RuntimeTrackingEmitContext<TOriginal> context)
    {
        for (int i = 0; i < context.Definition.Members.Count; i++)
        {
            RuntimeTrackingMemberDefinition<TOriginal> member = context.Definition.Members[i];
            RuntimeTrackingMemberEmitter<TOriginal> emitter = member.Emitter
                ?? throw new InvalidOperationException($"Runtime member '{member.Name}' has no emitter.");
            RuntimeTrackingMemberEmitContext<TOriginal> memberContext = context.MemberContext(member);
            PropertyBuilder property = context.TypeBuilder.DefineProperty(member.Name, PropertyAttributes.None, member.ValueType, Type.EmptyTypes);
            MethodAttributes visibility = member.ExposeProperty ? MethodAttributes.Public : MethodAttributes.Private;
            MethodAttributes attributes = visibility | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.SpecialName;

            MethodBuilder getter = context.TypeBuilder.DefineMethod($"get_{member.Name}", attributes, member.ValueType, Type.EmptyTypes);
            ILGenerator get = getter.GetILGenerator();
            emitter.EmitGet(memberContext, get);
            get.Emit(OpCodes.Ret);
            property.SetGetMethod(getter);

            MethodBuilder? setter = null;
            if (emitter.CanWrite)
            {
                setter = context.TypeBuilder.DefineMethod($"set_{member.Name}", attributes, typeof(void), [member.ValueType]);
                ILGenerator set = setter.GetILGenerator();
                emitter.EmitSet(memberContext, set);
                context.EmitChanged(set, member.Name);
                set.Emit(OpCodes.Ret);
                property.SetSetMethod(setter);
            }

            CopyMetadata(property, member.MetadataSources);
            context.Properties.Add(member, new(property, getter, setter));

        }
    }

    private static RuntimeAccessEntry[] BuildRuntimeAccess(RuntimeTrackingEmitContext<TOriginal> context)
    {
        var result = new List<RuntimeAccessEntry>();
        for (int i = 0; i < context.Definition.Members.Count; i++)
        {
            RuntimeTrackingMemberDefinition<TOriginal> member = context.Definition.Members[i];
            if (!member.IncludeInRuntimeAccess) continue;
            RuntimeEmittedProperty property = context.Properties[member];
            result.Add(new(member.Name, member.ValueType, property.Getter, property.Setter));

            RuntimeTrackingMemberEmitter<TOriginal> emitter = member.Emitter
                ?? throw new InvalidOperationException($"Runtime member '{member.Name}' has no emitter.");
            IReadOnlyList<MemberInfo> paths = emitter.GetNestedRuntimePathMembers();
            for (int p = 0; p < paths.Count; p++)
                result.Add(EmitNestedPath(context, member, paths[p], p));
        }
        return [.. result];
    }

    private static RuntimeAccessEntry EmitNestedPath(RuntimeTrackingEmitContext<TOriginal> context, RuntimeTrackingMemberDefinition<TOriginal> root, MemberInfo child, int childIndex)
    {
        RuntimeEmittedProperty rootProperty = context.Properties[root];
        Type childType = MemberType(child);
        string path = $"{root.Name}.{child.Name}";
        string methodName = $"__path_{Sanitize(root.Name)}_{childIndex}_{Sanitize(child.Name)}";

        MethodBuilder getter = context.TypeBuilder.DefineMethod($"{methodName}_get", MethodAttributes.Private | MethodAttributes.HideBySig, childType, Type.EmptyTypes);
        ILGenerator get = getter.GetILGenerator();
        EmitNestedPathGet(get, rootProperty.Getter, root.ValueType, child);
        get.Emit(OpCodes.Ret);

        MethodBuilder? setter = null;
        if (CanWrite(child))
        {
            setter = context.TypeBuilder.DefineMethod($"{methodName}_set", MethodAttributes.Private | MethodAttributes.HideBySig, typeof(void), [childType]);
            ILGenerator set = setter.GetILGenerator();
            EmitNestedPathSet(context, root, child, set, path);
            set.Emit(OpCodes.Ret);
        }

        return new(path, childType, getter, setter);
    }

    private static void EmitNestedPathGet(ILGenerator il, MethodBuilder rootGetter, Type rootType, MemberInfo child)
    {
        if (!rootType.IsValueType)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, rootGetter);
            EmitReadNestedMember(il, child, rootType);
            return;
        }

        LocalBuilder root = il.DeclareLocal(rootType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, rootGetter);
        il.Emit(OpCodes.Stloc, root);
        il.Emit(OpCodes.Ldloca, root);
        EmitReadNestedMember(il, child, rootType);
    }

    private static void EmitNestedPathSet(RuntimeTrackingEmitContext<TOriginal> context, RuntimeTrackingMemberDefinition<TOriginal> root, MemberInfo child, ILGenerator il, string path)
    {
        RuntimeTrackingMemberEmitContext<TOriginal> rootContext = context.MemberContext(root);
        FieldBuilder snapshotField = rootContext.SnapshotField
            ?? throw new InvalidOperationException($"Nested member '{root.Name}' has no snapshot field.");

        context.EmitEnsureEdit(il);
        if (root.ValueType.IsValueType)
            il.Emit(OpCodes.Ldflda, snapshotField);
        else
            il.Emit(OpCodes.Ldfld, snapshotField);
        il.Emit(OpCodes.Ldarg_1);
        EmitWriteNestedMember(il, child, root.ValueType);
        context.EmitChanged(il, path);
    }

    private static void EmitRuntimeAccess(RuntimeTrackingEmitContext<TOriginal> context, RuntimeAccessEntry[] access)
    {
        EmitRuntimeMapper(context);
        EmitRuntimeTryGet(context, access);
        EmitRuntimeSet(context, access);
    }

    private static void EmitRuntimeMapper(RuntimeTrackingEmitContext<TOriginal> context)
    {
        MethodInfo contract = typeof(IRuntimeMemberAccess).GetProperty(nameof(IRuntimeMemberAccess.Mapper))?.GetMethod
            ?? throw new MissingMethodException(typeof(IRuntimeMemberAccess).FullName, $"get_{nameof(IRuntimeMemberAccess.Mapper)}");
        MethodBuilder method = DefineExplicit(context.TypeBuilder, contract);
        ILGenerator il = method.GetILGenerator();
        il.Emit(OpCodes.Ldsfld, context.MapperField);
        il.Emit(OpCodes.Ret);
        context.TypeBuilder.DefineMethodOverride(method, contract);
    }

    private static void EmitRuntimeTryGet(RuntimeTrackingEmitContext<TOriginal> context, RuntimeAccessEntry[] access)
    {
        MethodInfo contract = typeof(IRuntimeMemberAccess).GetMethods().Single(static method => method.Name == nameof(IRuntimeMemberAccess.TryGet) && method.IsGenericMethodDefinition);
        MethodBuilder method = context.TypeBuilder.DefineMethod("__runtime_tryget", MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot);
        GenericTypeParameterBuilder expected = method.DefineGenericParameters("T")[0];
        method.SetReturnType(typeof(bool));
        method.SetParameters(typeof(int), expected.MakeByRefType());

        ILGenerator il = method.GetILGenerator();
        Label invalid = il.DefineLabel();
        Label[] labels = new Label[access.Length];
        for (int i = 0; i < labels.Length; i++) labels[i] = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        if (labels.Length != 0) il.Emit(OpCodes.Switch, labels);
        il.Emit(OpCodes.Br, invalid);

        for (int i = 0; i < access.Length; i++)
        {
            il.MarkLabel(labels[i]);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, access[i].Getter);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, CasterTryCast.MakeGenericMethod(access[i].ValueType, expected));
            il.Emit(OpCodes.Ret);
        }

        il.MarkLabel(invalid);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Initobj, expected);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        context.TypeBuilder.DefineMethodOverride(method, contract);
    }

    private static void EmitRuntimeSet(RuntimeTrackingEmitContext<TOriginal> context, RuntimeAccessEntry[] access)
    {
        MethodInfo contract = typeof(IRuntimeMemberAccess).GetMethods().Single(static method =>
        {
            ParameterInfo[] parameters = method.GetParameters();
            return method.Name == nameof(IRuntimeMemberAccess.Set)
                && method.IsGenericMethodDefinition
                && parameters.Length == 2
                && parameters[0].ParameterType == typeof(int);
        });
        MethodBuilder method = context.TypeBuilder.DefineMethod("__runtime_set", MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot);
        GenericTypeParameterBuilder input = method.DefineGenericParameters("T")[0];
        method.SetReturnType(typeof(bool));
        method.SetParameters(typeof(int), input);

        ILGenerator il = method.GetILGenerator();
        Label fail = il.DefineLabel();
        Label[] labels = new Label[access.Length];
        for (int i = 0; i < labels.Length; i++) labels[i] = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        if (labels.Length != 0) il.Emit(OpCodes.Switch, labels);
        il.Emit(OpCodes.Br, fail);

        for (int i = 0; i < access.Length; i++)
        {
            il.MarkLabel(labels[i]);
            MethodBuilder? setter = access[i].Setter;
            if (setter is null)
            {
                il.Emit(OpCodes.Br, fail);
                continue;
            }

            LocalBuilder converted = il.DeclareLocal(access[i].ValueType);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldloca, converted);
            il.Emit(OpCodes.Call, CasterTryCast.MakeGenericMethod(input, access[i].ValueType));
            Label convertedOk = il.DefineLabel();
            il.Emit(OpCodes.Brtrue, convertedOk);
            il.Emit(OpCodes.Br, fail);
            il.MarkLabel(convertedOk);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, converted);
            il.Emit(OpCodes.Call, setter);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ret);
        }

        il.MarkLabel(fail);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        context.TypeBuilder.DefineMethodOverride(method, contract);
    }

    private static void EmitConfirmationEvent(RuntimeTrackingEmitContext<TOriginal> context)
    {
        FieldBuilder field = context.TypeBuilder.DefineField("_confirmed", typeof(TrackingConfirmationHandler), FieldAttributes.Private);
        context.ConfirmationField = field;
        EventInfo contract = typeof(ITrackingConfirmationSource).GetEvent(nameof(ITrackingConfirmationSource.Confirmed))!;
        EventBuilder generated = context.TypeBuilder.DefineEvent(contract.Name, EventAttributes.None, typeof(TrackingConfirmationHandler));
        foreach (bool adding in new[] { true, false })
        {
            MethodInfo accessor = (adding ? contract.AddMethod : contract.RemoveMethod)!;
            MethodBuilder method = DefineExplicit(context.TypeBuilder, accessor);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldflda, field);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, typeof(TrackingEvents).GetMethod(adding ? nameof(TrackingEvents.Add) : nameof(TrackingEvents.Remove))!.MakeGenericMethod(typeof(TrackingConfirmationHandler)));
            il.Emit(OpCodes.Ret);
            context.TypeBuilder.DefineMethodOverride(method, accessor);
            if (adding)
            {
                generated.SetAddOnMethod(method);
            }
            else
            {
                generated.SetRemoveOnMethod(method);
            }
        }
    }

    private static void EmitConfirmed(RuntimeTrackingEmitContext<TOriginal> context, ILGenerator il, TrackingConfirmation operation)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, context.ConfirmationField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, (int)operation);
        il.Emit(OpCodes.Call, typeof(TrackingEvents).GetMethod(nameof(TrackingEvents.Raise))!);
    }

    private static void EmitLifecycle(RuntimeTrackingEmitContext<TOriginal> context)
    {
        EmitIsEditing(context);
        EmitEnsureEditing(context);
        MethodBuilder confirmUpdate = EmitConfirmEdit(context);
        EmitCancelEdit(context);
        EmitTryGetOriginal(context);
        EmitSetOriginal(context);
        EmitIsNew(context);
        EmitInsertConfirmation(context, confirmUpdate);
        EmitChanges(context);
    }

    private static void EmitIsEditing(RuntimeTrackingEmitContext<TOriginal> context)
    {
        MethodInfo contract = typeof(IEditable).GetProperty(nameof(IEditable.IsEditing))?.GetMethod
            ?? throw new MissingMethodException(typeof(IEditable).FullName, $"get_{nameof(IEditable.IsEditing)}");
        MethodBuilder method = DefineExplicit(context.TypeBuilder, contract);
        ILGenerator il = method.GetILGenerator();
        context.EmitLoadEdit(il);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Cgt_Un);
        il.Emit(OpCodes.Ret);
        context.TypeBuilder.DefineMethodOverride(method, contract);
    }

    private static void EmitEnsureEditing(RuntimeTrackingEmitContext<TOriginal> context)
    {
        MethodInfo contract = typeof(IEditable).GetMethod(nameof(IEditable.EnsureEditing))
            ?? throw new MissingMethodException(typeof(IEditable).FullName, nameof(IEditable.EnsureEditing));
        MethodBuilder method = DefineExplicit(context.TypeBuilder, contract);
        ILGenerator il = method.GetILGenerator();
        context.EmitEnsureEdit(il);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        context.TypeBuilder.DefineMethodOverride(method, contract);
    }

    private static MethodBuilder EmitConfirmEdit(RuntimeTrackingEmitContext<TOriginal> context)
    {
        MethodInfo contract = typeof(IEditable).GetMethod(nameof(IEditable.ConfirmEdit))
            ?? throw new MissingMethodException(typeof(IEditable).FullName, nameof(IEditable.ConfirmEdit));
        MethodBuilder method = context.TypeBuilder.DefineMethod("ConfirmSnapshot", MethodAttributes.Private | MethodAttributes.HideBySig, typeof(bool), Type.EmptyTypes);
        ILGenerator il = method.GetILGenerator();
        if (context.Definition.InsertConfirmation is not null || context.Definition.UpdateConfirmation is not null) {
            Label update = il.DefineLabel(), prepared = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, context.IsNewField);
            il.Emit(OpCodes.Brfalse, update);
            EmitPreparation(context.Definition.InsertConfirmation, "s_confirmInsert");
            il.Emit(OpCodes.Br, prepared);
            il.MarkLabel(update);
            EmitPreparation(context.Definition.UpdateConfirmation, "s_confirmUpdate");
            il.MarkLabel(prepared);
        }
        void EmitPreparation(Func<ITrackingItem<TOriginal>, TOriginal>? confirmation, string name) {
            if (confirmation is null) return;
            var callbackType = typeof(Func<ITrackingItem<TOriginal>, TOriginal>);
            var callback = context.TypeBuilder.DefineField(name, callbackType, FieldAttributes.Private | FieldAttributes.Static);
            context.AddInitializer(type => type.GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, confirmation));
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldsfld, callback);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, callbackType.GetMethod("Invoke")!);
            il.Emit(OpCodes.Callvirt, typeof(IOriginalWriter<TOriginal>).GetMethod(nameof(IOriginalWriter<TOriginal>.SetOriginal))!);
        }
        Label available = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, context.HasOriginalField);
        il.Emit(OpCodes.Brtrue, available);
        il.Emit(OpCodes.Ldstr, ErrorCodes.ValueNotSet);
        il.Emit(OpCodes.Ldstr, $"Confirmation of {typeof(TOriginal)} requires an original or a configured construction function.");
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Newobj, typeof(RinkuBindingException).GetConstructor([typeof(string), typeof(string), typeof(Exception)])!);
        il.Emit(OpCodes.Throw);
        il.MarkLabel(available);
        Label done = il.DefineLabel();
        context.EmitLoadEdit(il);
        il.Emit(OpCodes.Brfalse, done);

        for (int i = 0; i < context.Definition.Members.Count; i++)
        {
            RuntimeTrackingMemberDefinition<TOriginal> member = context.Definition.Members[i];
            RuntimeTrackingMemberEmitter<TOriginal> emitter = member.Emitter
                ?? throw new InvalidOperationException($"Runtime member '{member.Name}' has no emitter.");
            if (emitter.UsesSnapshot) emitter.EmitConfirm(context.MemberContext(member), il);
        }

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stfld, context.EditField);
        context.EmitChanged(il, null);
        il.MarkLabel(done);
        EmitLifecyclePolicy(context, il, context.Definition.ConfirmedPolicy, "s_confirmedPolicy");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        MethodBuilder confirm = DefineExplicit(context.TypeBuilder, contract);
        ILGenerator confirmIl = confirm.GetILGenerator();
        confirmIl.Emit(OpCodes.Ldarg_0);
        confirmIl.Emit(OpCodes.Call, method);
        Label rejected = confirmIl.DefineLabel();
        confirmIl.Emit(OpCodes.Brfalse, rejected);
        EmitConfirmed(context, confirmIl, TrackingConfirmation.Update);
        confirmIl.Emit(OpCodes.Ldc_I4_1);
        confirmIl.Emit(OpCodes.Ret);
        confirmIl.MarkLabel(rejected);
        confirmIl.Emit(OpCodes.Ldc_I4_0);
        confirmIl.Emit(OpCodes.Ret);
        context.TypeBuilder.DefineMethodOverride(confirm, contract);
        return method;
    }

    private static void EmitCancelEdit(RuntimeTrackingEmitContext<TOriginal> context)
    {
        MethodInfo contract = typeof(IEditable).GetMethod(nameof(IEditable.CancelEdit))
            ?? throw new MissingMethodException(typeof(IEditable).FullName, nameof(IEditable.CancelEdit));
        MethodBuilder method = DefineExplicit(context.TypeBuilder, contract);
        ILGenerator il = method.GetILGenerator();
        Label done = il.DefineLabel();
        context.EmitLoadEdit(il);
        il.Emit(OpCodes.Brfalse, done);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stfld, context.EditField);
        context.EmitChanged(il, null);
        il.MarkLabel(done);
        EmitLifecyclePolicy(context, il, context.Definition.RevertedPolicy, "s_revertedPolicy");
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        context.TypeBuilder.DefineMethodOverride(method, contract);
    }

    private static void EmitLifecyclePolicy(RuntimeTrackingEmitContext<TOriginal> context, ILGenerator il, Action<ITrackingItem<TOriginal>>? policy, string name) {
        if (policy is null) return;
        Type callbackType = typeof(Action<ITrackingItem<TOriginal>>);
        FieldBuilder callback = context.TypeBuilder.DefineField(name, callbackType, FieldAttributes.Private | FieldAttributes.Static);
        context.AddInitializer(type => type.GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, policy));
        il.Emit(OpCodes.Ldsfld, callback);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, callbackType.GetMethod("Invoke")!);
    }

    private static void EmitTryGetOriginal(RuntimeTrackingEmitContext<TOriginal> context)
    {
        MethodInfo contract = typeof(IOriginal<TOriginal>).GetMethod(nameof(IOriginal<>.TryGetOriginal))
            ?? throw new MissingMethodException(typeof(IOriginal<TOriginal>).FullName, nameof(IOriginal<>.TryGetOriginal));
        MethodBuilder method = DefineExplicit(context.TypeBuilder, contract);
        ILGenerator il = method.GetILGenerator();
        Label available = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, context.HasOriginalField);
        il.Emit(OpCodes.Brtrue, available);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Initobj, typeof(TOriginal));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(available);
        context.Definition.OriginalStorage.EmitTryGetOriginal(il, context.OriginalField);
        context.TypeBuilder.DefineMethodOverride(method, contract);
    }

    private static void EmitSetOriginal(RuntimeTrackingEmitContext<TOriginal> context) {
        Type contractType = typeof(IOriginalWriter<TOriginal>);
        context.TypeBuilder.AddInterfaceImplementation(contractType);
        MethodInfo contract = contractType.GetMethod(nameof(IOriginalWriter<TOriginal>.SetOriginal))!;
        MethodBuilder method = DefineExplicit(context.TypeBuilder, contract);
        ILGenerator il = method.GetILGenerator();
        context.Definition.OriginalStorage.EmitStoreFromArgument(il, context.OriginalField, 1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, context.HasOriginalField);
        Label noEdit = il.DefineLabel();
        context.EmitLoadEdit(il);
        il.Emit(OpCodes.Brfalse, noEdit);
        foreach (var member in context.Definition.Members) {
            var storage = context.MemberContext(member);
            if (storage.DirectField is not null && member.Emitter is { UsesSnapshot: true } emitter)
                emitter.EmitConfirm(storage, il);
        }
        il.MarkLabel(noEdit);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stfld, context.EditField);
        context.EmitChanged(il, null);
        il.Emit(OpCodes.Ret);
        context.TypeBuilder.DefineMethodOverride(method, contract);
    }

    private static void EmitIsNew(RuntimeTrackingEmitContext<TOriginal> context)
    {
        MethodInfo contract = typeof(ITrackingListNewState).GetProperty(nameof(ITrackingListNewState.IsNew))?.GetMethod
            ?? throw new MissingMethodException(typeof(ITrackingListNewState).FullName, $"get_{nameof(ITrackingListNewState.IsNew)}");
        MethodBuilder method = DefineExplicit(context.TypeBuilder, contract);
        ILGenerator il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, context.IsNewField);
        il.Emit(OpCodes.Ret);
        context.TypeBuilder.DefineMethodOverride(method, contract);
    }

    private static void EmitInsertConfirmation(RuntimeTrackingEmitContext<TOriginal> context, MethodBuilder confirmUpdate)
    {
        MethodInfo insertContract = typeof(ICanInsert).GetMethod(nameof(ICanInsert.ConfirmInsert))
            ?? throw new MissingMethodException(typeof(ICanInsert).FullName, nameof(ICanInsert.ConfirmInsert));
        MethodBuilder insert = DefineExplicit(context.TypeBuilder, insertContract);
        ILGenerator insertIl = insert.GetILGenerator();
        Label fail = insertIl.DefineLabel();
        insertIl.Emit(OpCodes.Ldarg_0);
        insertIl.Emit(OpCodes.Call, confirmUpdate);
        insertIl.Emit(OpCodes.Brfalse, fail);
        Label alreadyInserted = insertIl.DefineLabel();
        insertIl.Emit(OpCodes.Ldarg_0);
        insertIl.Emit(OpCodes.Ldfld, context.IsNewField);
        insertIl.Emit(OpCodes.Brfalse, alreadyInserted);
        insertIl.Emit(OpCodes.Ldarg_0);
        insertIl.Emit(OpCodes.Ldc_I4_0);
        insertIl.Emit(OpCodes.Stfld, context.IsNewField);
        context.EmitChanged(insertIl, nameof(ITrackingListNewState.IsNew));
        insertIl.MarkLabel(alreadyInserted);
        EmitConfirmed(context, insertIl, TrackingConfirmation.Insert);
        insertIl.Emit(OpCodes.Ldc_I4_1);
        insertIl.Emit(OpCodes.Ret);
        insertIl.MarkLabel(fail);
        insertIl.Emit(OpCodes.Ldc_I4_0);
        insertIl.Emit(OpCodes.Ret);
        context.TypeBuilder.DefineMethodOverride(insert, insertContract);
    }

    private static void EmitChanges(RuntimeTrackingEmitContext<TOriginal> context)
    {
        RuntimeTrackingMemberDefinition<TOriginal>[] tracked = [.. context.Definition.Members.Where(static member => member.Emitter?.TracksChanges == true)];
        EmitTrackedMemberCount(context, tracked.Length);
        EmitTryGetChange(context, tracked);
    }

    private static void EmitTrackedMemberCount(RuntimeTrackingEmitContext<TOriginal> context, int count)
    {
        MethodInfo contract = typeof(ITrackingChanges).GetProperty(nameof(ITrackingChanges.TrackedMemberCount))?.GetMethod
            ?? throw new MissingMethodException(typeof(ITrackingChanges).FullName, $"get_{nameof(ITrackingChanges.TrackedMemberCount)}");
        MethodBuilder method = DefineExplicit(context.TypeBuilder, contract);
        ILGenerator il = method.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4, count);
        il.Emit(OpCodes.Ret);
        context.TypeBuilder.DefineMethodOverride(method, contract);
    }

    private static void EmitTryGetChange(RuntimeTrackingEmitContext<TOriginal> context, RuntimeTrackingMemberDefinition<TOriginal>[] tracked)
    {
        MethodInfo contract = typeof(ITrackingChanges).GetMethod(nameof(ITrackingChanges.TryGetChange))
            ?? throw new MissingMethodException(typeof(ITrackingChanges).FullName, nameof(ITrackingChanges.TryGetChange));
        MethodBuilder method = DefineExplicit(context.TypeBuilder, contract);
        ILGenerator il = method.GetILGenerator();
        Label fail = il.DefineLabel();
        context.EmitLoadEdit(il);
        il.Emit(OpCodes.Brfalse, fail);

        Label[] labels = new Label[tracked.Length];
        for (int i = 0; i < labels.Length; i++) labels[i] = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        if (labels.Length != 0) il.Emit(OpCodes.Switch, labels);
        il.Emit(OpCodes.Br, fail);

        ConstructorInfo changeCtor = typeof(TrackingChange).GetConstructor([typeof(string), typeof(object), typeof(object)])
            ?? throw new MissingMethodException(typeof(TrackingChange).FullName, ".ctor(string, object, object)");

        for (int i = 0; i < tracked.Length; i++)
        {
            RuntimeTrackingMemberDefinition<TOriginal> member = tracked[i];
            RuntimeTrackingMemberEmitter<TOriginal> emitter = member.Emitter
                ?? throw new InvalidOperationException($"Runtime member '{member.Name}' has no emitter.");
            RuntimeTrackingMemberEmitContext<TOriginal> memberContext = context.MemberContext(member);
            il.MarkLabel(labels[i]);
            emitter.EmitHasChange(memberContext, il);
            il.Emit(OpCodes.Brfalse, fail);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldstr, member.Name);
            emitter.EmitOriginalValueAsObject(memberContext, il);
            emitter.EmitEditValueAsObject(memberContext, il);
            il.Emit(OpCodes.Newobj, changeCtor);
            il.Emit(OpCodes.Stobj, typeof(TrackingChange));
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ret);
        }

        il.MarkLabel(fail);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Initobj, typeof(TrackingChange));
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        context.TypeBuilder.DefineMethodOverride(method, contract);
    }

    private static void EmitMethods(RuntimeTrackingEmitContext<TOriginal> context)
    {
        for (int i = 0; i < context.Definition.Methods.Count; i++)
        {
            RuntimeTrackingMethodDefinition<TOriginal> method = context.Definition.Methods[i];
            RuntimeTrackingMethodEmitter<TOriginal> emitter = method.Emitter
                ?? throw new InvalidOperationException($"Interface method {method.Requirement} has no emitter.");
            MethodBuilder generated = emitter.Emit(context, method, i);
            context.TypeBuilder.DefineMethodOverride(generated, method.Requirement);
        }
    }

    private static void BindInterfaceProperties(RuntimeTrackingEmitContext<TOriginal> context)
    {
        for (int i = 0; i < context.Definition.Members.Count; i++)
        {
            RuntimeTrackingMemberDefinition<TOriginal> member = context.Definition.Members[i];
            RuntimeEmittedProperty implementation = context.Properties[member];
            IReadOnlyList<PropertyInfo> requirements = member.Requirements;
            for (int r = 0; r < requirements.Count; r++)
            {
                PropertyInfo requirement = requirements[r];
                if (requirement.GetMethod is MethodInfo getter) context.TypeBuilder.DefineMethodOverride(implementation.Getter, getter);
                if (requirement.SetMethod is MethodInfo setter)
                {
                    MethodBuilder generatedSetter = implementation.Setter
                        ?? throw new InvalidOperationException($"No generated setter can satisfy {setter}.");
                    context.TypeBuilder.DefineMethodOverride(generatedSetter, setter);
                }
            }
        }
    }

    private static void CopyMetadata(PropertyBuilder property, IReadOnlyList<RuntimeTrackingMetadataSource> sources)
        => RuntimeCustomAttributeCopy.Apply(property, sources);

    private static MethodBuilder DefineExplicit(TypeBuilder type, MethodInfo contract)
    {
        MethodBuilder method = type.DefineMethod(
            $"__{Sanitize(contract.DeclaringType?.Name ?? "contract")}_{contract.Name}",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            contract.CallingConvention,
            contract.ReturnType,
            [.. contract.GetParameters().Select(static parameter => parameter.ParameterType)]);
        return method;
    }

    private static Type MemberType(MemberInfo member)
        => member switch
        {
            PropertyInfo property => property.PropertyType,
            FieldInfo field => field.FieldType,
            _ => throw new NotSupportedException($"Unsupported nested path member {member}.")
        };

    private static bool CanWrite(MemberInfo member)
        => member switch
        {
            PropertyInfo property => property.SetMethod?.IsPublic == true,
            FieldInfo field => !field.IsInitOnly && !field.IsLiteral,
            _ => false
        };

    private static void EmitReadNestedMember(ILGenerator il, MemberInfo member, Type rootType)
    {
        if (member is PropertyInfo property)
        {
            MethodInfo getter = property.GetMethod ?? throw new MissingMethodException(property.DeclaringType?.FullName, $"get_{property.Name}");
            il.Emit(rootType.IsValueType || !getter.IsVirtual ? OpCodes.Call : OpCodes.Callvirt, getter);
        }
        else
        {
            il.Emit(OpCodes.Ldfld, (FieldInfo)member);
        }
    }

    private static void EmitWriteNestedMember(ILGenerator il, MemberInfo member, Type rootType)
    {
        if (member is PropertyInfo property)
        {
            MethodInfo setter = property.SetMethod ?? throw new MissingMethodException(property.DeclaringType?.FullName, $"set_{property.Name}");
            il.Emit(rootType.IsValueType || !setter.IsVirtual ? OpCodes.Call : OpCodes.Callvirt, setter);
        }
        else
        {
            il.Emit(OpCodes.Stfld, (FieldInfo)member);
        }
    }

    private static string Sanitize(string value)
    {
        char[] chars = value.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_') chars[i] = '_';
        return new string(chars);
    }
}

internal sealed class RuntimeOriginalForwardMethodEmitter<TOriginal> : RuntimeTrackingMethodEmitter<TOriginal>
{
    protected internal override MethodBuilder Emit(RuntimeTrackingEmitContext<TOriginal> context, RuntimeTrackingMethodDefinition<TOriginal> method, int index)
    {
        MethodInfo requirement = method.Requirement;
        MethodBuilder generated = context.TypeBuilder.DefineMethod(
            $"__forward_{index}_{requirement.Name}",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot);
        GenericTypeParameterBuilder[] generic = CopySignature(generated, requirement);
        ILGenerator il = generated.GetILGenerator();
        context.EmitLoadOriginalTarget(il);
        ParameterInfo[] parameters = requirement.GetParameters();
        for (int i = 0; i < parameters.Length; i++) il.Emit(OpCodes.Ldarg, i + 1);
        if (typeof(TOriginal).IsValueType) il.Emit(OpCodes.Constrained, typeof(TOriginal));
        MethodInfo call = requirement.IsGenericMethodDefinition ? requirement.MakeGenericMethod([.. generic.Cast<Type>()]) : requirement;
        il.Emit(OpCodes.Callvirt, call);
        il.Emit(OpCodes.Ret);
        return generated;
    }

    private static GenericTypeParameterBuilder[] CopySignature(MethodBuilder method, MethodInfo source)
    {
        Type[] sourceGeneric = source.IsGenericMethodDefinition ? source.GetGenericArguments() : [];
        GenericTypeParameterBuilder[] generated = sourceGeneric.Length == 0
            ? []
            : method.DefineGenericParameters([.. sourceGeneric.Select(static argument => argument.Name)]);

        Type Map(Type type)
        {
            if (type.IsGenericParameter)
            {
                for (int i = 0; i < sourceGeneric.Length; i++)
                    if (type == sourceGeneric[i]) return generated[i];
            }
            if (type.IsByRef)
            {
                Type element = type.GetElementType() ?? throw new InvalidOperationException($"{type} has no element type.");
                return Map(element).MakeByRefType();
            }
            if (type.IsArray)
            {
                Type element = type.GetElementType() ?? throw new InvalidOperationException($"{type} has no element type.");
                return type.GetArrayRank() == 1 ? Map(element).MakeArrayType() : Map(element).MakeArrayType(type.GetArrayRank());
            }
            if (type.IsGenericType)
                return type.GetGenericTypeDefinition().MakeGenericType([.. type.GetGenericArguments().Select(Map)]);
            return type;
        }

        for (int i = 0; i < sourceGeneric.Length; i++)
        {
            generated[i].SetGenericParameterAttributes(sourceGeneric[i].GenericParameterAttributes);
            Type[] constraints = sourceGeneric[i].GetGenericParameterConstraints();
            Type? baseConstraint = constraints.FirstOrDefault(static constraint => !constraint.IsInterface);
            if (baseConstraint is not null) generated[i].SetBaseTypeConstraint(Map(baseConstraint));
            Type[] interfaceConstraints = [.. constraints.Where(static constraint => constraint.IsInterface).Select(Map)];
            if (interfaceConstraints.Length != 0) generated[i].SetInterfaceConstraints(interfaceConstraints);
        }

        method.SetReturnType(Map(source.ReturnType));
        ParameterInfo[] parameters = source.GetParameters();
        method.SetParameters([.. parameters.Select(parameter => Map(parameter.ParameterType))]);
        for (int i = 0; i < parameters.Length; i++) method.DefineParameter(i + 1, parameters[i].Attributes, parameters[i].Name);
        return generated;
    }
}

internal readonly record struct RuntimeTrackingEmissionResult<TOriginal>(Type Type, Type SnapshotType, ConstructorInfo ExistingCtor, ConstructorInfo NewCtor, Mapper Mapper);
