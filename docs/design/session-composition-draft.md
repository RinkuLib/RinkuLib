# Session-based module and display composition — draft 1


> Historical draft. [Revised application project boundaries](application-project-boundaries.md) supersede this draft's project ownership and contract placement. In particular, application contracts belong outside Rinku.More and all application database operations belong in AppAPI.

Status: proposed design, 2026-09-19. This document defines the first implementation draft. Interface names below are proposals; existing Rinku mechanisms are identified separately. The application scenario is a WinForms shell with navigation, cached/lazy tabs, a main ribbon, and module-owned data and actions. The same logical objects and behaviors must support a separately implemented web presentation.

## 1. The composition model

A module is a project containing application-facing types, factories, metadata, actions, and other useful objects. Each object implements the contracts needed by its consumers. A contract expresses the inputs, operations, or values needed for a particular responsibility. Objects gain additional capabilities through independent interfaces and composed behaviors.

The main flow is:

```text
session starts
  → module contributions populate logical structures
  → navigation displays entries through their supported contracts
  → clicking an entry calls its behavior with session and host capabilities
  → an opening behavior submits a typed item factory
  → the workspace records an opening and selects it
  → selection creates the logical item and its display when needed
  → the display binds module data and actions using registered integrations
  → activation connects the display's current ribbon contribution
```

An entry owns entry information and click behavior. An opening is an occurrence of an item in a workspace, with its own identity and lifetime. The item contains the logical state and behaviors needed for that occurrence. Its display owns presentation resources and exposes the information/capabilities needed by the display host.

These responsibilities can be composed on objects as appropriate; they do not prescribe a class hierarchy.

## 2. Projects and ownership

| Project | Responsibilities | References |
| --- | --- | --- |
| Rinku | Mapping, schema discovery, parameter mapping, generated method callers, tracking, database execution | Existing runtime dependencies |
| Rinku.More | Executor plans, binding trees, localization, cached lists; reusable composition operations and small neutral contracts introduced by actual use | Rinku |
| AppAPI | Explicit application sessions, user access, application operations, preferences/customization persistence, PowerTools commands and schema metadata | Rinku, Rinku.More |
| Modules | Real interaction types, module metadata, item factories, static/reusable actions, optional session/item behaviors, logical contributions | AppAPI, Rinku.More |
| DevExpress.Tools | Reusable controls, renderers, concrete executor operations, tree projections, tab selection integration, ribbon merging, theme/layout integration | Rinku.More, DevExpress/WinForms |
| App | Module selection, final contract-to-renderer bindings, startup/session creation, shell composition, application-specific overrides | Modules, DevExpress.Tools; AppAPI as needed for startup |

Contracts live where their responsibility belongs. A module consumer and its implementation must both reference their contract's assembly. Shared neutral contracts must be accessible without bringing a UI dependency into modules. Technology-specific contracts stay in the presentation layer. Module-specific contracts can be connected by the app or an explicitly selected integration component.

Composition functions and delegates provide the defaults. Optional interfaces provide extension points. An implementation may use ordinary classes internally, but callers depend on the appropriate interfaces or delegate signatures rather than a prescribed base class.

## 3. Session and presentation scopes

The session is passed explicitly to item creation and behavior invocation. AppAPI defines the application session contract required by its operations. Reusable Rinku.More contracts can be generic in the session type; presentation components consume only the session facets they use.

Identity, authorization, language, theme preference, customization access, and change notifications are separate capabilities. A component requiring language receives a language contract; a component that merely supports language can check for it. The application session implementation composes these facilities. Its lifetime is controlled by the runtime that hosts the application.

The following scopes determine state ownership:

| Scope | State and resources |
| --- | --- |
| Application | Immutable registrations, logical metadata plans, compiled callers/executors that capture no session, stateless action objects |
| Session | User/API context, preference services, permission state, explicitly shared user data/cache state |
| Workspace/presentation | Openings, selected opening, display cache, UI dispatcher, navigation projection, main ribbon merge tree |
| Opening/item | Materialized module data, edit state, parameterized actions, item creation cancellation, logical subscriptions |
| Display | Controls/components, bindings, display event subscriptions, applied preference revisions |
| Activation | The current display's contributions to the active shell, including ribbon merge entries |

A session may have more than one workspace. WinForms commonly has one; a browser tab/circuit/window may have its own workspace while sharing a user session. Selection, control instances, and activation subscriptions belong to the workspace/display. Static actions and compiled plans receive the current session/item/host as inputs.

