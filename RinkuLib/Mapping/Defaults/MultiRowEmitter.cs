using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.ExceptionServices;
using Rinku.Mapping.Parsers;
using Rinku.Mapping.Parsers.Defaults;
using Rinku.Internal;

namespace Rinku.Mapping.Defaults;

internal static class MultiRowEmitter {
    private static readonly Type[] ReadValueArgs = [typeof(object[]), typeof(DbDataReader)];
    private static readonly MethodInfo BuildRecursiveMethod = typeof(MultiRowEmitter).GetMethod(nameof(BuildRecursiveClosed), BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException(typeof(MultiRowEmitter).FullName, nameof(BuildRecursiveClosed));
    private const MethodAttributes InterfaceMethod =
        MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final
        | MethodAttributes.HideBySig | MethodAttributes.NewSlot;
    private const MethodAttributes StateHelper = MethodAttributes.Private | MethodAttributes.Static;
    private const FieldAttributes Priv = FieldAttributes.Private;
    private static T Require<T>(T? value, string name) where T : class
        => value ?? throw new RinkuInternalException(ErrorCodes.InternalInvariant, $"emitted-state member {name} was not initialized");

    internal static ITypeParser<T> Build<T>(DbItemPlan rd, ColumnInfo[] cols) {
        return DbItemPlan.AllSimple(rd) ? BuildCollapsed<T>(rd, cols)
            : rd is IMultiRowPlan { Element: IMultiRowPlan } recursive ? BuildRecursive<T>(recursive, cols)
            : rd is IMultiRowPlan acc ? BuildCollectionRoot<T>(acc, cols)
            : BuildSpine<T>(rd, cols);
    }

    private static ITypeParser<T> BuildRecursive<T>(IMultiRowPlan plan, ColumnInfo[] cols) {
        try {
            return (ITypeParser<T>)(BuildRecursiveMethod.MakeGenericMethod(typeof(T), plan.ElementType, plan.BufferType).Invoke(null, [plan, cols])
                ?? throw new RinkuInternalException(ErrorCodes.InternalInvariant, "The recursive parser factory returned null."));
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null) {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static ITypeParser<TResult> BuildRecursiveClosed<TResult, TElement, TBuffer>(IMultiRowPlan plan, ColumnInfo[] cols) {
        var elementParser = Build<TElement>(plan.Element, cols);
        var strategy = EmitAccumulatorStrategy<TResult, TElement, TBuffer>(plan);
        var parserType = typeof(RecursiveAccumulatorTypeParser<,,,>).MakeGenericType(typeof(TResult), typeof(TElement), typeof(TBuffer), strategy);
        return (ITypeParser<TResult>)(Activator.CreateInstance(parserType, [elementParser])
            ?? throw new RinkuInternalException(ErrorCodes.InternalInvariant, "The recursive parser type could not be instantiated."));
    }

    private static Type EmitAccumulatorStrategy<TResult, TElement, TBuffer>(IMultiRowPlan plan) {
        StateTypeAssembly.AllowAccessTo(typeof(TResult));
        StateTypeAssembly.AllowAccessTo(typeof(TElement));
        StateTypeAssembly.AllowAccessTo(typeof(TBuffer));
        StateTypeAssembly.AllowAccessTo(plan.AddMethod.DeclaringType
            ?? throw new RinkuInternalException(ErrorCodes.InternalInvariant, "The accumulator add method had no declaring type."));
        if (plan.Construct?.DeclaringType is { } finishType)
            StateTypeAssembly.AllowAccessTo(finishType);
        var contract = typeof(IAccumulatorStrategy<,,>).MakeGenericType(typeof(TResult), typeof(TElement), typeof(TBuffer));
        var tb = StateTypeAssembly.DefineState($"Accumulator_{typeof(TResult).Name}_{Guid.NewGuid():N}");
        tb.AddInterfaceImplementation(contract);
        EmitStrategySeed(tb, contract, plan.InitialState);
        EmitStrategyAdd(tb, contract, plan.AddMethod);
        EmitStrategyFinish(tb, contract, plan.Construct);
        return tb.CreateType();
    }

    private static void EmitStrategySeed(TypeBuilder tb, Type contract, MethodBase initialState) {
        var method = tb.DefineMethod("Seed", InterfaceMethod, initialState is ConstructorInfo ctor
            ? ctor.DeclaringType ?? throw new RinkuInternalException(ErrorCodes.InternalInvariant, "The seed constructor had no declaring type.")
            : ((MethodInfo)initialState).ReturnType, Type.EmptyTypes);
        var il = method.GetILGenerator();
        if (initialState is ConstructorInfo seedCtor)
            il.Emit(OpCodes.Newobj, seedCtor);
        else
            il.Emit(OpCodes.Call, (MethodInfo)initialState);
        il.Emit(OpCodes.Ret);
        tb.DefineMethodOverride(method, Require(contract.GetMethod("Seed"), "Seed"));
    }

    private static void EmitStrategyAdd(TypeBuilder tb, Type contract, MethodInfo add) {
        if (add.IsStatic)
            throw Unsupported("a static accumulator add method");
        var bufferType = contract.GetGenericArguments()[2];
        var elementType = contract.GetGenericArguments()[1];
        var method = tb.DefineMethod("Add", InterfaceMethod, typeof(void), [bufferType.MakeByRefType(), elementType]);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_1);
        if (!bufferType.IsValueType)
            il.Emit(OpCodes.Ldind_Ref);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(add.IsVirtual && !bufferType.IsValueType ? OpCodes.Callvirt : OpCodes.Call, add);
        if (add.ReturnType != typeof(void))
            il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ret);
        tb.DefineMethodOverride(method, Require(contract.GetMethod("Add"), "Add"));
    }

    private static void EmitStrategyFinish(TypeBuilder tb, Type contract, MethodBase? finish) {
        var types = contract.GetGenericArguments();
        var resultType = types[0];
        var bufferType = types[2];
        var method = tb.DefineMethod("Finish", InterfaceMethod, resultType, [bufferType]);
        var il = method.GetILGenerator();
        if (finish is null) il.Emit(OpCodes.Ldarg_1);
        else if (finish is ConstructorInfo ctor) {
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Newobj, ctor);
        }
        else {
            var factory = (MethodInfo)finish;
            if (factory.IsStatic)
                il.Emit(OpCodes.Ldarg_1);
            else if (bufferType.IsValueType)
                il.Emit(OpCodes.Ldarga_S, 1);
            else
                il.Emit(OpCodes.Ldarg_1);
            il.Emit(factory.IsVirtual && !factory.IsStatic && !bufferType.IsValueType ? OpCodes.Callvirt : OpCodes.Call, factory);
        }
        il.Emit(OpCodes.Ret);
        tb.DefineMethodOverride(method, Require(contract.GetMethod("Finish"), "Finish"));
    }

