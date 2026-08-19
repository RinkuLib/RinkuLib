using System;
using System.Reflection;
using System.Reflection.Emit;
using Rinku.Querying;
using Rinku.Querying.Parameters;
using Xunit;

namespace Rinku.Querying.Tests;

public sealed class StackParameterAccessorTests {
    private sealed class MethodArgs {
        public int Id { get; init; }
        public string? Name { get; init; }
    }

    private static string CallTarget(int Id, string Name) => $"{Id}:{Name}";

    [Fact]
    public void Stack_accessor_can_feed_a_method_call_without_an_object_array() {
        Mapper mapper = Mapper.GetMapper(["Id", "Name"]);
        try {
            var method = new DynamicMethod("CallFromParameters", typeof(string), [typeof(MethodArgs)], typeof(StackParameterAccessorTests).Module, true);
            ILGenerator il = method.GetILGenerator();
            var accessor = ParameterAccessorGenerator.CreateStack(typeof(MethodArgs), mapper);
            var emission = accessor.Begin(il);
            Label missing = il.DefineLabel();

            for (int i = 0; i < mapper.Count; i++) {
                Assert.NotNull(emission.GetValueType(i));
                emission.EmitUsage(i);
                il.Emit(OpCodes.Brfalse, missing);
            }

            emission.EmitValue(0); // int stays int32 on the IL stack
            emission.EmitValue(1); // string stays an object reference on the IL stack
            il.Emit(OpCodes.Call, typeof(StackParameterAccessorTests).GetMethod(nameof(CallTarget), BindingFlags.Static | BindingFlags.NonPublic)!);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(missing);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);

            var call = method.CreateDelegate<Func<MethodArgs, string?>>();
            Assert.Equal("7:Ada", call(new MethodArgs { Id = 7, Name = "Ada" }));
            Assert.Null(call(new MethodArgs { Id = 7, Name = null }));
        }
        finally {
            mapper.Dispose();
        }
    }

    private sealed class NestedCore { public int Id { get; init; } }
    private sealed class NestedArgs {
        [NestedParameters] public NestedCore? Core { get; init; }
        public string? Name { get; init; }
    }

    private static string NestedTarget(int Id, string Name) => $"{Id}:{Name}";

    [Fact]
    public void Stack_usage_prepares_nested_values_before_arguments_are_pushed() {
        Mapper mapper = Mapper.GetMapper(["Id", "Name"]);
        try {
            var method = new DynamicMethod("NestedCallFromParameters", typeof(string), [typeof(NestedArgs)], typeof(StackParameterAccessorTests).Module, true);
            ILGenerator il = method.GetILGenerator();
            var emission = ParameterAccessorGenerator.CreateStack(typeof(NestedArgs), mapper).Begin(il);
            Label missing = il.DefineLabel();

            for (int i = 0; i < mapper.Count; i++) {
                emission.EmitUsage(i);
                il.Emit(OpCodes.Brfalse, missing);
            }

            emission.EmitValue(0);
            emission.EmitValue(1);
            il.Emit(OpCodes.Call, typeof(StackParameterAccessorTests).GetMethod(nameof(NestedTarget), BindingFlags.Static | BindingFlags.NonPublic)!);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(missing);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);

            var call = method.CreateDelegate<Func<NestedArgs, string?>>();
            Assert.Equal("4:Grace", call(new NestedArgs { Core = new NestedCore { Id = 4 }, Name = "Grace" }));
            Assert.Null(call(new NestedArgs { Core = null, Name = "Grace" }));
        }
        finally {
            mapper.Dispose();
        }
    }

    private sealed class DbNullArgs {
        [UseDbNull] public int? ParentId { get; init; }
    }

    private static int? RawTarget(int? ParentId) => ParentId;

    [Fact]
    public void Stack_mode_emits_the_raw_typed_value_not_the_db_terminal_value() {
        Mapper mapper = Mapper.GetMapper(["ParentId"]);
        try {
            var method = new DynamicMethod("RawDbNullCall", typeof(int?), [typeof(DbNullArgs)], typeof(StackParameterAccessorTests).Module, true);
            ILGenerator il = method.GetILGenerator();
            var emission = ParameterAccessorGenerator.CreateStack(typeof(DbNullArgs), mapper).Begin(il);
            Label missing = il.DefineLabel();

            emission.EmitUsage(0);
            il.Emit(OpCodes.Brfalse, missing);
            Assert.Equal(typeof(int?), emission.GetValueType(0));
            emission.EmitValue(0);
            il.Emit(OpCodes.Call, typeof(StackParameterAccessorTests).GetMethod(nameof(RawTarget), BindingFlags.Static | BindingFlags.NonPublic)!);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(missing);
            LocalBuilder fallback = il.DeclareLocal(typeof(int?));
            il.Emit(OpCodes.Ldloca, fallback);
            il.Emit(OpCodes.Initobj, typeof(int?));
            il.Emit(OpCodes.Ldloc, fallback);
            il.Emit(OpCodes.Ret);

            var call = method.CreateDelegate<Func<DbNullArgs, int?>>();
            Assert.Null(call(new DbNullArgs { ParentId = null }));
            Assert.Equal(9, call(new DbNullArgs { ParentId = 9 }));
        }
        finally {
            mapper.Dispose();
        }
    }
}
