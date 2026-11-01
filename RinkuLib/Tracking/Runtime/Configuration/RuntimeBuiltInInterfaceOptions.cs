using System.ComponentModel;
using Rinku.Tracking.Binding;

namespace Rinku.Tracking.Runtime.Configuration;

internal static class RuntimeBuiltInInterfaceOptions
{
    internal static bool TryApply<TOriginal>(RuntimeTrackingTypeDefinition<TOriginal> type, Type contract)
    {
        if (contract == typeof(IEditable) ||
            contract == typeof(ICanInsert) ||
            contract == typeof(ICanUpdate) ||
            contract == typeof(ITrackingListNewState) ||
            contract == typeof(ITrackingConfirmationSource) ||
            contract == typeof(IRuntimeMemberAccess) ||
            contract == typeof(ITrackingChanges) ||
            contract == typeof(ITrackingItem<TOriginal>) ||
            contract == typeof(IOriginal<TOriginal>) ||
            contract == typeof(IOriginalWriter<TOriginal>))
            return true;

        if (contract == typeof(ITrackingParameters)) {
            var property = contract.GetProperty(nameof(ITrackingParameters.PendingParameters))!;
            var member = type.GetOrAddMember(property.Name, property.PropertyType);
            member.Emitter ??= new RuntimeDirectFieldEmitter<TOriginal>();
            member.IncludeInParameters = false;
            member.ExposeProperty = false;
            member.AddRequirement(property);
            type.ConfirmedPolicy += static item => item.PendingParameters = null;
            type.RevertedPolicy += static item => item.PendingParameters = null;
            return true;
        }

        if (contract == typeof(INotifyPropertyChanged) || contract == typeof(IEditableObject))
        {
            RuntimeBindingOption<TOriginal>.Instance.Apply(type);
            return true;
        }

        return false;
    }
}
