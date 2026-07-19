using System.Collections.Concurrent;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using RinkuLib.DbParsing;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.Tools;
using Xunit;

namespace RinkuLib.Tests.DbParsing;

/// <summary>
/// The <see cref="Generator"/> wrapper forwards every ILGenerator operation to the real generator, so
/// code emitted through it runs exactly as written; the debug build logs each instruction on the way.
/// </summary>
public class GeneratorTests {
    static readonly ColumnInfo[] Cols = [new("First", typeof(int), false)];

    static Generator Wrap(ILGenerator il) =>
#if DEBUG
        new(il, Cols);
#else
        new(il);
#endif

    static (Generator G, DynamicMethod M) Method(Type returnType, params Type[] args) {
        var method = new DynamicMethod($"gen_{Guid.NewGuid():N}", returnType, args, typeof(GeneratorTests).Module);
        return (Wrap(method.GetILGenerator()), method);
    }

    [Fact]
    public void Every_constant_width_flows_through_the_wrapper() {
        var (g, m) = Method(typeof(double));
        g.Emit(OpCodes.Ldc_I4_S, (byte)5); 
        g.Emit(OpCodes.Conv_R8);
        g.Emit(OpCodes.Ldc_I8, 3L);
        g.Emit(OpCodes.Conv_R8);
        g.Emit(OpCodes.Add);
        g.Emit(OpCodes.Ldc_R4, 2f);
        g.Emit(OpCodes.Conv_R8);
        g.Emit(OpCodes.Add);
        g.Emit(OpCodes.Ldc_R8, 0.5d);
        g.Emit(OpCodes.Add);
        g.Emit(OpCodes.Ret);
        Assert.Equal(10.5d, m.CreateDelegate<Func<double>>()());
    }

    [Fact]
    public void Locals_are_declared_reused_and_addressed_by_short_index() {
        var (g, m) = Method(typeof(int));
        var local = g.GetLocal(typeof(int));
        Assert.Same(local, g.GetLocal(typeof(int)));
        var pinnedHost = g.DeclareLocal(typeof(int[]), pinned: true);
        Assert.True(pinnedHost.IsPinned);

        g.Emit(OpCodes.Ldc_I4, 41);
        g.Emit(OpCodes.Stloc, local);
        g.Emit(OpCodes.Ldloc, (short)local.LocalIndex);
        g.Emit(OpCodes.Ldc_I4_1);
        g.Emit(OpCodes.Add);
        g.Emit(OpCodes.Ret);
        Assert.Equal(42, m.CreateDelegate<Func<int>>()());
        Assert.True(g.ILOffset > 0);
    }

    [Fact]
    public void A_switch_over_labels_lands_on_the_right_arm() {
        var (g, m) = Method(typeof(string), typeof(int));
        var zero = g.DefineLabel();
        var one = g.DefineLabel();
        var fallout = g.DefineLabel();
        g.Emit(OpCodes.Ldarg_0);
        g.Emit(OpCodes.Switch, new[] { zero, one });
        g.Emit(OpCodes.Br, fallout);
        g.MarkLabel(zero);
        g.Emit(OpCodes.Ldstr, "zero");
        g.Emit(OpCodes.Ret);
        g.MarkLabel(one);
        g.Emit(OpCodes.Ldstr, "one");
        g.Emit(OpCodes.Ret);
        g.MarkLabel(fallout);
        g.Emit(OpCodes.Ldstr, "other");
        g.Emit(OpCodes.Ret);
        var f = m.CreateDelegate<Func<int, string>>();
        Assert.Equal("zero", f(0));
        Assert.Equal("one", f(1));
        Assert.Equal("other", f(7));
    }

    [Fact]
    public void A_label_defined_outside_the_wrapper_is_still_usable() {
        var (g, m) = Method(typeof(int));
        var raw = g.Il.DefineLabel();
        g.Emit(OpCodes.Br_S, raw);
        g.MarkLabel(raw);
        g.Emit(OpCodes.Ldc_I4_3);
        g.Emit(OpCodes.Ret);
        Assert.Equal(3, m.CreateDelegate<Func<int>>()());
    }

    [Fact]
    public void Ldc_I4_logs_the_probable_column_for_small_indexes() {
        var (g, m) = Method(typeof(int));
        g.Emit(OpCodes.Ldc_I4, 0);
        g.Emit(OpCodes.Ldc_I4, 900);
        g.Emit(OpCodes.Add);
        g.Emit(OpCodes.Ret);
        Assert.Equal(900, m.CreateDelegate<Func<int>>()());
    }

    public struct PairHolder(int a) {
        public int A = a;
    }

