using System.Reflection;
using System.Reflection.Emit;
using RinkuLib.Commands;
using RinkuLib.DbParsing;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.Tests.Mapping;
using RinkuLib.Tools;
using Xunit;

namespace RinkuLib.Tests.DbParsing;

/// <summary>
/// The reflection helpers behind parser construction: closing open generics, stack-slot equivalence,
/// reader-method resolution, default-value detection, and the column-usage checkpoints.
/// </summary>
public class TypeExtensionsTests {

    [Fact]
    public void ShortName_reads_plain_generic_and_null_types() {
        Assert.Equal("void", ((Type?)null).ShortName());
        Assert.Equal("Int32", typeof(int).ShortName());
        Assert.Equal("List<Int32>", typeof(List<int>).ShortName());
        Assert.Equal("Dictionary<String, List<Int32>>", typeof(Dictionary<string, List<int>>).ShortName());
    }


    [Fact]
    public void Stack_equivalence_groups_by_slot() {
        Assert.True(typeof(int).IsStackEquivalent(typeof(int)));
        Assert.True(typeof(int).IsStackEquivalent(typeof(DayOfWeek)));
        Assert.True(typeof(uint).IsStackEquivalent(typeof(int)));
        Assert.True(typeof(bool).IsStackEquivalent(typeof(byte)));
        Assert.True(typeof(char).IsStackEquivalent(typeof(ushort)));
        Assert.True(typeof(long).IsStackEquivalent(typeof(ulong)));
        Assert.True(typeof(string).IsStackEquivalent(typeof(object)));
        Assert.True(typeof(nint).IsStackEquivalent(typeof(nuint)));

        Assert.False(typeof(int).IsStackEquivalent(typeof(long)));
        Assert.False(typeof(float).IsStackEquivalent(typeof(double)));
        Assert.False(typeof(object).IsStackEquivalent(typeof(string)));
        Assert.False(typeof(int).IsStackEquivalent(typeof(string)));
        Assert.False(typeof(decimal).IsStackEquivalent(typeof(int)));
        Assert.False(typeof(decimal).IsStackEquivalent(typeof(TimeSpan)));
        Assert.False(typeof(int).IsStackEquivalent(typeof(nint)));
    }


    [Fact]
    public void Reader_methods_resolve_typed_getters_and_fall_back_per_kind() {
        Assert.Equal("GetInt32", typeof(int).GetDbMethod().Name);
        Assert.Equal("GetString", typeof(string).GetDbMethod().Name);

        var span = typeof(TimeSpan).GetDbMethod();       
        Assert.Equal(typeof(TimeSpan?), span.ReturnType);
        Assert.Same(span, typeof(TimeSpan).GetDbMethod());

        var version = typeof(Version).GetDbMethod();     
        Assert.Equal(typeof(Version), version.ReturnType);

        var bytes = typeof(byte[]).GetDbMethod();
        Assert.Equal(typeof(byte[]), bytes.ReturnType);
    }

    [Fact]
    public void Nullable_constructors_resolve_cached_and_uncached_value_types() {
        foreach (var t in new[] { typeof(int), typeof(long), typeof(byte), typeof(short), typeof(char),
                typeof(bool), typeof(float), typeof(double), typeof(decimal), typeof(DateTime), typeof(Guid),
                typeof(TimeSpan) })
            Assert.Equal(typeof(Nullable<>).MakeGenericType(t), t.GetNullableConstructor().DeclaringType);
        Assert.Throws<ArgumentException>(() => typeof(string).GetNullableConstructor());
    }


    class Holder<T> {
        public T? Val;
        public T? Prop { get; set; }
        public T? GetOnly { get; }
        public Holder() { }
        public Holder(T val) => Val = val;
        public void Push(T val) => Val = val;
        public static void Assign<U>(Holder<U> host, U val) => host.Val = val;
    }

