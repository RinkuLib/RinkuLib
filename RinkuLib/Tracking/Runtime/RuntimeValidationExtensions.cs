namespace Rinku.Tracking.Runtime;

public static class RuntimeValidationExtensions {
    public static bool TryValidate(this IRuntimeMemberAccess? item, out bool valid) {
        valid = false;
        if (item is not IValidatable validator)
            return false;
        valid = validator.Validate();
        return true;
    }

    public static bool TryValidate<TContext>(this IRuntimeMemberAccess? item, TContext context, out bool valid) {
        valid = false;
        if (item is not IValidatable<TContext> validator)
            return false;
        valid = validator.Validate(context);
        return true;
    }

    public static bool TryValidateResult<TResult>(this IRuntimeMemberAccess? item, TResult result, out bool valid) {
        valid = false;
        if (item is not IResultValidatable<TResult> validator)
            return false;
        valid = validator.ValidateResult(result);
        return true;
    }
}
