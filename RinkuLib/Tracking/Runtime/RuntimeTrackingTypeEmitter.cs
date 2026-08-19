using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Rinku.Mapping;
using Rinku.Mapping.Conversion;
using Rinku.Querying.Parameters;

namespace Rinku.Tracking.Runtime;

internal static class RuntimeTrackingTypeEmitter<TOriginal, TEdit> where TEdit : class, IRuntimeTrackingItem<TOriginal> {
    private readonly record struct PropertyMethods(IRuntimeTrackingMember Member, MethodBuilder Getter, MethodBuilder? Setter);

    private static readonly MethodInfo CasterTryCast = typeof(Caster).GetMethods(BindingFlags.Static | BindingFlags.Public)
        .Single(x => x.Name == nameof(Caster.TryCast) && x.IsGenericMethodDefinition && x.GetGenericArguments().Length == 2);

    public static (Type Type, ConstructorInfo ExistingCtor, ConstructorInfo? NewCtor) Build(IReadOnlyList<IRuntimeTrackingMember> members,
        IReadOnlyList<IRuntimeTrackingMember> runtimeMembers, Mapper? mapper, IRuntimeEditStorage<TOriginal> editStorage,
        RuntimeNewOriginalCall<TOriginal>? newOriginal, IReadOnlyList<IRuntimeTrackingCapability<TOriginal>> capabilities,
        bool dynamicAccess, bool notifications) {
        string name = $"RinkuTracking_{Sanitize(typeof(TOriginal).Name)}_{RuntimeTrackingModule.NextId()}";
        TypeBuilder type = RuntimeTrackingModule.Module.DefineType(name, TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class);
        CopyParameterTypeConfiguration(type);
        bool parameterProjection = NeedsParameterProjection(members);
        if (parameterProjection)
            type.SetCustomAttribute(new CustomAttributeBuilder(
                typeof(RuntimeTrackingParameterSourceAttribute).GetConstructor(Type.EmptyTypes)!, []));

        FieldBuilder original = type.DefineField("_original", typeof(TOriginal), FieldAttributes.Private);
        FieldBuilder edit = type.DefineField("_edit", typeof(DynaObject), FieldAttributes.Private);
        FieldBuilder? mapperField = dynamicAccess
            ? type.DefineField("s_mapper", typeof(Mapper), FieldAttributes.Private | FieldAttributes.Static)
            : null;
        FieldBuilder storageField = type.DefineField("s_editStorage", typeof(IRuntimeEditStorage<TOriginal>), FieldAttributes.Private | FieldAttributes.Static);
        FieldBuilder? propertyChanged = notifications
            ? type.DefineField("_propertyChanged", typeof(PropertyChangedEventHandler), FieldAttributes.Private)
            : null;
        var capabilityBuilder = new RuntimeTrackingCapabilityBuilder(type, typeof(TEdit), original, edit, propertyChanged);
        capabilityBuilder.AddInterface(typeof(TEdit));
        if (dynamicAccess) capabilityBuilder.AddInterface(typeof(IRuntimeMemberAccess));
        if (notifications) capabilityBuilder.AddInterface(typeof(INotifyPropertyChanged));

        ConstructorBuilder existingCtor = EmitExistingCtor(type, original);
        ConstructorBuilder? newCtor = newOriginal is null ? null : EmitNewCtor(type, original, edit, storageField, newOriginal, capabilityBuilder);
        MethodBuilder ensureEdit = EmitEnsureEdit(type, original, edit, storageField);
        Dictionary<IRuntimeTrackingMember, int> editIndexes = BuildEditIndexes(members);
        PropertyMethods[] properties = EmitProperties(type, original, edit, ensureEdit, capabilityBuilder, members, editIndexes);
        PropertyMethods[] runtimeProperties = SelectRuntimeProperties(properties, runtimeMembers);

        if (dynamicAccess) {
            EmitRuntimeMapper(type, mapperField!);
            EmitRuntimeTryGet(type, runtimeProperties);
            EmitRuntimeGet(type, runtimeProperties);
            EmitRuntimeSet(type, runtimeProperties);
        }
        EmitIsEditing(type, edit);
        EmitEnsureEditing(type, edit, ensureEdit);
        EmitCommit(type, original, edit, storageField, capabilityBuilder);
        EmitCancel(type, original, edit, storageField, capabilityBuilder);
        EmitHasOriginal(type, edit, storageField);
        EmitOriginalAccess(type, original, edit, storageField);
        if (notifications) EmitPropertyChanged(type, capabilityBuilder);
        MarkCoreRequirements(capabilityBuilder, dynamicAccess);

        foreach (IRuntimeTrackingCapability<TOriginal> capability in capabilities) capability.Emit(capabilityBuilder);
        BindCustomContractProperties(type, capabilityBuilder, properties);
        RuntimeTrackingContract<TOriginal, TEdit>.ValidateRequirements(capabilityBuilder);

        Type created;
        try { created = type.CreateType()!; }
        catch (TypeLoadException ex) {
            throw new InvalidOperationException(
                $"The requested runtime contract {typeof(TEdit)} is not fully implemented by the resolved members/capabilities. " +
                "Add a runtime member or type contributor for unresolved custom contract methods/properties.", ex);
        }

        if (dynamicAccess) created.GetField("s_mapper", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, mapper!);
        created.GetField("s_editStorage", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, editStorage);
        if (parameterProjection) {
            RuntimeTrackingParameterRegistry.Register(created, members);
            RuntimeTrackingParameterRegistry.Register(typeof(TEdit), members);
        }
        capabilityBuilder.Initialize(created);

        if (!typeof(TEdit).IsAssignableFrom(created))
            throw new InvalidOperationException($"Generated type {created} does not implement requested runtime contract {typeof(TEdit)}.");
        return (created, created.GetConstructor([typeof(TOriginal)])!, newCtor is null ? null : created.GetConstructor(Type.EmptyTypes));
    }