Web requests obtain the session through the web host's normal scope. A stateful web UI may retain a workspace in its circuit. A stateless UI persists logical opening identities and reconstructs presentation state per request. The contracts support both; implementing the web host is a separate presentation project.

## 4. Small contract vocabulary

The following signatures illustrate the proposed boundaries. They are independent contracts, and consumers ask only for the ones they need. Existing `IExecutor<T...>` remains the setup entry point when synchronous initialization is sufficient.

```csharp
public interface IHasText { Word Text { get; } }
public interface IHasActions { IEnumerable<object> Actions { get; } }
public interface IItems<T> { IEnumerable<T> Items { get; } }
public interface ISelection<T> { bool TryGetSelected(out T item); }
public interface IClick<TSession, THost> { void OnClick(TSession session, THost host); }
public interface IAsyncClick<TSession, THost> { ValueTask OnClickAsync(TSession session, THost host, CancellationToken cancellationToken); }
public interface IAttach<TSession, THost> { ValueTask<IAsyncDisposable?> AttachAsync(TSession session, THost host, CancellationToken cancellationToken); }
public interface IActivate<TSession, THost> { ValueTask<IAsyncDisposable?> ActivateAsync(TSession session, THost host, CancellationToken cancellationToken); }
public interface ICreateItem<TSession, TItem> { ValueTask<TItem> CreateAsync(TSession session, CancellationToken cancellationToken); }
public interface IRenderItem<TSession, TItem, TDisplay> { ValueTask<TDisplay> RenderAsync(TSession session, TItem item, CancellationToken cancellationToken); }
public interface IOpenItem<TSession> { ValueTask OpenAsync<TItem>(ICreateItem<TSession, TItem> factory, CancellationToken cancellationToken); }
```

`IOpenItem<TSession>` is supplied by a workspace already bound to its session. Here, `OpenAsync` completes when the opening is recorded and selection is scheduled. Optional readiness observation is a separate capability. A factory's declared `TItem` tells the workspace which rendering contract to resolve before materialization. The created value must satisfy it.

The signatures above are a starting vocabulary rather than a mandatory set on any object. Separate optional contracts cover item activation events, text changes, availability, display titles, close approval, identity, reuse, hierarchy, and customization. Define their members only when the corresponding consumer is implemented.

An action collection can contain unrelated capabilities; `IEnumerable<object>` is sufficient for that heterogeneous boundary. Typed calls resume after a contract is recognized. No common action base is necessary for enumeration.

`IAttach` is useful when an object manages its own wiring. It returns an optional cleanup handle for that attachment. `IActivate` has the same ownership pattern for work that is valid only while selected. Simple delegates can supply implementations of these contracts. One shared action can produce independent attachment handles for several displays.

Ordinary interface checks require actual implementation. Executor structural specialization is a separate mechanism: a concrete type associated with an `IExpressionContract` need not implement that interface. Registering a structural association does not change the result of a runtime `is` check.

## 5. Registration and module composition

The app selects the technology and registers renderers/integrations against contracts. Reusable presentation packages supply the wiring behind those registrations. Module contributions run with the session and the particular logical targets they need, using executor/delegate contracts. A module project can provide several contributions independently.

The complete sample bootstrap consists of:

1. Select the module contribution functions to run.
2. Register DevExpress integration for the neutral capabilities used by those modules.
3. Bind the list-content contract to the reusable grid presentation, with any specific overrides.
4. Select the shell composition and open a session/workspace through the runtime.

The runtime supplies contribution targets such as a navigation tree or an opening service. A contribution that needs both requests both as parameters. Additional behavior is composed through extra contributions or capability checks.

The technology registration for `IItems<T>` composes a grid renderer, source binding, action presentation, item-event forwarding, session appearance handling, and the display's activation participation. This registration performs the common wiring once for all compatible module objects.

Dispatch is a narrow integration mechanism at rendering/attachment boundaries. Code inside an action continues to call its explicit contracts directly. Registration entries match declared contracts and invoke typed factories/delegates prepared during composition.

App overrides take explicit precedence over package defaults. Equally applicable competing renderers at the same precedence produce a configuration error. Independent behaviors are additive; alternatives for the same responsibility are selected once.

An opening uses the renderer binding selected when it is created. A user preference that explicitly changes presentation can request rematerialization through its policy; registration order is not an implicit display-selection rule.

