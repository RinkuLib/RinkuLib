using System.Data;
using System.Data.Common;

namespace Rinku.Mapping.Parsers.Defaults;

internal interface IAccumulatorStrategy<TResult, TElement, TBuffer> {
    TBuffer Seed();
    void Add(ref TBuffer buffer, TElement element);
    TResult Finish(TBuffer buffer);
}

internal sealed class RecursiveAccumulatorTypeParser<TResult, TElement, TBuffer, TStrategy>(ITypeParser<TElement> elementParser) : BaseTypeParser<TResult>
    where TStrategy : struct, IAccumulatorStrategy<TResult, TElement, TBuffer> {
    private readonly ITypeParser<TElement> ElementParser = elementParser;
    private readonly TStrategy Strategy = default;

    public override CommandBehavior Behavior => ElementParser.Behavior & ~CommandBehavior.SingleRow;
    public override bool CanParse(ColumnInfo[] schema) => ElementParser.CanParse(schema);
    public override TResult Default() {
        var buffer = Strategy.Seed();
        return Strategy.Finish(buffer);
    }
    public override (bool CanContinue, TResult Result) Parse(DbDataReader reader) {
        var buffer = Strategy.Seed();
        bool canContinue;
        do {
            (canContinue, var item) = ElementParser.Parse(reader);
            Strategy.Add(ref buffer, item);
        } while (canContinue);
        return (false, Strategy.Finish(buffer));
    }
    public override ValueTask<(bool CanContinue, TResult Result)> ParseAsync(DbDataReader reader, CancellationToken ct = default) {
        var buffer = Strategy.Seed();
        while (true) {
            var pending = ElementParser.ParseAsync(reader, ct);
            if (!pending.IsCompletedSuccessfully)
                return ContinueAsync(pending, buffer, reader, ct);
            var (canContinue, item) = pending.Result;
            Strategy.Add(ref buffer, item);
            if (!canContinue)
                return new((false, Strategy.Finish(buffer)));
        }
    }
    private async ValueTask<(bool CanContinue, TResult Result)> ContinueAsync(ValueTask<(bool CanContinue, TElement Result)> pending,
        TBuffer buffer, DbDataReader reader, CancellationToken ct) {
        while (true) {
            var (canContinue, item) = await pending.ConfigureAwait(false);
            Strategy.Add(ref buffer, item);
            if (!canContinue)
                return (false, Strategy.Finish(buffer));
            pending = ElementParser.ParseAsync(reader, ct);
        }
    }
    public override void Dispose() => ElementParser.Dispose();
}