    private static void CopyParameterTypeConfiguration(TypeBuilder type) {
        ParameterConflictAttribute? conflict = typeof(TEdit).GetCustomAttribute<ParameterConflictAttribute>(inherit: false)
            ?? typeof(TOriginal).GetCustomAttribute<ParameterConflictAttribute>(inherit: true);
        if (conflict is null) return;
        type.SetCustomAttribute(new CustomAttributeBuilder(
            typeof(ParameterConflictAttribute).GetConstructor([typeof(ParameterConflictBehavior)])!,
            [conflict.Behavior]));
    }

    private static Dictionary<IRuntimeTrackingMember, int> BuildEditIndexes(IReadOnlyList<IRuntimeTrackingMember> members) {
        var indexes = new Dictionary<IRuntimeTrackingMember, int>(ReferenceEqualityComparer.Instance);
        int index = 1;
        for (int i = 0; i < members.Count; i++)
            if (members[i] is IRuntimeEditableTrackingMember) indexes.Add(members[i], index++);
        return indexes;
    }

    private static PropertyMethods[] SelectRuntimeProperties(PropertyMethods[] properties, IReadOnlyList<IRuntimeTrackingMember> runtimeMembers) {
        var result = new PropertyMethods[runtimeMembers.Count];
        for (int i = 0; i < runtimeMembers.Count; i++) {
            IRuntimeTrackingMember member = runtimeMembers[i];
            bool found = false;
            for (int p = 0; p < properties.Length; p++)
                if (ReferenceEquals(properties[p].Member, member)) {
                    result[i] = properties[p];
                    found = true;
                    break;
                }
            if (!found) throw new InvalidOperationException($"Runtime member '{member.Name}' has no emitted accessor.");
        }
        return result;
    }