Modules can now perform their logical work themselves: populate entries, create items, provide actions, react to capabilities, and request further openings. App code does not attach individual event handlers or create each ribbon button.

## 6. Item creation and rendering

Use two independently lazy operations:

1. A module factory creates the logical item with the current session and any captured immutable request arguments, such as a customer ID.
2. A registered renderer creates a display for that logical item's declared contract.

For a list, the factory can return an object exposing `IItems<Customer>` and `IHasActions`. Its source can use a Rinku cached list or tracking list. Additional initialization/policy objects are composed alongside it. The returned object can expose further capabilities without changing the list contract.

The standard grid renderer has enough information to construct the reusable control in the accepted shape:

```csharp
new GridControl<Customer>(actions, session);
```

`GridControl<T>` here denotes the proposed reusable DevExpress composite control. The renderer obtains actions through the supplied object's relevant contract and performs source attachment through the control's data-binding capability. That source attachment is common renderer code, not application glue. Module setup contributions can also bind data through `IExecutor`/`IAttach` when that better fits the feature.

The item factory is the sample's owner of data acquisition. Metadata configures its presentation and can contribute setup operations; the default pipeline does not independently load a second copy of the same source while rendering. The factory may return a lazy source so logical creation and actual data loading can remain separate.

The control exposes actual selected items and item events. Sorting/filtering is resolved at the control boundary. Module behavior operates on item identity or object references supplied by the capability, rather than interpreting UI row handles as list indexes.

The display can obtain its title/default presentation information from module metadata and expose that information to its host. The navigation entry's text remains independent. A tab reservation may initially show a shell loading label; it acquires the display title when that capability becomes available.

## 7. Metadata, executor plans, and extension behavior

Rinku's executor builder is the common configuration mechanism:

```text
type/member metadata and logical setup contributions
  → editable executor plan
  → DevExpress build configuration
  → compiled executor with session/item/control inputs
  → each display instance
```

Use data-only attributes interpreted by independent recording extensions, ordinary `Merge`, generated methods, subscriptions, and build-time specialization. Reusable technology tools supply independent behaviors; application integration registers those behaviors and compiles plans for concrete renderers.

Cache compiled plans by metadata type, rendering binding, build-configuration revision, and any policy that actually changes the emitted plan. Session values are runtime inputs. Language, theme, and user layout changes normally invoke targeted update operations rather than rebuilding control structure.

Action handling has two composition paths:

- A renderer recognizes a behavior/presentation contract and creates the appropriate control or event connection.
- A supplied object exposes an attachment entry point and performs its own capability-dependent wiring against the host.

For each effect, one integration owns its wiring. When an object supplies both a button action and a custom attachment, the declared responsibilities determine whether those are complementary; the same click must not be attached through both paths. Behavior ordering is explicit where necessary (bind source before selection-dependent setup, for example).

Generated executor callbacks derive their invocation data from sender/arguments or use an explicitly supplied per-display delegate. The existing generated callback mechanism does not capture arbitrary initialization inputs. Static action definitions never retain the latest session or display.

Unsupported optional facets are skipped. Missing required inputs for a selected renderer/behavior produce a clear attachment error identifying the contract. Recognition of a presentation facet alone does not imply the object offers every possible interaction.

## 8. Navigation entries and opening policies

A navigation renderer consumes the contracts required for entry appearance and interaction. A click implementation may submit an item factory to the opening capability, call an API operation, change a preference, or perform another action.

An entry that opens an item composes:

- Its own label and optional entry appearance.
- Its click behavior.
- A typed lazy item factory/delegate and any request arguments.
- Optional reuse/identity policies for the requested opening.

The module can provide the entire neutral opening behavior. The app's renderer registration supplies the concrete presentation. For a special application case, the app can instead provide a concrete display factory delegate through the appropriate opening implementation.

Identity and reuse are opt-in. For the sample, identity-bearing reusable requests focus an existing opening with the same semantic key in the current workspace. Requests without that policy create a new opening. Keys use stable feature/item identifiers, not localized text. Two requests for different parameter sets remain distinct. Opening identity and customization identity are separate: many customer-detail tabs may share one layout preference.

Close approval is an independent capability of the relevant item/display behaviors. Accepted closing removes the opening, releases activation and display resources, and follows its declared retention policy. Shared actions and session services outlive the closing display.

## 9. Lazy creation, caching, and selection

