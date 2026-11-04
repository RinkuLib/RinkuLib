using Rinku;

namespace Rinku.More.Tests;

[Collection("Executor subsystem")]
public class ExecutorMemberResolutionTests
{
    [Fact]
    public void Public_class_members_win_over_an_unrelated_explicit_interface_member()
    {
        var plan = ExecutorBuilder.Start<ExplicitTarget>(out var target).Call(nameof(PropertyTarget.Caption), [target, "public"]);
        var instance = new ExplicitTarget();

        plan.Build<IExecutor<ExplicitTarget>>().Execute(instance);

        Assert.Equal("public", instance.Caption);
        Assert.Equal("interface", ((IExplicitTarget)instance).Caption);
    }

    public interface IExplicitTarget
    {
        string Caption
        {
            get; set;
        }
    }

    public class ExplicitTarget : PropertyTarget, IExplicitTarget
    {
        string IExplicitTarget.Caption { get; set; } = "interface";
    }

    [Fact]
    public void Property_authoring_resolves_to_fields_or_methods_with_the_same_name()
    {
        var plan = ExecutorBuilder.Start<PropertyTarget>(out var target)
            .Then(() => target.Val.Caption, "configured")
            .Then(out var caption, () => target.Val.Caption)
            .Call(nameof(FieldTarget.Record), [target, caption]);
        var fields = new FieldTarget();
        var methods = new MethodTarget();
        var executor = plan.Build(new ExecutorBuildConfiguration().Support<IExecutor<FieldTarget>>().Support<IExecutor<MethodTarget>>());

        ((IExecutor<FieldTarget>)executor).Execute(fields);
        ((IExecutor<MethodTarget>)executor).Execute(methods);

        Assert.Equal("configured", fields.Observed);
        Assert.Equal("configured", methods.Observed);
    }

    [Fact]
    public void Method_authoring_resolves_to_properties_and_fields()
    {
        var plan = ExecutorBuilder.Start<MethodTarget>(out var target)
            .Then(() => target.Val.Caption("from method"))
            .Then(out var caption, () => target.Val.Caption())
            .Call(nameof(MethodTarget.Record), [target, caption]);
        var fields = new FieldTarget();
        var properties = new PropertyTarget();

        plan.Build<IExecutor<FieldTarget>>().Execute(fields);
        plan.Build<IExecutor<PropertyTarget>>().Execute(properties);

        Assert.Equal("from method", fields.Observed);
        Assert.Equal("from method", properties.Observed);
    }

    [Fact]
    public void Named_calls_can_access_fields_without_member_metadata()
    {
        var plan = ExecutorBuilder.Start<FieldTarget>(out var target)
            .Call(nameof(FieldTarget.Caption), [target, "named"])
            .Call<string>(out var caption, nameof(FieldTarget.Caption), [target])
            .Call(nameof(FieldTarget.Record), [target, caption]);
        var instance = new FieldTarget();

        plan.Build<IExecutor<FieldTarget>>().Execute(instance);

        Assert.Equal("named", instance.Observed);
    }

    [Fact]
    public void Field_and_method_candidates_use_the_same_ambiguity_and_priority_rules()
    {
        var plan = ExecutorBuilder.Start<FieldTarget>(out var target).Call(nameof(FieldTarget.Caption), [target, "selected"]);
        var ambiguous = new ExecutorBuildConfiguration().RegisterProvider(typeof(OtherOperations));
        var prioritized = new ExecutorBuildConfiguration().RegisterProvider(typeof(OtherOperations), 1);
        var instance = new FieldTarget();

        Assert.Throws<InvalidOperationException>(() => plan.Build<IExecutor<FieldTarget>>(ambiguous));
        plan.Build<IExecutor<FieldTarget>>(prioritized).Execute(instance);

        Assert.Equal("selected", instance.Observed);
        Assert.Equal("", instance.Caption);
    }

    [Fact]
    public void Static_field_reads_and_writes_are_zero_and_one_operand_calls()
    {
        var plan = ExecutorBuilder.Start<object>(out _)
            .Then(() => StaticFields.Caption, "static")
            .Then(out var caption, () => StaticFields.Caption)
            .Call(nameof(StaticFields.Record), [caption]);

        plan.Build<IExecutor<object>>(new ExecutorBuildConfiguration().RegisterProvider(typeof(StaticFields))).Execute(new object());

        Assert.Equal("static", StaticFields.Observed);
    }

    [Fact]
    public void Unified_member_calls_survive_clone_merge_and_serialization()
    {
        var source = ExecutorBuilder.Start<PropertyTarget>(out var target).Then(() => target.Val.Caption, "serialized");
        var restored = ExecutorJson.Deserialize(ExecutorJson.Serialize(source.Clone()));
        var plan = ExecutorBuilder.Start<PropertyTarget>(out var field).Merge(restored.ToScope());
        var instance = new FieldTarget();

        plan.Build<IExecutor<FieldTarget>>().Execute(instance);

        Assert.Equal("serialized", instance.Caption);
        Assert.DoesNotContain("memberAssignment", ExecutorJson.Serialize(plan));
    }

    [Fact]
    public void Readonly_fields_are_not_writable_and_nullable_fields_obey_their_contract()
    {
        var writable = ExecutorBuilder.Start<FieldTarget>(out var target)
            .Call(nameof(FieldTarget.Optional), [target, null]);
        var readOnly = ExecutorBuilder.Start<FieldTarget>(out var other)
            .Call(nameof(FieldTarget.ReadOnly), [other, 1]);
        var instance = new FieldTarget();

        writable.Build<IExecutor<FieldTarget>>().Execute(instance);

        Assert.Null(instance.Optional);
        Assert.Throws<ExecutorBindingException>(() => readOnly.Build<IExecutor<FieldTarget>>());
    }

    public class PropertyTarget
    {
        public string Caption { get; set; } = "";
        public string? Observed
        {
            get; set;
        }
        public void Record(string value) => Observed = value;
    }

    public class FieldTarget
    {
        public string Caption = "";
        public string? Optional = "initial";
        public readonly int ReadOnly;
        public string? Observed;
        public void Record(string value) => Observed = value;
    }

    public class MethodTarget
    {
        private string caption = "";
        public string? Observed;
        public string Caption() => caption;
        public void Caption(string value) => caption = value;
        public void Record(string value) => Observed = value;
    }

    public static class StaticFields
    {
        public static string Caption = "";
        public static string? Observed;
        public static void Record(string value) => Observed = value;
    }

    public static class OtherOperations
    {
        public static void Caption(FieldTarget target, string value) => target.Observed = value;
    }
}
