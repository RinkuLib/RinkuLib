using System.Data.Common;

namespace Rinku.Mapping.Parsers;

/// <summary>Collects rows for one value and builds that value when its group ends.</summary>
public interface IMultiRowState<T> {
    /// <summary>
    /// Adds the current row. Return <see langword="false"/> when the row belongs to the next value.
    /// </summary>
    bool Read(DbDataReader reader);
    /// <summary>Builds the value from the rows already added.</summary>
    T Build();
}