    private static ITypeParser<T> BuildCollectionRoot<T>(IMultiRowPlan acc, ColumnInfo[] cols) {
        var (tb, stateInterface) = BeginState(typeof(T));
        var master = new Level { ResultType = typeof(T), IsMaster = true, Boundary = AlwaysGroupedBoundary.Instance };
        master.CaptureInto = master;
        if (acc is not DbItemPlan accPlan)
            throw Unsupported($"a multi-row plan that is not a DbItemPlan ({acc.GetType().Name})");
        master.CtorSlots = [ClassifySlot(accPlan, typeof(T), member: null, tb, cols, master, "0")];
        return Close<T>(Assemble(tb, stateInterface, typeof(T), master), cols, BehaviorFor(acc.Element), collectionRoot: true);
    }

    private static (TypeBuilder Tb, Type StateInterface) BeginState(Type resultType) {
        StateTypeAssembly.AllowAccessTo(resultType);
        var tb = StateTypeAssembly.DefineState(StateName(resultType));
        var stateInterface = typeof(IMultiRowState<>).MakeGenericType(resultType);
        tb.AddInterfaceImplementation(stateInterface);
        return (tb, stateInterface);
    }

    private static Type Assemble(TypeBuilder tb, Type stateInterface, Type resultType, Level master) {
        EmitStateCtor(tb, master);
        EmitCloseMethods(master);
        EmitRead(tb, stateInterface, master);
        EmitBuild(tb, stateInterface, resultType, master);
        var created = tb.CreateType();
        SetTargetsFields(created, master);
        return created;
    }