An opening records its typed factory, optional identity/policies, and current materialization state. Reserving a tab does not evaluate the factory or enumerate a lazy source. First selection requests materialization. Repeated requests for the same opening share one in-flight operation.

Creation and activation follow separate lifetimes:

| Event | Logical item | Display | Active shell contribution |
| --- | --- | --- | --- |
| Reserve opening | Pending | Pending | None |
| First selection | Create/load as needed | Create/bind | Attach after readiness |
| Select another opening | Retained by policy | Retained by policy | Release |
| Return to cached opening | Reuse | Reuse; apply stale preferences | Attach again |
| Evict an inactive display | Retain if policy allows | Dispose | None |
| Select after display eviction | Reuse or recreate by policy | Recreate/bind | Attach |
| Close opening | Dispose or explicitly retain | Dispose or explicitly retain | Release |

Sample defaults: factories are lazy; open tabs retain their materialized item/display; closing releases both; stable-key reuse is enabled only by its request policy. Optional policies can bound the inactive display cache or retain an item after its display is released. Unsaved item state participates through close/eviction approval instead of being silently discarded.

The workspace serializes selection transitions on its presentation dispatcher and assigns a monotonically increasing selection generation. Every transition releases the previous activation before attaching the new one. A page still loading contributes no page ribbon. Once materialization finishes, it may activate only if its opening and selection generation are still current.

Activation behaviors are acquired in their declared order and released in reverse order. A behavior that fails before returning its handle cleans up its own partial work. The coordinator releases already-acquired handles if a later behavior fails, and presents an activation error with no stale page contribution. Required UI changes remain on the dispatcher even when loading, preference access, or cleanup is asynchronous.

If a slow page finishes after another tab is selected, its result is retained or released according to the opening's policy. It cannot select itself or replace the current ribbon. Closing an opening cancels its work; a late result is disposed if it no longer has an owner. Creation failures leave a retryable opening/error presentation with no active contribution from the failed page.

Programmatic selection and user selection enter the same coordinator. Closing the selected opening invokes the same transition for the successor. Duplicate native notifications for an already active selection are coalesced. Returning to a cached page is a new activation even though initialization does not rerun.

## 10. Ribbon activation through the binding tree

The page/display participates in activation through its capability. The standard DevExpress grid presentation composes a ribbon activation behavior; the tab host invokes activation every time that display becomes current.

Keep a workspace-owned binding tree for the active ribbon merge chain. Its persistent shell contributions remain attached. A logical attachment slot identifies where the active display's contribution subtree belongs, including optional nested content contributions in a defined order.

In the concrete `BindingTreeEntry<RibbonControl>` projection, the main ribbon is the root and child entries represent actual ribbon controls; the active subtree exists only while a display contributes it. An empty slot does not require a placeholder ribbon control.

On activation, the display's ribbon behavior inserts its contribution subtree at the supplied attachment slot and returns a cleanup handle owning that entry. On deactivation, disposing the handle removes exactly that entry/subtree. The `TreeBinder` propagates these structural changes to `RibbonControlMergeAdapter`, which handles the concrete DevExpress merge/unmerge operations.

```text
select A → activate A → attach A's ribbon entry → binder merges A
select B → release A's entry → binder unmerges A
         → activate B → attach B's entry → binder merges B
select A → release B's entry → activate cached A → attach A's entry again
```

This makes ribbon participation a property of the current activation. Constructing or caching a display does not attach it permanently to the main ribbon.

Structural attachment/removal triggers the merge operations. `Refresh` is reserved for updating an attached contribution's appearance/state; a refresh alone does not change which ribbon is merged. A display with no ribbon contribution leaves only the persistent shell contributions.

Logical navigation, each page's logical actions, and the concrete active ribbon chain can use separate binding trees. The concrete `BindingTreeEntry<RibbonControl>` belongs to DevExpress.Tools. Modules can publish neutral action/tree contributions; the presentation builds and attaches their ribbon representation. A single binding-tree entry has one parent, so a contribution mounted in two targets needs separate projection entries around the shared source object.

The tab host coordinates selection. The display's activation behavior owns its ribbon contribution. The app supplies the final composition registration and shell targets once.

## 11. Language, theme, and user customization

Preference handling is composed from separate read, change-notification, and persistence capabilities. AppAPI manages the user's preference identity and storage. DevExpress.Tools translates effective preferences into control settings. Modules can contribute defaults, semantic labels, and feature-specific customization behavior.

