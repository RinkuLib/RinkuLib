using System.Reflection;
using BindingTree;
using Rinku;
using Rinku.More;

namespace Rinku.More.Tests;

[Collection("Executor subsystem")]
public sealed class BindingConsolidationTests
{
    [Fact]
    public void An_external_attribute_can_initialize_inherited_interface_declarations()
    {
        var plan = ExecutorBuilder.Start<Trace>(out var target);
        foreach (var attribute in typeof(IDerived).GetProperty(nameof(IDerived.Value))!.GetMetadataAttributes<MarkerAttribute>())
        {
            string text = attribute.Text;
            plan.Then(() => target.Val.Items.Add(text));
        }
        var trace = new Trace();
        plan.Build<IExecutor<Trace>>().Execute(trace);
        Assert.Equal(["base", "derived"], trace.Items);
    }

    public sealed class Trace
    {
        public List<string> Items { get; } = [];
    }
    public interface IBase
    {
        [Marker("base")]
        int Value { get; }
    }
    public interface IDerived : IBase
    {
        [Marker("derived")]
        new int Value { get; }
    }
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class MarkerAttribute(string text) : Attribute
    {
        public string Text { get; } = text;
    }
}
