using Rinku.Tracking;
using Rinku.Tracking.Runtime;

namespace Rinku.More;

public static class TrackingValidation {
    public static RuntimeTrackingOptions<T> WithValidation<T>(this RuntimeTrackingOptions<T> options) => options.Member((IValidationState state) => state.Validation, member => member.Direct().Parameters(false).ResetOnConfirmAndRevert(null));
    public static IValidationMessages? GetValidation(this IRuntimeMemberAccess? item) => (item as IValidationState)?.Validation;
    public static bool Validate<TItem>(this IExecutor<TItem, string?, ValidationErrors> validator, TItem item) where TItem : IRuntimeMemberAccess => validator.Validate(item, null);
    public static bool Validate<TItem>(this IExecutor<TItem, string?, ValidationErrors> validator, TItem item, string? field) where TItem : IRuntimeMemberAccess {
        if (item is not IValidationState state)
            return false;
        var errors = OwnedErrors(state);
        validator.Execute(item, field, errors);
        state.Validation = errors;
        return !errors.HasErrors;
    }
    public static bool ApplyValidation(this IRuntimeMemberAccess item, ValidationErrors result) {
        if (item is not IValidationState state)
            return false;
        var errors = OwnedErrors(state);
        errors.ReplaceWith(result);
        state.Validation = errors;
        return !errors.HasErrors;
    }
    private static ValidationErrors OwnedErrors(IValidationState item) => item.Validation is { IsReadOnly: false } errors ? errors : new ValidationErrors();
}
