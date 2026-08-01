using System.Data.Common;
using System.Reflection;
using System.Reflection.Emit;
using RinkuLib.TypeAccessing;
using RinkuLib.Tools;

namespace RinkuLib.DbParsing;

/// <summary>
/// Emits the state struct for a multi-row plan and closes <see cref="MultiRowTypeParser{T, TState}"/> over it.
/// The struct implements <see cref="IMultiRowState{T}"/>, reuses the single-row <see cref="SimpleDbItemParser.Emit"/>
/// for every collapsed subtree by pointing its <see cref="Generator"/> at a method on the struct, and reaches
/// non-public members through the <see cref="StateTypeAssembly"/> access-check bypass. A fully-simple plan
/// collapses to one value slot; a spanning plan lays out one level per grouping node, folding rows top-down and
/// closing bottom-up.
/// </summary>
internal static class MultiRowEmitter {
    private static readonly Type[] ReadValueArgs = [typeof(object), typeof(DbDataReader)];
    private const MethodAttributes InterfaceMethod =
        MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final
        | MethodAttributes.HideBySig | MethodAttributes.NewSlot;
    private const MethodAttributes StateHelper = MethodAttributes.Private | MethodAttributes.Static;
    private const FieldAttributes Priv = FieldAttributes.Private;

    /// <summary>Builds the multi-row parser for <typeparamref name="T"/> from its negotiated plan.</summary>
    internal static ITypeParser<T> Build<T>(DbItemPlan rd, ColumnInfo[] cols)
        => DbItemPlan.AllSimple(rd) ? BuildCollapsed<T>(rd, cols)
         : rd is AccumulatorPlan acc ? BuildCollectionRoot<T>(acc, cols)
         : BuildSpine<T>(rd, cols);

    /// <summary>
    /// The top-level collection case: the result itself is a registered collection, so the state accumulates
    /// every element, grouping a spanning element the same way a nested collection does, and <c>Build</c>
    /// finishes the buffer into the declared collection. This is what lets a query ask for a registered
    /// collection at the top without a dedicated parser. The boundary never changes, so the read runs to the
    /// end of the rows.
    /// </summary>
    private static ITypeParser<T> BuildCollectionRoot<T>(AccumulatorPlan acc, ColumnInfo[] cols) {
        var (tb, stateInterface) = BeginState(typeof(T));
        var master = new Level { ResultType = typeof(T), IsMaster = true, Boundary = AlwaysGroupedBoundary.Instance };
        master.CaptureInto = master;
        master.CtorSlots = [ClassifySlot(acc, typeof(T), member: null, tb, cols, master, "0")];
        return Close<T>(Assemble(tb, stateInterface, typeof(T), master), collection: true);
    }

    /// <summary>Defines a fresh state struct for <paramref name="resultType"/> and wires its interface, the head every build path shares.</summary>
    private static (TypeBuilder Tb, Type StateInterface) BeginState(Type resultType) {
        StateTypeAssembly.AllowAccessTo(resultType.Assembly);
        var tb = StateTypeAssembly.DefineState(StateName(resultType));
        var stateInterface = typeof(IMultiRowState<>).MakeGenericType(resultType);
        tb.AddInterfaceImplementation(stateInterface);
        return (tb, stateInterface);
    }

    /// <summary>Emits the level-driven struct body (ctor, closes, read, build), bakes the type, and binds its targets.</summary>
    private static Type Assemble(TypeBuilder tb, Type stateInterface, Type resultType, Level master) {
        EmitStateCtor(tb, master);
        EmitCloseMethods(master);
        EmitRead(tb, stateInterface, master);
        EmitBuild(tb, stateInterface, resultType, master);
        var created = tb.CreateType();
        SetTargetFields(created, master);
        return created;
    }

