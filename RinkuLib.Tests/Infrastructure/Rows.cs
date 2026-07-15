using System.Data;
using RinkuLib.DbParsing;
using RinkuLib.Tools;

namespace RinkuLib.Tests.Infrastructure;

/// <summary>
/// Builds in-memory readers from a column schema and rows, the input every mapping test parses from.
/// </summary>
public static class Rows {
    public static DataTableReader Reader(ColumnInfo[] columns, params object[][] rows) {
        DataTable table = new();
        foreach (var col in columns)
            table.Columns.Add(new DataColumn(col.Name, col.Type) { AllowDBNull = col.IsNullable });
        foreach (var row in rows)
            table.Rows.Add(row);
        return table.CreateDataReader();
    }

    /// <summary>Parses the first row of the given data as <typeparamref name="T"/>.</summary>
    public static T ParseOne<T>(ColumnInfo[] columns, params object[] row) {
        using var reader = Reader(columns, row);
        var parser = TypeParser.GetTypeParser<T>(ref columns);
        reader.Read();
        return parser.Parse(reader).Result;
    }

    /// <summary>Parses every row of the given data as <typeparamref name="T"/>.</summary>
    public static List<T> ParseAll<T>(ColumnInfo[] columns, params object[][] rows) {
        using var reader = Reader(columns, rows);
        var parser = TypeParser.GetTypeParser<T>(ref columns);
        var results = new List<T>();
        reader.Read();
        for (int i = 0; i < rows.Length; i++)
            results.Add(parser.Parse(reader).Result);
        return results;
    }
}
