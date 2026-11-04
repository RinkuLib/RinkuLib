namespace Rinku;

/// <summary>Marks an authoring surface whose members are resolved structurally for each requested execution signature.</summary>
/// <remarks>A concrete specialization does not need to implement the marked contract. Members used by the plan are resolved during <c>Build</c>.</remarks>
public interface IExpressionContract;

public interface IExecutor;

/// <summary>Represents an emitted executor for one input.</summary>
/// <typeparam name="T">The type accepted by the executor.</typeparam>
public interface IExecutor<in T> : IExecutor
{
    /// <summary>Runs the plan with <paramref name="value"/>.</summary>
    /// <param name="value">The input value.</param>
    void Execute(T value);
}

/// <summary>Represents an emitted executor for two inputs.</summary>
/// <typeparam name="T1">The first type accepted by the executor.</typeparam>
/// <typeparam name="T2">The second type accepted by the executor.</typeparam>
public interface IExecutor<in T1, in T2> : IExecutor
{
    /// <summary>Runs the plan with the two input values.</summary>
    /// <param name="value1">The first input value.</param>
    /// <param name="value2">The second input value.</param>
    void Execute(T1 value1, T2 value2);
}

/// <summary>Represents an emitted executor for three inputs.</summary>
/// <typeparam name="T1">The first type accepted by the executor.</typeparam>
/// <typeparam name="T2">The second type accepted by the executor.</typeparam>
/// <typeparam name="T3">The third type accepted by the executor.</typeparam>
public interface IExecutor<in T1, in T2, in T3> : IExecutor
{
    /// <summary>Runs the plan with the three input values.</summary>
    /// <param name="value1">The first input value.</param>
    /// <param name="value2">The second input value.</param>
    /// <param name="value3">The third input value.</param>
    void Execute(T1 value1, T2 value2, T3 value3);
}

/// <summary>Represents an emitted executor for four inputs.</summary>
/// <typeparam name="T1">The first type accepted by the executor.</typeparam>
/// <typeparam name="T2">The second type accepted by the executor.</typeparam>
/// <typeparam name="T3">The third type accepted by the executor.</typeparam>
/// <typeparam name="T4">The fourth type accepted by the executor.</typeparam>
public interface IExecutor<in T1, in T2, in T3, in T4> : IExecutor
{
    /// <summary>Runs the plan with the four input values.</summary>
    /// <param name="value1">The first input value.</param>
    /// <param name="value2">The second input value.</param>
    /// <param name="value3">The third input value.</param>
    /// <param name="value4">The fourth input value.</param>
    void Execute(T1 value1, T2 value2, T3 value3, T4 value4);
}

/// <summary>Represents an emitted executor for five inputs.</summary>
/// <typeparam name="T1">The first type accepted by the executor.</typeparam>
/// <typeparam name="T2">The second type accepted by the executor.</typeparam>
/// <typeparam name="T3">The third type accepted by the executor.</typeparam>
/// <typeparam name="T4">The fourth type accepted by the executor.</typeparam>
/// <typeparam name="T5">The fifth type accepted by the executor.</typeparam>
public interface IExecutor<in T1, in T2, in T3, in T4, in T5> : IExecutor
{
    /// <summary>Runs the plan with the five input values.</summary>
    /// <param name="value1">The first input value.</param>
    /// <param name="value2">The second input value.</param>
    /// <param name="value3">The third input value.</param>
    /// <param name="value4">The fourth input value.</param>
    /// <param name="value5">The fifth input value.</param>
    void Execute(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5);
}

/// <summary>Represents an emitted executor for six inputs.</summary>
/// <typeparam name="T1">The first type accepted by the executor.</typeparam>
/// <typeparam name="T2">The second type accepted by the executor.</typeparam>
/// <typeparam name="T3">The third type accepted by the executor.</typeparam>
/// <typeparam name="T4">The fourth type accepted by the executor.</typeparam>
/// <typeparam name="T5">The fifth type accepted by the executor.</typeparam>
/// <typeparam name="T6">The sixth type accepted by the executor.</typeparam>
public interface IExecutor<in T1, in T2, in T3, in T4, in T5, in T6> : IExecutor
{
    /// <summary>Runs the plan with the six input values.</summary>
    /// <param name="value1">The first input value.</param>
    /// <param name="value2">The second input value.</param>
    /// <param name="value3">The third input value.</param>
    /// <param name="value4">The fourth input value.</param>
    /// <param name="value5">The fifth input value.</param>
    /// <param name="value6">The sixth input value.</param>
    void Execute(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6);
}

