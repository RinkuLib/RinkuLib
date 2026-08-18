using System;
using System.ComponentModel;
using System.Reflection;

namespace Rinku.Tracking.Runtime;

// Strong-contract lists expose a schema that works for any implementation of TEdit, not only the emitted CLR type.
internal static class RuntimeContractPropertyDescriptors<TOriginal, TEdit>
    where TEdit : class, IRuntimeTrackingItem<TOriginal> {
    internal static PropertyDescriptorCollection Create(Type generatedType) {
        PropertyDescriptorCollection generated = TypeDescriptor.GetProperties(generatedType);
        var descriptors = new PropertyDescriptor[generated.Count];
        int count = 0;
        for (int i = 0; i < generated.Count; i++) {
            PropertyDescriptor source = generated[i];
            PropertyInfo? contract = RuntimeTrackingContract<TOriginal, TEdit>.FindProperty(source.Name, source.PropertyType);
            if (contract is null) continue;
            descriptors[count++] = new ContractPropertyDescriptor(source, contract);
        }
        if (count != descriptors.Length) Array.Resize(ref descriptors, count);
        return new(descriptors, true);
    }

    private sealed class ContractPropertyDescriptor : PropertyDescriptor {
        private readonly PropertyInfo _property;
        private readonly MethodInfo _getter;
        private readonly MethodInfo? _setter;
        private readonly Type _propertyType;

        internal ContractPropertyDescriptor(PropertyDescriptor source, PropertyInfo property)
            : base(source.Name, Attributes(source)) {
            _property = property;
            _getter = property.GetMethod ?? throw new InvalidOperationException($"Contract property {property} has no getter.");
            _setter = property.SetMethod;
            _propertyType = source.PropertyType;
        }

        public override Type ComponentType => typeof(TEdit);
        public override bool IsReadOnly => _setter is null;
        public override Type PropertyType => _propertyType;
        public override bool CanResetValue(object component) => false;
        public override object? GetValue(object? component) => _getter.Invoke(component, null);
        public override void ResetValue(object component) { }
        public override void SetValue(object? component, object? value) {
            if (_setter is null) throw new InvalidOperationException($"Contract property {_property} is read-only.");
            _setter.Invoke(component, [value]);
        }
        public override bool ShouldSerializeValue(object component) => false;

        private static new Attribute[] Attributes(PropertyDescriptor source) {
            var attributes = new Attribute[source.Attributes.Count];
            source.Attributes.CopyTo(attributes, 0);
            return attributes;
        }
    }
}
