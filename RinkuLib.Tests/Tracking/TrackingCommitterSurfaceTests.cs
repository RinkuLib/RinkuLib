using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Rinku.Tracking;
using Rinku.Tracking.Binding;
using Rinku.Tracking.Committers;
using Rinku.Tracking.Runtime;
using Xunit;

namespace RinkuLib.Tests.Tracking;

public sealed class TrackingCommitterSurfaceTests
{
    [Fact]
    public void Independent_item_commit_assigns_id_and_keeps_observing_lists_in_sync()
    {
        using SqliteConnection connection = OpenDatabase(seed: false);
        using var insert = new QueryCommand("INSERT INTO CommitRows (Name) VALUES (@Title) RETURNING ID");
        using var update = new QueryCommand("UPDATE CommitRows SET Name = @Title WHERE ID = @Id");
        var committer = Committer.Create<int>(insert, nameof(Album.Id), update);
        var originals = new List<Album>();
        using var rows = originals.ToBindingList<Album, ITrackingItem<Album>>(RuntimeTracking.CreateOptions<Album, ITrackingItem<Album>>().Binding().ConstructOnInsert());
        ITrackingItem<Album> item = rows.AddNew();
        item.Set(nameof(Album.Title), "Created independently");

        Assert.True(committer.CommitItem(connection, item));
        int id = item.Get<int>(nameof(Album.Id));
        Assert.True(id > 0);
        Assert.Equal(id, Assert.Single(originals).Id);
        Assert.False(item.IsNew);
        Assert.False(rows.HasChanges);

        item.Set(nameof(Album.Title), "Updated independently");
        Assert.True(committer.CommitItem(connection, item));
        Assert.Equal("Updated independently", Assert.Single(originals).Title);
        Assert.Equal(new[] { "Updated independently" }, ReadNames(connection));
        Assert.False(rows.HasChanges);
    }

    [Fact]
    public void Failed_independent_save_keeps_the_draft_and_does_not_notify_the_list()
    {
        using SqliteConnection connection = OpenDatabase(seed: false);
        using var insert = new QueryCommand("INSERT INTO CommitRows (Name) VALUES (NULL)");
        var committer = Committer.Create(insert);
        var originals = new List<Album>();
        using var rows = originals.ToBindingList<Album, ITrackingItem<Album>>(RuntimeTracking.CreateOptions<Album, ITrackingItem<Album>>().Binding().ConstructOnInsert());
        ITrackingItem<Album> item = rows.AddNew();
        item.Set(nameof(Album.Title), "Unsaved");
        int confirmed = 0;
        item.Confirmed += (_, _) => confirmed++;

        Assert.Throws<SqliteException>(() => committer.CommitItem(connection, item));
        Assert.True(item.IsNew);
        Assert.True(rows.HasChanges);
        Assert.Equal("Unsaved", item.Get<string>(nameof(Album.Title)));
        Assert.Empty(originals);
        Assert.Equal(0, confirmed);
        Assert.Equal(0, CountRows(connection));
    }

    [Fact]
    public void Interface_typed_tracking_list_is_edited_and_committed_through_the_interface()
    {
        using SqliteConnection connection = OpenDatabase();
        using var insert = new QueryCommand("INSERT INTO CommitRows (ID, Name) VALUES (@Id, @Title)");
        using var update = new QueryCommand("UPDATE CommitRows SET Name = @Title WHERE ID = @Id");
        using var delete = new QueryCommand("DELETE FROM CommitRows WHERE ID = @Id");
        QueryCommitter committer = Committer.Create(insert, update, delete);
        var original = new Album { Id = 1, Title = "old" };
        var removed = new Album { Id = 2, Title = "deleted" };
        var source = new List<Album> { original, removed };
        TrackingList<IAlbum> albums = source.ToTrackingList<Album, IAlbum>(RuntimeTracking.CreateOptions<Album, IAlbum>().ConstructOnInsert());

        albums[0].Title = "changed through interface";
        albums.RemoveAt(1);
        IAlbum added = albums.AddNew();
        added.Id = 3;
        added.Title = "added through interface";

        Assert.True(committer.Commit(connection, albums));

        Assert.Equal(new[] { "changed through interface", "added through interface" }, ReadNames(connection));
        Assert.Equal(["changed through interface", "added through interface"], source.Select(static album => album.Title));
        Assert.Same(original, source[0]);
        Assert.False(albums.HasChanges);
    }

