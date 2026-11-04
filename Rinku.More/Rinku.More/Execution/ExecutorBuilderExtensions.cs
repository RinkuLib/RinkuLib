using System.Linq.Expressions;

namespace Rinku;

/// <summary>Provides expression conveniences for executor builders.</summary>
public static class ExecutorBuilderExtensions
{
    /// <summary>Records an expression for inline use as an operation argument.</summary>
    public static Variable<TResult> Value<TResult>(this ExecutorBuilder builder, Expression<Func<TResult>> expression, ExecutorRequirement requirement = ExecutorRequirement.Required)
    {
        builder.Then(out Variable<TResult> value, expression, requirement: requirement);
        return value;
    }
    /// <summary>Records a side-effect expression.</summary>
    public static ExecutorBuilder Then(this ExecutorBuilder builder, Expression<Action> expression, ExecutorRequirement requirement = ExecutorRequirement.Required)
        => builder.RecordAction(expression, requirement);

    /// <summary>Evaluates an expression and stores the result in an existing variable.</summary>
    public static ExecutorBuilder Then<TResult>(this ExecutorBuilder builder, Variable<TResult> destination, Expression<Func<TResult>> expression, ExecutorRequirement requirement = ExecutorRequirement.Required)
        => builder.RecordValue(expression, destination, requirement);

    /// <summary>Evaluates an expression and stores the result in an expression-selected destination.</summary>
    public static ExecutorBuilder Then<TDestination, TValue>(this ExecutorBuilder builder, Expression<Func<TDestination>> destination, Expression<Func<TValue>> expression, ExecutorRequirement requirement = ExecutorRequirement.Required)
        => builder.RecordAssignment(destination, expression, requirement);

    /// <summary>Stores a constant or plan variable in an expression-selected destination.</summary>
    public static ExecutorBuilder Then<TDestination>(this ExecutorBuilder builder, Expression<Func<TDestination>> destination, object? value, ExecutorRequirement requirement = ExecutorRequirement.Required)
        => builder.RecordAssignment(destination, value, requirement);

    /// <summary>Stores a constant in an existing variable.</summary>
    public static ExecutorBuilder Then<TResult>(this ExecutorBuilder builder, Variable<TResult> destination, TResult value, ExecutorRequirement requirement = ExecutorRequirement.Required)
        => builder.StoreValue(destination, value, requirement);

    /// <summary>Stores one variable in another variable.</summary>
    public static ExecutorBuilder Then<TResult>(this ExecutorBuilder builder, Variable<TResult> destination, Variable<TResult> value, ExecutorRequirement requirement = ExecutorRequirement.Required)
        => builder.StoreValue(destination, value, requirement);
}