    /// <summary>
    /// The one-slot case: a plan with no accumulator collapses to a single value slot whose <c>Read</c> fills
    /// it from the group's first row and whose <c>Build</c> returns it, reproducing the single-row parse.
    /// </summary>
    private static ITypeParser<T> BuildCollapsed<T>(DbItemPlan rd, ColumnInfo[] cols) {
        var resultType = typeof(T);
        var (tb, stateInterface) = BeginState(resultType);
        var valueField = tb.DefineField("_v", resultType, Priv);
        var liveField = tb.DefineField("_live", typeof(bool), Priv);
        var readValue = EmitConstruct(tb, rd, cols, "ReadValue", resultType, out var targetObj);
        FieldBuilder? targetField = targetObj is null
            ? null
            : tb.DefineField("_target", typeof(object), Priv | FieldAttributes.Static);

        EmitCollapsedRead(tb, stateInterface, valueField, liveField, readValue, targetField);
        EmitReturnField(tb, stateInterface, "Build", valueField, resultType);

        var created = tb.CreateType();
        if (targetField is not null)
            created.GetField("_target", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, targetObj);
        return Close<T>(created);
    }

    private enum SlotKind { Simple, SimpleBuffer, SubLevelBuffer, SubLevelDirect }

    private sealed class Slot {
        public SlotKind Kind;
        public FieldBuilder Field = null!;
        public MethodBuilder Reader = null!;
        public Type ElementType = null!;
        public MethodBase? Construct;
        public MethodBase InitialState = null!;
        public MethodInfo? Add;
        public Level SubLevel = null!;
        public MemberInfo? Member;
        public FieldBuilder? TargetField;
        public object? Target;
        public INullColHandler? NullRule;
        public bool IsBuffer => Kind is SlotKind.SimpleBuffer or SlotKind.SubLevelBuffer;
    }

    /// <summary>
    /// The build surface a maker lowers its negotiated key through: it compiles readers into state helpers and
    /// defines the state fields that hold the key across rows, all on the level's state builder.
    /// </summary>
    private sealed class BoundaryBuild(TypeBuilder tb, ColumnInfo[] cols, string tag) : IBoundaryBuild {
        private int next;
        public IBoundaryReader Reader(DbItemPlan reader, Type type) => new ReaderHandle(tb, reader, cols, $"Key_{tag}_{next++}", type);
        public IBoundaryField Field(Type type) => new FieldHandle(tb.DefineField($"_{tag}_k{next++}", type, Priv));
    }

    /// <summary>A key reader compiled into a state helper, invoked as <c>helper(target, reader)</c>.</summary>
    private sealed class ReaderHandle : IBoundaryReader {
        private readonly MethodBuilder Method;
        private readonly FieldBuilder? TargetField;
        public Type Type { get; }
        public int? Column { get; }
        public (FieldInfo Field, object Target)? Target { get; }
        public ReaderHandle(TypeBuilder tb, DbItemPlan reader, ColumnInfo[] cols, string name, Type type) {
            Type = type;
            Method = EmitConstruct(tb, reader, cols, name, type, out var target);
            Column = reader is BasicParser bp ? bp.ColumnIndex : null;
            if (target is not null) {
                TargetField = tb.DefineField($"_tgt_{name}", typeof(object), Priv | FieldAttributes.Static);
                Target = (TargetField, target);
            }
        }
        public void EmitRead(Generator g) {
            EmitTarget(g, TargetField);
            g.Emit(OpCodes.Ldarg_1);
            g.Emit(OpCodes.Call, Method);
        }
    }

    /// <summary>A per-instance state field on the state struct, loaded and stored through <c>arg0</c>.</summary>
    private sealed class FieldHandle(FieldBuilder field) : IBoundaryField {
        public void EmitThis(Generator g) => g.Emit(OpCodes.Ldarg_0);
        public void EmitLoad(Generator g) {
            g.Emit(OpCodes.Ldarg_0);
            g.Emit(OpCodes.Ldfld, field);
        }
        public void EmitStore(Generator g) => g.Emit(OpCodes.Stfld, field);
    }