    [Fact]
    public void Open_members_close_onto_the_concrete_type() {
        var open = typeof(Holder<>);

        var field = (FieldInfo)open.GetField("Val")!.GetClosedMember(typeof(Holder<int>));
        Assert.Equal(typeof(Holder<int>), field.DeclaringType);
        Assert.Equal(typeof(int), field.FieldType);

        var ctor = (ConstructorInfo)open.GetConstructor([open.GetGenericArguments()[0]])!.GetClosedMember(typeof(Holder<int>));
        Assert.Equal(typeof(Holder<int>), ctor.DeclaringType);

        var setter = (MethodInfo)open.GetProperty("Prop")!.GetClosedMember(typeof(Holder<int>));
        Assert.Equal(typeof(Holder<int>), setter.DeclaringType);
        Assert.Equal("set_Prop", setter.Name);

        var method = (MethodInfo)open.GetMethod("Push")!.GetClosedMember(typeof(Holder<int>));
        Assert.Equal(typeof(Holder<int>), method.DeclaringType);

        var generic = (MethodInfo)open.GetMethod("Assign")!.GetClosedMember(typeof(Holder<int>));
        Assert.Equal(typeof(int), generic.GetGenericArguments()[0]);

        var untouched = typeof(TypeExtensionsTests).GetMethod(nameof(Open_members_close_onto_the_concrete_type))!;
        Assert.Same(untouched, untouched.GetClosedMember(typeof(string))); 

        Refusals.Raises(ErrorCodes.UnusableMember, () => open.GetProperty("GetOnly")!.GetClosedMember(typeof(Holder<int>)));
        Refusals.Raises(ErrorCodes.UnusableMember,
            () => typeof(Evented<>).GetEvent("E")!.GetClosedMember(typeof(Evented<int>)));
    }

    class Evented<T> {
#pragma warning disable CS0067
        public event Action? E;
#pragma warning restore CS0067
    }

    [Fact]
    public void Open_types_close_through_every_shape() {
        var t = typeof(Holder<>).GetGenericArguments()[0];

        Assert.Equal(typeof(int), t.CloseType([typeof(int)]));
        Assert.Equal(typeof(List<int>), typeof(List<>).MakeGenericType(t).CloseType([typeof(int)]));
        Assert.Equal(typeof(int[]), t.MakeArrayType().CloseType([typeof(int)]));
        Assert.Equal(typeof(int[,]), t.MakeArrayType(2).CloseType([typeof(int)]));
        Assert.Equal(typeof(int).MakePointerType(), t.MakePointerType().CloseType([typeof(int)]));
        Assert.Equal(typeof(int).MakeByRefType(), t.MakeByRefType().CloseType([typeof(int)]));

        Assert.Equal(typeof(string), typeof(string).CloseType([typeof(int)]));       
        Assert.Equal(typeof(int[]), typeof(int[]).CloseType([typeof(long)]));     
        Assert.Equal(typeof(List<int>), typeof(List<int>).CloseType([typeof(long)]));

        Assert.Equal(typeof(KeyValuePair<int, List<string>>),
            typeof(KeyValuePair<,>).MakeGenericType(
                typeof(Holder<>).GetGenericArguments()[0],
                typeof(List<>).MakeGenericType(typeof(KeyValuePair<,>).GetGenericArguments()[1]))
            .CloseType(typeof(KeyValuePair<int, string>)));

        Assert.Equal(typeof(KeyValuePair<string, int*[]>),
            typeof(KeyValuePair<,>).MakeGenericType(t, typeof(int*[])).CloseType([typeof(string)]));
    }


    sealed class FakeParameter(Type type, bool hasDefault, object? value) : ParameterInfo {
        public override Type ParameterType => type;
        public override bool HasDefaultValue => hasDefault;
        public override object? DefaultValue => value;
    }

    [Fact]
    public void Default_parameter_values_are_recognized_per_kind() {
        static bool Check(Type t, bool has, object? v) => new FakeParameter(t, has, v).IsTypeDefault();

        Assert.False(Check(typeof(int), false, null));         
        Assert.True(Check(typeof(string), true, null));       
        Assert.True(Check(typeof(Guid), true, DBNull.Value));  

        Assert.True(Check(typeof(bool), true, false));
        Assert.False(Check(typeof(bool), true, true));
        Assert.True(Check(typeof(char), true, '\0'));
        Assert.False(Check(typeof(char), true, 'x'));
        Assert.True(Check(typeof(int), true, 0));
        Assert.False(Check(typeof(int), true, 3));
        Assert.True(Check(typeof(ulong), true, 0ul));
        Assert.True(Check(typeof(double), true, 0.0));
        Assert.False(Check(typeof(float), true, 1f));
        Assert.True(Check(typeof(decimal), true, 0m));
        Assert.False(Check(typeof(decimal), true, 1m));
        Assert.True(Check(typeof(DateTime), true, default(DateTime)));
        Assert.False(Check(typeof(DateTime), true, DateTime.MaxValue));
        Assert.True(Check(typeof(DayOfWeek), true, 0));
        Assert.False(Check(typeof(DayOfWeek), true, (int)DayOfWeek.Friday));

        Assert.True(Check(typeof(Guid), true, Guid.Empty));
        Assert.False(Check(typeof(Guid), true, Guid.NewGuid()));
        Assert.True(Check(typeof(TimeSpan), true, TimeSpan.Zero));
        Assert.False(Check(typeof(TimeSpan), true, TimeSpan.FromHours(1)));
        Assert.True(Check(typeof(DateTimeOffset), true, default(DateTimeOffset)));
        Assert.False(Check(typeof(DateTimeOffset), true, DateTimeOffset.UtcNow));
        Assert.True(Check(typeof(WrappedInt), true, default(WrappedInt))); 
        Assert.False(Check(typeof(WrappedInt), true, new WrappedInt(3)));

        Assert.False(Check(typeof(int), true, "not a number")); 
        Assert.False(Check(typeof(Version), true, "x"));        
        Assert.False(Check(typeof(string), true, "abc"));    
    }

