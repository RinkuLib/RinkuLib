using System.Collections;
using System.ComponentModel;

namespace Rinku.Caching;

public interface ICacheUpdate<T>
{
    void Apply(ICacheCollection<T> target, IEnumerable<T> items, bool full);
}

public class ReplaceCacheItems<T> : ICacheUpdate<T>
{
    public void Apply(ICacheCollection<T> target, IEnumerable<T> items, bool full) => target.SetItems(items);
}

public class UpsertCacheItems<T, TKey>(Func<T, TKey> key, ListSortDirection? order = null) : ICacheUpdate<T>
{
    public void Apply(ICacheCollection<T> target, IEnumerable<T> items, bool full)
    {
        if (full)
        {
            target.SetItems(items);
            return;
        }
        T[] changes = [.. items];
        lock (target is ICollection collection ? collection.SyncRoot : target)
        {
            int index = 0;
            foreach (var item in changes)
            {
                TKey id = key(item);
                if (!order.HasValue)
                {
                    index = 0;
                }
                while (index < target.Count)
                {
                    TKey existing = key(target[index]);
                    bool stop = order switch
                    {
                        ListSortDirection.Ascending => Comparer<TKey>.Default.Compare(existing, id) >= 0,
                        ListSortDirection.Descending => Comparer<TKey>.Default.Compare(existing, id) <= 0,
                        _ => EqualityComparer<TKey>.Default.Equals(existing, id)
                    };
                    if (stop)
                    {
                        break;
                    }
                    index++;
                }
                if (index < target.Count && EqualityComparer<TKey>.Default.Equals(key(target[index]), id))
                {
                    if (!EqualityComparer<T>.Default.Equals(target[index], item))
                    {
                        target[index] = item;
                    }
                }
                else
                {
                    target.Insert(index, item);
                }
            }
        }
    }
}