    [Fact]
    public void Member_emits_reach_fields_ctors_and_methods() {
        var (g, m) = Method(typeof(int));
        g.Emit(OpCodes.Ldc_I4, 6);
        g.Emit(OpCodes.Newobj, typeof(PairHolder).GetConstructor([typeof(int)])!);
        var local = g.DeclareLocal(typeof(PairHolder));
        g.Emit(OpCodes.Stloc, local);
        g.Emit(OpCodes.Ldloc, local);
        g.Emit(OpCodes.Ldfld, typeof(PairHolder).GetField(nameof(PairHolder.A))!);
        g.EmitCall(OpCodes.Call, typeof(Math).GetMethod(nameof(Math.Abs), [typeof(int)])!, null);
        g.Emit(OpCodes.Ret);
        Assert.Equal(6, m.CreateDelegate<Func<int>>()());
    }

    public static int SevenTimes(int x) => 7 * x;

    [Fact]
    public void A_managed_calli_goes_through_a_function_pointer() {
        var (g, m) = Method(typeof(int));
        g.Emit(OpCodes.Ldc_I4_3);
        g.Emit(OpCodes.Ldftn, typeof(GeneratorTests).GetMethod(nameof(SevenTimes))!);
        g.EmitCalli(OpCodes.Calli, System.Reflection.CallingConventions.Standard, typeof(int), [typeof(int)], null);
        g.Emit(OpCodes.Ret);
        Assert.Equal(21, m.CreateDelegate<Func<int>>()());
    }

    [Fact]
    public void A_calli_signature_helper_is_forwarded() {
        var (g, m) = Method(typeof(int));
        var sig = SignatureHelper.GetMethodSigHelper(System.Reflection.CallingConventions.Standard, typeof(int));
        sig.AddArgument(typeof(int));
        g.Emit(OpCodes.Ldc_I4_2);
        g.Emit(OpCodes.Ldftn, typeof(GeneratorTests).GetMethod(nameof(SevenTimes))!);
        g.Emit(OpCodes.Calli, sig);
        g.Emit(OpCodes.Ret);
        Assert.Equal(14, m.CreateDelegate<Func<int>>()());
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int NativeInt();
    public static int Nine() => 9;
    public static readonly NativeInt KeepAlive = Nine;
    public static readonly IntPtr NativeNine = Marshal.GetFunctionPointerForDelegate(KeepAlive);

    [Fact]
    public void An_unmanaged_calli_goes_through_a_native_thunk() {
        var (g, m) = Method(typeof(int));
        g.Emit(OpCodes.Ldsfld, typeof(GeneratorTests).GetField(nameof(NativeNine))!);
        g.EmitCalli(OpCodes.Calli, CallingConvention.Cdecl, typeof(int), []);
        g.Emit(OpCodes.Ret);
        Assert.Equal(9, m.CreateDelegate<Func<int>>()());
    }

    [Fact]
    public void Try_catch_finally_and_filter_blocks_route_the_exception() {
        var (g, m) = Method(typeof(int));
        var result = g.DeclareLocal(typeof(int));
        g.BeginExceptionBlock();
        g.Emit(OpCodes.Ldstr, "boom");
        g.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor([typeof(string)])!);
        g.Emit(OpCodes.Throw);
        g.BeginExceptFilterBlock();
        g.Emit(OpCodes.Pop);
        g.Emit(OpCodes.Ldc_I4_1);
        g.BeginCatchBlock(null);
        g.Emit(OpCodes.Pop);
        g.Emit(OpCodes.Ldc_I4, 11);
        g.Emit(OpCodes.Stloc, result);
        g.BeginFinallyBlock();
        g.Emit(OpCodes.Ldloc, result);
        g.Emit(OpCodes.Ldc_I4_1);
        g.Emit(OpCodes.Add);
        g.Emit(OpCodes.Stloc, result);
        g.EndExceptionBlock();
        g.Emit(OpCodes.Ldloc, result);
        g.Emit(OpCodes.Ret);
        Assert.Equal(12, m.CreateDelegate<Func<int>>()());
    }

    [Fact]
    public void A_typed_catch_and_a_fault_block_are_forwarded() {
        var (g, m) = Method(typeof(int));
        var result = g.DeclareLocal(typeof(int));
        g.BeginExceptionBlock();
        g.Emit(OpCodes.Ldnull);
        g.Emit(OpCodes.Throw);
        g.BeginCatchBlock(typeof(NullReferenceException));
        g.Emit(OpCodes.Pop);
        g.Emit(OpCodes.Ldc_I4, 21);
        g.Emit(OpCodes.Stloc, result);
        g.EndExceptionBlock();
        g.Emit(OpCodes.Ldloc, result);
        g.Emit(OpCodes.Ret);
        Assert.Equal(21, m.CreateDelegate<Func<int>>()());

        var (g2, m2) = Method(typeof(int));
        g2.BeginExceptionBlock();
        g2.Emit(OpCodes.Nop);
        g2.BeginFaultBlock();
        g2.Emit(OpCodes.Nop);
        g2.EndExceptionBlock();
        g2.Emit(OpCodes.Ldc_I4_5);
        g2.Emit(OpCodes.Ret);
        Assert.Equal(5, m2.CreateDelegate<Func<int>>()());
    }

