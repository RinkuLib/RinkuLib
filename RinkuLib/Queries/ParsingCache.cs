using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using RinkuLib.Tools;

namespace RinkuLib.Queries;
/// <summary>
/// Represent an cached item used for parsing a <see cref="DbDataReader"/>
/// </summary>
public struct ParsingCacheItem(object Parser, int[] FalseIndexes, ColumnInfo[] Schema, CommandBehavior CommandBehavior, int ResultSetIndex) {
    /// <summary>
    /// The actual parser func
    /// </summary>
    public object Parser  = Parser;
    /// <summary>
    /// The indexes at which the condition muts be false
    /// </summary>
    public int[] FalseIndexes  = FalseIndexes;
    /// <summary>
    /// The schema for which the <see cref="Parser"/> is for
    /// </summary>
    public ColumnInfo[] Schema = Schema;
    /// <summary>
    /// The default behavior of the reader
    /// </summary>
    public CommandBehavior CommandBehavior = CommandBehavior;
    /// <summary>
    /// The index of the corresponding result set, (non returning set (FieldCount == 0) are not taken into consideration)
    /// </summary>
    public int ResultSetIndex = ResultSetIndex;
}