    [Fact]
    public void Query_committer_is_reusable_and_commits_insert_update_then_delete()
    {
        using SqliteConnection connection = OpenDatabase();
        using var insert = new QueryCommand("INSERT INTO CommitRows (ID, Name) VALUES (@Id, @Name)");
        using var update = new QueryCommand("UPDATE CommitRows SET Name = @Name WHERE ID = @Id");
        using var delete = new QueryCommand("DELETE FROM CommitRows WHERE ID = @Id");
        QueryCommitter committer = Committer.Create(insert, update, delete);
        var changed = new ShapeRow(1, "changed", changed: true);
        var deleted = new ShapeRow(2, "deleted");
        var added = new ShapeRow(3, "added");
        var tracking = new TrackingList<ShapeRow>([changed, deleted]);
        tracking.Remove(deleted);
        tracking.Add(added);

        Assert.True(committer.Commit(connection, tracking));

        var alternate = new AlternateRow(1, "alternate", changed: true);
        var alternateTracking = new TrackingList<AlternateRow>([alternate]);

        Assert.True(committer.Commit(connection, alternateTracking));
        Assert.Equal(new[] { "alternate", "added" }, ReadNames(connection));
        Assert.False(tracking.HasChanges);
        Assert.False(alternate.HasPendingChange);
    }

    [Fact]
    public void Scalar_insert_applies_output_before_confirmation_without_using_it_as_the_result()
    {
        using SqliteConnection connection = OpenDatabase(seed: false);
        using var insert = new QueryCommand("INSERT INTO CommitRows (Name) VALUES (@Name) RETURNING ID");
        var calls = new List<string>();
        QueryScalarInsertCommitter<ShapeRow, int> committer = Committer.Create(insert, (ShapeRow item, int id) => { item.Id = id; calls.Add($"apply:{item.Id}"); });
        var item = new ShapeRow(0, "added") { IsResultValid = false };
        var tracking = new TrackingList<ShapeRow>
        {
            item
        };
        using IDisposable subscription = tracking.SubscribeConfirmations(onAdded: (_, value) => calls.Add($"confirm:{value.Id}"));

        Assert.True(committer.Commit(connection, tracking));

        Assert.True(item.Id > 0);
        Assert.Equal(new[] { $"apply:{item.Id}", $"confirm:{item.Id}" }, calls);
        Assert.Equal(0, item.ResultValidationCalls);
        Assert.Equal(1, CountRows(connection));
    }

    [Fact]
    public void Runtime_member_name_assigns_scalar_output_through_the_generated_item()
    {
        using SqliteConnection connection = OpenDatabase(seed: false);
        using var insert = new QueryCommand("INSERT INTO CommitRows (Name) VALUES (@Title) RETURNING ID");
        QueryScalarInsertCommitter<IRuntimeMemberAccess, int> committer = Committer.Create<int>(insert, nameof(IAlbum.Id));
        TrackingList<IAlbum> albums = new List<Album>().ToTrackingList<Album, IAlbum>(RuntimeTracking.CreateOptions<Album, IAlbum>().ConstructOnInsert());
        IAlbum added = albums.AddNew();
        added.Title = "runtime member";

        Assert.True(committer.Commit(connection, albums));

        Assert.True(added.Id > 0);
        Assert.False(albums.HasChanges);
    }

