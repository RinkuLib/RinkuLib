using Rinku.Tracking;
using Rinku.Tracking.Runtime;

namespace Rinku.More;

public static class TrackingPolicies {
    public static RuntimeTrackingOptions<T> OnConfirmAndRevert<T>(this RuntimeTrackingOptions<T> options, Action<ITrackingItem<T>> policy) => options.OnConfirm(policy).OnRevert(policy);

    public static RuntimeTrackingMemberOptions<T, TValue> OnConfirmAndRevert<T, TValue>(this RuntimeTrackingMemberOptions<T, TValue> member, Action<ITrackingItem<T>> policy) => member.OnConfirm(policy).OnRevert(policy);

    public static RuntimeTrackingMemberOptions<T, TValue> ResetOnConfirmAndRevert<T, TValue>(this RuntimeTrackingMemberOptions<T, TValue> member, TValue value) {
        string name = member.Name;
        return member.OnConfirmAndRevert(item => item.Set(name, value));
    }
}