    [Fact]
    public void Scopes_and_namespaces_forward_to_the_real_generator() {
        var (g, m) = Method(typeof(int));
        Assert.Throws<NotSupportedException>(g.BeginScope);
        Assert.Throws<NotSupportedException>(g.EndScope);
        Assert.Throws<NotSupportedException>(() => g.UsingNamespace("System"));
        Assert.Throws<NotSupportedException>(() => g.UsingNamespace(""));
        g.Emit(OpCodes.Ldc_I4_4);
        g.Emit(OpCodes.Ret);
        Assert.Equal(4, m.CreateDelegate<Func<int>>()());
    }

    [Fact]
    public void The_boxing_and_casting_converters_emit_runnable_code() {
        var (g, m) = Method(typeof(IComparable), typeof(int));
        g.Emit(OpCodes.Ldarg_0);
        new BoxConverter(typeof(IComparable)).EmitConversion(g, typeof(int));
        g.Emit(OpCodes.Ret);
        Assert.Equal(5, m.CreateDelegate<Func<int, IComparable>>()(5));

        var (g2, m2) = Method(typeof(string), typeof(object));
        g2.Emit(OpCodes.Ldarg_0);
        new CastClassConverter(typeof(string)).EmitConversion(g2, typeof(object));
        g2.Emit(OpCodes.Ret);
        Assert.Equal("s", m2.CreateDelegate<Func<object, string>>()("s"));
    }

    class DispatchHost {
        public int GetOnly { get; }
#pragma warning disable CS0067
        public event Action? E;
#pragma warning restore CS0067
    }

    [Fact]
    public void Member_dispatch_builds_through_a_ctor_and_refuses_unsupported_members() {
        var (g, m) = Method(typeof(PairHolder), typeof(int));
        g.Emit(OpCodes.Ldarg_0);
        DbItemParser.EmitMemberDispatch(g, typeof(PairHolder).GetConstructor([typeof(int)])!);
        g.Emit(OpCodes.Ret);
        Assert.Equal(8, m.CreateDelegate<Func<int, PairHolder>>()(8).A);

        var (g2, _) = Method(typeof(int));
        Refusals.Raises(ErrorCodes.UnusableMember, () => DbItemParser.EmitMemberDispatch(g2, typeof(DispatchHost).GetProperty("GetOnly")!));
        Refusals.Raises(ErrorCodes.UnusableMember, () => DbItemParser.EmitMemberDispatch(g2, typeof(DispatchHost).GetEvent("E")!));
    }

    [Fact]
    public void The_null_jump_helper_lands_on_the_default_value() {
        var (g, m) = Method(typeof(int), typeof(bool));
        var nullJump = g.DefineLabel();
        g.Emit(OpCodes.Ldarg_0);
        g.Emit(OpCodes.Brtrue, nullJump); 
        g.Emit(OpCodes.Ldc_I4, 42);
        DbItemParser.EmitNullJump(nullJump, typeof(int), g);
        g.Emit(OpCodes.Ret);
        var f = m.CreateDelegate<Func<bool, int>>();
        Assert.Equal(42, f(false));
        Assert.Equal(0, f(true));
    }

    public readonly struct ExplicitParsable : IParsable<ExplicitParsable> {
        public readonly int V;
        private ExplicitParsable(int v) => V = v;
        public static ExplicitParsable Parse(string s) => new(int.Parse(s));
        static ExplicitParsable IParsable<ExplicitParsable>.Parse(string s, IFormatProvider? provider) => Parse(s);
        static bool IParsable<ExplicitParsable>.TryParse(string? s, IFormatProvider? provider, out ExplicitParsable result) {
            result = Parse(s!);
            return true;
        }
    }

    [Fact]
    public void The_parsable_converter_uses_the_plain_parse_when_no_culture_overload_is_public() {
        Assert.True(ITypeConverter.TryGetConverter(typeof(string), typeof(ExplicitParsable), out var chosen));
        var converter = Assert.IsType<ParsableConverter>(chosen);

        var (g, m) = Method(typeof(ExplicitParsable), typeof(string));
        g.Emit(OpCodes.Ldarg_0);
        converter.EmitConversion(g, typeof(string));
        g.Emit(OpCodes.Ret);
        Assert.Equal(12, m.CreateDelegate<Func<string, ExplicitParsable>>()("12").V);

        var (g2, _) = Method(typeof(object), typeof(string));
        Refusals.Raises(ErrorCodes.TargetTypeMismatch, () => new ParsableConverter(typeof(GeneratorTests)).EmitConversion(g2, typeof(string)));
    }

#if DEBUG
    [Fact]
    public void The_debug_build_traces_each_instruction() {
        var captured = new ConcurrentQueue<string>();
        var previous = Generator.Write;
        Generator.Write = s => { captured.Enqueue(s); previous(s); };
        try {
            var (g, m) = Method(typeof(int));
            g.Emit(OpCodes.Ldc_I4, 0);
            g.Emit(OpCodes.Ret);
            Assert.Equal(0, m.CreateDelegate<Func<int>>()());
        }
        finally {
            Generator.Write = previous;
        }
        Assert.Contains(captured, s => s.Contains("probable index for First"));
    }
#endif
}
