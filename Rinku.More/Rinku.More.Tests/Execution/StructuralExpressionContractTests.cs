using System.Reflection;
using Rinku;
using Xunit;

namespace Rinku.More.Tests;

public sealed class StructuralExpressionContractTests
{
    [Fact]
    public void One_logical_plan_builds_independently_for_each_configuration()
    {
        ExecutorBuilder builder = ExecutorBuilder.Start<IHasColumns>(out var owner)
            .Then(out var column, () => owner.Val.GetOrAddColumn("Name"))
            .Then(() => column.Val.Width, 120);

        var gridExecutor = builder.Build<IExecutor<GridView>>(new ExecutorBuildConfiguration().RegisterProvider(typeof(StructuralColumnOperations)));
        var treeExecutor = builder.Build<IExecutor<TreeList>>(new ExecutorBuildConfiguration().RegisterProvider(typeof(StructuralColumnOperations)));

        GridView grid = new();
        TreeList tree = new();
        gridExecutor.Execute(grid);
        treeExecutor.Execute(tree);

        Assert.Equal(120, grid.ColumnByFieldName("Name")?.Width);
        Assert.Equal(120, tree.ColumnByFieldName("Name")?.Width);
        Assert.IsAssignableFrom<IExecutor<GridView>>(gridExecutor);
        Assert.IsNotAssignableFrom<IExecutor<TreeList>>(gridExecutor);
        Assert.IsAssignableFrom<IExecutor<TreeList>>(treeExecutor);
        Assert.IsNotAssignableFrom<IExecutor<GridView>>(treeExecutor);
    }

    [Fact]
    public void One_logical_extension_specializes_members_loops_and_events_for_each_concrete_surface()
    {
        StructuralColumnOperations.EventCalls = 0;
        ExecutorBuildConfiguration configuration = new ExecutorBuildConfiguration().RegisterProvider(typeof(StructuralColumnOperations));

        var builder = ExecutorBuilder.Start<IHasColumns>(out var owner)
            .Then(out var column, () => owner.Val.GetOrAddColumn("Name"))
            .Then(() => column.Val.ApplyStyle("wide"))
            .Then(() => column.Val.Width, 120)
            .Then(() => StructuralColumnOperations.PrepareChanged(owner.Val))
            .ForEach(() => owner.Val.GetColumns(), (loop, current) => loop.Then(() => current.Val.Width, () => current.Val.Width + 1));
        var gridExecutor = builder.Build<IExecutor<GridView>>(configuration);
        var treeExecutor = builder.Build<IExecutor<TreeList>>(configuration);

        GridView grid = new();
        TreeList tree = new();
        gridExecutor.Execute(grid);
        gridExecutor.Execute(grid);
        treeExecutor.Execute(tree);
        treeExecutor.Execute(tree);

        Assert.Equal(121, grid.ColumnByFieldName("Name")?.Width);
        Assert.Equal(121, tree.ColumnByFieldName("Name")?.Width);
        Assert.Equal("wide", grid.ColumnByFieldName("Name")?.Style);
        Assert.Equal("wide", tree.ColumnByFieldName("Name")?.Style);
        grid.RaiseChanged();
        tree.RaiseChanged();
        Assert.Equal(2, StructuralColumnOperations.EventCalls);
    }

    [Fact]
    public void Prepared_callback_plans_are_idempotent_for_each_target()
    {
        StructuralColumnOperations.GeneratedEventCalls = 0;
        ExecutorBuildConfiguration configuration = new ExecutorBuildConfiguration().RegisterProvider(typeof(StructuralColumnOperations));

        var builder = ExecutorBuilder.Start<IHasColumns>(out var owner)
            .Then(() => StructuralColumnOperations.PrepareGenerated(owner.Val, "changed"));
        var gridExecutor = builder.Build<IExecutor<GridView>>(configuration);
        var treeExecutor = builder.Build<IExecutor<TreeList>>(configuration);

        GridView grid = new();
        TreeList tree = new();
        gridExecutor.Execute(grid);
        gridExecutor.Execute(grid);
        treeExecutor.Execute(tree);
        treeExecutor.Execute(tree);
        grid.RaiseChanged();
        tree.RaiseChanged();

        Assert.Equal("changed", grid.ColumnByFieldName("Event")?.Style);
        Assert.Equal("changed", tree.ColumnByFieldName("Event")?.Style);
        Assert.Equal(2, StructuralColumnOperations.GeneratedEventCalls);
    }

    [Fact]
    public void Prepared_callback_plans_survive_clone_merge_and_json_round_trip()
    {
        ExecutorBuildConfiguration configuration = new ExecutorBuildConfiguration().RegisterProvider(typeof(StructuralColumnOperations));

        ExecutorBuilder source = ExecutorBuilder.Start<IHasColumns>(out var sourceOwner)
            .Then(() => StructuralColumnOperations.PrepareGenerated(sourceOwner.Val, "copied"));
        ExecutorBuilder merged = ExecutorBuilder.Start<IHasColumns>(out var mergedOwner).Merge(source.ToScope());
        ExecutorBuilder restored = ExecutorJson.Deserialize(ExecutorJson.Serialize(source));

        GridView clonedGrid = new();
        GridView mergedGrid = new();
        GridView restoredGrid = new();
        source.Clone().Build<IExecutor<GridView>>(configuration).Execute(clonedGrid);
        merged.Build<IExecutor<GridView>>(configuration).Execute(mergedGrid);
        restored.Build<IExecutor<GridView>>(configuration).Execute(restoredGrid);
        clonedGrid.RaiseChanged();
        mergedGrid.RaiseChanged();
        restoredGrid.RaiseChanged();

        Assert.Equal("copied", clonedGrid.ColumnByFieldName("Event")?.Style);
        Assert.Equal("copied", mergedGrid.ColumnByFieldName("Event")?.Style);
        Assert.Equal("copied", restoredGrid.ColumnByFieldName("Event")?.Style);
    }
    [Fact]
    public void Missing_structural_member_fails_during_build()
    {
        ExecutorBuildConfiguration configuration = new ExecutorBuildConfiguration().RegisterProvider(typeof(StructuralColumnOperations));

        ExecutorBuilder builder = ExecutorBuilder.Start<IColumn>(out var column)
            .Then(() => column.Val.Width, 120);

        InvalidOperationException exception = Assert.Throws<ExecutorBindingException>(() => builder.Build<IExecutor<MissingWidthColumn>>(configuration));
        Assert.Contains(nameof(IColumn.Width), exception.Message);
    }

