# Runtime tracking

## Typed generated contract

```csharp
public record Album(int Id, string Title);

public interface IAlbumEdit : IRuntimeTrackingItem<Album>
{
    int Id { get; }
    string Title { get; set; }
}

Album original = new(12, "Blue");
RuntimeTrackingOptions<Album> options = RuntimeTracking.CreateOptions<Album, IAlbumEdit>();
RuntimeTrackingRegistration<Album, IAlbumEdit> registration = options.GetRegistration<IAlbumEdit>();

IAlbumEdit edit = registration.Create(original);
edit.Title = "Kind of Blue";
```

The generated CLR type implements the requested interface.

## Runtime member surface

```csharp
IRuntimeTrackingItem<Album> edit = RuntimeTracking.Default<Album>().Create(original);

edit.Set(nameof(Album.Title), "Kind of Blue");
string title = edit.Get<string>(nameof(Album.Title));
```

The default registration is shared for the original type.

## Custom options

```csharp
RuntimeTrackingOptions<Album> options = RuntimeTracking.CreateOptions<Album>();
options.Member<string>(nameof(Album.Title));

IRuntimeTrackingItem<Album> edit = options.GetRegistration<IRuntimeTrackingItem<Album>>().Create(original);
```

Options freeze when the first registration is created.

```csharp
RuntimeTrackingOptions<Album> options = RuntimeTracking.CreateOptions<Album>();
options.Member<string>(nameof(Album.Title)).ReadOnly();

RuntimeTrackingRegistration<Album, IRuntimeTrackingItem<Album>> registration = options.GetRegistration<IRuntimeTrackingItem<Album>>();
```

## Member surface

```csharp
RuntimeTrackingOptions<Album> options = RuntimeTracking.CreateOptions<Album>();

options.Member<int>(nameof(Album.Id)).ReadOnly();
options.Member<string>(nameof(Album.Title)).Expose();
```

```csharp
options.Member<string>("DisplayText").Ignore();
```

```csharp
options.Member<string>("InternalText").RuntimeAccess(false);
```

```csharp
options.Member<string>("DisplayText").Parameters(false);
```

`ReadOnly`, `Expose`, `Ignore`, `RuntimeAccess`, and `Parameters` change different generated surfaces of the same member configuration.

## Apply another contract

```csharp
RuntimeTrackingOptions<Album> options = RuntimeTracking.CreateOptions<Album>();
options.Apply<IAlbumEdit>();

IAlbumEdit edit = options.GetRegistration<IAlbumEdit>().Create(original);
```

`Apply<TContract>()` adds the interface contract to the same option tree before generation.

## Runtime only storage

```csharp
RuntimeTrackingOptions<Album> options = RuntimeTracking.CreateOptions<Album>();
options.Member<string>("SelectionState").Direct();
options.Member<string>("DraftNote").SnapshotValue();
```

`Direct()` stores the value on the generated item. `SnapshotValue()` stores its accepted value on the generated item and moves edits into the lazy snapshot. Both members are independent from the original `Album`.

The same configuration can be declared on generated contract members with [`RuntimeDirectAttribute`](xref:Rinku.Tracking.Runtime.RuntimeDirectAttribute), [`RuntimeSnapshotValueAttribute`](xref:Rinku.Tracking.Runtime.RuntimeSnapshotValueAttribute), and [`RuntimeValueAttribute`](xref:Rinku.Tracking.Runtime.RuntimeValueAttribute). Read and write projection can be bound separately with [`ReadFromAttribute`](xref:Rinku.Tracking.Runtime.ReadFromAttribute) and [`WriteToAttribute`](xref:Rinku.Tracking.Runtime.WriteToAttribute). [`IncludeOriginalMembersAttribute`](xref:Rinku.Tracking.Runtime.IncludeOriginalMembersAttribute) controls original member inclusion from a contract.

## Attribute configuration

[`RuntimeReadOnlyAttribute`](xref:Rinku.Tracking.Runtime.RuntimeReadOnlyAttribute) · [`RuntimeIgnoreAttribute`](xref:Rinku.Tracking.Runtime.RuntimeIgnoreAttribute) · [`NestedEditAttribute`](xref:Rinku.Tracking.Runtime.NestedEditAttribute)

[`BindToAttribute`](xref:Rinku.Tracking.Runtime.BindToAttribute) · [`ReadFromAttribute`](xref:Rinku.Tracking.Runtime.ReadFromAttribute) · [`WriteToAttribute`](xref:Rinku.Tracking.Runtime.WriteToAttribute) · [`ReadWithAttribute`](xref:Rinku.Tracking.Runtime.ReadWithAttribute) · [`WriteWithAttribute`](xref:Rinku.Tracking.Runtime.WriteWithAttribute)

[`RuntimeDynamicAccessAttribute`](xref:Rinku.Tracking.Runtime.RuntimeDynamicAccessAttribute) · [`NoRuntimeAccessAttribute`](xref:Rinku.Tracking.Runtime.NoRuntimeAccessAttribute)

[`RuntimeParameterAttribute`](xref:Rinku.Tracking.Runtime.RuntimeParameterAttribute) · [`RuntimeParameterNameAttribute`](xref:Rinku.Tracking.Runtime.RuntimeParameterNameAttribute) · [`RuntimeParameterAliasAttribute`](xref:Rinku.Tracking.Runtime.RuntimeParameterAliasAttribute) · [`RuntimeParametersAttribute`](xref:Rinku.Tracking.Runtime.RuntimeParametersAttribute)

