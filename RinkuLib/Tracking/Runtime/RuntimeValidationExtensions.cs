namespace Rinku.Tracking.Runtime;

/// <summary>Provides validation helpers for generated runtime tracking objects.</summary>
public static class RuntimeValidationExtensions {
    /// <summary>Validates an item when it implements the non-generic validation contract.</summary>
    public static bool TryValidate(this IRuntimeMemberAccess? item, out bool valid) {
        valid = false;
        if (item is not IValidatable validator)
            return false;
        valid = validator.Validate();
        return true;
    }

    /// <summary>Validates an item with a caller-supplied context.</summary>
    public static bool TryValidate<TContext>(this IRuntimeMemberAccess? item, TContext context, out bool valid) {
        valid = false;
        if (item is not IValidatable<TContext> validator)
            return false;
        valid = validator.Validate(context);
        return true;
    }

    /// <summary>Validates a result produced by an item.</summary>
    public static bool TryValidateResult<TResult>(this IRuntimeMemberAccess? item, TResult result, out bool valid) {
        valid = false;
        if (item is not IResultValidatable<TResult> validator)
            return false;
        valid = validator.ValidateResult(result);
        return true;
    }
}