    private static ITypeParser<T> BuildCollapsed<T>(DbItemPlan rd, ColumnInfo[] cols) {
        var resultType = typeof(T);
        var (tb, stateInterface) = BeginState(resultType);
        var valueField = tb.DefineField("_v", resultType, Priv);
        var liveField = tb.DefineField("_live", typeof(bool), Priv);
        var readValue = EmitConstruct(tb, rd, cols, "ReadValue", resultType, out var targets);
        FieldBuilder targetField = tb.DefineField("_targets", typeof(object[]), Priv | FieldAttributes.Static);

        EmitCollapsedRead(tb, stateInterface, valueField, liveField, readValue, targetField);
        EmitReturnField(tb, stateInterface, "Build", valueField, resultType);

        var created = tb.CreateType();
        Require(created.GetField("_targets", BindingFlags.NonPublic | BindingFlags.Static), "_targets").SetValue(null, targets);
        return Close<T>(created, cols, BehaviorFor(rd));
    }

    private enum SlotKind { Simple, SimpleBuffer, SubLevelBuffer, SubLevelDirect }

    private sealed class Slot {
        public SlotKind Kind;
        public FieldBuilder? Field;
        public MethodBuilder? Reader;
        public Type? ElementType;
        public MethodBase? Construct;
        public MethodBase? InitialState;
        public MethodInfo? Add;
        public Level? SubLevel;
        public MemberInfo? Member;
        public FieldBuilder? TargetsField;
        public object[]? Targets;
        public INullColHandler? NullRule;
        public bool CanCollapse;
        public bool IsBuffer => Kind is SlotKind.SimpleBuffer or SlotKind.SubLevelBuffer;
    }

    private sealed class BoundaryBuild(TypeBuilder tb, ColumnInfo[] cols, string tag) : IBoundaryBuild {
        private int next;
        public IBoundaryReader Reader(DbItemPlan reader, Type type) => new ReaderHandle(tb, reader, cols, $"Key_{tag}_{next++}", type);
        public IBoundaryField Field(Type type) => new FieldHandle(tb.DefineField($"_{tag}_k{next++}", type, Priv));
    }

    private sealed class ReaderHandle : IBoundaryReader {
        private readonly MethodBuilder Method;
        private readonly FieldBuilder TargetsField;
        public Type Type { get; }
        public int? Column { get; }
        public (FieldInfo Field, object[] Targets) Targets { get; }
        public ReaderHandle(TypeBuilder tb, DbItemPlan reader, ColumnInfo[] cols, string name, Type type) {
            Type = type;
            Method = EmitConstruct(tb, reader, cols, name, type, out var targets);
            Column = reader is IColumnOrdinalPlan ordinal ? ordinal.ColumnOrdinal : null;
            TargetsField = tb.DefineField($"_tgts_{name}", typeof(object[]), Priv | FieldAttributes.Static);
            Targets = (TargetsField, targets);
        }
        public void EmitRead(Generator g) {
            EmitTargets(g, TargetsField);
            g.Emit(OpCodes.Ldarg_1);
            g.Emit(OpCodes.Call, Method);
        }
    }

    private sealed class FieldHandle(FieldBuilder field) : IBoundaryField {
        public void EmitThis(Generator g) => g.Emit(OpCodes.Ldarg_0);
        public void EmitLoad(Generator g) {
            g.Emit(OpCodes.Ldarg_0);
            g.Emit(OpCodes.Ldfld, field);
        }
        public void EmitStore(Generator g) => g.Emit(OpCodes.Stfld, field);
    }

    private sealed class Level {
        public Type? ResultType;
        public MethodBase? Construction;
        public Slot[]? CtorSlots;
        public List<Slot> MemberSlots = [];
        public GroupingBoundary? Boundary;
        public List<Slot> Simples = [];
        public List<Slot> SimpleBuffers = [];
        public bool IsMaster;
        public FieldBuilder? Live;
        public FieldBuilder? ParentBuffer;
        public Level? Child;
        public MethodInfo? ParentAdd;
        public MethodBuilder? CloseMethod;
        public Level? CaptureInto;
        public bool BuildsFromBuffer => Construction is null;
    }

