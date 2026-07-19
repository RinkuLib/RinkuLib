using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using RinkuLib.Queries;
using RinkuLib.Tools;

namespace RinkuLib.TypeAccessing;

/// <summary>
/// The "a collection with no elements is no value" rule, in the two depths the engine reads it at.
/// <see cref="MultiVariableHandler"/> is the handler that claims it, through
/// <see cref="SpecialHandler.CanHandle"/> and <see cref="SpecialHandler.GetUsageEmitter"/>.
/// </summary>
/// <remarks>
/// This is not the rule <see cref="NotNullOrWhitespaceAttribute"/> and <see cref="NotDefaultAttribute"/>
/// carry. Those say what the caller counts as supplying a value at all, the same call as reaching for
/// <c>Use</c> or leaving it alone, and they sit on the parameter object because that is whose decision it
/// is. This one is asked of a value that arrived, and it is the handler's own.
/// </remarks>
public static class SpreadUsage {
    /// <summary>
    /// The cheap look, for the presence check a parameter object is read with. Only a value carrying its own
    /// count answers here, which is a field read away. A sequence is passed through untouched rather than
    /// walked, since nothing on that road can hold the walk's result and the bind would pay for it twice.
    /// </summary>
    /// <remarks>
    /// This turns away the empty collections it can recognise at a glance and promises nothing about the
    /// rest, so it saves work without being an answer. <see cref="HasElement(ref object?)"/> is the answer,
    /// and it is asked of everything this lets through.
    /// </remarks>
    public static bool HasElement(object? value) => value switch {
        null => false,
        Array array => array.Length > 0,
        ICollection collection => collection.Count > 0,
        _ => true,
    };
    /// <summary>
    /// The whole answer, for the handler, which owns its slot and has to be right about any value that
    /// reaches it. A sequence that can only answer by being walked is walked, and the slot is replaced with
    /// one that replays the element that cost, so the bind that follows still reads every element once.
    /// </summary>
    /// <param name="value">The slot holding the value, rewritten when the answer cost an element.</param>
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

/// <summary>
/// Reads a member as supplied only when it does not hold a collection that is plainly empty, the cheap half
/// of the spread's rule compiled into the presence check.
/// </summary>
public class SpreadUsageEmitter(Type targetType, MemberInfo member) : IAccessorEmiter {
    private readonly Type TargetType = targetType;
    private readonly MemberInfo _member = member;

    private static readonly MethodInfo HasElementMethod =
        typeof(SpreadUsage).GetMethod(nameof(SpreadUsage.HasElement), [typeof(object)])!;

    /// <inheritdoc/>
    public void Emit(ILGenerator il) {
        IAccessorEmiter.EmitMemberLoad(il, TargetType, _member);
        Type mType = _member is FieldInfo f ? f.FieldType : ((PropertyInfo)_member).PropertyType;
        if (mType.IsValueType)
            il.Emit(OpCodes.Box, mType);
        il.Emit(OpCodes.Call, HasElementMethod);
    }
}