    [Fact]
    public void An_optional_trailing_parameter_falls_back_to_its_default() {
        ColumnInfo[] cols = [new("A", typeof(int), false)];
        var parsed = Rows.ParseOne<WithOptional>(cols, 4);
        Assert.Equal(4, parsed.A);
        Assert.Equal(0, parsed.B);
    }

    public record WithOptional(int A, int B = 0) : IDbReadable;


    [Fact]
    public void Column_usage_checkpoints_snapshot_and_roll_back() {
        Span<bool> backing = stackalloc bool[3];
        var usage = new ColumnUsage(backing);
        usage.Use(0);
        Assert.True(usage.IsUsed(0));
        Assert.Equal(1, usage.NbUsed);
        Assert.Equal(0, usage.LastIndexUsed);
        Assert.Equal(3, usage.Length);

        Span<bool> checkpoint = stackalloc bool[3];
        usage.InitCheckpoint(checkpoint, out var last);
        usage.Use(2);
        Assert.Equal(2, usage.NbUsed);
        usage.Rollback(checkpoint, last);
        Assert.Equal(1, usage.NbUsed);
        Assert.False(usage.IsUsed(2));
        Assert.Equal(0, usage.LastIndexUsed);

        bool threw = false;
        try {
            Span<bool> wrong = stackalloc bool[2];
            usage.InitCheckpoint(wrong, out _);
        }
        catch (Exception) { threw = true; }
        Assert.True(threw);

        threw = false;
        try {
            Span<bool> wrong = stackalloc bool[5];
            usage.Rollback(wrong, 0);
        }
        catch (Exception) { threw = true; }
        Assert.True(threw);
    }


    public record SchemaShape(int Id, string Name) : IDbReadable;

    [Fact]
    public void Parsers_derive_their_columns_from_a_type_method_ctor_or_delegate() {
        var fromType = TypeParser.GetTypeParser<SchemaShape>(typeof(SchemaShape), out var typeCols);
        Assert.Equal(["Id", "Name"], typeCols.Select(c => c.Name));
        Assert.NotNull(fromType);

        var ctor = typeof(SchemaShape).GetConstructors()[0];
        TypeParser.GetTypeParser<SchemaShape>(ctor, out var ctorCols);
        Assert.Equal(["Id", "Name"], ctorCols.Select(c => c.Name));

        var method = typeof(TypeExtensionsTests).GetMethod(nameof(Shape))!;
        TypeParser.GetTypeParser<SchemaShape>(method, out var methodCols);
        Assert.Equal(["Id", "Name"], methodCols.Select(c => c.Name));

        TypeParser.GetTypeParser<SchemaShape>((Func<int, string, SchemaShape>)Shape, out var delegateCols);
        Assert.Equal(["Id", "Name"], delegateCols.Select(c => c.Name));

        var byShape = TypeParser.GetTypeParser<SchemaShape, DynaObject>(out var schemaCols);
        Assert.Equal(["Id", "Name"], schemaCols.Select(c => c.Name));
        Assert.NotNull(byShape);
    }

    public static SchemaShape Shape(int Id, string Name) => new(Id, Name);


    [Fact]
    public void The_stack_printers_dump_values_and_leave_the_stack_intact() {
        var buffer = new StringWriter();
        var writer = TextWriter.Synchronized(buffer);
        var original = Console.Out;
        Console.SetOut(writer);
        try {
            var method = new DynamicMethod("dump", typeof(int), Type.EmptyTypes, typeof(TypeExtensionsTests).Module);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldc_I4, 7);
            typeof(int).EmitWriteLineStackTop(il);
            il.Emit(OpCodes.Ldstr, "txt");
            il.EmitWriteLineStack(typeof(int), typeof(string));
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);
            il.EmitWriteLineStack();         
            Assert.Equal(7, method.CreateDelegate<Func<int>>()());
        }
        finally {
            Console.SetOut(original);
        }
        writer.Flush();
        var output = buffer.ToString();
        Assert.Contains("7", output);
        Assert.Contains("txt", output);
        Assert.Contains("[Stack Index 0]", output);
    }
}
