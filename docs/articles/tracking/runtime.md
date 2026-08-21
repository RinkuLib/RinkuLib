# Runtime tracking

Runtime tracking generates a concrete edit type from `TOriginal` and caches its registration for reuse.

## Default runtime shape

```csharp
using Rinku.Tracking;
using Rinku.Tracking.Runtime;

public record Album(int Id, string Title);

IRuntimeTrackingItem<Album> edit = RuntimeTracking.Default<Album>().Create(new Album(12, "Blue"));

string? title = edit.Get<string>(nameof(Album.Title));
edit.Set(nameof(Album.Title), "Kind of Blue");
```

`IRuntimeTrackingItem<TOriginal>` combines edit lifecycle, original access, read-only new state, change enumeration, and `IRuntimeMemberAccess`.

```csharp
int titleIndex = edit.GetIndex(nameof(Album.Title));
string? title = edit.Get<string>(titleIndex);
edit.Set(titleIndex, "Kind of Blue");
```

## Configure the generated shape

`RuntimeTrackingOptions<TOriginal>` is an ordered option tree. It starts with discovered original members, can apply multiple interface contracts, and freezes after producing its first registration.

```csharp
var options = RuntimeTracking.CreateOptions<Album>();
options.Member<int>(nameof(Album.Id)).ReadOnly();
options.WithNewOriginal(static () => new Album(0, string.Empty));

RuntimeTrackingRegistration<Album, IRuntimeTrackingItem<Album>> registration =
    options.GetRegistration<IRuntimeTrackingItem<Album>>();
IRuntimeTrackingItem<Album> edit = registration.Create(new Album(12, "Blue"));
```

Member options can select read-only, snapshot, direct-state, nested-edit, runtime-access, and parameter-projection behavior. Options may also add binding, validation, metadata, or custom emitters.

## Lists use the same registration model

```csharp
Album[] originals = [new(1, "Blue")];
RuntimeTrackingOptions<Album> options = RuntimeTracking.CreateOptions<Album>();
TrackingList<IRuntimeTrackingItem<Album>> albums = originals.ToTrackingList(options);
```

Materialization generates once, constructs every wrapper through cached delegates, and selects a source-aware context when the original enumerable is indexable.
