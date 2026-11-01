# Runtime tracking

The examples use the `Album`, `IAlbumEdit`, `original`, and `existingAlbums` setup from the [tracking overview](index.md).

## Typed generated contract

```csharp
RuntimeTrackingOptions<Album> options = RuntimeTracking.CreateOptions<Album, IAlbumEdit>();
IAlbumEdit edit = options.GetRegistration<IAlbumEdit>().Create(original);
edit.Title = "Kind of Blue";
```

The returned item implements the requested interface and the tracking contracts. The requested interface does not need to inherit tracking interfaces or reference the original type. Tracking capabilities remain available through pattern matching:

```csharp
if (edit is IRuntimeMemberAccess members)
{
    string title = members.Get<string>(nameof(IAlbumEdit.Title));
}
```

Without a requested interface, the default result is `ITrackingItem<Album>` with member access by name/index. A requested interface may explicitly inherit a tracking contract when direct compile-time access is wanted; otherwise the generated runtime type supplies it independently. Both tracking-list and binding-list materialization support these three forms.

A requested interface may expose just one capability, such as an ID. Members discovered from the original type remain on the generated object even when that interface does not declare them.

```csharp
public interface IHasId
{
    int Id { get; }
}

var rows = existingAlbums.ToBindingList<Album, IHasId>();
if (rows[0] is IRuntimeMemberAccess members)
{
    members.Set(nameof(Album.Title), "Kind of Blue");
}
```

`MethodCaller` can consume `IRuntimeMemberAccess` directly. It uses the target method's parameter types to read values without building a dictionary or boxing value-type members. Generated tracking parameter names, aliases and exclusions still apply.

## Runtime member surface

```csharp
ITrackingItem<Album> edit = RuntimeTracking.Default<Album>().Create(original);

edit.Set(nameof(Album.Title), "Kind of Blue");
string title = edit.Get<string>(nameof(Album.Title));
```

## Custom options

```csharp
RuntimeTrackingOptions<Album> options = RuntimeTracking.CreateOptions<Album>();
options.Member<string>(nameof(Album.Title));

ITrackingItem<Album> edit = options.GetRegistration<ITrackingItem<Album>>().Create(original);
```

Options freeze when the first registration is created.

## Member surface

```csharp
RuntimeTrackingOptions<Album> options = RuntimeTracking.CreateOptions<Album>();

options.Member<int>(nameof(Album.Id)).ReadOnly();
options.Member<string>(nameof(Album.Title)).Expose();
options.Member<string>("DisplayText").Ignore();
options.Member<string>("InternalText").RuntimeAccess(false);
options.Member<string>("DisplayText").Parameters(false);
```

`ReadOnly()` prevents writes, `Expose()` controls the CLR property, `Ignore()` removes the member, and `RuntimeAccess()` and `Parameters()` control runtime access and query projection.

## Apply another contract

```csharp
RuntimeTrackingOptions<Album> options = RuntimeTracking.CreateOptions<Album>();
options.Apply<IAlbumEdit>();

IAlbumEdit edit = options.GetRegistration<IAlbumEdit>().Create(original);
```

`Apply<TContract>()` adds another interface contract to the options.

## Runtime only storage

```csharp
RuntimeTrackingOptions<Album> options = RuntimeTracking.CreateOptions<Album>();
options.Member<string>("SelectionState").Direct().Parameters(false);
options.Member<string>("DraftNote").SnapshotValue();
```

`Direct()` keeps only the current value. `SnapshotValue()` also participates in cancel and confirmation. Neither member belongs to the original `Album`.

Storage and parameter projection are independent: neither `Direct()` nor `SnapshotValue()` changes the parameter setting. Use `Parameters(false)` to exclude a member from command mapping.

Member configuration can remain in the options chain:

```csharp
var options = RuntimeTracking.CreateOptions<Album>()
    .Member<Dictionary<string, string>>("Notes", member => member.Direct().Parameters(false).CreateOnEdit(static () => new Dictionary<string, string>()));
```

An interface-property selector supplies the member name and value type and requests that interface's implementation. The selector must refer directly to an instance property or field; it is inspected, never executed.

```csharp
var options = RuntimeTracking.CreateOptions<Album>()
    .Member((IAlbumState state) => state.Note, member => member.Direct().Parameters(false));
```

Member configuration carries the value type, so its factory return type and reset values are checked by C#. In `Rinku.More`, lifecycle conveniences can be chained inside that configuration:

```csharp
var options = RuntimeTracking.CreateOptions<Album>()
    .Member<string>("Password", member => member.Direct().ResetOnConfirmAndRevert(string.Empty));
```

`CreateOnEdit` initializes a missing direct reference value when an edit snapshot is first created. It does not allocate when an existing item is loaded, and it preserves a value that is already present.

The pending confirmation bag is an ordinary member exposed by `ITrackingParameters`. Pending values remain separate from accepted model values and are cleared by confirmation/revert policies. Other state, such as validation errors, is configured as additional members and can be exposed through ordinary application interfaces with `Apply<TContract>()`; core tracking has no metadata category.

The same choices can be declared with [`RuntimeDirectAttribute`](xref:Rinku.Tracking.Runtime.RuntimeDirectAttribute) and [`RuntimeSnapshotValueAttribute`](xref:Rinku.Tracking.Runtime.RuntimeSnapshotValueAttribute).

## Attribute configuration

The same member options can be declared on a generated contract.

```csharp
public sealed class ConfiguredAlbum
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
}

public interface IConfiguredAlbumEdit
{
    [RuntimeReadOnly]
    int Id { get; }

    [RuntimeIgnore]
    string? DebugLabel { get; }

    string Title { get; set; }
}
```