    private static ITypeParser<T> BuildSpine<T>(DbItemPlan rd, ColumnInfo[] cols) {
        if (rd is not ICompositeDbItemPlan root)
            throw Unsupported($"a multi-row root of shape {rd.GetType().Name}");
        var (tb, stateInterface) = BeginState(typeof(T));
        var master = BuildLevel(root, tb, cols, isMaster: true, parentBuffer: null, tag: "0");
        return Close<T>(Assemble(tb, stateInterface, typeof(T), master), cols, BehaviorFor(rd));
    }

    private static void SetTargetsFields(Type created, Level level) {
        foreach (var s in Require(level.CtorSlots, nameof(level.CtorSlots)).Concat(level.MemberSlots)) {
            if (s.TargetsField is { } targetsField)
                Require(created.GetField(targetsField.Name, BindingFlags.NonPublic | BindingFlags.Static), targetsField.Name).SetValue(null, Require(s.Targets, nameof(s.Targets)));
            if (s.Kind == SlotKind.SubLevelDirect)
                SetTargetsFields(created, Require(s.SubLevel, nameof(s.SubLevel)));
        }
        if (level.Boundary is not null)
            foreach (var (field, targets) in level.Boundary.Targets)
                Require(created.GetField(field.Name, BindingFlags.NonPublic | BindingFlags.Static), field.Name).SetValue(null, targets);
        if (level.Child is not null)
            SetTargetsFields(created, level.Child);
    }

    private static void EmitAdd(ILGenerator il, MethodInfo add) {
        il.Emit(add.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, add);
        if (add.ReturnType != typeof(void))
            il.Emit(OpCodes.Pop);
    }

    private static void EmitLoadBuffer(ILGenerator il, FieldBuilder field) {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(field.FieldType.IsValueType ? OpCodes.Ldflda : OpCodes.Ldfld, field);
    }

    private static void EmitTargets(ILGenerator il, FieldInfo targetsField) => il.Emit(OpCodes.Ldsfld, targetsField);

    private static Level BuildLevel(ICompositeDbItemPlan node, TypeBuilder tb, ColumnInfo[] cols, bool isMaster, FieldBuilder? parentBuffer, string tag, Level? hoistTo = null) {
        StateTypeAssembly.AllowAccessTo(node.ResultType);
        var construction = node.Construction;
        var ctorParams = construction.GetParameters();
        var args = node.ConstructorArguments;
        var level = new Level { ResultType = node.ResultType, Construction = construction, IsMaster = isMaster, ParentBuffer = parentBuffer };
        level.CaptureInto = hoistTo ?? level;
        level.CtorSlots = new Slot[args.Count];
        for (int i = 0; i < args.Count; i++)
            level.CtorSlots[i] = ClassifySlot(args[i], ctorParams[i].ParameterType, member: null, tb, cols, level, $"{tag}_a{i}");
        foreach (var (member, plan) in node.PostMembers)
            level.MemberSlots.Add(ClassifySlot(plan, MemberValueType(member), member, tb, cols, level, $"{tag}_m{level.MemberSlots.Count}"));

        if (hoistTo is not null) return level;

        level.Boundary = BuildBoundary(node, tb, cols, tag);
        if (level.Simples.Count > 0 || level.Boundary.Captures)
            level.Live = tb.DefineField($"_{tag}_live", typeof(bool), Priv);
        if (!isMaster)
            level.CloseMethod = tb.DefineMethod($"Close_{tag}",
                MethodAttributes.Private | MethodAttributes.HideBySig, typeof(void), Type.EmptyTypes);
        return level;
    }

    private static GroupingBoundary BuildBoundary(ICompositeDbItemPlan node, TypeBuilder tb, ColumnInfo[] cols, string tag) {
        var build = new BoundaryBuild(tb, cols, tag);
        var rules = node.GroupingRules;
        for (int i = 0; i < rules.Count; i++) {
            var boundary = rules[i].MakeBoundary(node.ResultType, cols, node.Context, build);
            if (boundary is not null)
                return boundary;
        }
        return new InferredGroupingRule(node.ConstructorArguments, (MethodBase)node.Construction, node.ResultType)
            .MakeBoundary(node.ResultType, cols, node.Context, build)
            ?? throw new RinkuInternalException(ErrorCodes.InternalInvariant, "Grouping rules did not produce a boundary.");
    }