    [Fact]
    public void Assignment_delegate_applies_command_output_before_confirmation()
    {
        using SqliteConnection connection = OpenDatabase(seed: false);
        using var insert = new QueryCommand("INSERT INTO CommitRows (Name) VALUES (@Name)");
        QueryOutputInsertCommitter<MetadataRow, long> committer = Committer.Create(insert, ReadLastInsertId, static (MetadataRow item, long id) => item.Id = id);
        var item = new MetadataRow("command output");
        var tracking = new TrackingList<MetadataRow>
        {
            item
        };
        long confirmedId = 0;
        using IDisposable subscription = tracking.SubscribeConfirmations(onAdded: (_, value) => confirmedId = value.Id);

        Assert.True(committer.Commit(connection, tracking));

        Assert.True(confirmedId > 0);
        Assert.Equal(confirmedId, item.Id);
    }

    [Fact]
    public void Query_result_and_implementation_output_are_independent()
    {
        using SqliteConnection connection = OpenDatabase(seed: false);
        using var insert = new QueryCommand("INSERT INTO CommitRows (Name) VALUES (@Name) RETURNING Name AS Message");
        QueryResultOutputInsertCommitter<MetadataRow, SaveMetadata, long> committer = Committer<SaveMetadata>.Create(insert, ReadLastInsertId, static (MetadataRow item, long id) => item.Id = id);
        var item = new MetadataRow("result-output");
        var tracking = new TrackingList<MetadataRow>
        {
            item
        };

        Assert.True(committer.Commit(connection, tracking));

        Assert.Null(item.Metadata);
        Assert.True(item.Id > 0);
    }

    [Fact]
    public void Command_factory_committer_uses_generated_command_shape_directly()
    {
        using SqliteConnection connection = OpenDatabase(seed: false);
        DbCommandFactoryCommitter<ShapeRow> committer = Committer.Create<ShapeRow>(insert: static (item, cnn) => CreateInsertCommand(item, cnn));
        var tracking = new TrackingList<ShapeRow>
        {
            new(7, "factory")
        };

        Assert.True(committer.Commit(connection, tracking));

        Assert.Equal(new[] { "factory" }, ReadNames(connection));
    }

    [Fact]
    public void Direct_executor_does_not_implicitly_store_its_result()
    {
        using SqliteConnection connection = OpenDatabase(seed: false);
        ExecutorCommitter<MetadataRow, SaveMetadata, int, bool> committer = Committer.Create<MetadataRow, SaveMetadata, int, bool>(insert: InsertMetadata);
        var item = new MetadataRow("executor");
        var tracking = new TrackingList<MetadataRow>
        {
            item
        };

        Assert.True(committer.Commit(connection, tracking));

        Assert.Null(item.Metadata);
        Assert.Equal(new[] { "executor" }, ReadNames(connection));
    }

    [Fact]
    public void Missing_operation_is_rejected_before_opening_the_connection()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        QueryCommitter committer = Committer.Create();
        var tracking = new TrackingList<ShapeRow>
        {
            new(1, "added")
        };

        Assert.Throws<NotSupportedException>(() => committer.Commit(connection, tracking));
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public void No_work_does_not_open_the_connection()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        QueryCommitter committer = Committer.Create();
        var tracking = new TrackingList<ShapeRow>([new ShapeRow(1, "unchanged")]);

        Assert.True(committer.Commit(connection, tracking));
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    [Fact]
    public void Required_committer_creation_parameters_are_checked()
    {
        Assert.Throws<ArgumentNullException>(() => Committer.Create<int>(insert: (QueryCommand)null!, outputMember: nameof(IAlbum.Id)));
        Assert.Throws<ArgumentNullException>(() => Committer.Create<ShapeRow, int>(null!, static (_, _) => { }));
        Assert.Throws<ArgumentNullException>(() => Committer.Create<ShapeRow, long>(null!, ReadLastInsertId, static (_, _) => { }));
        Assert.Throws<ArgumentNullException>(() => Committer<SaveMetadata>.Create<MetadataRow, long>(null!, ReadLastInsertId, static (_, _) => { }));
        Assert.Throws<ArgumentNullException>(() => Committer.Create<ShapeRow, int>(new QueryCommand("SELECT 1"), null!));
    }