Use `Word` and the session's explicit culture for translated text. Entries, action labels, display titles, and field captions resolve their own text. Culture changes update those presentations through their change contracts and binding-tree refreshes. Formatting also uses the session culture. Shared metadata/actions retain the `Word`, not a translation cached for the last user.

Theme preferences use neutral identifiers/options where appropriate. DevExpress mappings choose the concrete skin/palette/fonts. Prefer scoped control/look-and-feel settings for independent workspaces. A toolkit setting that is genuinely process-wide is applied at the desktop-host boundary with that limitation explicit; the session contracts continue to support independent web users.

Customization is contributed per feature. A layout component can expose capture/apply behavior, stable setting identities, and a schema version. Another component can add saved filters, command placement, or dashboard configuration without changing a shared settings class.

Default precedence for the sample is:

```text
tool defaults → module metadata defaults → app profile → user overrides → workspace overrides
```

Each provider only supplies settings it owns. Required permissions, validation, and fixed feature constraints remain authoritative. Personalization changes presentation within those constraints. Resetting user customization removes that layer's overrides and recomputes the effective result.

Persisted customization keys include user scope (and tenant where applicable), stable feature identity, optional view variant, and customization kind/version. Technology-specific layout payloads also include the renderer identity. Cross-technology preferences such as language remain portable; a DevExpress layout blob is consumed only by its compatible layout behavior.

Preferences publish revisions/change notifications. Materialized displays receive targeted updates on their dispatcher. Cached inactive displays either update immediately or record the latest revision to apply before next activation. Unmaterialized openings read current preferences on first creation. Preference updates preserve current edits, data, and selection unless the user explicitly requests an operation that changes them.

Structural metadata executes once per display; preference application has its own repeatable path so a language/theme change does not reload data or duplicate action subscriptions. Restore completes before ordinary layout-change persistence is enabled. Persistence is debounced and scoped; stale concurrent writes use a revision policy supplied by the preference store.

## 12. PowerTools, mapping, and editing

AppAPI owns PowerTools-generated database command methods and generated schema-reference types. Module types define the actual application-facing shape. They can contain nested values, computed members, behavior, attributes, and capabilities according to their mapping configuration.

Rinku parses the generated command directly into the requested module type through `CachedTypeParser<T>`/existing parser APIs. `SchemaReference` and `BasedOn` supply schema linkage where useful. Schema metadata does not prescribe the module object's runtime shape.

Item factories receive the session and obtain application operations from AppAPI. Session-dependent cached lists/loaders capture that session only within their owned scope. A fresh command/connection is obtained per database operation. Compiled parser/mapping machinery may be shared where its existing guarantees permit; materialized user state follows its explicit scope.

`MethodCaller` can prepare a reusable delegate mapping a module object's members to a generated command method, with the connection or cancellation argument supplied separately. It maps against the declared source type when created. Constructor mapping and forwarded `out` arguments use that same caller. Target `ref`/`in` arguments and external bound instances remain unsupported.

Editing composes the existing tracking/runtime/binding capabilities only for editable features. Module behavior owns validation and the meaning of an operation. AppAPI owns persistence and authorization. Command-factory committers can consume PowerTools commands. UI adapters expose binding and validation results through their supported capabilities.

## 13. End-to-end sample walkthrough

1. App startup selects the customer module, registers the DevExpress list renderer and standard shell behaviors, and obtains a session.
2. The module's contribution receives that session and the navigation target. It contributes a text-bearing clickable entry containing a lazy customer-item factory.
3. Clicking the entry invokes its behavior with session and host. The behavior checks the opening capability and submits the factory. Reuse policy either focuses the matching opening or records a new one.
4. Selection evaluates the module factory. It creates a session-bound customer source/edit state and provides actions, most of which are reusable static definitions.
5. The renderer resolves the list contract, constructs `GridControl<Customer>(actions, session)`, binds the source using its reusable binding behavior, and applies module metadata through the DevExpress executor configuration.
6. Recognized action contracts create ribbon buttons or connect item events. Optional attachment contributions receive the control's capabilities. Per-display cleanup handles are owned by this display.
7. Activation invokes the display's composed activation behaviors. Its ribbon behavior attaches a contribution to the workspace merge tree. The existing binder/merge adapter applies it to the main ribbon.
8. A customer double-click is translated into a neutral item event. Module behavior requests a detail opening using a factory capturing the selected customer or its stable key, according to that feature's edit-sharing semantics. The registered detail renderer supplies the presentation.
9. Switching back reuses the cached customer display, applies any newer language/theme/customization revision, and activates its ribbon again.
10. Closing or ending the session releases activation, bindings, displays, item state, and session resources in their respective ownership order. Reusable static definitions remain available.