    private static Slot ClassifySlot(DbItemPlan plan, Type slotType, MemberInfo? member, TypeBuilder tb, ColumnInfo[] cols, Level level, string tag) {
        var into = Require(level.CaptureInto, nameof(level.CaptureInto));
        Slot slot;
        if (plan is IMultiRowPlan acc) {
            var buffer = tb.DefineField($"_b{tag}", acc.BufferType, Priv);
            StateTypeAssembly.AllowAccessTo(acc.ElementType);
            if (DbItemPlan.AllSimple(acc.Element)) {
                slot = new Slot {
                    Kind = SlotKind.SimpleBuffer, Field = buffer, ElementType = acc.ElementType, Construct = acc.Construct, InitialState = acc.InitialState, Add = acc.AddMethod, Member = member, NullRule = acc.NullRule,
                    Reader = EmitTryReadElement(tb, acc.Element, acc.ElementType, cols, tag, out var targets),
                    CanCollapse = acc.Element.NeedNullSetPoint(cols),
                };
                CaptureTargets(slot, targets, tb, tag);
                into.SimpleBuffers.Add(slot);
            }
            else if (acc.Element is ICompositeDbItemPlan sub) {
                if (into.Child is not null)
                    throw Unsupported("two sibling collections that both span rows in one nested element");
                var subLevel = BuildLevel(sub, tb, cols, isMaster: false, parentBuffer: buffer, tag: tag);
                subLevel.ParentAdd = acc.AddMethod;
                slot = new Slot {
                    Kind = SlotKind.SubLevelBuffer, Field = buffer, ElementType = acc.ElementType,
                    Construct = acc.Construct, InitialState = acc.InitialState, SubLevel = subLevel, Member = member,
                };
                into.Child = subLevel;
            }
            else
                throw Unsupported($"a spanning collection element of shape {acc.Element.GetType().Name}");
        }
        else if (DbItemPlan.AllSimple(plan)) {
            slot = new Slot {
                Kind = SlotKind.Simple, Field = tb.DefineField($"_s{tag}", slotType, Priv), Member = member,
                Reader = EmitConstruct(tb, plan, cols, $"Read_{tag}", slotType, out var targets),
            };
            CaptureTargets(slot, targets, tb, tag);
            into.Simples.Add(slot);
        }
        else if (plan is ICompositeDbItemPlan directSpanning) {
            var subLevel = BuildLevel(directSpanning, tb, cols, isMaster: false, parentBuffer: null, tag: tag, hoistTo: into);
            slot = new Slot { Kind = SlotKind.SubLevelDirect, SubLevel = subLevel, Member = member };
        }
        else
            throw Unsupported($"a nested spanning member of shape {plan.GetType().Name}");
        return slot;
    }

    private static void CaptureTargets(Slot slot, object[] targets, TypeBuilder tb, string tag) {
        slot.TargetsField = tb.DefineField($"_tgts_{tag}", typeof(object[]), Priv | FieldAttributes.Static);
        slot.Targets = targets;
    }

    private static Type MemberValueType(MemberInfo member) => member switch {
        PropertyInfo p => p.PropertyType,
        FieldInfo f => f.FieldType,
        MethodInfo m => m.GetParameters()[^1].ParameterType,
        _ => throw Unsupported($"a construction member of kind {member.MemberType}"),
    };

    private static IEnumerable<Slot> BufferSlots(Level level) {
        foreach (var s in Require(level.CtorSlots, nameof(level.CtorSlots)))
            foreach (var b in BuffersOf(s))
                yield return b;
        foreach (var s in level.MemberSlots)
            foreach (var b in BuffersOf(s))
                yield return b;
    }

    private static IEnumerable<Slot> BuffersOf(Slot slot) {
        if (slot.IsBuffer)
            yield return slot;
        else if (slot.Kind == SlotKind.SubLevelDirect)
            foreach (var b in BufferSlots(Require(slot.SubLevel, nameof(slot.SubLevel))))
                yield return b;
    }

