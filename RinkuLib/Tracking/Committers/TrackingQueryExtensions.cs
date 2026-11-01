using System.Data.Common;
using Rinku.Tracking.Runtime;

namespace Rinku.Tracking.Committers;

/// <summary>Supplies tracked changes to authored SQL templates.</summary>
public static class TrackingQueryExtensions {
    /// <summary>Supplies changed conditional values and retains the template's unconditional inputs.</summary>
    public static QueryBuilder UseChanges(this QueryBuilder builder, ITrackingChanges item) {
        builder.UseWith((object)item);
        var command = builder.QueryCommand;
        int count = command.Mapper.Count;
        Span<bool> changed = count <= 256 ? stackalloc bool[count] : new bool[count];
        changed.Clear();
        RuntimeTrackingParameterRegistry.TryGet(item.GetType(), out var shape);
        for (int member = 0; member < item.TrackedMemberCount; member++)
            if (item.TryGetChange(member, out var change))
                for (int index = 0; index < count; index++) {
                    string key = command.Mapper.Keys[index];
                    ReadOnlySpan<char> name = key;
                    if (name.Length != 0 && !char.IsLetterOrDigit(name[0]) && name[0] != '_')
                        name = name[1..];
                    if (shape is not null ? shape.MatchesParameter(name, change.Name) : name.Equals(change.Name.AsSpan(), StringComparison.OrdinalIgnoreCase))
                        changed[index] = true;
                }
        for (int index = 0; index < count; index++)
            if (!changed[index] && command.QueryText.IsInCondition(index))
                builder.Variables[index] = null;
        return builder;
    }

    /// <summary>Creates a command using changed conditional values, suitable for a committer command factory.</summary>
    public static DbCommand GetChangesCommand(this QueryCommand command, ITrackingChanges item, DbConnection connection) {
        var builder = command.StartBuilder().UseChanges(item);
        return QueryBuilderExtensions.GetCommand(command, builder.Variables, connection);
    }

    internal static int ExecuteUpdate<T>(QueryCommand command, T item, DbCommitState state) {
        if (item is not ITrackingChanges changes)
            return command.Execute(state.Connection, (object?)item, state.Transaction, state.Timeout);
        return command.StartBuilder().UseChanges(changes).Execute(state.Connection, state.Transaction, state.Timeout);
    }
}
