namespace Rinku;

/// <summary>Creates logical executor builders.</summary>
public partial class ExecutorBuilder
{
    /// <summary>Creates a plan fragment without inputs.</summary>
    public static ExecutorBuilder Start() => new();

    /// <summary>Creates a one-input executor builder and exposes its input value.</summary>
    public static ExecutorBuilder Start<T1>(out Variable<T1> input1, string? name1 = null)
    {
        ExecutorBuilder builder = new(typeof(T1));
        input1 = builder.Input<T1>(0, name1);
        return builder;
    }

    /// <summary>Creates a two-input executor builder and exposes its input values.</summary>
    public static ExecutorBuilder Start<T1, T2>(out Variable<T1> input1, out Variable<T2> input2, string? name1 = null, string? name2 = null)
    {
        ExecutorBuilder builder = new(typeof(T1), typeof(T2));
        input1 = builder.Input<T1>(0, name1);
        input2 = builder.Input<T2>(1, name2);
        return builder;
    }

    /// <summary>Creates a three-input executor builder and exposes its input values.</summary>
    public static ExecutorBuilder Start<T1, T2, T3>(out Variable<T1> input1, out Variable<T2> input2, out Variable<T3> input3, string? name1 = null, string? name2 = null, string? name3 = null)
    {
        ExecutorBuilder builder = new(typeof(T1), typeof(T2), typeof(T3));
        input1 = builder.Input<T1>(0, name1);
        input2 = builder.Input<T2>(1, name2);
        input3 = builder.Input<T3>(2, name3);
        return builder;
    }

    /// <summary>Creates a four-input executor builder and exposes its input values.</summary>
    public static ExecutorBuilder Start<T1, T2, T3, T4>(out Variable<T1> input1, out Variable<T2> input2, out Variable<T3> input3, out Variable<T4> input4, string? name1 = null, string? name2 = null, string? name3 = null, string? name4 = null)
    {
        ExecutorBuilder builder = new(typeof(T1), typeof(T2), typeof(T3), typeof(T4));
        input1 = builder.Input<T1>(0, name1);
        input2 = builder.Input<T2>(1, name2);
        input3 = builder.Input<T3>(2, name3);
        input4 = builder.Input<T4>(3, name4);
        return builder;
    }

    /// <summary>Creates a five-input executor builder and exposes its input values.</summary>
    public static ExecutorBuilder Start<T1, T2, T3, T4, T5>(out Variable<T1> input1, out Variable<T2> input2, out Variable<T3> input3, out Variable<T4> input4, out Variable<T5> input5, string? name1 = null, string? name2 = null, string? name3 = null, string? name4 = null, string? name5 = null)
    {
        ExecutorBuilder builder = new(typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5));
        input1 = builder.Input<T1>(0, name1);
        input2 = builder.Input<T2>(1, name2);
        input3 = builder.Input<T3>(2, name3);
        input4 = builder.Input<T4>(3, name4);
        input5 = builder.Input<T5>(4, name5);
        return builder;
    }

    /// <summary>Creates a six-input executor builder and exposes its input values.</summary>
    public static ExecutorBuilder Start<T1, T2, T3, T4, T5, T6>(out Variable<T1> input1, out Variable<T2> input2, out Variable<T3> input3, out Variable<T4> input4, out Variable<T5> input5, out Variable<T6> input6, string? name1 = null, string? name2 = null, string? name3 = null, string? name4 = null, string? name5 = null, string? name6 = null)
    {
        ExecutorBuilder builder = new(typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6));
        input1 = builder.Input<T1>(0, name1);
        input2 = builder.Input<T2>(1, name2);
        input3 = builder.Input<T3>(2, name3);
        input4 = builder.Input<T4>(3, name4);
        input5 = builder.Input<T5>(4, name5);
        input6 = builder.Input<T6>(5, name6);
        return builder;
    }

    /// <summary>Creates a seven-input executor builder and exposes its input values.</summary>
    public static ExecutorBuilder Start<T1, T2, T3, T4, T5, T6, T7>(out Variable<T1> input1, out Variable<T2> input2, out Variable<T3> input3, out Variable<T4> input4, out Variable<T5> input5, out Variable<T6> input6, out Variable<T7> input7, string? name1 = null, string? name2 = null, string? name3 = null, string? name4 = null, string? name5 = null, string? name6 = null, string? name7 = null)
    {
        ExecutorBuilder builder = new(typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6), typeof(T7));
        input1 = builder.Input<T1>(0, name1);
        input2 = builder.Input<T2>(1, name2);
        input3 = builder.Input<T3>(2, name3);
        input4 = builder.Input<T4>(3, name4);
        input5 = builder.Input<T5>(4, name5);
        input6 = builder.Input<T6>(5, name6);
        input7 = builder.Input<T7>(6, name7);
        return builder;
    }

    /// <summary>Creates an eight-input executor builder and exposes its input values.</summary>
    public static ExecutorBuilder Start<T1, T2, T3, T4, T5, T6, T7, T8>(out Variable<T1> input1, out Variable<T2> input2, out Variable<T3> input3, out Variable<T4> input4, out Variable<T5> input5, out Variable<T6> input6, out Variable<T7> input7, out Variable<T8> input8, string? name1 = null, string? name2 = null, string? name3 = null, string? name4 = null, string? name5 = null, string? name6 = null, string? name7 = null, string? name8 = null)
    {
        ExecutorBuilder builder = new(typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6), typeof(T7), typeof(T8));
        input1 = builder.Input<T1>(0, name1);
        input2 = builder.Input<T2>(1, name2);
        input3 = builder.Input<T3>(2, name3);
        input4 = builder.Input<T4>(3, name4);
        input5 = builder.Input<T5>(4, name5);
        input6 = builder.Input<T6>(5, name6);
        input7 = builder.Input<T7>(6, name7);
        input8 = builder.Input<T8>(7, name8);
        return builder;
    }
}