    private static ConstructorBuilder EmitExistingCtor(TypeBuilder type, FieldBuilder original) {
        ConstructorBuilder ctor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [typeof(TOriginal)]);
        ILGenerator il = ctor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, original);
        il.Emit(OpCodes.Ret);
        return ctor;
    }

    private static ConstructorBuilder EmitNewCtor(TypeBuilder type, FieldBuilder original, FieldBuilder edit, FieldBuilder storage,
        RuntimeNewOriginalCall<TOriginal> newOriginal, RuntimeTrackingCapabilityBuilder builder) {
        ConstructorBuilder ctor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
        ILGenerator il = ctor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Ldarg_0);
        newOriginal.Emit(builder, il);
        il.Emit(OpCodes.Stfld, original);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, storage);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, original);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, typeof(IRuntimeEditStorage<TOriginal>).GetMethod(nameof(IRuntimeEditStorage<TOriginal>.Create))!);
        il.Emit(OpCodes.Stfld, edit);
        il.Emit(OpCodes.Ret);
        return ctor;
    }

    private static MethodBuilder EmitEnsureEdit(TypeBuilder type, FieldBuilder original, FieldBuilder edit, FieldBuilder storage) {
        MethodBuilder method = type.DefineMethod("EnsureEdit", MethodAttributes.Private | MethodAttributes.HideBySig, typeof(DynaObject), Type.EmptyTypes);
        ILGenerator il = method.GetILGenerator();
        Label create = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, edit);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse_S, create);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(create);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, storage);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, original);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, typeof(IRuntimeEditStorage<TOriginal>).GetMethod(nameof(IRuntimeEditStorage<TOriginal>.Create))!);
        il.Emit(OpCodes.Stfld, edit);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, edit);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private static PropertyMethods[] EmitProperties(TypeBuilder type, FieldBuilder original, FieldBuilder edit, MethodBuilder ensureEdit,
        RuntimeTrackingCapabilityBuilder capabilityBuilder, IReadOnlyList<IRuntimeTrackingMember> members,
        IReadOnlyDictionary<IRuntimeTrackingMember, int> editIndexes) {
        var result = new PropertyMethods[members.Count];
        for (int i = 0; i < members.Count; i++) {
            IRuntimeTrackingMember member = members[i];
            int editIndex = editIndexes.TryGetValue(member, out int value) ? value : -1;
            var context = new RuntimeTrackingMemberEmitContext(capabilityBuilder, typeof(TOriginal), original, edit, ensureEdit, editIndex, member.Name);
            PropertyBuilder property = type.DefineProperty(member.Name, PropertyAttributes.None, member.ValueType, null);
            MethodAttributes visibility = member.ExposeProperty ? MethodAttributes.Public : MethodAttributes.Private;
            MethodAttributes common = visibility | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig |
                MethodAttributes.NewSlot | MethodAttributes.SpecialName;
            MethodBuilder getter = type.DefineMethod($"get_{member.Name}", common, member.ValueType, Type.EmptyTypes);
            ILGenerator get = getter.GetILGenerator();
            member.EmitGet(context, get);
            get.Emit(OpCodes.Ret);
            property.SetGetMethod(getter);

            MethodBuilder? setter = null;
            if (member.CanWrite) {
                setter = type.DefineMethod($"set_{member.Name}", common, typeof(void), [member.ValueType]);
                ILGenerator set = setter.GetILGenerator();
                member.EmitSet(context, set);
                set.Emit(OpCodes.Ret);
                property.SetSetMethod(setter);
            }
            member.ApplyMetadata(property);
            result[i] = new(member, getter, setter);
        }
        return result;
    }

    private static void BindCustomContractProperties(TypeBuilder type, RuntimeTrackingCapabilityBuilder capabilityBuilder, PropertyMethods[] properties) {
        foreach (Type contractType in RuntimeTrackingContract<TOriginal, TEdit>.CustomContracts())
            foreach (PropertyInfo contract in contractType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)) {
                PropertyMethods? match = null;
                for (int i = 0; i < properties.Length; i++)
                    if (string.Equals(properties[i].Member.Name, contract.Name, StringComparison.OrdinalIgnoreCase)) {
                        match = properties[i];
                        break;
                    }

                if (RuntimeTrackingContract<TOriginal, TEdit>.RequiresGeneration(contract.GetMethod) &&
                    contract.GetMethod is MethodInfo getter && !capabilityBuilder.IsImplemented(getter)) {
                    if (match is null) throw new InvalidOperationException($"Exposed runtime property {contractType}.{contract.Name} has no generated implementation.");
                    if (match.Value.Member.ValueType != contract.PropertyType)
                        throw new InvalidOperationException($"Exposed runtime property {contractType}.{contract.Name} is {contract.PropertyType}, but generated member is {match.Value.Member.ValueType}.");
                    type.DefineMethodOverride(match.Value.Getter, getter);
                    capabilityBuilder.MarkImplemented(getter);
                }

                if (RuntimeTrackingContract<TOriginal, TEdit>.RequiresGeneration(contract.SetMethod) &&
                    contract.SetMethod is MethodInfo setter && !capabilityBuilder.IsImplemented(setter)) {
                    if (match?.Setter is null)
                        throw new InvalidOperationException($"Exposed runtime property {contractType}.{contract.Name} requires a setter, but the generated member is read-only or missing.");
                    type.DefineMethodOverride(match.Value.Setter, setter);
                    capabilityBuilder.MarkImplemented(setter);
                }
            }
    }

    private static void MarkCoreRequirements(RuntimeTrackingCapabilityBuilder builder, bool dynamicAccess) {
        if (dynamicAccess) {
            builder.MarkImplemented(typeof(IRuntimeMemberAccess).GetProperty(nameof(IRuntimeMemberAccess.Mapper))!.GetMethod!);
            builder.MarkImplemented(typeof(IRuntimeMemberAccess).GetMethods().Single(x => x.Name == nameof(IRuntimeMemberAccess.TryGet) && x.IsGenericMethodDefinition));
            builder.MarkImplemented(typeof(IRuntimeMemberAccess).GetMethods().Single(x => x.Name == nameof(IRuntimeMemberAccess.Set) && x.IsGenericMethodDefinition && x.GetParameters().Length == 2 && x.GetParameters()[0].ParameterType == typeof(int)));
        }
        builder.MarkImplemented(typeof(IHasOriginal).GetProperty(nameof(IHasOriginal.HasOriginal))!.GetMethod!);
        builder.MarkImplemented(typeof(IEditable).GetProperty(nameof(IEditable.IsEditing))!.GetMethod!);
        builder.MarkImplemented(typeof(IEditable).GetMethod(nameof(IEditable.EnsureEditing))!);
        builder.MarkImplemented(typeof(IEditable).GetMethod(nameof(IEditable.CommitEdit))!);
        builder.MarkImplemented(typeof(IEditable).GetMethod(nameof(IEditable.CancelEdit))!);
        builder.MarkImplemented(typeof(ITrackingItem<TOriginal>).GetMethod(nameof(ITrackingItem<TOriginal>.TryGetOriginal), new[] { typeof(TOriginal).MakeByRefType() })!);
    }

    private static void EmitRuntimeMapper(TypeBuilder type, FieldBuilder mapper) {
        MethodInfo contract = typeof(IRuntimeMemberAccess).GetProperty(nameof(IRuntimeMemberAccess.Mapper))!.GetMethod!;
        MethodBuilder method = DefineExplicit(type, contract, true);
        ILGenerator il = method.GetILGenerator();
        il.Emit(OpCodes.Ldsfld, mapper);
        il.Emit(OpCodes.Ret);
        type.DefineMethodOverride(method, contract);
    }

    private static void EmitRuntimeTryGet(TypeBuilder type, PropertyMethods[] properties) {
        MethodInfo contract = typeof(IRuntimeMemberAccess).GetMethods()
            .Single(x => x.Name == nameof(IRuntimeMemberAccess.TryGet) && x.IsGenericMethodDefinition);
        MethodBuilder method = type.DefineMethod("Rinku.Tracking.Runtime.IRuntimeMemberAccess.TryGet",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot);
        GenericTypeParameterBuilder expected = method.DefineGenericParameters("T")[0];
        method.SetReturnType(typeof(bool));
        method.SetParameters(typeof(int), expected.MakeByRefType());
        ILGenerator il = method.GetILGenerator();
        Label[] labels = Enumerable.Range(0, properties.Length).Select(_ => il.DefineLabel()).ToArray();
        Label invalid = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        if (labels.Length != 0) il.Emit(OpCodes.Switch, labels);
        il.Emit(OpCodes.Br, invalid);

        for (int i = 0; i < properties.Length; i++) {
            il.MarkLabel(labels[i]);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, properties[i].Getter);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, CasterTryCast.MakeGenericMethod(properties[i].Member.ValueType, expected));
            il.Emit(OpCodes.Ret);
        }

        il.MarkLabel(invalid);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Initobj, expected);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        type.DefineMethodOverride(method, contract);
    }

    private static void EmitRuntimeGet(TypeBuilder type, PropertyMethods[] properties) {
        MethodInfo contract = typeof(IRuntimeMemberAccess).GetMethods()
            .Single(x => x.Name == nameof(IRuntimeMemberAccess.Get) && x.IsGenericMethodDefinition &&
                x.GetParameters() is [{ ParameterType: var p }] && p == typeof(int));
        MethodBuilder method = type.DefineMethod("Rinku.Tracking.Runtime.IRuntimeMemberAccess.Get",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot);
        GenericTypeParameterBuilder expected = method.DefineGenericParameters("T")[0];
        method.SetReturnType(expected);
        method.SetParameters(typeof(int));
        ILGenerator il = method.GetILGenerator();
        LocalBuilder converted = il.DeclareLocal(expected);
        Label[] labels = Enumerable.Range(0, properties.Length).Select(_ => il.DefineLabel()).ToArray();
        Label fail = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        if (labels.Length != 0) il.Emit(OpCodes.Switch, labels);
        il.Emit(OpCodes.Br, fail);

        for (int i = 0; i < properties.Length; i++) {
            il.MarkLabel(labels[i]);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, properties[i].Getter);
            il.Emit(OpCodes.Ldloca, converted);
            il.Emit(OpCodes.Call, CasterTryCast.MakeGenericMethod(properties[i].Member.ValueType, expected));
            il.Emit(OpCodes.Brfalse, fail);
            il.Emit(OpCodes.Ldloc, converted);
            il.Emit(OpCodes.Ret);
        }

        il.MarkLabel(fail);
        il.Emit(OpCodes.Ldstr, "Unable to read runtime tracking member at the requested index/type.");
        il.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Throw);
        type.DefineMethodOverride(method, contract);
    }

    private static void EmitRuntimeSet(TypeBuilder type, PropertyMethods[] properties) {
        MethodInfo contract = typeof(IRuntimeMemberAccess).GetMethods()
            .Single(x => x.Name == nameof(IRuntimeMemberAccess.Set) && x.IsGenericMethodDefinition && x.GetParameters()[0].ParameterType == typeof(int));
        MethodBuilder method = type.DefineMethod("Rinku.Tracking.Runtime.IRuntimeMemberAccess.Set",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot);
        GenericTypeParameterBuilder input = method.DefineGenericParameters("T")[0];
        method.SetReturnType(typeof(bool));
        method.SetParameters(typeof(int), input);
        ILGenerator il = method.GetILGenerator();
        Label[] labels = Enumerable.Range(0, properties.Length).Select(_ => il.DefineLabel()).ToArray();
        Label fail = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        if (labels.Length != 0) il.Emit(OpCodes.Switch, labels);
        il.Emit(OpCodes.Br, fail);

        for (int i = 0; i < properties.Length; i++) {
            il.MarkLabel(labels[i]);
            PropertyMethods property = properties[i];
            if (property.Setter is null) { il.Emit(OpCodes.Br, fail); continue; }
            LocalBuilder converted = il.DeclareLocal(property.Member.ValueType);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldloca, converted);
            il.Emit(OpCodes.Call, CasterTryCast.MakeGenericMethod(input, property.Member.ValueType));
            Label ok = il.DefineLabel();
            il.Emit(OpCodes.Brtrue_S, ok);
            il.Emit(OpCodes.Br, fail);
            il.MarkLabel(ok);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, converted);
            il.Emit(OpCodes.Call, property.Setter);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ret);
        }

        il.MarkLabel(fail);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        type.DefineMethodOverride(method, contract);
    }

    private static void EmitIsEditing(TypeBuilder type, FieldBuilder edit) {
        MethodInfo contract = typeof(IEditable).GetProperty(nameof(IEditable.IsEditing))!.GetMethod!;
        MethodBuilder method = DefineExplicit(type, contract, true);
        ILGenerator il = method.GetILGenerator();
        Label no = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, edit); il.Emit(OpCodes.Brfalse_S, no);
        il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);
        il.MarkLabel(no); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
        type.DefineMethodOverride(method, contract);
    }

    private static void EmitEnsureEditing(TypeBuilder type, FieldBuilder edit, MethodBuilder ensureEdit) {
        MethodInfo contract = typeof(IEditable).GetMethod(nameof(IEditable.EnsureEditing))!;
        MethodBuilder method = DefineExplicit(type, contract);
        ILGenerator il = method.GetILGenerator();
        Label create = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, edit); il.Emit(OpCodes.Brfalse_S, create);
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
        il.MarkLabel(create);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, ensureEdit); il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);
        type.DefineMethodOverride(method, contract);
    }

    private static void EmitCommit(TypeBuilder type, FieldBuilder original, FieldBuilder edit, FieldBuilder storage, RuntimeTrackingCapabilityBuilder builder) {
        MethodInfo contract = typeof(IEditable).GetMethod(nameof(IEditable.CommitEdit))!;
        MethodBuilder method = DefineExplicit(type, contract);
        ILGenerator il = method.GetILGenerator();
        LocalBuilder current = il.DeclareLocal(typeof(DynaObject));
        Label hasEdit = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, edit); il.Emit(OpCodes.Stloc, current);
        il.Emit(OpCodes.Ldloc, current); il.Emit(OpCodes.Brtrue_S, hasEdit);
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);

        il.MarkLabel(hasEdit);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, storage);
        il.Emit(OpCodes.Ldloc, current);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, original);
        il.Emit(OpCodes.Callvirt, typeof(IRuntimeEditStorage<TOriginal>).GetMethod(nameof(IRuntimeEditStorage<TOriginal>.Apply))!);
        il.Emit(OpCodes.Stfld, original);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Stfld, edit);
        builder.EmitRaiseChanged(il, null);
        il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);
        type.DefineMethodOverride(method, contract);
    }

    private static void EmitCancel(TypeBuilder type, FieldBuilder original, FieldBuilder edit, FieldBuilder storage, RuntimeTrackingCapabilityBuilder builder) {
        MethodInfo contract = typeof(IEditable).GetMethod(nameof(IEditable.CancelEdit))!;
        MethodBuilder method = DefineExplicit(type, contract);
        ILGenerator il = method.GetILGenerator();
        LocalBuilder current = il.DeclareLocal(typeof(DynaObject));
        Label hasEdit = il.DefineLabel();
        Label existing = il.DefineLabel();
        Label done = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, edit); il.Emit(OpCodes.Stloc, current);
        il.Emit(OpCodes.Ldloc, current); il.Emit(OpCodes.Brtrue_S, hasEdit);
        il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
        il.MarkLabel(hasEdit);
        il.Emit(OpCodes.Ldsfld, storage); il.Emit(OpCodes.Ldloc, current);
        il.Emit(OpCodes.Callvirt, typeof(IRuntimeEditStorage<TOriginal>).GetMethod(nameof(IRuntimeEditStorage<TOriginal>.IsNew))!);
        il.Emit(OpCodes.Brfalse_S, existing);
        // A new row must remain in edit state after CancelEdit, but its existing DynaObject can be reset in place.
        // This avoids allocating another snapshot every time a new row is cancelled/reset.
        il.Emit(OpCodes.Ldsfld, storage);
        il.Emit(OpCodes.Ldloc, current);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, original);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, typeof(IRuntimeEditStorage<TOriginal>).GetMethod(nameof(IRuntimeEditStorage<TOriginal>.Reset))!);
        il.Emit(OpCodes.Br_S, done);
        il.MarkLabel(existing);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Stfld, edit);
        il.MarkLabel(done);
        builder.EmitRaiseChanged(il, null);
        il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);
        type.DefineMethodOverride(method, contract);
    }

    private static void EmitHasOriginal(TypeBuilder type, FieldBuilder edit, FieldBuilder storage) {
        MethodInfo contract = typeof(IHasOriginal).GetProperty(nameof(IHasOriginal.HasOriginal))!.GetMethod!;
        MethodBuilder method = DefineExplicit(type, contract, true);
        ILGenerator il = method.GetILGenerator();
        Label yes = il.DefineLabel();
        LocalBuilder current = il.DeclareLocal(typeof(DynaObject));
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, edit); il.Emit(OpCodes.Stloc, current);
        il.Emit(OpCodes.Ldloc, current); il.Emit(OpCodes.Brfalse_S, yes);
        il.Emit(OpCodes.Ldsfld, storage); il.Emit(OpCodes.Ldloc, current);
        il.Emit(OpCodes.Callvirt, typeof(IRuntimeEditStorage<TOriginal>).GetMethod(nameof(IRuntimeEditStorage<TOriginal>.IsNew))!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(yes);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        type.DefineMethodOverride(method, contract);
    }

    private static void EmitOriginalAccess(TypeBuilder type, FieldBuilder original, FieldBuilder edit, FieldBuilder storage) {
        MethodInfo contract = typeof(ITrackingItem<TOriginal>).GetMethod(nameof(ITrackingItem<TOriginal>.TryGetOriginal))!;
        MethodBuilder method = DefineExplicit(type, contract);
        ILGenerator il = method.GetILGenerator();
        Label attached = il.DefineLabel();
        LocalBuilder current = il.DeclareLocal(typeof(DynaObject));
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, edit); il.Emit(OpCodes.Stloc, current);
        il.Emit(OpCodes.Ldloc, current); il.Emit(OpCodes.Brfalse_S, attached);
        il.Emit(OpCodes.Ldsfld, storage); il.Emit(OpCodes.Ldloc, current);
        il.Emit(OpCodes.Callvirt, typeof(IRuntimeEditStorage<TOriginal>).GetMethod(nameof(IRuntimeEditStorage<TOriginal>.IsNew))!);
        il.Emit(OpCodes.Brfalse_S, attached);
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Initobj, typeof(TOriginal)); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
        il.MarkLabel(attached);
        il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, original); il.Emit(OpCodes.Stobj, typeof(TOriginal));
        il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);
        type.DefineMethodOverride(method, contract);
    }

    private static void EmitPropertyChanged(TypeBuilder type, RuntimeTrackingCapabilityBuilder builder) {
        FieldBuilder field = builder.PropertyChangedField ?? throw new InvalidOperationException("PropertyChanged storage was not created.");
        EventInfo contractEvent = typeof(INotifyPropertyChanged).GetEvent(nameof(INotifyPropertyChanged.PropertyChanged))!;
        EventBuilder evt = type.DefineEvent(nameof(INotifyPropertyChanged.PropertyChanged), EventAttributes.None, typeof(PropertyChangedEventHandler));
        MethodBuilder add = type.DefineMethod("System.ComponentModel.INotifyPropertyChanged.add_PropertyChanged",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.SpecialName,
            typeof(void), [typeof(PropertyChangedEventHandler)]);
        ILGenerator a = add.GetILGenerator();
        a.Emit(OpCodes.Ldarg_0); a.Emit(OpCodes.Ldflda, field); a.Emit(OpCodes.Ldarg_1);
        a.Emit(OpCodes.Call, typeof(RuntimePropertyChangedHub).GetMethod(nameof(RuntimePropertyChangedHub.Add))!); a.Emit(OpCodes.Ret);
        MethodBuilder remove = type.DefineMethod("System.ComponentModel.INotifyPropertyChanged.remove_PropertyChanged",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.SpecialName,
            typeof(void), [typeof(PropertyChangedEventHandler)]);
        ILGenerator r = remove.GetILGenerator();
        r.Emit(OpCodes.Ldarg_0); r.Emit(OpCodes.Ldflda, field); r.Emit(OpCodes.Ldarg_1);
        r.Emit(OpCodes.Call, typeof(RuntimePropertyChangedHub).GetMethod(nameof(RuntimePropertyChangedHub.Remove))!); r.Emit(OpCodes.Ret);
        evt.SetAddOnMethod(add); evt.SetRemoveOnMethod(remove);
        type.DefineMethodOverride(add, contractEvent.AddMethod!);
        type.DefineMethodOverride(remove, contractEvent.RemoveMethod!);
        builder.MarkImplemented(contractEvent.AddMethod!);
        builder.MarkImplemented(contractEvent.RemoveMethod!);
    }

    private static MethodBuilder DefineExplicit(TypeBuilder type, MethodInfo contract, bool specialName = false) {
        MethodAttributes attributes = MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot;
        if (specialName) attributes |= MethodAttributes.SpecialName;
        return type.DefineMethod($"__{contract.DeclaringType?.Name}_{contract.Name}", attributes, contract.ReturnType,
            contract.GetParameters().Select(static x => x.ParameterType).ToArray());
    }

    private static bool NeedsParameterProjection(IReadOnlyList<IRuntimeTrackingMember> members) {
        for (int i = 0; i < members.Count; i++) {
            IRuntimeTrackingMember member = members[i];
            bool renamed = member.ParameterNames is { Count: > 0 };
            if (member.ExposeProperty && (!member.IncludeInParameters || renamed)) return true;
            else if (member.IncludeInParameters) return true;
        }
        return false;
    }

    private static string Sanitize(string name) {
        char[] chars = name.ToCharArray();
        for (int i = 0; i < chars.Length; i++) if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_') chars[i] = '_';
        return new string(chars);
    }
}