[`RuntimeReadOnlyAttribute`](xref:Rinku.Tracking.Runtime.RuntimeReadOnlyAttribute) · [`RuntimeIgnoreAttribute`](xref:Rinku.Tracking.Runtime.RuntimeIgnoreAttribute) · [`NestedEditAttribute`](xref:Rinku.Tracking.Runtime.NestedEditAttribute) · [`RuntimeDirectAttribute`](xref:Rinku.Tracking.Runtime.RuntimeDirectAttribute) · [`RuntimeSnapshotValueAttribute`](xref:Rinku.Tracking.Runtime.RuntimeSnapshotValueAttribute)

## Query parameter projection

```csharp
static readonly QueryCommand UpdateAlbum = new("UPDATE albums SET Title = @Title WHERE AlbumId = @Id");

RuntimeTrackingOptions<Album> typedOptions = RuntimeTracking.CreateOptions<Album, IAlbumEdit>();
IAlbumEdit edit = typedOptions.GetRegistration<IAlbumEdit>().Create(original);
edit.Title = "Kind of Blue";

UpdateAlbum.Execute(cnn, edit);
```

`Parameters(false)` removes a configured member from this parameter projection.

## Create new

`CreateNew()` allocates editable values without constructing an accepted original. An initializer can supply defaults, and a per-call initializer can use additional inputs.

```csharp
options.InitializeNew<IAlbumEdit>(static edit => edit.Title = "");
var registration = options.GetRegistration<IAlbumEdit>();
IAlbumEdit item = registration.CreateNew(edit => edit.Title = title);
```

`TryGetOriginal` returns false until an original is supplied. The committer constructs that original from edited values and outputs while its transaction can still roll back, then calls `IOriginalWriter<Album>.SetOriginal` after persistence succeeds. Tracking confirmation follows. Construction does not require a parameterless `Album` constructor, and generated identifiers can remain read-only.

## Missing original represented by null

```csharp
RuntimeTrackingOptions<Album?> options = RuntimeTracking.CreateOptions<Album?>();
options.UseNullAsMissingOriginal();

ITrackingItem<Album?> edit = options.GetRegistration<ITrackingItem<Album?>>().Create(null);
```

```csharp
if (edit.TryGetOriginal(out Album? accepted))
    Console.WriteLine(accepted.Title);
```

## Nested edit in place

```csharp
public record Artist(int Id, string Name);
public record AlbumWithArtist(int Id, string Title, Artist Artist);

RuntimeTrackingOptions<AlbumWithArtist> options = RuntimeTracking.CreateOptions<AlbumWithArtist>();
options.Member<Artist>(nameof(AlbumWithArtist.Artist)).NestedEdit(NestedEditMode.InPlace);
```

Confirmation copies changed nested members into the accepted `Artist` instance.

## Nested edit replacement

```csharp
RuntimeTrackingOptions<AlbumWithArtist> replacementOptions = RuntimeTracking.CreateOptions<AlbumWithArtist>();
replacementOptions.Member<Artist>(nameof(AlbumWithArtist.Artist)).NestedEdit(NestedEditMode.Replacement);
```

Replacement mode keeps a detached draft and copies it when confirming. Arrays are cloned and compared by their contents, so assigning an array or retaining the draft does not expose the accepted array to later edits.

The same policy can be declared on a member:

```csharp
[NestedEdit(NestedEditMode.Replacement)]
public int[] DiplomaIds { get; set; } = [];
```

Assigning the member starts editing automatically. Call `EnsureEditing()` before mutating the contents of a retrieved array in place. Cancel discards the detached draft. Members without this policy keep their normal reference behavior.

## Materialize a list contract

```csharp
TrackingList<IAlbumEdit> albums = existingAlbums.ToTrackingList<Album, IAlbumEdit>();

albums[0].Title = "Kind of Blue";
```

```csharp
RuntimeTrackingOptions<Album> listOptions = RuntimeTracking.CreateOptions<Album, IAlbumEdit>();
TrackingList<IAlbumEdit> albums = existingAlbums.ToTrackingList<Album, IAlbumEdit>(listOptions);
```

[Tracking lists](lists.md) · [Validation](validation.md)

## Computed readonly getters

Declare a generated member in tracking options without adding a property to the original model:

```csharp
public const string HasDiplomas = nameof(HasDiplomas);

public static RuntimeTrackingOptions<User> Tracking { get; } = RuntimeTracking.CreateOptions<User, ITrackingItem<User>>()
    .Binding()
    .DefaultNewValues()
    .Getter(HasDiplomas, user => user.DiplomaIds.Length > 0);
```

`Getter` rewrites direct source property and field accesses to generated tracking property accesses. Those properties read edits before the accepted original. Nested access and calls such as `Calculate(user.Name, user.Count)` retain the expression's normal behavior. Passing the source itself, such as `Calculate(user)`, is rejected because accesses hidden inside that method cannot be rewritten. Getters should be side-effect-free.

The expression is compiled once for current values and once for accepted originals. There is no computed-value field per item and no reflection or name lookup on each evaluation. A new item without an original compares against the result type's default value. Dependencies participate in property notifications; revert and confirmation use the normal tracking lifecycle. Missing dependencies and cycles fail generation.

The generated property is readonly and participates in runtime member access, parameter projection and change enumeration. A boolean result of `false` remains a parameter value; it is not an absent parameter. For example:

```sql
/*@HasDiplomas*/
BEGIN
    IF @HasDiplomas = 0
        DELETE FROM demo.UserDiplomas WHERE UserId = @Id;
END;
```

With normal `UseChanges` projection, this block appears when the computed value changes. A separate optional collection expansion handles replacement of one nonempty selection by another. No model-specific update command factory is needed.