    private sealed class Level {
        public Type ResultType = null!;
        public ConstructorInfo Ctor = null!;
        public Slot[] CtorSlots = null!;
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
        public Level CaptureInto = null!;
        /// <summary>A collection root has no constructor; its <c>Build</c> finishes its single buffer slot instead of invoking one.</summary>
        public bool BuildsFromBuffer => Ctor is null;
    }

    /// <summary>
    /// Builds the spine and emits its state struct. The master level owns <c>Read</c>/<c>Build</c>; every sub
    /// level owns a <c>Close</c> that builds its instance, appends it to its parent's buffer, and resets, which
    /// is the flush cascade the master's boundary switches and end-of-data both call.
    /// </summary>
    private static ITypeParser<T> BuildSpine<T>(DbItemPlan rd, ColumnInfo[] cols) {
        if (rd is not CustomClassParser root)
            throw NotYet($"a multi-row root of shape {rd.GetType().Name}");
        var (tb, stateInterface) = BeginState(typeof(T));
        var master = BuildLevel(root, tb, cols, isMaster: true, parentBuffer: null, tag: "0");
        return Close<T>(Assemble(tb, stateInterface, typeof(T), master));
    }

    /// <summary>After the type is baked, fills the static field that holds each construction's bound target (a DynaObject mapper).</summary>
    private static void SetTargetFields(Type created, Level level) {
        foreach (var s in level.CtorSlots.Concat(level.MemberSlots)) {
            if (s.TargetField is not null)
                created.GetField(s.TargetField.Name, BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, s.Target);
            if (s.Kind == SlotKind.SubLevelDirect)
                SetTargetFields(created, s.SubLevel);
        }
        if (level.Boundary is not null)
            foreach (var (field, target) in level.Boundary.Targets)
                created.GetField(field.Name, BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, target);
        if (level.Child is not null)
            SetTargetFields(created, level.Child);
    }

    /// <summary>Emits the accumulator's <c>Add</c> on the never-null buffer, non-virtual when it can be, discarding a non-void return so a set filled in place folds like a list.</summary>
    private static void EmitAdd(ILGenerator il, MethodInfo add) {
        il.Emit(add.IsVirtual ? OpCodes.Callvirt : OpCodes.Call, add);
        if (add.ReturnType != typeof(void))
            il.Emit(OpCodes.Pop);
    }

    private static void EmitTarget(ILGenerator il, FieldInfo? targetField) {
        if (targetField is null)
            il.Emit(OpCodes.Ldnull);
        else
            il.Emit(OpCodes.Ldsfld, targetField);
    }

    private static Level BuildLevel(CustomClassParser node, TypeBuilder tb, ColumnInfo[] cols, bool isMaster, FieldBuilder? parentBuffer, string tag, Level? hoistTo = null) {
        StateTypeAssembly.AllowAccessTo(node.ResultType.Assembly);
        if (node.Construction is not ConstructorInfo ctor)
            throw NotYet($"a spanning construction that is not a constructor ({node.Construction.GetType().Name})");

        var ctorParams = ctor.GetParameters();
        var args = node.ConstructorArguments;
        var level = new Level { ResultType = node.ResultType, Ctor = ctor, IsMaster = isMaster, ParentBuffer = parentBuffer };
        level.CaptureInto = hoistTo ?? level;
        level.CtorSlots = new Slot[args.Count];
        for (int i = 0; i < args.Count; i++)
            level.CtorSlots[i] = ClassifySlot(args[i], ctorParams[i].ParameterType, member: null, tb, cols, level, $"{tag}_a{i}");
        foreach (var (member, plan) in node.PostMembers)
            level.MemberSlots.Add(ClassifySlot(plan, MemberValueType(member), member, tb, cols, level, $"{tag}_m{level.MemberSlots.Count}"));

        if (hoistTo is not null) {
            if (node.GroupKey is not null)
                throw NotYet("a group key on a nested object that is not a collection element");
            return level;
        }

        level.Boundary = BuildBoundary(node, tb, cols, tag);
        if (level.Simples.Count > 0 || level.Boundary.Captures)
            level.Live = tb.DefineField($"_{tag}_live", typeof(bool), Priv);
        if (!isMaster)
            level.CloseMethod = tb.DefineMethod($"Close_{tag}",
                MethodAttributes.Private | MethodAttributes.HideBySig, typeof(void), Type.EmptyTypes);
        return level;
    }

