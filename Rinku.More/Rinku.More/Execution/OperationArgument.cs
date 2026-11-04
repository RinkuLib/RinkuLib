using System.Runtime.CompilerServices;

namespace Rinku;

public readonly struct OperationArgument
{
    private OperationArgument(OperationArgumentKind kind, Type type, object? data)
    {
        Kind = kind;
        this.type = type;
        Data = data;
    }

    public Type AuthoringType => Type;
    public bool IsConstant => !IsValue && !IsVariable;
    public Variable? Variable => IsValue ? global::Rinku.Variable.Create(Value.Plan, Value) : IsVariable ? global::Rinku.Variable.Create(Local.Plan, Local) : null;
    public static OperationArgument Constant<T>(T value) => FromConstant(typeof(T), value);
    public static OperationArgument FromVariable(Variable value) => value.IsLocal ? FromLocal(value.Local) : FromValue(value.Value);
    public static implicit operator OperationArgument(Variable value) => FromVariable(value);
    internal static OperationArgument FromRaw(object? value) => value is OperationArgument argument ? argument : value is Variable variable ? FromVariable(variable) : FromConstant(value?.GetType() ?? typeof(object), value);

    internal OperationArgumentKind Kind
    {
        get;
    }
    private readonly Type? type;
    internal Type Type => type ?? typeof(object);
    internal object? Data
    {
        get;
    }
    internal PlanValue Value => Kind == OperationArgumentKind.Value ? (PlanValue)Data! : throw new InvalidOperationException();
    internal PlanLocal Local => Kind == OperationArgumentKind.Variable ? (PlanLocal)Data! : throw new InvalidOperationException();
    internal bool IsValue => Kind == OperationArgumentKind.Value;
    internal bool IsVariable => Kind == OperationArgumentKind.Variable;
    internal static OperationArgument FromValue(PlanValue value) => new(OperationArgumentKind.Value, value.LogicalType, value);
    internal static OperationArgument FromLocal(PlanLocal local) => new(OperationArgumentKind.Variable, local.LogicalType, local);
    internal static OperationArgument FromConstant(Type type, object? value) => new(OperationArgumentKind.Constant, type, value);
    internal static OperationArgument FromTypedConstant(Type type, object? value) => new(OperationArgumentKind.TypedConstant, type, value);
}

internal enum OperationArgumentKind : byte
{
    Constant, Value, Variable, TypedConstant
}

internal sealed class OperationArgumentComparer : IEqualityComparer<OperationArgument>
{
    internal static OperationArgumentComparer Instance { get; } = new();

    public bool Equals(OperationArgument x, OperationArgument y)
    {
        if (x.Kind != y.Kind || x.Type != y.Type)
            return false;
        if (x.Kind == OperationArgumentKind.Value)
            return ReferenceEquals(x.Value, y.Value);
        if (x.Kind == OperationArgumentKind.Variable)
            return ReferenceEquals(x.Local, y.Local);
        if (x.Data is null || y.Data is null)
            return x.Data is null && y.Data is null;
        if (IsStableValue(x.Data))
            return x.Data.Equals(y.Data);
        return ReferenceEquals(x.Data, y.Data);
    }

    public int GetHashCode(OperationArgument obj)
    {
        HashCode hash = new();
        hash.Add(obj.Kind);
        hash.Add(obj.Type);
        if (obj.Kind == OperationArgumentKind.Value)
            hash.Add(RuntimeHelpers.GetHashCode(obj.Value));
        else if (obj.Kind == OperationArgumentKind.Variable)
            hash.Add(RuntimeHelpers.GetHashCode(obj.Local));
        else if (obj.Data is not null)
            hash.Add(IsStableValue(obj.Data) ? obj.Data.GetHashCode() : RuntimeHelpers.GetHashCode(obj.Data));
        return hash.ToHashCode();
    }

    private static bool IsStableValue(object value) => value is string || value.GetType().IsPrimitive || value.GetType().IsEnum || value is decimal;
}
