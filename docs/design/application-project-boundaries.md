# Revised application project boundaries

Design supplied for the project setup on 2026-09-19. This supersedes draft 1 wherever their project ownership or contracts differ. Implementation status is tracked in the DevExpressDemo README; this document describes the intended design, not completed functionality.

The design follows three firm rules:

1. AppAPI is the only place with database responsibility.
2. Rinku.More provides reusable mechanisms, not application-specific contracts.
3. Modules compose their own behavior through a separate contract assembly that the module and renderer both reference.

The project relationship becomes:

```text
Rinku
  ↓
Rinku.More

AppAPI
  → Rinku
  → Rinku.More

Application.Contracts
  → neutral contracts for this application/integration

Modules
  → AppAPI
  → Rinku.More
  → Application.Contracts

DevExpress.Tools
  → Rinku.More
  → Application.Contracts

App
  → Modules
  → DevExpress.Tools
  → Application.Contracts
```

`Application.Contracts` is deliberately outside Rinku.More. It can define the contracts needed by this application’s modules and renderers without pretending they are universal Rinku concepts. Another application can create a different contract assembly.

Rinku.More remains focused on mechanisms such as:

- `BindingTreeEntry<T>` and its binders/adapters;
- executor plans, structural contracts, metadata contributors, and specialization;
- cached lists;
- localization primitives;
- generic utility behavior.

It should not define concepts such as application pages, sidebar entries, actions, selections, user sessions, or display contracts merely because one application needs them.

AppAPI owns all database behavior:

- PowerTools-generated `DbCommand` methods;
- `QueryCommand` definitions;
- query execution methods;
- database-specific parameter handling;
- command factories;
- committers;
- database caching and repository operations.

There is no SQL responsibility in Modules, DevExpress.Tools, or App.

A module can use an AppAPI operation such as “load employees” or “update employee.” It can pass its own application type into that operation if the API contract allows it. `MethodCaller`, parameter mapping, and committers can connect the module type to the API’s generated command method. The module does not know that the operation uses SQL, `QueryCommand`, a generated command, or a committer.

PowerTools-generated result types remain schema references. A module can define a richer application type and associate it with the generated schema metadata where appropriate. The generated type does not become the module’s model.

The module owns composition. It can provide:

- logical items;
- item factories;
- static action objects;
- metadata contributors;
- optional capability implementations;
- neutral tree entries;
- session-aware behavior;
- factories for detail or secondary items.

The App calls the module’s composition entry point with the session and the neutral targets it is allowed to contribute to. The module then performs its own registration and wiring against those targets. The App makes the final technology choice and connects DevExpress.Tools to the neutral contract system.

The App should not individually register every module action or event. Its job is to select the module set, create the session/workspace, select the renderer registrations, and connect the shell to the renderer.

The session is explicit. It is passed to module composition and to any item/action factory that needs it. A session may contain or expose independent capabilities for:

- AppAPI access;
- user identity and permissions;
- language;
- theme;
- customization storage;
- user preferences;
- cancellation and lifetime;
- application/workspace access.

A control receives the session only through a contract that requires it. The base control constructor should remain minimal; it should receive the action collection and whatever display-specific inputs its own contract requires. The session should not become a universal context object.

The application has five main binding trees.

```text
Application workspace
├── Sidebar tree
├── User options tree
├── Main ribbon merge tree
├── Active page ribbon item tree
└── Page tabs tree
```

The sidebar tree contains navigation entries. An entry is not a page. It has entry-specific information and an interaction contract. Clicking it may open an item, activate an existing item, change a user option, or execute another behavior.

The page is produced by the behavior associated with the entry. The page’s own display and page metadata come from the control/display created by that behavior.

The user options tree contains settings and customization choices. It is neutral and can be rendered as a sidebar, popup, menu, dialog, or another control. Language, theme, layout, and user customization can contribute nodes to this tree without the module referencing a specific UI.

The main ribbon merge tree represents the currently active page contributions. It is not the page’s actual ribbon content. It manages which page contribution is currently attached to the shell.

Each page may have its own ribbon item tree. That tree describes the page’s actual logical ribbon structure: groups, commands, submenus, and other actions. The module and page use neutral contracts from `Application.Contracts`; they do not reference DevExpress ribbon types.

DevExpress.Tools maps the page ribbon tree into DevExpress controls, then mounts the resulting page contribution into the main ribbon merge tree.

Some pages may also have a tabs tree. That tree can represent detail tabs, document tabs, nested editors, or another hierarchical tab arrangement. It is separate from the main shell tabs and is rendered according to the selected technology.

The activation flow is:

```text
select page A
  → detach the previous page contribution
  → attach A's page ribbon contribution to the main ribbon merge tree
  → render/update the main ribbon

select page B
  → detach A
  → attach B
  → render/update the main ribbon

return to cached page A
  → reuse A if retained
  → apply current session customization
  → attach A's ribbon contribution again
```

The page may be lazy-created and cached. Creating or caching a page does not attach its ribbon. Activation owns that attachment. When the page becomes current, its contribution is inserted into the main ribbon merge tree. When it is no longer current, the contribution is removed.

`BindingTree` already provides the useful generic behavior here: parent/child structure, ordered changes, incremental updates, refresh, rebuild, middleware, and technology-specific adapters. The new design should use separate tree instances with separate purposes rather than creating a universal tree object that understands every shell feature.

Actions are usually static module-owned objects. An action collection can contain unrelated objects. DevExpress.Tools inspects the objects for the contracts it supports:

- a button presentation contract;
- a double-click behavior;
- a selection requirement;
- an attachment entry point;
- a ribbon contribution;
- a refresh behavior;
- a user-option contribution.

There is no requirement for a universal base action interface. A module action implements the smallest contract it needs and may implement several independent contracts when those behaviors compose.

An action that needs a selected employee requests the selection contract. An action that needs to open a detail item requests the opening contract. An action that wants to attach itself to a control requests an attachment entry point. The control supplies itself only when it implements the required capability.

A module can therefore define all of its action behavior and let the renderer discover what it can use. The renderer performs the concrete wiring, while the module supplies the behavior.

The first complete sample should therefore look like this conceptually:

```text
App
  creates session and workspace
  connects DevExpress shell adapters
  invokes module composition

Module
  contributes sidebar entries
  contributes user-option entries
  creates session-aware item factories
  exposes static actions
  contributes page ribbon/tab trees

DevExpress.Tools
  renders sidebar
  creates GridControl<T> from action information
  renders page ribbon trees
  attaches/detaches page contributions
  renders tabs
  applies theme/language/customization
```

The existing sample code that directly creates `AdministrationUsersView`, receives `BindingTree.Ribbon` objects, and dispatches actions by translated text is the part that should be reorganized. The module should contribute neutral behavior and tree information. DevExpress.Tools should create the control, connect selection and double-click behavior, render the page ribbon, and manage activation.

The goal is extensibility through composition:

- new modules can introduce new contracts in the application’s bridge assembly;
- another renderer can implement those contracts without changing the module;
- another application can use a different bridge assembly;
- Rinku.More remains useful without knowing what a page, action, sidebar, or ribbon means;
- AppAPI remains the single source of database behavior;
- the App remains the final technology-specific composition point.