    /// <summary>
    /// Negotiates the level's boundary: the maker lowers its key through a <see cref="BoundaryBuild"/> into the
    /// emitter, always-grouped for a tuple, else a build-time throw. The emitter never inspects a concrete
    /// boundary; the member key, the method, and the always-grouped boundary all reach emit through
    /// <see cref="GroupingBoundary"/>.
    /// </summary>
    private static GroupingBoundary BuildBoundary(CustomClassParser node, TypeBuilder tb, ColumnInfo[] cols, string tag)
        => (node.GroupKey ?? new InferredGroupingRule(node.ConstructorArguments, (MethodBase)node.Construction, node.ResultType))
            .MakeBoundary(node.ResultType, cols, node.Context, new BoundaryBuild(tb, cols, tag));

    /// <summary>
    /// Turns one construction slot, a constructor argument or a settable member, into a state slot. A simple
    /// value collapses to one field captured once; an accumulator becomes a buffer, either of simple elements
    /// or of a nested spanning sub level. A member slot carries the member it is assigned to at construction.
    /// </summary>
    private static Slot ClassifySlot(DbItemPlan plan, Type slotType, MemberInfo? member, TypeBuilder tb, ColumnInfo[] cols, Level level, string tag) {
        var into = level.CaptureInto;
        Slot slot;
        if (plan is AccumulatorPlan acc) {
            var buffer = tb.DefineField($"_b{tag}", acc.BufferType, Priv);
            StateTypeAssembly.AllowAccessTo(acc.ElementType.Assembly);
            if (DbItemPlan.AllSimple(acc.Element)) {
                slot = new Slot {
                    Kind = SlotKind.SimpleBuffer, Field = buffer, ElementType = acc.ElementType, Construct = acc.Construct, InitialState = acc.InitialState, Add = acc.AddMethod, Member = member, NullRule = acc.NullRule,
                    Reader = EmitTryReadElement(tb, acc.Element, acc.ElementType, cols, tag, out var tobj),
                };
                CaptureTarget(slot, tobj, tb, tag);
                into.SimpleBuffers.Add(slot);
            }
            else if (acc.Element is CustomClassParser sub) {
                if (into.Child is not null)
                    throw NotYet("two sibling collections that both span rows (a cross-product)");
                var subLevel = BuildLevel(sub, tb, cols, isMaster: false, parentBuffer: buffer, tag: tag);
                subLevel.ParentAdd = acc.AddMethod;
                slot = new Slot {
                    Kind = SlotKind.SubLevelBuffer, Field = buffer, ElementType = acc.ElementType,
                    Construct = acc.Construct, InitialState = acc.InitialState, SubLevel = subLevel, Member = member,
                };
                into.Child = subLevel;
            }
            else
                throw NotYet($"a spanning collection element of shape {acc.Element.GetType().Name}");
        }
        else if (DbItemPlan.AllSimple(plan)) {
            slot = new Slot {
                Kind = SlotKind.Simple, Field = tb.DefineField($"_s{tag}", slotType, Priv), Member = member,
                Reader = EmitConstruct(tb, plan, cols, $"Read_{tag}", slotType, out var tobj),
            };
            CaptureTarget(slot, tobj, tb, tag);
            into.Simples.Add(slot);
        }
        else if (plan is CustomClassParser directSpanning) {
            var subLevel = BuildLevel(directSpanning, tb, cols, isMaster: false, parentBuffer: null, tag: tag, hoistTo: into);
            slot = new Slot { Kind = SlotKind.SubLevelDirect, SubLevel = subLevel, Member = member };
        }
        else
            throw NotYet($"a nested spanning member of shape {plan.GetType().Name}");
        return slot;
    }

