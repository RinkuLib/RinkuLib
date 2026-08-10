using System.Data.Common;

namespace Rinku.Mapping.Parsers;

/// <summary>
/// The flat state a multi-row parse folds rows into. It is a runtime-emitted struct, one per parse group, that
/// <see cref="Defaults.MultiRowTypeParser{T, TState}"/> drives. <see cref="Read"/> folds the current row into the state,
/// and <see cref="Build"/> materialises the accumulated value once the group closes.
/// </summary>
public interface IMultiRowState<T> {
    /// <summary>
    /// Folds the current row into the state. Returns <see langword="false"/> when this row belongs to the next
    /// group, so the driver stops without consuming it, and <see langword="true"/> when the row was folded in.
    /// </summary>
    bool Read(DbDataReader reader);
    /// <summary>Materialises the value accumulated across the group's rows.</summary>
    T Build();
}