## 14. Existing support and implementation work

| Mechanism inspected | Reuse | Work required for this draft |
| --- | --- | --- |
| `ExecutorMetadata.cs`, builder/compiler and structural-expression tests | Attribute contributions, plan composition, specialization, generated callbacks | Neutral sample metadata contracts; session/item runtime inputs; separated preference updates |
| `MethodCaller.cs` and its tests | Parameter mapping and typed generated delegates | Session-aware module/API integration using supported signatures |
| `BindingTreeEntry`, `TreeBinder`, adapter contracts | Change propagation, parent access, ordered projections | Composed navigation/opening/activation behaviors |
| `DisplayFactoryCollection<TDisplay>` | Existing small factory-selection precedent | Separate session-aware typed item/render factories, explicit registration precedence and lazy resolution; current collection is synchronous and selects its first match |
| `CachedTypeParser<T>`, `CachedList<T, DbConnection>`, tracking | Independent requested result shape, cached loading, optional editing | Sample PowerTools-based session-scoped operations |
| `XtraTabControlAdapter<T>` | Tab projection | Selection coordinator, truly lazy population, asynchronous materialization guards, cache ownership; its current insertion populates immediately |
| `RibbonControlMergeAdapter` | Concrete merge/unmerge chain | Activation-owned binding-tree entries and cleanup on every switch |
| Existing DevExpress metadata helpers | Concrete column/editor operations | Neutral logical contracts/attributes usable by modules; current attributes target `GridView` directly |

Implement this as composed operations behind small interfaces. Existing binding adapters can be consumed through `IBinderAdapter<T>`; new behavior objects can implement that contract directly or delegate to helpers. The extension model does not require deriving from adapter/control base classes provided by this design. Framework-required WinForms inheritance stays inside the concrete presentation implementation.

## 15. First implementation and acceptance checks

The first implementation should cover one list, one detail display, static actions, session-aware loading, two tabs, and one user customization example end to end.

1. Define the small creation/render/open/interaction contracts needed by that example and compose the runtime services around them.
2. Make the module's types, factories, and actions build against AppAPI/Rinku.More with no UI dependency.
3. Provide DevExpress renderer registrations, common source/action binding, executor associations, and scoped appearance integration.
4. Add lazy opening materialization, selection generations, activation cleanup, and ribbon-tree participation.
5. Add language/theme revision propagation and a user-scoped grid-layout customization contribution.
6. Reduce app bootstrap to selecting modules, registering technology bindings/overrides, and starting the session/workspace.

The following scenarios define acceptance:

- Two sessions using the same static actions retain independent user data, selection, culture, and customization.
- Two workspaces in one session retain independent selected openings and ribbon merge chains.
- Creating an entry or reserving an unselected tab performs no logical item creation or data enumeration.
- Repeated selection during creation shares one in-flight operation. A late completion cannot reactivate a stale opening.
- Switching A → B → cached A produces the correct merge chain every time, including A/B displays without ribbons.
- Closing the active tab removes its contribution before releasing its controls and activates the chosen successor.
- Repeated preference changes/activation create no duplicate click, selection, or localization subscriptions.
- A language change updates entry labels and display titles independently and never changes semantic identity.
- A cached display receives current theme/layout/language before reactivation while preserving edits.
- A user layout round-trip remains scoped to the correct user/feature/renderer and survives a supported schema migration.
- Sorting/filtering does not cause a selected-item action to operate on another row.
- A module-owned type with computed/nested/application members materializes from a PowerTools command independently of its generated schema record.
- A second renderer can consume the same neutral item contract through a different registration without changes to module behavior.
- Every subscription and created resource has an owner; closing, cancellation, failed creation, and eviction release only that owner's resources.

## Source references

- The display factory collection has moved to `Application.Contracts/Presentation/DisplayFactoryCollection.cs` in the sibling DevExpressDemo workspace.
- [Method caller](../../RinkuLib/Querying/Parameters/MethodCaller.cs)
- [Cached type parser](../../RinkuLib/Facade/CachedTypeParser.cs)

The DevExpress files referenced above were inspected in the sibling `DevExpressDemo` repository. This draft changes only the design document in RinkuLib.