    private static void CaptureTarget(Slot slot, object? targetObj, TypeBuilder tb, string tag) {
        if (targetObj is null)
            return;
        slot.TargetField = tb.DefineField($"_tgt_{tag}", typeof(object), Priv | FieldAttributes.Static);
        slot.Target = targetObj;
    }

    private static Type MemberValueType(MemberInfo member) => member switch {
        PropertyInfo p => p.PropertyType,
        FieldInfo f => f.FieldType,
        MethodInfo m => m.GetParameters()[^1].ParameterType,
        _ => throw NotYet($"a construction member of kind {member.MemberType}"),
    };

    /// <summary>Every buffer this level owns, reaching through inlined direct sub-objects whose buffers live on their own slots.</summary>
    private static IEnumerable<Slot> BufferSlots(Level level) {
        foreach (var s in level.CtorSlots)
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
            foreach (var b in BufferSlots(slot.SubLevel))
                yield return b;
    }

    /// <summary>Emits the parameterless struct constructor that pre-fills every buffer across all levels.</summary>
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

    /// <summary>Emits seeding one buffer field with its accumulator's construction, a <c>Newobj</c> for a constructor or a <c>Call</c> for a static factory.</summary>
    private static void EmitSeed(ILGenerator il, Slot slot) {
        il.Emit(OpCodes.Ldarg_0);
        if (slot.InitialState is ConstructorInfo ctor)
            il.Emit(OpCodes.Newobj, ctor);
        else
            il.Emit(OpCodes.Call, (MethodInfo)slot.InitialState);
        il.Emit(OpCodes.Stfld, slot.Field);
    }

    private static void CollectBuffers(Level level, List<Slot> into) {
        foreach (var s in BufferSlots(level))
            into.Add(s);
        if (level.Child is not null)
            CollectBuffers(level.Child, into);
    }

    /// <summary>Emits each sub level's <c>Close</c>: cascade-close its own child, build its instance, append it to the parent buffer, reset.</summary>
    private static void EmitCloseMethods(Level level) {
        if (level.Child is not null)
            EmitCloseMethods(level.Child);
        if (level.CloseMethod is null)
            return;
        var il = level.CloseMethod.GetILGenerator();
        EmitCascadeCloseChild(il, level);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, level.ParentBuffer!);
        EmitConstructNode(il, level);
        EmitAdd(il, level.ParentAdd!);
        foreach (var s in BufferSlots(level))
            EmitSeed(il, s);
        if (level.Live is not null) {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Stfld, level.Live);
        }
        il.Emit(OpCodes.Ret);
    }

    /// <summary>Emits <c>if (child.live) child.Close();</c>, the head of the cascade.</summary>
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

    /// <summary>
    /// Emits the construction of a level's instance: load each constructor-argument slot in order (finishing
    /// buffers into their declared collection) and <c>newobj</c>, then assign each settable member from its
    /// slot. Leaves the built instance on the stack.
    /// </summary>
    private static void EmitConstructNode(ILGenerator il, Level level) {
        foreach (var s in level.CtorSlots)
            EmitLoadSlotValue(il, s);
        il.Emit(OpCodes.Newobj, level.Ctor);
        if (level.MemberSlots.Count == 0)
            return;
        var instance = il.DeclareLocal(level.ResultType);
        il.Emit(OpCodes.Stloc, instance);
        var gen = Wrap(il);
        var load = level.ResultType.IsValueType ? OpCodes.Ldloca : OpCodes.Ldloc;
        foreach (var s in level.MemberSlots) {
            il.Emit(load, instance);
            EmitLoadSlotValue(il, s);
            DbItemPlan.EmitMemberDispatch(gen, s.Member!);
        }
        il.Emit(OpCodes.Ldloc, instance);
    }

    /// <summary>
    /// Leaves a slot's value on the stack: a captured field (a buffer finished into its collection), or, for a
    /// nested object that is not a collection, that object built inline from its own hoisted slots.
    /// </summary>
    private static void EmitLoadSlotValue(ILGenerator il, Slot slot) {
        if (slot.Kind == SlotKind.SubLevelDirect) {
            EmitConstructNode(il, slot.SubLevel);
            return;
        }
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, slot.Field);
        if (slot.IsBuffer && slot.Construct is not null)
            DbItemPlan.EmitMemberDispatch(Wrap(il), slot.Construct);
    }

    private static Generator Wrap(ILGenerator il) =>