/// <summary>Represents an emitted executor for seven inputs.</summary>
/// <typeparam name="T1">The first type accepted by the executor.</typeparam>
/// <typeparam name="T2">The second type accepted by the executor.</typeparam>
/// <typeparam name="T3">The third type accepted by the executor.</typeparam>
/// <typeparam name="T4">The fourth type accepted by the executor.</typeparam>
/// <typeparam name="T5">The fifth type accepted by the executor.</typeparam>
/// <typeparam name="T6">The sixth type accepted by the executor.</typeparam>
/// <typeparam name="T7">The seventh type accepted by the executor.</typeparam>
public interface IExecutor<in T1, in T2, in T3, in T4, in T5, in T6, in T7> : IExecutor
{
    /// <summary>Runs the plan with the seven input values.</summary>
    /// <param name="value1">The first input value.</param>
    /// <param name="value2">The second input value.</param>
    /// <param name="value3">The third input value.</param>
    /// <param name="value4">The fourth input value.</param>
    /// <param name="value5">The fifth input value.</param>
    /// <param name="value6">The sixth input value.</param>
    /// <param name="value7">The seventh input value.</param>
    void Execute(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7);
}

/// <summary>Represents an emitted executor for eight inputs.</summary>
/// <typeparam name="T1">The first type accepted by the executor.</typeparam>
/// <typeparam name="T2">The second type accepted by the executor.</typeparam>
/// <typeparam name="T3">The third type accepted by the executor.</typeparam>
/// <typeparam name="T4">The fourth type accepted by the executor.</typeparam>
/// <typeparam name="T5">The fifth type accepted by the executor.</typeparam>
/// <typeparam name="T6">The sixth type accepted by the executor.</typeparam>
/// <typeparam name="T7">The seventh type accepted by the executor.</typeparam>
/// <typeparam name="T8">The eighth type accepted by the executor.</typeparam>
public interface IExecutor<in T1, in T2, in T3, in T4, in T5, in T6, in T7, in T8> : IExecutor
{
    /// <summary>Runs the plan with the eight input values.</summary>
    /// <param name="value1">The first input value.</param>
    /// <param name="value2">The second input value.</param>
    /// <param name="value3">The third input value.</param>
    /// <param name="value4">The fourth input value.</param>
    /// <param name="value5">The fifth input value.</param>
    /// <param name="value6">The sixth input value.</param>
    /// <param name="value7">The seventh input value.</param>
    /// <param name="value8">The eighth input value.</param>
    void Execute(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8);
}

/// <summary>Describes a comparison operation in an executor plan.</summary>
internal enum ExecutorComparison : byte
{
    /// <summary>Tests equality.</summary>
    Equal,

    /// <summary>Tests inequality.</summary>
    NotEqual,

    /// <summary>Tests whether the left value is less than the right value.</summary>
    LessThan,

    /// <summary>Tests whether the left value is less than or equal to the right value.</summary>
    LessThanOrEqual,

    /// <summary>Tests whether the left value is greater than the right value.</summary>
    GreaterThan,

    /// <summary>Tests whether the left value is greater than or equal to the right value.</summary>
    GreaterThanOrEqual
}

/// <summary>Describes a binary value operation in an executor plan.</summary>
internal enum ExecutorBinaryOperator : byte
{
    /// <summary>Adds two values.</summary>
    Add,

    /// <summary>Subtracts the right value from the left value.</summary>
    Subtract,

    /// <summary>Multiplies two values.</summary>
    Multiply,

    /// <summary>Divides the left value by the right value.</summary>
    Divide,

    /// <summary>Computes the remainder of the left value divided by the right value.</summary>
    Modulo,

    /// <summary>Computes a bitwise and operation.</summary>
    And,

    /// <summary>Computes a bitwise or operation.</summary>
    Or,

    /// <summary>Computes a bitwise exclusive-or operation.</summary>
    ExclusiveOr,

    /// <summary>Shifts the left value left.</summary>
    LeftShift,

    /// <summary>Shifts the left value right.</summary>
    RightShift
}

/// <summary>Describes a unary value operation.</summary>
internal enum ExecutorUnaryOperator : byte
{
    /// <summary>Negates a numeric value.</summary>
    Negate,

    /// <summary>Computes the bitwise complement of an integral value.</summary>
    OnesComplement
}

/// <summary>Describes a short-circuiting logical operation.</summary>
internal enum ExecutorLogicalOperator : byte
{
    /// <summary>Evaluates the right branch only when the left value is true.</summary>
    AndAlso,

    /// <summary>Evaluates the right branch only when the left value is false.</summary>
    OrElse
}

/// <summary>Determines how compilation handles a missing implementation or unavailable input.</summary>
public enum ExecutorRequirement : byte
{
    /// <summary>Fails compilation when the operation cannot bind.</summary>
    Required,
    /// <summary>Omits the operation and leaves its new outputs unavailable.</summary>
    Optional,
    /// <summary>Omits the operation and materializes its new outputs as null, lifting value types to Nullable.</summary>
    Nullable
}

/// <summary>Specifies how a symbolic call passes an operand.</summary>
public enum ExecutorArgumentPassing : byte
{
    Value,
    Ref,
    Out,
    In
}
