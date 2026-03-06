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