#if DEBUG
        new(il, []);
#else
        new(il);
#endif

    private static void EmitRead(TypeBuilder tb, Type stateInterface, Level master) {
        var mb = tb.DefineMethod("Read", InterfaceMethod, typeof(bool), [typeof(DbDataReader)]);
        var il = mb.GetILGenerator();
        EmitLevelRead(il, master);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        tb.DefineMethodOverride(mb, stateInterface.GetMethod("Read")!);
    }

    /// <summary>
    /// Emits one level's fold, then descends. The master ends the group (<c>return false</c>) when its key
    /// changes; a sub level closes itself within its parent (the cascade) when its key changes; each level
    /// captures its key and simple slots on its first row and appends its simple-element buffers every row.
    /// </summary>
    private static void EmitLevelRead(ILGenerator il, Level level) {
        Label absent = default;
        bool gated = !level.IsMaster && level.Boundary?.PresenceColumn is int;
        if (gated) {
            absent = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4, level.Boundary!.PresenceColumn!.Value);
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
                    il.Emit(OpCodes.Call, level.CloseMethod!);
                }
            }
            else {
                il.Emit(OpCodes.Br, afterCapture);
            }
            il.MarkLabel(afterCompare);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, level.Live);
            il.Emit(OpCodes.Brtrue, afterCapture);
            if (level.Boundary is { Captures: true } capturing)
                capturing.EmitCapture(Wrap(il));
            foreach (var s in level.Simples) {
                il.Emit(OpCodes.Ldarg_0);
                EmitTarget(il, s.TargetField);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, s.Reader);
                il.Emit(OpCodes.Stfld, s.Field);
            }
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Stfld, level.Live);
            il.MarkLabel(afterCapture);
        }

        foreach (var b in level.SimpleBuffers) {
            var element = il.DeclareLocal(b.ElementType);
            var add = il.DefineLabel();
            var done = il.DefineLabel();
            EmitTarget(il, b.TargetField);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloca, element);
            il.Emit(OpCodes.Call, b.Reader);
            il.Emit(OpCodes.Brtrue, add);
            b.NullRule!.HandleNullForMultiRow(b.Field.FieldType, b.ElementType, "element", element, Wrap(il), new(done, 0));
            il.MarkLabel(add);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, b.Field);
            il.Emit(OpCodes.Ldloc, element);
            EmitAdd(il, b.Add!);
            il.MarkLabel(done);
        }

        if (level.Child is not null)
            EmitLevelRead(il, level.Child);

        if (gated)
            il.MarkLabel(absent);
    }

    private static void EmitBuild(TypeBuilder tb, Type stateInterface, Type resultType, Level master) {
        var mb = tb.DefineMethod("Build", InterfaceMethod, resultType, Type.EmptyTypes);
        var il = mb.GetILGenerator();
        EmitCascadeCloseChild(il, master);
        if (master.BuildsFromBuffer)
            EmitLoadSlotValue(il, master.CtorSlots[0]);
        else
            EmitConstructNode(il, master);
        il.Emit(OpCodes.Ret);
        tb.DefineMethodOverride(mb, stateInterface.GetMethod("Build")!);
    }

    /// <summary>
    /// Emits a static construction method reusing the single-row emit verbatim: the reader stays <c>arg1</c>
    /// and the optional bound target is <c>arg0</c>, so <see cref="SimpleDbItemParser.Emit"/> writes byte-for
    /// -byte what the single-row road writes. The outer null jump lands on a <c>default</c> return.
    /// </summary>
    private static MethodBuilder EmitConstruct(TypeBuilder tb, DbItemPlan node, ColumnInfo[] cols, string name, Type resultType, out object? targetObj) {
        var mb = tb.DefineMethod(name, StateHelper, resultType, ReadValueArgs);
        Generator gen =
#if DEBUG
            new(mb.GetILGenerator(), cols);
#else
            new(mb.GetILGenerator());
#endif
        Label? nullJump = node.NeedNullSetPoint(cols) ? gen.DefineLabel() : null;
        ((SimpleDbItemParser)node).Emit(cols, gen, nullJump.HasValue ? new(nullJump.Value, 0) : default, out targetObj);
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

    /// <summary>
    /// Emits <c>bool TryReadElem(object, DbDataReader, out E)</c>: it reads one element with the single-row
    /// emit, and a collapse (an <c>[AbortOnNull]</c> null) lands on <c>value = default; return false</c> so
    /// the accumulator skips the add. The bool carries the signal for both reference and value-type elements.
    /// </summary>
    private static MethodBuilder EmitTryReadElement(TypeBuilder tb, DbItemPlan element, Type elementType, ColumnInfo[] cols, string tag, out object? targetObj) {
        var mb = tb.DefineMethod($"Elem_{tag}", StateHelper, typeof(bool),
            [typeof(object), typeof(DbDataReader), elementType.MakeByRefType()]);
        mb.DefineParameter(3, ParameterAttributes.Out, "value");
        Generator gen =
#if DEBUG
            new(mb.GetILGenerator(), cols);
#else
            new(mb.GetILGenerator());
#endif
        bool collapses = element.NeedNullSetPoint(cols);
        Label collapse = gen.DefineLabel();
        ((SimpleDbItemParser)element).Emit(cols, gen, collapses ? new(collapse, 0) : default, out targetObj);
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

    /// <summary>
    /// Emits the one-slot <c>Read</c>: fill the value slot from the first row and go live, return false on any
    /// later row so the driver stops without consuming it.
    /// </summary>
    private static void EmitCollapsedRead(TypeBuilder tb, Type stateInterface, FieldInfo valueField, FieldInfo liveField, MethodInfo readValue, FieldInfo? targetField) {
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
        if (targetField is null)
            il.Emit(OpCodes.Ldnull);
        else
            il.Emit(OpCodes.Ldsfld, targetField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, readValue);
        il.Emit(OpCodes.Stfld, valueField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, liveField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        tb.DefineMethodOverride(mb, stateInterface.GetMethod("Read")!);
    }

    private static void EmitReturnField(TypeBuilder tb, Type stateInterface, string name, FieldInfo field, Type resultType) {
        var mb = tb.DefineMethod(name, InterfaceMethod, resultType, Type.EmptyTypes);
        var il = mb.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, field);
        il.Emit(OpCodes.Ret);
        tb.DefineMethodOverride(mb, stateInterface.GetMethod(name)!);
    }

    /// <summary>Closes the driver over the baked state, the collection driver when the result is a top-level collection so no rows give an empty one.</summary>
    private static ITypeParser<T> Close<T>(Type created, bool collection = false) {
        var driver = (collection ? typeof(MultiRowCollectionTypeParser<,>) : typeof(MultiRowTypeParser<,>)).MakeGenericType(typeof(T), created);
        return (ITypeParser<T>)Activator.CreateInstance(driver)!;
    }

    private static string StateName(Type resultType) => $"State_{resultType.Name}_{Guid.NewGuid():N}";

    private static RinkuInternalException NotYet(string what)
        => new(ErrorCodes.InternalInvariant, $"multi-row emit does not yet support {what}");
}