    private static void EmitStateCtor(TypeBuilder tb, Level master) {
        var buffers = new List<Slot>();
        CollectBuffers(master, buffers);
        if (buffers.Count == 0)
            return;
        var ctor = tb.DefineConstructor(
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            CallingConventions.Standard, Type.EmptyTypes);
        var il = ctor.GetILGenerator();
        foreach (var s in buffers)
            EmitSeed(il, s);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitSeed(ILGenerator il, Slot slot) {
        il.Emit(OpCodes.Ldarg_0);
        var initialState = Require(slot.InitialState, nameof(slot.InitialState));
        if (initialState is ConstructorInfo ctor)
            il.Emit(OpCodes.Newobj, ctor);
        else
            il.Emit(OpCodes.Call, (MethodInfo)initialState);
        il.Emit(OpCodes.Stfld, Require(slot.Field, nameof(slot.Field)));
    }

    private static void CollectBuffers(Level level, List<Slot> into) {
        foreach (var s in BufferSlots(level))
            into.Add(s);
        if (level.Child is not null)
            CollectBuffers(level.Child, into);
    }

    private static void EmitCloseMethods(Level level) {
        if (level.Child is not null)
            EmitCloseMethods(level.Child);
        if (level.CloseMethod is null)
            return;
        var il = level.CloseMethod.GetILGenerator();
        EmitCascadeCloseChild(il, level);
        EmitLoadBuffer(il, Require(level.ParentBuffer, nameof(level.ParentBuffer)));
        EmitConstructNode(il, level);
        EmitAdd(il, Require(level.ParentAdd, nameof(level.ParentAdd)));
        foreach (var s in BufferSlots(level))
            EmitSeed(il, s);
        if (level.Live is not null) {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stfld, level.Live);
        }
        il.Emit(OpCodes.Ret);
    }

