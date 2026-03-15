using RinkuLib.Tools;

namespace RinkuLib.DBActions;
/// <summary></summary>
public static class ToManyRelation<TParent, TItem> {
    /// <inheritdoc/>
    public static ToManyRelation<TParent, TID, TItem> New<TID>(Getter<TParent, TID> Getter, Setter<TParent, PooledArray<TItem>> Setter) => new(Getter, Setter);
    /// <inheritdoc/>
    public static ToManyRelationTwoLevel<TParent, TParentID, TTransient, TTransientID, TItem, ArrayAccess<TTransient>> New<TParentID, TTransient, TTransientID>(Getter<TParent, TParentID> GetterParentID, Getter<TParent, ArrayAccess<TTransient>> GetterTransients, Getter<TTransient, TTransientID> GetterTransientID, Setter<TTransient, PooledArray<TItem>> SetterTransient) => new(GetterParentID, GetterTransients, GetterTransientID, SetterTransient);
    /// <inheritdoc/>
    public static ToManyRelationTwoLevel<TParent, TParentID, TTransient, TTransientID, TItem, ListAccess<TTransient>> New<TParentID, TTransient, TTransientID>(Getter<TParent, TParentID> GetterParentID, Getter<TParent, ListAccess<TTransient>> GetterTransients, Getter<TTransient, TTransientID> GetterTransientID, Setter<TTransient, PooledArray<TItem>> SetterTransient) => new(GetterParentID, GetterTransients, GetterTransientID, SetterTransient);
}
