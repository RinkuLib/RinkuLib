using System.Data.Common;

namespace Rinku.Mapping.Parsers;

internal static class ResultSetDrainer {
    public static void Drain(DbDataReader reader) {
        while (reader.NextResult()) { }
    }

    public static async Task DrainAsync(DbDataReader reader, CancellationToken ct) {
        while (await reader.NextResultAsync(ct).ConfigureAwait(false)) { }
    }
}
