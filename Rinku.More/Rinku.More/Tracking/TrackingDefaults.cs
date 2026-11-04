using System.ComponentModel;
using System.Linq.Expressions;
using System.Reflection;
using Rinku.Tracking.Runtime;
using Rinku.Tracking;

namespace Rinku.More;

public static class TrackingDefaults {
    public static RuntimeTrackingOptions<T> DefaultNewValues<T>(this RuntimeTrackingOptions<T> options) => options.InitializeNew<ITrackingItem<T>>(Defaults<T>.Initialize);

    private static class Defaults<T> {
        internal static readonly Action<ITrackingItem<T>> Initialize = Compile();
        private static Action<ITrackingItem<T>> Compile() {
            var item = Expression.Parameter(typeof(ITrackingItem<T>), "item");
            var calls = new List<Expression>();
            var nullability = new NullabilityInfoContext();
            var set = typeof(IRuntimeMemberAccess).GetMethod(nameof(IRuntimeMemberAccess.Set), [typeof(string), Type.MakeGenericMethodParameter(0)])!;
            foreach (var member in typeof(T).GetMembers(BindingFlags.Public | BindingFlags.Instance)) {
                Type type;
                NullabilityState nullable;
                if (member is PropertyInfo { SetMethod: not null } property && property.GetIndexParameters().Length == 0) {
                    type = property.PropertyType;
                    nullable = nullability.Create(property).WriteState;
                }
                else if (member is FieldInfo { IsInitOnly: false } field) {
                    type = field.FieldType;
                    nullable = nullability.Create(field).WriteState;
                }
                else
                    continue;
                Expression value;
                if (member.GetCustomAttribute<DefaultValueAttribute>() is { } specified)
                    value = Expression.Constant(specified.Value, type);
                else if (nullable == NullabilityState.NotNull && type == typeof(string))
                    value = Expression.Constant(string.Empty);
                else if (nullable == NullabilityState.NotNull && type.IsSZArray)
                    value = Expression.Call(typeof(Array).GetMethod(nameof(Array.Empty))!.MakeGenericMethod(type.GetElementType()!));
                else if (nullable == NullabilityState.NotNull && !type.IsValueType && typeof(ITrackingValue<>).MakeGenericType(type).IsAssignableFrom(type) && type.GetConstructor(Type.EmptyTypes) is { } constructor)
                    value = Expression.New(constructor);
                else
                    continue;
                calls.Add(Expression.Call(item, set.MakeGenericMethod(type), Expression.Constant(member.Name), value));
            }
            calls.Add(Expression.Empty());
            return Expression.Lambda<Action<ITrackingItem<T>>>(Expression.Block(calls), item).Compile();
        }
    }
}
