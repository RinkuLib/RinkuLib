using System.Data.Common;
using Rinku.Tracking.Runtime;

namespace Rinku.Tracking.Committers;

internal static class CommitterRuntime
{
    internal static TItem RequireItem<TItem, T>(T item)
    {
        if (item is TItem typed) return typed;
        throw new InvalidOperationException($"The committer requires items assignable to {typeof(TItem)}, but the tracking list contains {typeof(T)}.");
    }

    internal static void SetRuntimeMember<TOutput>(IRuntimeMemberAccess item, string outputMember, TOutput? output)
    {
        if (!item.Set(outputMember, output))
            throw new InvalidOperationException($"Unable to assign output member '{outputMember}' on {item.GetType()}.");
    }

    internal static Action<T, TOutput> OutputWriter<T, TOutput>(string member, string[]? clearMembers) where T : IRuntimeMemberAccess => (item, output) => {
        SetRuntimeMember(item, member, output);
        if (clearMembers is not null)
            foreach (string name in clearMembers)
                SetRuntimeMember(item, name, "");
    };
    internal static Func<DbCommand, TOutput> OutputReader<TOutput>(string outputParameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputParameter);
        return command => command.GetOutputValue<TOutput>(outputParameter)
            ?? throw new InvalidOperationException($"Output parameter '{outputParameter}' produced no value.");
    }
}
