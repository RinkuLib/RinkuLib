using System.Collections;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace RinkuLib.DbRegister;

/// <summary></summary>
public abstract class ActionMaker : Attribute {
    /// <summary></summary>
    public abstract (string Name, IDbAction<TObj> Action, bool IsDefault) MakeAction<TObj>(MemberInfo? member);
}
/// <summary></summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method)]
public sealed class PopulateListAttribute(string idMemberName, string query, bool isDefault) : ActionMaker {
    private readonly string _idMemberName = idMemberName;
    private readonly string _query = query;
    private readonly bool _isDefault = isDefault;
    ///<inheritdoc/>
    public override (string Name, IDbAction<TObj> Action, bool IsDefault) MakeAction<TObj>(MemberInfo? member) {
        Type tObj = typeof(TObj);
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                   BindingFlags.Instance | BindingFlags.Static;
        ArgumentNullException.ThrowIfNull(member);
        MemberInfo idMember = tObj.GetMember(_idMemberName, flags).FirstOrDefault()
                               ?? throw new MissingMemberException(tObj.Name, _idMemberName);
        return (member.Name, (IDbAction<TObj>)PopulateListActionFactory.Build(tObj, member, idMember, _query), _isDefault);
    }
}
/// <summary></summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method)]
public sealed class AddSubItemDefaultDbActionsAttribute(bool isDefault) : ActionMaker {
    private readonly bool _isDefault = isDefault;
    ///<inheritdoc/>
    public override (string Name, IDbAction<TObj> Action, bool IsDefault) MakeAction<TObj>(MemberInfo? member) {
        Type tObj = typeof(TObj);
        ArgumentNullException.ThrowIfNull(member);
        Type listType = member switch {
            FieldInfo f => f.FieldType,
            PropertyInfo p => p.PropertyType,
            MethodInfo m => PopulateListActionFactory.GetListTypeFromMethod(m, tObj),
            _ => throw new NotSupportedException($"Unsupported List member: {member.MemberType}")
        };
        if (!listType.IsGenericType || listType.GetGenericTypeDefinition() != typeof(List<>))
            throw new ArgumentException($"Member must be List<T>. Found: {listType.Name}");

        Type tItem = listType.GetGenericArguments()[0];
        var getter = AccessorFactory.CreateGetter(tObj, listType, member);
        var actionType = typeof(SubItemAction<,>).MakeGenericType(tObj, tItem);

        return ($"{member.Name}.{DbActions.ExecuteDefaultActions}", (IDbAction<TObj>)Activator.CreateInstance(actionType, getter)!, _isDefault);
    }
}