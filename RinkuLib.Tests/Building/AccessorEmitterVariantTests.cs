using RinkuLib.Commands;
using RinkuLib.Queries;
using RinkuLib.Tests.Infrastructure;
using RinkuLib.TypeAccessing;
using Xunit;

namespace RinkuLib.Tests.Building;

/// <summary>
/// The accessor attributes across member kinds: fields and properties, value and reference types, and the
/// misuse and not-a-key roads.
/// </summary>
public class AccessorEmitterVariantTests {
    static QueryCommand VarQuery => new("SELECT * FROM u WHERE a = ?@V");
    static QueryCommand CondQuery => new("SELECT * FROM u WHERE /*F*/a = 1");

    class WhitespaceField {
        [NotNullOrWhitespace] public string? V;
    }

    [Fact]
    public void NotNullOrWhitespace_works_on_a_field() {
        var query = VarQuery;
        var b = query.StartBuilder();
        b.UseWith(new WhitespaceField { V = "  " });
        Render.Expect(b, "SELECT * FROM u");
        var b2 = query.StartBuilder();
        b2.UseWith(new WhitespaceField { V = "x" });
        Render.Expect(b2, "SELECT * FROM u WHERE a = @V", ("@V", "x"));
    }

    struct BoolCondProperty {
        [ForBoolCond] public bool F { get; set; }
    }

    [Fact]
    public void ForBoolCond_works_on_a_struct_property() {
        var query = CondQuery;
        var b = query.StartBuilder();
        b.UseWith(new BoolCondProperty { F = true });
        Render.Expect(b, "SELECT * FROM u WHERE a = 1");
        var b2 = query.StartBuilder();
        b2.UseWith(new BoolCondProperty { F = false });
        Render.Expect(b2, "SELECT * FROM u");
    }

    class DefaultKinds {
        [NotDefault] public double V { get; set; }
        [NotDefault] public Guid G;
        [NotDefault] public string? S;
    }

    [Fact]
    public void NotDefault_handles_floats_structs_and_references() {
        var query = new QueryCommand("SELECT * FROM u WHERE a = ?@V AND b = ?@G AND c = ?@S");
        var b = query.StartBuilder();
        b.UseWith(new DefaultKinds());
        Render.Expect(b, "SELECT * FROM u");

        var g = Guid.NewGuid();
        var b2 = query.StartBuilder();
        b2.UseWith(new DefaultKinds { V = 1.5, G = g, S = "s" });
        Render.Expect(b2, "SELECT * FROM u WHERE a = @V AND b = @G AND c = @S",
            ("@V", 1.5), ("@G", (object?)g), ("@S", "s"));
    }

    class BadBool {
        [ForBoolCond] public int F { get; set; }
    }
    class BadString {
        [NotNullOrWhitespace] public int V;
    }
    class BadStringProp {
        [NotNullOrWhitespace] public int V { get; set; }
    }

    [Fact]
    public void The_attributes_refuse_the_wrong_member_type() {
        var b = CondQuery.StartBuilder();
        Assert.ThrowsAny<Exception>(() => { b.UseWith(new BadBool { F = 1 }); });
        var b2 = VarQuery.StartBuilder();
        Assert.ThrowsAny<Exception>(() => { b2.UseWith(new BadString { V = 1 }); });
        var b3 = VarQuery.StartBuilder();
        Assert.ThrowsAny<Exception>(() => { b3.UseWith(new BadStringProp { V = 1 }); });
    }

    [UsesBoolConds("F", "NotAConditionKey")]
    class TypeLevelConds {
        public bool F { get; set; }
        public bool NotAConditionKey { get; set; }
        public int V { get; set; }
    }

    [Fact]
    public void Type_level_bool_conds_skip_names_that_are_not_keys() {
        var query = new QueryCommand("SELECT * FROM u WHERE a = ?@V AND /*F*/b = 1");
        var b = query.StartBuilder();
        b.UseWith(new TypeLevelConds { F = true, NotAConditionKey = true, V = 4 });
        Render.Expect(b, "SELECT * FROM u WHERE a = @V AND b = 1", ("@V", 4));
    }

    class NotAKey {
        public int V { get; set; }
        [ForBoolCond] public bool Unrelated;
        [NotDefault] public int AlsoUnrelated;
        [NotNullOrWhitespace] public string? StillUnrelated;
    }

    [Fact]
    public void Attributed_members_that_match_no_key_are_ignored() {
        var query = VarQuery;
        var b = query.StartBuilder();
        b.UseWith(new NotAKey { V = 3, Unrelated = true, AlsoUnrelated = 5, StillUnrelated = "x" });
        Render.Expect(b, "SELECT * FROM u WHERE a = @V", ("@V", 3));
    }

    class StaticSource {
        public static bool F { get; set; }
        [ForBoolCond] public static bool FStatic;
        public int V { get; set; }
    }

    [Fact]
    public void A_static_attributed_field_reads_the_static_state() {
        StaticSource.FStatic = true;
        try {
            var query = new QueryCommand("SELECT * FROM u WHERE a = ?@V AND /*FStatic*/b = 1");
            var b = query.StartBuilder();
            b.UseWith(new StaticSource { V = 2 });
            Render.Expect(b, "SELECT * FROM u WHERE a = @V AND b = 1", ("@V", 2));
        }
        finally {
            StaticSource.FStatic = false;
        }
    }

    class StaticPropSource {
        [ForBoolCond] public static bool FProp { get; set; }
        public int V { get; set; }
    }

    [Fact]
    public void A_static_attributed_property_reads_the_static_state() {
        StaticPropSource.FProp = true;
        try {
            var query = new QueryCommand("SELECT * FROM u WHERE a = ?@V AND /*FProp*/b = 1");
            var b = query.StartBuilder();
            b.UseWith(new StaticPropSource { V = 2 });
            Render.Expect(b, "SELECT * FROM u WHERE a = @V AND b = 1", ("@V", 2));
        }
        finally {
            StaticPropSource.FProp = false;
        }
    }

#pragma warning disable CS0067
    class WithEvent {
        public event Action? E;
    }
#pragma warning restore CS0067

    [Fact]
    public void EmitMemberLoad_refuses_a_member_that_is_not_loadable() {
        var method = new System.Reflection.Emit.DynamicMethod("bad", typeof(void), []);
        Assert.ThrowsAny<Exception>(() =>
            IAccessorEmiter.EmitMemberLoad(method.GetILGenerator(), typeof(WithEvent), typeof(WithEvent).GetEvent("E")!));
    }
}