    private static SqliteConnection OpenDatabase(bool seed = true)
    {
        SQLitePCL.Batteries.Init();
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using DbCommand command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE CommitRows (ID INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL)";
        command.ExecuteNonQuery();

        if (seed)
        {
            command.CommandText = "INSERT INTO CommitRows (ID, Name) VALUES (1, 'old'), (2, 'deleted')";
            command.ExecuteNonQuery();
        }

        return connection;
    }

    private static string[] ReadNames(DbConnection connection)
    {
        using var query = new QueryCommand("SELECT Name FROM CommitRows ORDER BY ID");
        return [.. query.Query<List<string>>(connection)];
    }

    private static int CountRows(DbConnection connection)
    {
        using var query = new QueryCommand("SELECT COUNT(*) FROM CommitRows");
        return query.ExecuteScalar<int>(connection);
    }

    private static long ReadLastInsertId(DbCommand source)
    {
        using DbCommand command = source.Connection!.CreateCommand();
        command.Transaction = source.Transaction;
        command.CommandText = "SELECT last_insert_rowid()";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static DbCommand CreateInsertCommand(ShapeRow item, DbConnection connection)
    {
        DbCommand command = connection.CreateCommand();
        command.CommandText = "INSERT INTO CommitRows (ID, Name) VALUES (@Id, @Name)";
        DbParameter id = command.CreateParameter();
        id.ParameterName = "@Id";
        id.Value = item.Id;
        command.Parameters.Add(id);
        DbParameter name = command.CreateParameter();
        name.ParameterName = "@Name";
        name.Value = item.Name;
        command.Parameters.Add(name);
        return command;
    }

    private static SaveMetadata InsertMetadata(MetadataRow item, DbConnection connection, DbTransaction transaction)
    {
        using DbCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO CommitRows (Name) VALUES (@Name)";
        DbParameter name = command.CreateParameter();
        name.ParameterName = "@Name";
        name.Value = item.Name;
        command.Parameters.Add(name);
        command.ExecuteNonQuery();
        return new(item.Name);
    }

    private class ShapeRow : IEditable, ICanDelete, ITrackingChanges, IValidatable, IResultValidatable<int>
    {
        public ShapeRow(int id, string name, bool changed = false)
        {
            Id = id;
            Name = name;
            HasPendingChange = changed;
        }

        public int Id { get; set; }
        public string Name { get; }
        public bool HasPendingChange { get; private set; }
        public bool IsValid { get; set; } = true;
        public bool IsResultValid { get; set; } = true;
        public int ResultValidationCalls { get; private set; }
        public bool IsEditing => HasPendingChange;
        public int TrackedMemberCount => 1;
        public bool EnsureEditing() { HasPendingChange = true; return true; }
        public bool ConfirmEdit() { HasPendingChange = false; return true; }
        public bool CancelEdit() { HasPendingChange = false; return true; }
        public bool ConfirmDelete() => true;
        public bool Validate() => IsValid;
        public bool ValidateResult(int result) { ResultValidationCalls++; return IsResultValid; }

        public bool TryGetChange(int memberIndex, out TrackingChange change)
        {
            if (memberIndex == 0 && HasPendingChange)
            {
                change = new(nameof(Name), Name, Name);
                return true;
            }

            change = default;
            return false;
        }
    }

    private sealed class AlternateRow(int id, string name, bool changed = false) : ShapeRow(id, name, changed) { }

    public sealed class Album
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
    }

    public interface IAlbum : ITrackingItem<Album>
    {
        int Id { get; set; }
        string Title { get; set; }
    }

    private sealed class MetadataRow(string name) : IEditable
    {
        public long Id { get; set; }
        public string Name { get; } = name;
        public SaveMetadata? Metadata { get; private set; }
        public bool IsEditing => false;
        public bool EnsureEditing() => true;
        public bool ConfirmEdit() => true;
        public bool CancelEdit() => true;
        public void SetMetadata(SaveMetadata metadata) => Metadata = metadata;
    }

    private sealed record SaveMetadata(string Message);
}