    [Fact]
    public void Missing_structural_extension_fails_during_build()
    {
        ExecutorBuildConfiguration configuration = new ExecutorBuildConfiguration().RegisterProvider(typeof(StructuralColumnOperations));

        ExecutorBuilder builder = ExecutorBuilder.Start<IHasColumns>(out var owner)
            .Then(out var column, () => owner.Val.MissingColumn());

        InvalidOperationException exception = Assert.Throws<ExecutorBindingException>(() => builder.Build<IExecutor<GridView>>(configuration));
        Assert.Contains(nameof(StructuralColumnOperations.MissingColumn), exception.Message);
    }

}

public interface IHasColumns : IExpressionContract
{
    event EventHandler Changed;
}

public interface IColumn : IExpressionContract
{
    int Width
    {
        get; set;
    }
}

public sealed class GridView
{
    private readonly Dictionary<string, GridColumn> columns = [];
    public event EventHandler? Changed;
    public GridColumn? ColumnByFieldName(string fieldName) => columns.GetValueOrDefault(fieldName);
    public GridColumn AddVisible(string fieldName) => columns[fieldName] = new GridColumn();
    public GridColumn[] GetColumns() => columns.Values.ToArray();
    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}

public sealed class TreeList
{
    private readonly Dictionary<string, TreeListColumn> columns = [];
    public event EventHandler? Changed;
    public TreeListColumn? ColumnByFieldName(string fieldName) => columns.GetValueOrDefault(fieldName);
    public TreeListColumn AddVisible(string fieldName) => columns[fieldName] = new TreeListColumn();
    public TreeListColumn[] GetColumns() => columns.Values.ToArray();
    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}

public sealed class GridColumn
{
    public int Width
    {
        get; set;
    }
    public string? Style
    {
        get; set;
    }
}

public sealed class TreeListColumn
{
    public int Width
    {
        get; set;
    }
    public string? Style
    {
        get; set;
    }
}

public sealed class MissingWidthColumn;

public static class StructuralColumnOperations
{
    public static ExecutorScope PrepareChanged<T>(T target)
    {
        EventHandler handler = OnChanged;
        return ExecutorBuilder.Start<T>(out var input).Call(nameof(ApplyChanged), [input, handler]).ToScope();
    }
    public static ExecutorScope PrepareGenerated<T>(T target, string style)
    {
        var plan = ExecutorBuilder.Start<T, EventArgs>(out var owner, out var args)
            .Call<IColumn>(out var column, nameof(StructuralColumnOperations.GetOrAddColumn), [owner, "Event"])
            .Then(() => OnGeneratedChanged())
            .Then(() => column.Val.ApplyStyle(style));
        var executor = plan.Build<IExecutor<T, EventArgs>>(new ExecutorBuildConfiguration().RegisterProvider(typeof(StructuralColumnOperations)));
        EventHandler handler = (sender, args) => executor.Execute((T)sender!, args);
        return ExecutorBuilder.Start().Reference<T>(out var input).Call(nameof(ApplyChanged), [input, handler]).ToScope();
    }
    public static void ApplyChanged(GridView target, EventHandler handler)
    {
        target.Changed -= handler;
        target.Changed += handler;
    }
    public static void ApplyChanged(TreeList target, EventHandler handler)
    {
        target.Changed -= handler;
        target.Changed += handler;
    }

    public static int EventCalls
    {
        get; set;
    }
    public static int GeneratedEventCalls
    {
        get; set;
    }
    public static IColumn GetOrAddColumn(this IHasColumns owner, string fieldName) => throw new NotSupportedException();
    public static GridColumn GetOrAddColumn(this GridView owner, string fieldName) => owner.ColumnByFieldName(fieldName) ?? owner.AddVisible(fieldName);
    public static TreeListColumn GetOrAddColumn(this TreeList owner, string fieldName) => owner.ColumnByFieldName(fieldName) ?? owner.AddVisible(fieldName);
    public static IColumn[] GetColumns(this IHasColumns owner) => throw new NotSupportedException();
    public static void ApplyStyle<TStyle>(this IColumn column, TStyle style) => throw new NotSupportedException();
    public static void ApplyStyle<TStyle>(this GridColumn column, TStyle style) => column.Style = style?.ToString();
    public static void ApplyStyle<TStyle>(this TreeListColumn column, TStyle style) => column.Style = style?.ToString();
    public static IColumn MissingColumn(this IHasColumns owner) => throw new NotSupportedException();
    public static void OnChanged(object? sender, EventArgs args) => EventCalls++;
    public static void OnGeneratedChanged() => GeneratedEventCalls++;
}
