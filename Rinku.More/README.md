# Rinku.More

The solution currently contains the library plus two development projects:

- `Rinku.More` — the production class library, including globalization, cached lists, binding trees,
  and executor plans.
- `Rinku.More.Tests` — xUnit tests for the library.

Run the automated tests with:

```text
dotnet test Rinku.More.slnx
```

The globalization API uses `Word` for culture-specific translations:

```csharp
Word greeting = new()
{
    [C.INV] = "Hello",
    [C.FR] = "Bonjour"
};

var french = greeting.GetTranslation(C.FR_CA);
```

Culture lookup walks from the specific culture to its parent cultures, then
uses `DefaultCulture`, the first available translation, or `Word.NotSupported`
when the word has no translations at all. Formatting uses each translation's
culture as its format provider.

Use `Word.Interpolate` when an interpolated value can itself be a `Word`; it
keeps that value localized for every output culture:

```csharp
var message = Word.Interpolate($"Welcome, {userName}!");
```

Methods that accept localizable text can use `TranslatableString`. It accepts
an existing `Word` or an interpolated string and keeps `Word` interpolation
arguments localized:

```csharp
void Show(TranslatableString message) { /* ... */ }

Show(greeting);
Show($"Welcome, {userName}!");
```

`Word` stores only an interned `CulturesKey` and a parallel translation array.
Words with the same ordered culture layout share the same key. `CulturesKey`
has specialized unsafe comparisons for the common two-character (`xx`) and
five-character (`xx-XX`) culture codes, with a general fallback for other
culture names.

`Rinku.More` references the `Rinku` NuGet package and registers a custom
`TypeParsingInfo` for `Word` automatically. A `Word` member claims its neutral
column and its culture-suffixed siblings in the same database row; it is not
the normal single-column constructor conversion. Thus this projection maps all
three columns into the one `Name` value. A missing column or a database `NULL`
simply means that translation is absent.

```csharp
public sealed record Product(Word Name);

// SELECT Name, NameFr, NameEn FROM Products
// Name    -> invariant translation
// NameFr  -> fr
// NameEn  -> en
```

The suffix is interpreted as an IETF culture name, so `NameFr`, `NameEn`, and
`Namefr-CA` are supported. `_` or `-` may separate the property name and
culture (`Name_Fr` also works).

Load a CSV localization catalog with `WordDict.FromCsvFile`. The first column
is the translation key and following columns are culture names; an empty
culture header represents the invariant culture. For example:

```text
key,,en,fr
greeting,Hello,Hello,Bonjour
```

For standard ResX localization files, pass only the neutral resource. Every
culture-suffixed sibling is discovered automatically, so adding
`Strings.es.resx` later requires no code change:

```csharp
var catalog = WordDict.FromLocalizationFile("locales/Strings.resx");

var greeting = catalog.Get("greeting", C.FR_CA);
```

For incremental loading, keep a catalog and add files as they become
available. `LoadFile` selects the loader from the file extension; ResX files
contribute one culture and CSV files contribute every populated culture column.
Existing values for other cultures are retained.

```csharp
var catalog = new WordDict();
catalog.LoadFile("locales/Strings.resx");
catalog.LoadFile("locales/Strings.fr.resx");
catalog.LoadFile("locales/Strings.es.resx"); // can arrive later
```

## Database execution conveniences

`IDatabase`, `ConnectionDatabase` and their convenience extensions live in Rinku.More. It supplies typed delegate and command-factory overloads over the two execution primitives. They carry arguments and source text into the implementation; they contain no logging policy, connection configuration or transaction management.

```csharp
var users = database.Query<List<User>>(DbCommands.LoadUsers);
await database.ExecuteAsync(DbCommands.SaveUser, userId, displayName, cancellationToken);
```

The implementation owns the connection lifetime. Commands are disposed after execution or mapping. Cache `EnsureLoaded` uses the same asynchronous overload and returns its stable collection immediately.
For a scalar projection, `CachedList.Project(source, select, comparer)` composes a projection loader with the same sealed cache type. It deduplicates internally and follows source change notifications. Its source is another cache and its loading context remains the source context.

## Cached lists

A cache stores a command factory, an instance-owned parser and fetched data. It stores no connection or connection provider.

```csharp
public static readonly CachedList<Employee, DbConnection> Employees = CachedList.New<Employee>(DbCommands.LoadEmployees);

grid.DataSource = database.EnsureLoaded(Employees);
```

`EnsureLoaded` returns the same list immediately and invokes `ExecuteAsync(Employees.LoadAsync)` without awaiting. The database execution retains its supplied connection until loading finishes. Existing bindings receive `ListChanged` when rows arrive.

For explicit completion, call `await database.ExecuteAsync(Employees.LoadAsync, cancellationToken)` or `database.Execute(Employees.Load)`. Invalidate the same instance before refreshing it. Reading the list itself never opens a connection.

`CachedList<T, TContext>` is sealed and implements `ICacheLoader<T, TContext>`. Loading behavior is composed rather than inherited. `ProjectedBindingDictionary` is a standalone view over any `IBindingList`; it has no cache or loading lifecycle. Load its source externally and its contents follow the source events. Dictionary projections deduplicate internally and expose key/value rows for binding.

## Executor plans

Ordinary recording extensions can read data-only attributes and append operations to the same editable plan:

```csharp
IExecutor<ISettings> executor = ExecutorBuilder.Start<ISettings>(out var settings)
    .ReadSettings<UserSettingsMetadata>(settings)
    .Then(() => settings.Val.Enabled, true)
    .Build<IExecutor<ISettings>>();
```

`ReadSettings` is an application-defined extension that reads metadata and records ordinary operations; it is not a built-in attribute callback.

The [executor guide](https://rinkulib.github.io/RinkuLib/articles/more/executors/) covers
metadata attributes, explicit builder operations, composition, and generated executors.



## Behavior and lifetime

Executable behaviors use the executor plan. Technology methods may return scopes that specialize recursively into native calls, stable delegates and retained values. Tree adapters remain responsible for structural synchronization and ownership; there is no parallel registration tree.

`OnPropertyChanged` and `OnListChanged` are plan-recording helpers with one shared subscription sequence. Generated fields retain the source and callback; replacement detaches the old subscription, and owner disposal detaches and clears the retained values. Compilation emits the concrete add/remove calls directly.

`IInvokable` provides targetless invocation with an `Enabled` predicate. Input gestures and display metadata remain independent contracts.