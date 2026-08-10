using System.Collections;
using Rinku.Mapping.Defaults;
using System.Data.Common;
using System.Reflection;
using System.Reflection.Emit;
using Rinku.Mapping;
using Rinku.Internal;
using Rinku.Mapping.Parsers;
using Xunit;

namespace RinkuLib.Tests.DbParsing;

/// <summary>
/// The two properties the multi-row emit stands on: an emitted method on a state struct reaches non-public
/// members across assemblies with no delegate, and a scalar parse allocates nothing for the state machinery
/// because the state is a struct on the stack.
/// </summary>
public class MultiRowEmitInternalsTests {

    public sealed class PrivateSettable {
        private PrivateSettable() { }
        public string City { get; private set; } = "";
        public int Code { get; private set; }
    }

    [Fact]
    public void An_emitted_state_method_reaches_a_private_ctor_and_private_setters() {
        var tb = StateTypeAssembly.DefineState("Spike_" + Guid.NewGuid().ToString("N"));
        tb.DefineField("_probe", typeof(int), FieldAttributes.Private);
        StateTypeAssembly.AllowAccessTo(typeof(PrivateSettable).Assembly);

        var ctor = typeof(PrivateSettable).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null)!;
        var setCity = typeof(PrivateSettable).GetProperty(nameof(PrivateSettable.City))!.GetSetMethod(nonPublic: true)!;
        var setCode = typeof(PrivateSettable).GetProperty(nameof(PrivateSettable.Code))!.GetSetMethod(nonPublic: true)!;

        var mb = tb.DefineMethod(
            "Make", MethodAttributes.Public | MethodAttributes.Static,
            typeof(PrivateSettable), [typeof(string), typeof(int)]);
        var il = mb.GetILGenerator();
        var loc = il.DeclareLocal(typeof(PrivateSettable));
        il.Emit(OpCodes.Newobj, ctor);
        il.Emit(OpCodes.Stloc, loc);
        il.Emit(OpCodes.Ldloc, loc);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, setCity);
        il.Emit(OpCodes.Ldloc, loc);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, setCode);
        il.Emit(OpCodes.Ldloc, loc);
        il.Emit(OpCodes.Ret);

        var created = tb.CreateType();
        var make = created.GetMethod("Make")!.CreateDelegate<Func<string, int, PrivateSettable>>();

        var result = make("Paris", 42);
        Assert.Equal("Paris", result.City);
        Assert.Equal(42, result.Code);
    }

    [Fact]
    public void Granting_access_to_the_same_assembly_twice_is_harmless() {
        StateTypeAssembly.AllowAccessTo(typeof(PrivateSettable).Assembly);
        StateTypeAssembly.AllowAccessTo(typeof(PrivateSettable).Assembly);
    }

    private sealed class EndlessIntReader : DbDataReader {
        public override bool Read() => true;
        public override int GetInt32(int ordinal) => 0;
        public override bool IsDBNull(int ordinal) => false;
        public override int FieldCount => 1;
        public override bool HasRows => true;
        public override bool IsClosed => false;
        public override int Depth => 0;
        public override int RecordsAffected => -1;
        public override object this[int ordinal] => 0;
        public override object this[string name] => 0;
        public override bool NextResult() => false;
        public override object GetValue(int ordinal) => 0;
        public override int GetValues(object[] values) => 0;
        public override string GetName(int ordinal) => "V";
        public override int GetOrdinal(string name) => 0;
        public override string GetDataTypeName(int ordinal) => nameof(Int32);
        public override Type GetFieldType(int ordinal) => typeof(int);
        public override IEnumerator GetEnumerator() => throw new NotSupportedException();
        public override bool GetBoolean(int ordinal) => throw new NotSupportedException();
        public override byte GetByte(int ordinal) => throw new NotSupportedException();
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
        public override char GetChar(int ordinal) => throw new NotSupportedException();
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => throw new NotSupportedException();
        public override DateTime GetDateTime(int ordinal) => throw new NotSupportedException();
        public override decimal GetDecimal(int ordinal) => throw new NotSupportedException();
        public override double GetDouble(int ordinal) => throw new NotSupportedException();
        public override float GetFloat(int ordinal) => throw new NotSupportedException();
        public override Guid GetGuid(int ordinal) => throw new NotSupportedException();
        public override short GetInt16(int ordinal) => throw new NotSupportedException();
        public override long GetInt64(int ordinal) => throw new NotSupportedException();
        public override string GetString(int ordinal) => throw new NotSupportedException();
    }

    [Fact]
    public void A_scalar_multi_row_parse_allocates_nothing_for_the_machinery() {
        ColumnInfo[] cols = [new("V", typeof(int), false)];
        var parser = Assert.IsType<DefaultTypeParserMaker>(TypeParser.DefaultTypeParserMaker).ForceMultiRow<int>(TypeParser.GetDefaultNullColHandler<int>(), cols);
        var reader = new EndlessIntReader();

        for (int i = 0; i < 1000; i++)
            parser.Parse(reader);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100_000; i++)
            parser.Parse(reader);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated == 0, $"expected zero per-group machinery allocation, saw {allocated} bytes over 100000 parses");
    }
}