    private static void EmitCascadeCloseChild(ILGenerator il, Level level) {
        if (level.Child?.CloseMethod is null || level.Child.Live is null)
            return;
        var skip = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, level.Child.Live);
        il.Emit(OpCodes.Brfalse, skip);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, level.Child.CloseMethod);
        il.MarkLabel(skip);
    }

    private static void EmitConstructNode(ILGenerator il, Level level) {
        foreach (var s in Require(level.CtorSlots, nameof(level.CtorSlots)))
            EmitLoadSlotValue(il, s);
        var construction = Require(level.Construction, nameof(level.Construction));
        if (construction is ConstructorInfo ctor)
            il.Emit(OpCodes.Newobj, ctor);
        else
            il.Emit(OpCodes.Call, (MethodInfo)construction);
        if (level.MemberSlots.Count == 0)
            return;
        var resultType = Require(level.ResultType, nameof(level.ResultType));
        var instance = il.DeclareLocal(resultType);
        il.Emit(OpCodes.Stloc, instance);
        var gen = Wrap(il);
        var load = resultType.IsValueType ? OpCodes.Ldloca : OpCodes.Ldloc;
        foreach (var s in level.MemberSlots) {
            il.Emit(load, instance);
            EmitLoadSlotValue(il, s);
            DbItemPlan.EmitMemberDispatch(gen, Require(s.Member, nameof(s.Member)));
        }
        il.Emit(OpCodes.Ldloc, instance);
    }

    private static void EmitLoadSlotValue(ILGenerator il, Slot slot) {
        if (slot.Kind == SlotKind.SubLevelDirect) {
            EmitConstructNode(il, Require(slot.SubLevel, nameof(slot.SubLevel)));
            return;
        }
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, Require(slot.Field, nameof(slot.Field)));
        if (slot.IsBuffer && slot.Construct is not null)
            DbItemPlan.EmitMemberDispatch(Wrap(il), slot.Construct);
    }

    private static Generator Wrap(ILGenerator il) => new(il);

    private static MethodBuilder EmitRead(TypeBuilder tb, Type stateInterface, Level master) {
        var mb = tb.DefineMethod("Read", InterfaceMethod, typeof(bool), [typeof(DbDataReader)]);
        var il = mb.GetILGenerator();
        EmitLevelRead(il, master);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        tb.DefineMethodOverride(mb, Require(stateInterface.GetMethod("Read"), "Read"));
        return mb;
    }

    private static void EmitLevelRead(ILGenerator il, Level level) {
        Label absent = default;
        bool gated = !level.IsMaster && level.Boundary?.PresenceColumn is int;
        if (gated) {
            absent = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4, level.Boundary?.PresenceColumn
                ?? throw new RinkuInternalException(ErrorCodes.InternalInvariant, "A gated level had no presence column."));
            il.Emit(OpCodes.Callvirt, TypeExtensions.IsNull);
            il.Emit(OpCodes.Brtrue, absent);
        }

        if (level.Live is not null) {
            var afterCapture = il.DefineLabel();
            var afterCompare = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, level.Live);
            il.Emit(OpCodes.Brfalse, afterCompare);
            if (level.Boundary is { CanChange: true } boundary) {
                var changed = il.DefineLabel();
                boundary.EmitCompare(Wrap(il), changed);
                if (level.IsMaster) {
                    il.Emit(OpCodes.Br, afterCapture);
                    il.MarkLabel(changed);
                    il.Emit(OpCodes.Ldc_I4_0);
                    il.Emit(OpCodes.Ret);
                }
                else {
                    il.Emit(OpCodes.Br, afterCompare);
                    il.MarkLabel(changed);
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Call, Require(level.CloseMethod, nameof(level.CloseMethod)));
                }
            }
            else
                il.Emit(OpCodes.Br, afterCapture);
            il.MarkLabel(afterCompare);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, level.Live);
            il.Emit(OpCodes.Brtrue, afterCapture);
            if (level.Boundary is { Captures: true } capturing)
                capturing.EmitCapture(Wrap(il));
            foreach (var s in level.Simples) {
                var targetsField = Require(s.TargetsField, nameof(s.TargetsField));
                var reader = Require(s.Reader, nameof(s.Reader));
                var field = Require(s.Field, nameof(s.Field));
                il.Emit(OpCodes.Ldarg_0);
                EmitTargets(il, targetsField);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, reader);
                il.Emit(OpCodes.Stfld, field);
            }
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Stfld, level.Live);
            il.MarkLabel(afterCapture);
        }

        foreach (var b in level.SimpleBuffers) {
            var elementType = Require(b.ElementType, nameof(b.ElementType));
            var targetsField = Require(b.TargetsField, nameof(b.TargetsField));
            var reader = Require(b.Reader, nameof(b.Reader));
            var field = Require(b.Field, nameof(b.Field));
            var element = il.DeclareLocal(elementType);
            EmitTargets(il, targetsField);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloca, element);
            il.Emit(OpCodes.Call, reader);
            Label done = default;
            if (b.CanCollapse) {
                var add = il.DefineLabel();
                done = il.DefineLabel();
                il.Emit(OpCodes.Brtrue, add);
                Require(b.NullRule, nameof(b.NullRule)).HandleNullForMultiRow(field.FieldType, elementType, "element", element, Wrap(il), new(done, 0));
                il.MarkLabel(add);
            }
            else
                il.Emit(OpCodes.Pop);
            EmitLoadBuffer(il, field);
            il.Emit(OpCodes.Ldloc, element);
            EmitAdd(il, Require(b.Add, nameof(b.Add)));
            if (b.CanCollapse)
                il.MarkLabel(done);
        }

        if (level.Child is not null)
            EmitLevelRead(il, level.Child);

        if (gated)
            il.MarkLabel(absent);
    }

    private static MethodBuilder EmitBuild(TypeBuilder tb, Type stateInterface, Type resultType, Level master) {
        var mb = tb.DefineMethod("Build", InterfaceMethod, resultType, Type.EmptyTypes);
        var il = mb.GetILGenerator();
        EmitCascadeCloseChild(il, master);
        if (master.BuildsFromBuffer)
            EmitLoadSlotValue(il, Require(master.CtorSlots, nameof(master.CtorSlots))[0]);
        else
            EmitConstructNode(il, master);
        il.Emit(OpCodes.Ret);
        tb.DefineMethodOverride(mb, Require(stateInterface.GetMethod("Build"), "Build"));
        return mb;
    }

    private static MethodBuilder EmitConstruct(TypeBuilder tb, DbItemPlan node, ColumnInfo[] cols, string name, Type resultType, out object[] targets) {
        var mb = tb.DefineMethod(name, StateHelper, resultType, ReadValueArgs);
        Generator gen = new(mb.GetILGenerator());
        Label? nullJump = node.NeedNullSetPoint(cols) ? gen.DefineLabel() : null;
        ((ISimpleDbItemPlan)node).Emit(cols, gen, nullJump.HasValue ? new(nullJump.Value, 0) : default);
        targets = gen.GetTargets();
        if (nullJump.HasValue) {
            var parsed = gen.DefineLabel();
            gen.Emit(OpCodes.Br, parsed);
            gen.MarkLabel(nullJump.Value);
            DbItemPlan.EmitDefaultValue(resultType, gen);
            gen.MarkLabel(parsed);
        }
        gen.Emit(OpCodes.Ret);
        return mb;
    }

    private static MethodBuilder EmitTryReadElement(TypeBuilder tb, DbItemPlan element, Type elementType, ColumnInfo[] cols, string tag, out object[] targets) {
        var mb = tb.DefineMethod($"Elem_{tag}", StateHelper, typeof(bool),
            [typeof(object[]), typeof(DbDataReader), elementType.MakeByRefType()]);
        mb.DefineParameter(3, ParameterAttributes.Out, "value");
        Generator gen = new(mb.GetILGenerator());
        bool collapses = element.NeedNullSetPoint(cols);
        Label collapse = gen.DefineLabel();
        ((ISimpleDbItemPlan)element).Emit(cols, gen, collapses ? new(collapse, 0) : default);
        targets = gen.GetTargets();
        var tmp = gen.DeclareLocal(elementType);
        gen.Emit(OpCodes.Stloc, tmp);
        gen.Emit(OpCodes.Ldarg_2);
        gen.Emit(OpCodes.Ldloc, tmp);
        gen.Emit(OpCodes.Stobj, elementType);
        gen.Emit(OpCodes.Ldc_I4_1);
        gen.Emit(OpCodes.Ret);
        if (collapses) {
            gen.MarkLabel(collapse);
            gen.Emit(OpCodes.Ldarg_2);
            gen.Emit(OpCodes.Initobj, elementType);
            gen.Emit(OpCodes.Ldc_I4_0);
            gen.Emit(OpCodes.Ret);
        }
        return mb;
    }

    private static void EmitCollapsedRead(TypeBuilder tb, Type stateInterface, FieldInfo valueField, FieldInfo liveField, MethodInfo readValue, FieldInfo targetField) {
        var mb = tb.DefineMethod("Read", InterfaceMethod, typeof(bool), [typeof(DbDataReader)]);
        var il = mb.GetILGenerator();
        var notLive = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, liveField);
        il.Emit(OpCodes.Brfalse, notLive);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notLive);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, targetField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, readValue);
        il.Emit(OpCodes.Stfld, valueField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, liveField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        tb.DefineMethodOverride(mb, Require(stateInterface.GetMethod("Read"), "Read"));
    }

    private static void EmitReturnField(TypeBuilder tb, Type stateInterface, string name, FieldInfo field, Type resultType) {
        var mb = tb.DefineMethod(name, InterfaceMethod, resultType, Type.EmptyTypes);
        var il = mb.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, field);
        il.Emit(OpCodes.Ret);
        tb.DefineMethodOverride(mb, Require(stateInterface.GetMethod(name), name));
    }

    private static CommandBehavior BehaviorFor(DbItemPlan plan) {
        var previousOrdinal = -1;
        return CommandBehavior.SingleResult | (plan.IsSequencial(ref previousOrdinal) ? CommandBehavior.SequentialAccess : 0);
    }

    private static ITypeParser<T> Close<T>(Type created, ColumnInfo[] schema, CommandBehavior behavior, bool collectionRoot = false) {
        var driver = (collectionRoot ? typeof(MultiRowCollectionTypeParser<,>) : typeof(MultiRowTypeParser<,>)).MakeGenericType(typeof(T), created);
        return (ITypeParser<T>)(Activator.CreateInstance(driver, [schema, behavior])
            ?? throw new RinkuInternalException(ErrorCodes.InternalInvariant, "The generated parser driver could not be instantiated."));
    }

    private static string StateName(Type resultType) => $"State_{resultType.Name}_{Guid.NewGuid():N}";

    private static RinkuConfigurationException Unsupported(string what)
        => new(ErrorCodes.OperationNotSupportedForType, $"multi-row mapping does not support {what}");
}
