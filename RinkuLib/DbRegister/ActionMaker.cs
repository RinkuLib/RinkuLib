using System.Reflection;

namespace RinkuLib.DbRegister;

/// <summary></summary>
public abstract class ActionMaker : Attribute {
    /// <summary></summary>
    public abstract (string Name, DbAction<TObj> Action) MakeAction<TObj>(MemberInfo? member);
}
/// <summary></summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method)]
public sealed class ToManyAttribute(string idMemberName, string query, string keyColumnName) : ActionMaker {
    private readonly string _idMemberName = idMemberName;
    private readonly string _keyColumnName = keyColumnName;
    private readonly string _query = query;
    ///<inheritdoc/>
    public override (string Name, DbAction<TObj> Action) MakeAction<TObj>(MemberInfo? member) {
        Type tObj = typeof(TObj);
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                   BindingFlags.Instance | BindingFlags.Static;
        ArgumentNullException.ThrowIfNull(member);
        MemberInfo idMember = tObj.GetMember(_idMemberName, flags).FirstOrDefault()
                               ?? throw new MissingMemberException(tObj.Name, _idMemberName);
        return (member.Name, (DbAction<TObj>)CollectionRelationActionFactory.Build(tObj, member, idMember, _query, _keyColumnName));
    }
}