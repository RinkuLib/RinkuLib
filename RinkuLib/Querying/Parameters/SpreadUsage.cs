using System.Collections;
using Rinku.Querying.Defaults;
using System.Reflection;
using System.Reflection.Emit;
using Rinku.Querying;
using Rinku.Internal;

namespace Rinku.Querying.Parameters;

internal static class SpreadUsage {
    public static bool HasElement(object? value) => value switch {
        null => false,
        Array array => array.Length > 0,
        ICollection collection => collection.Count > 0,
        _ => true,
    };
    public static bool HasElement(ref object? value) {
        switch (value) {
            case null:
                return false;
            case Array array:
                return array.Length > 0;
            case ICollection collection:
                return collection.Count > 0;
            case not IEnumerable:
            case string:
                return true;
        }
        var source = (IEnumerable)value;
        if (source is IEnumerable<object> generic && generic.TryGetNonEnumeratedCount(out var nb))
            return nb > 0;
        if (source.TryGetNonEnumeratedCount(out nb))
            return nb > 0;
        var enumerator = source.GetEnumerator();
        if (enumerator.MoveNext()) {
            value = new PeekableWrapper(enumerator.Current, enumerator);
            return true;
        }
        (enumerator as IDisposable)?.Dispose();
        return false;
    }
}

internal class SpreadUsageEmitter(Type targetType, MemberInfo member) {
    private readonly Type TargetType = targetType;
    private readonly MemberInfo _member = member;

    private static readonly MethodInfo HasElementMethod =
        typeof(SpreadUsage).GetMethod(nameof(SpreadUsage.HasElement), [typeof(object)])!;

    public void Emit(ILGenerator il, int sourceArgument) {
        AccessorEmitter.EmitMemberLoad(il, TargetType, _member, sourceArgument);
        Type mType = _member is FieldInfo f ? f.FieldType : ((PropertyInfo)_member).PropertyType;
        if (mType.IsValueType)
            il.Emit(OpCodes.Box, mType);
        il.Emit(OpCodes.Call, HasElementMethod);
    }
}