[`RuntimeNotificationsAttribute`](xref:Rinku.Tracking.Runtime.RuntimeNotificationsAttribute)

Attributes can configure the generated contract directly.

```csharp
public sealed class ConfiguredAlbum
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
}

public interface IConfiguredAlbumEdit : IRuntimeTrackingItem<ConfiguredAlbum>
{
    [RuntimeReadOnly]
    int Id { get; }

    [RuntimeIgnore]
    string? DebugLabel { get; }

    string Title { get; set; }
}
```

## Query parameter projection

```csharp
static readonly QueryCommand UpdateAlbum = new("UPDATE albums SET Title = @Title WHERE AlbumId = @Id");

RuntimeTrackingOptions<Album> typedOptions = RuntimeTracking.CreateOptions<Album, IAlbumEdit>();
RuntimeTrackingRegistration<Album, IAlbumEdit> typedRegistration = typedOptions.GetRegistration<IAlbumEdit>();
IAlbumEdit edit = typedRegistration.Create(original);
edit.Title = "Kind of Blue";

UpdateAlbum.Execute(cnn, edit);
```

`Parameters(false)` removes a configured member from this parameter projection.

## New original factory

```csharp
public sealed class AlbumDraft
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
}

RuntimeTrackingOptions<AlbumDraft> options = RuntimeTracking.CreateOptions<AlbumDraft>();
options.WithNewOriginal(static () => new AlbumDraft());

RuntimeTrackingRegistration<AlbumDraft, IRuntimeTrackingItem<AlbumDraft>> registration = options.GetRegistration<IRuntimeTrackingItem<AlbumDraft>>();

IRuntimeTrackingItem<AlbumDraft> edit = registration.CreateNew();
```

```csharp
if (registration.CanCreateNew)
    registration.CreateNew();
```

## Missing original represented by null

```csharp
RuntimeTrackingOptions<Album?> options = RuntimeTracking.CreateOptions<Album?>();
options.UseNullAsMissingOriginal();

IRuntimeTrackingItem<Album?> edit = options.GetRegistration<IRuntimeTrackingItem<Album?>>().Create(null);
```

```csharp
if (edit.TryGetOriginal(out Album? accepted))
    Console.WriteLine(accepted.Title);
```

## Nested edit in place

```csharp
public record Artist(int Id, string Name);
public record Album(int Id, string Title, Artist Artist);

RuntimeTrackingOptions<Album> options = RuntimeTracking.CreateOptions<Album>();
options.Member<Artist>(nameof(Album.Artist)).NestedEdit(NestedEditMode.InPlace);
```

Confirming an in-place nested edit copies changed nested members into the accepted nested object.

```csharp
public sealed class Address
{
    public string City { get; set; } = "";
}

public sealed class Contact
{
    public Address Address { get; set; } = new();
}

public interface IContactEdit : IRuntimeTrackingItem<Contact>
{
    Address Address { get; }
}

Contact original = new() { Address = new() { City = "Toronto" } };
RuntimeTrackingOptions<Contact> contactOptions = RuntimeTracking.CreateOptions<Contact>();
contactOptions.Member<Address>(nameof(Contact.Address)).NestedEdit(NestedEditMode.InPlace);
IContactEdit edit = contactOptions.GetRegistration<IContactEdit>().Create(original);

IEditable editable = (IEditable)edit;
editable.EnsureEditing();
edit.Address.City = "Montreal";
editable.ConfirmEdit();
// InPlace keeps the accepted Address instance and applies the changed City to it.
```

## Nested edit replacement

```csharp
options.Member<Artist>(nameof(Album.Artist)).NestedEdit(NestedEditMode.Replacement);
```

Confirming replacement assigns the accepted nested value from the edited nested value instead.

```csharp
public sealed class ReplacementAddress
{
    public string City { get; set; } = "";
}

public sealed class ReplacementContact
{
    public ReplacementAddress Address { get; set; } = new();
}

public interface IReplacementContactEdit : IRuntimeTrackingItem<ReplacementContact>
{
    ReplacementAddress Address { get; }
}

ReplacementContact original = new() { Address = new() { City = "Toronto" } };
RuntimeTrackingOptions<ReplacementContact> replacementOptions = RuntimeTracking.CreateOptions<ReplacementContact>();
replacementOptions.Member<ReplacementAddress>(nameof(ReplacementContact.Address)).NestedEdit(NestedEditMode.Replacement);
IReplacementContactEdit edit = replacementOptions.GetRegistration<IReplacementContactEdit>().Create(original);

IEditable editable = (IEditable)edit;
editable.EnsureEditing();
edit.Address.City = "Montreal";
editable.ConfirmEdit();
// Replacement assigns the edited Address instance to original.Address.
```

The two modes change confirmation behavior. The nested edit state is tracked in both forms.

## Materialize a list contract

```csharp
List<Album> source = cnn.Query<List<Album>>("SELECT AlbumId AS Id, Title FROM albums ORDER BY AlbumId");
TrackingList<IAlbumEdit> albums = source.ToTrackingList<Album, IAlbumEdit>();

albums[0].Title = "Kind of Blue";
```

```csharp
TrackingList<IAlbumEdit> albums = source.ToTrackingList<Album, IAlbumEdit>(options);
```

[Tracking lists](lists.md) · [Validation](validation.md)
