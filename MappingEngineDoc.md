# The Mapping Engine

The Mapping Engine is a system for defining how C# types should be interpreted. Its sole purpose is to produce an optimized `Func<DbDataReader, T>` based on a specific database schema.

The interaction is with the **Metadata Cache**, a registry of rules that the engine builds as it encounters types which is used to **Negotiate** with the database to generate the final function.

## The Goal: `GetParser`

To get the compiled function, a `ColumnInfo[]` is required.
A `ColumnInfo` is a simple `struct` that save the `Type`, the `Name` and the nullability of a column.

```csharp
// 1. Identify the columns from the reader
ColumnInfo[] cols = reader.GetColumns();

// 2. Obtain the parser and the execution behavior
var parser = TypeParser<User>.GetParser(cols, out var behavior);

```

### Caching for Performance

While `TypeParser<T>` internally caches the generated function for a given schema, the `defaultBehavior` is returned so that you can implement your own high-level cache. 

By storing the `parser` alongside the `behavior`, you can bypass the need for the schema and optimize futur reader execution.
> The `defaultBehavior` may indicate `SequentialAccess` or `SingleResult`

---

### **Dynamic Mapping with ValueTuple**

The engine can map directly to a `ValueTuple`. In this mode, the **first level is name-agnostic**: the engine ignores the tuple's parameter names (`Item1`, `Item2`, etc.).

For complex types, this means the engine doesn't look for a prefix. Normally, a property `Name` on a parameter named `User` would look for `UserName`. In a tuple, since `Item1` is ignored, the engine looks directly for `Name`.

#### **Basic Types**

Mapping is strictly sequential based on the SQL column order.

```csharp
// Index 0 -> int
// Index 1 -> string
var (id, name) = await builder.QueryOneAsync<(int, string)>(cnn, ct);
```

#### **Mixed Types**

A basic type consumes the next available column, then the complex type begins its search.

```csharp
// Index 0 -> int
// Index 1+ -> Location Negociate as is Query<Location> directly but now the first col is allready used
var (id, employee) = await builder.QueryOneAsync<(int, Location)>(cnn, ct);
```

#### **Complex Types**

The engine ignores the tuple's "Item" name and matches the object's internal property/parameter names directly against the columns.

```csharp
public record struct Person(int ID, string Name);
// Matches 'ID' and 'Name' directly (no 'Item1' prefix used)
// Then matches the next available 'ID' and 'Name' for the second person
var (p1, p2) = await builder.QueryOneAsync<(Person, Person)>(cnn, ct);
```

#### **Unexpected Consumption**

With a standard parameterized constructor, the engine matches parameters sequentially, which is safe. However, because there is no "name fence" (like a prefix) protecting the columns, certain configurations can lead to unexpected scavenging. Configurations that allows for non-sequential, may be problematic in cases where multiple Items may map to the same name.
> In the case of a parameterless ctor, after initialization, it scavenge all the row to assign individualy all the possible members 

**Schema:** `| ID | Name | ID | Name | Email |`

```csharp
public struct Person { int ID; string Name; string Email}
// 0. Person 1 does not have any parameterized ctors and uses a parameterless ctor (scavenges members individually)
// 1. Person 1 new() then maps ID(0) and Name(1) but also finds Email(4).
// 2. Person 2 new() then maps ID(2) and Name(3).
// Result: Person 2 has no Email because Person 1 already consumed it.
var (p1, p2) = await builder.QueryOneAsync<(Person, Person)>(cnn, ct);
```

#### **Unexpected sequence**

Simple types always look at the column immediately following the **last used index**. If a complex type's configuration causes it to finish at an index further ahead than expected, the cursor jumps.

**Schema:** `| ID | Name | RoleId | Email |`

```csharp
// 1. Person maps ID(0) and Name(1) but also finds Email(3).
// 2. The cursor is now at Index 3.
// 3. The following 'int' looks at Index 4 (Next-in-Line).
// Result: Fails to match the schema since 'int' could not be mapped"
var (person, roleId) = await builder.QueryOneAsync<(Person, int)>(cnn, ct);
```
---
### **Dynamic Mapping with DynaObject**

You can call `QueryX` using `DynaObject` as the type. This returns a dictionary-like object that provides access to columns via their names or indices.


Access columns using the indexer (which returns `object?`) or the `Get<T>` method for a direct, typed result. `DynaObject` supports lookups via string keys, `ReadOnlySpan<char>`, or integer indices.

```csharp
// Schema: | ID | Name | Email |
var row = await builder.QueryOneAsync<DynaObject>(cnn, ct);

// Using string keys
int id = row.Get<int>("ID");
string name = row.Get<string>("Name");

// Using ReadOnlySpan<char> (useful for high-performance parsing)
var email = row.Get<string?>(spanKey);

// Using integer index
object? firstColumn = row[0];
```

#### **Handling Duplicate Names**

If the result set contains duplicate column names, the engine ensures every key is unique by appending a suffix to every subsequent appearance.

**Schema:** `| ID | Name | ID | Name |`

```csharp
var row = await builder.QueryOneAsync<DynaObject>(cnn, ct);

// First occurrence
int id1 = row.Get<int>("ID");

// Subsequent occurrences append #2, #3, etc.
int id2 = row.Get<int>("ID#2");
object name2 = row["Name#2"];
```

#### **Modifying Values**

Although it serves as a representation of a row, `DynaObject` allows you to update its values using `Set<T>`.

```csharp
// Update by key or index
row.Set("Name", "New Name");
row.Set(0, 99);
```
---

### **Type Registration**

A type must be registered for the engine to know how to handle it. This happens in one of four ways:

1. **Implicit Use:** Calling `GetParser<T>` for the first time automatically registers that type.
2. **Basic Types & Enums:** The engine automatically registers any type supported directly by `DbDataReader` (like `string`, `int`, etc.) and all **Enums** the first time they are encountered.
3. **Marker Interface:** Any class that implements the `IDbReadable` interface is automatically registered.
4. **Manual Registration:** You can manually add a type using static methods on `TypeParsingInfo`. This is useful if you cannot modify the type or want to configure it before its first use.

```csharp
var info = TypeParsingInfo.GetOrAdd<User>();
var info = TypeParsingInfo.GetOrAdd(typeof(KeyValuePair<,>));
```

---

### Discovery Criteria

The engine searches for "Entry Points" (constructors and factory methods) using these strict rules:

* **Public Visibility:** Only **public** constructors and **public** static methods are discovered.
* **Static Factory Methods:** A static method is only considered a factory if it:
    * Is **non-generic**.
    * Returns the **exact target type**.


* **Viable Parameters:** Every parameter in the signature must be a type the engine knows how to handle. A parameter is viable if it is:
    * A **Basic Type** (string, int, DateTime, etc.) or an **Enum**.
    * An **IDbReadable** type.
    * A **Generic placeholder** (resolved at generation time).
    * Any type already registered in **TypeParsingInfo**.

### **Specificity & Ordering Logic**

When the engine populates the registry, it keeps the items in the order they were discovered (typically the order they appear in the code). However, it applies a **Specificity Rule** to resolve priority between related paths.

* **The Specificity Rule:** A signature is considered **More Specific** than another if:
    * It has **equal or more parameters**. AND
    * **Every parameter** in the signature is either the same type or a more specific implementation of the corresponding parameter in the other signature.

* **The Move-Forward Behavior:** The engine doesn't do a global "sort." Instead:
    * It maintains the natural order of appearance.
    * If a signature is identified as **More Specific** than one that currently precedes it, the engine moves the specific one **directly in front** of the less specific one.

---

## **Example: Discovery & Specificity**

The following example demonstrates how the engine handles discovery, filters out invalid signatures, and reorders the registry based on specificity.

```csharp
public class UserProfile {
    // A: Discovered first. Remains at top (nothing below is "More Specific").
    public UserProfile(string username) { }

    // B: Discovered second. 
    public UserProfile(int id) { }

    // IGNORED: Private visibility.
    private UserProfile(Guid internalId) { }

    // C: More specific than B. Moves directly in front of B.
    public UserProfile(int id, string username) { }

    // D: Most specific. Moves directly in front of C.
    public static UserProfile Create(int id, string username, DateTime lastLogin) { }

    // E: Unrelated to B/C/D. Stays at the end in discovery order.
    public UserProfile(DateTime manualExpiry, bool isAdmin) { }

    // IGNORED: Returns 'object', not exact type 'UserProfile'.
    public static object Build(int id) => new UserProfile(id);

    // IGNORED: Static factories must be non-generic.
    public static UserProfile Build<T>(T parameter) => ...;
}

```

### **Final Registry Order**

The "Move-Forward" logic creates "bubbles" of specificity without performing a global sort. Notice that **A** remains at the top because **D** and **C** are only more specific than **B**, not **A**.

| Priority | Signature | Source | Logic |
| --- | --- | --- | --- |
| **1** | `(string)` | **A** | **First Found:** No subsequent item was "More Specific" than this. |
| **2** | `(int, string, DateTime)` | **D** | **Jumped:** Moved to front of **C** because it is more specific. |
| **3** | `(int, string)` | **C** | **Jumped:** Moved to front of **B** because it is more specific. |
| **4** | `(int)` | **B** | **Base Case:** Pushed down by its more specific variants. |
| **5** | `(DateTime, bool)` | **E** | **Unrelated:** No overlap with B/C/D; maintains discovery order. |

---

## **Manual Registration**

Manual registration allows you to explicitly define "Entry Points" that the engine might not otherwise find. This is useful for methods inside the class that are not public, or for logic located in entirely different classes.

### **Working with Stack Equivalence**

Manual registration is governed by **Stack Equivalence**. The engine will accept any `MethodBase` (constructor or method) as long as the resulting object can be treated as the target type on the evaluation stack.

This allows you to map constructors from derived classes or factory methods from external builders directly to the target type's registry.

### **Key Use Cases**

```csharp
var info = registry.GetOrAdd<UserProfile>();

// 1. Visibility Overrides
// Manually register a private constructor.
var privateCtor = typeof(UserProfile).GetConstructor(
    BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(Guid) }, null);
info.AddPossibleConstruction(privateCtor);

// 2. External Factory Builders
// Register a static method from an external "UserFactory" class.
var externalFactory = typeof(UserFactory).GetMethod("CreateLegacyUser");
info.AddPossibleConstruction(externalFactory);

// 3. Constructors from Derived Types
// Register a constructor from a derived type. This is valid because 
// DerivedUserProfile is stack-equivalent to UserProfile.
var derivedCtor = typeof(DerivedUserProfile).GetConstructor(new[] { typeof(int), typeof(int) });
info.AddPossibleConstruction(derivedCtor);
```

### **Prioritization Logic**

When you manually add a construction, it is treated as a **High Priority** item.

1. **The Jump:** It attempts to move to the very top of the registry.
2. **The Constraint:** It will only be stopped if an existing entry is **More Specific** than the one being added.
3. **The Result:** If stopped, it settles directly behind that more specific entry.

---
Ah, that makes the API even cleaner. Having `PossibleConstructors` as a property with a setter implies a very direct "get, modify, set" workflow.

Here is the final, corrected version of that section.

---

## **Refinement & Bulk Injection**

For scenarios requiring total control, you can directly manipulate the collection of entry points. This is useful for re-sorting, filtering, or adding multiple items at once by interacting with the registry property.

`public ReadOnlySpan<MethodCtorInfo> PossibleConstructors { get{...} set{...} }`

### **Lazy Discovery & The `Init()` Method**

The engine performs its automatic discovery **lazily**. This means that if you access or modify the registry before it has been initialized, the engine may still find and append public constructors later.

* **To ensure you are working with the full set:** You should explicitly call `info.Init()` first. This forces the discovery phase to complete immediately.
* **If you don't call `Init()`:** Any items you add manually will still follow the **Priority Logic**, but subsequent naturally discovered items will be added at the end (unless they are more specific than an existing item, in which case they will jump forward as usual).

> You can create a `MethodCtorInfo` directly from any `MethodBase` using:
> `new MethodCtorInfo(MethodBase methodBase)`. The `MethodCtorInfo` constructor performs validation for **Viable Parameters**. Use `public static bool TryNew(MethodBase MethodBase, out MethodCtorInfo mci)` for a safe alternative.

> **Return Type Validation:** When setting the `PossibleConstructors` property, the engine performs a final check to ensure every entry is **Stack Equivalent** to the target type. If a method with an incompatible return type is found in your new collection, the engine will **throw an exception**.

---

## **Generic Type Handling**

The engine treats generic types as their **Generic Type Definition** (e.g., `Result<>` rather than `Result<int>`) by default. This design ensures a single, centralized registry; any configurations or manual entry points added to the definition will automatically apply to every variation of that type.

### **Priority: Closed vs. Open Definitions**

When resolving a type, the engine prioritizes specificity to allow for "special case" overrides:

1. **Exact Match:** It first checks if there is a registry entry for the specific **closed type** (e.g., a specialized configuration for `Result<string>`).
2. **Generic Definition:** If no exact match is found, it uses the registry for the **generic definition** (e.g., `Result<>`) and closes the discovered methods during usage to match the required type arguments.

### **Adding Generic Entry Points**

To ensure the engine can successfully close methods at runtime, the following rules apply when adding entry points to a generic definition:

* **Argument Mapping:** You can add a generic method as an entry point only if its generic arguments **match the target type exactly** in order and count.
* **Non-Generic Declaring Types (Universal Rule):** Regardless of whether your target type is generic or non-generic, the **declaring class** of the method or constructor you are adding **cannot be generic**. The engine requires a concrete host to resolve the call.

### **Example: Generic Factory Mapping**

```csharp
// The target type definition
public class DataWrapper<T> 
{ 
    public DataWrapper(T value) { } 
}

// VALID: Non-generic static class
public static class WrapperFactory
{
    public static DataWrapper<T> Create<T>(T value) => new DataWrapper<T>(value);
}

// INVALID: The declaring class is generic. 
// Even if the method is valid, the engine cannot use this.
public static class GenericFactory<T> 
{
    public static DataWrapper<T> Create(T value) => new DataWrapper<T>(value);
}
```

## **Post-Creation Initialization**

The `AvailableMembers` collection defines how the engine can continue to populate an object after it has been instantiated.

`public ReadOnlySpan<MemberParser> AvailableMembers { get{...} set{...} }`

### **Automatic Discovery**

The engine identifies standard members for post-creation assignment:

* **Fields:** Public fields (excluding `readonly` or `const`).
* **Properties:** Properties with a **public setter**.

> **Init-Only Properties are ignored**. Since this phase occurs after construction, `init` accessors cannot be called without causing runtime errors.

### **Manual Registration & External Setters**

You can manually add entries to `AvailableMembers` to include private members or **external setter methods**. When using an external method to initialize a value, it must follow these rules:

* **Signature:** The method must be `static`. The **first parameter** must be the object instance being populated, and the **second parameter** must be the value to assign. (exactly 2 parameters)
* **Non-Generic Declaring Types:** The class hosting the method **cannot be generic**.
* **Generic Alignment:** If the target type is generic (e.g., `Wrapper<T>`), the setter method must also be generic with the exact same type arguments in the same order.

```csharp
public static class ExternalLogic
{
    // Instance first (UserProfile), Value second (string)
    public static void SetSecretCode(UserProfile instance, string code) => ...
}

// Wrapping the method for the registry
var method = typeof(ExternalLogic).GetMethod("SetSecretCode");
var matcher = ParamInfo.TryNew(method.GetParameters()[1]); // Match against the value parameter
var manualInit = new MemberParser(method, matcher);

```

---

## **Authorization & Execution**

During negociation, the engine only consider these post-creation assignments if the selected construction path allows it:

* **Authorized by Default:** Onlt the **parameterless constructor** authorize post-creation assignements by default.
* **Authorized by Attribute:** Any path (constructor or factory) decorated with **`[CanCompleteWithMembers]`**.

### **Warning: The Overwrite Risk**

Post-initialization happens **after** the constructor runs. If a constructor sets a value that is also available in the data source, the post-initialization phase **will overwrite** the constructor's value.

> When using `[CanCompleteWithMembers]`, ensure that values set by your constructor won't be overridden. You can prevent this by:
>   1. Making the member non-public or non-writable (so it isn't discovered).
>   2. Manually removing the member from the `AvailableMembers` collection.

---

## **The Negotiation Matcher**

The **`IDbTypeParserMatcher`** is the component that negotiates with the data schema. Every "slot" (constructor parameter or available member) is paired with a matcher that decides if the schema can satisfy its requirements.

### **1. Discovery & Registry Building**

When the engine builds its internal registry, it decides which matcher to use based on the attributes present on the item:

* **Attribute Presence (`IDbReadingMatcherMaker`):** If a parameter or member is decorated with an attribute that implements this interface, the engine calls that attribute to create the matcher. This provides **total control** over how that specific slot negotiates with the schema.
* **Default Fallback (`ParamInfo`):** If no such attribute is present, the engine defaults to `ParamInfo`. While it is the default, it is highly configurable through other specific functional attributes.

---
## **ParamInfo: The Type**

Since the type may not be final, the `ParamInfo` doesn't store a fixed set of matching rules. Instead, it waits until it is actually used. During the **negotiation phase**, it "closes" the type. Once the type is concrete, the `ParamInfo` decides how to handle it:

* **Basic Types:** If it resolves to something like an `int`, `string`, or `Enum`, it looks for a single column in the schema that matches the name and is implicitly convertible.
* **Complex Types:** If it resolves to a complex type (like `T` becoming a specific class or `Result<T>` becoming `Result<User>`), it delegates the work. It asks the `TypeParsingInfo` for that specific type to handle the matching.
That�s the most accurate way to frame it. The logic doesn't change based on depth; it is a consistent, recursive process where every `ParamInfo` simply applies the **Column Modifier** it received, regardless of whether that modifier is currently empty or already contains three layers of prefixes.
---

## **ParamInfo: The Names**

The **`INameComparer`** manages a list of candidate names (the parameter/member name plus any **`[Alt(string altName)]`** names). This list is used to negotiate with the schema, but it never acts in isolation�it always operates through a **Column Modifier**. The Column Modifier is essentially a cumulative prefix that is passed down the construction tree. At the root level, this modifier is empty. As the engine moves deeper into nested types, the modifier grows.

* **Simple Types:** The engine looks for a column by combining the current **Column Modifier** with its own **Candidate Names**. It iterates through its candidates and tries to find a match for `[Modifier] + [Candidate]`. The first one that is both name-matched and type-compatible wins.
* **Complex Types (Delegation):** The `ParamInfo` doesn't match a column itself. Instead, it "updates" the Column Modifier. It appends its own name(s) to the existing modifier and passes this new, cumulative version down to the `TypeParsingInfo` registry.
---

## **ParamInfo: The Null Handler**

The **`INullColHandler`** provides instructions for the execution phase. It does not participate in the negotiation or the search for matching columns; instead, it defines how the compiled function reacts when a data source returns a `NULL`.

### **Passing the Instruction**

The `ParamInfo` carries the handler and dispatches it based on how the type is handled:

* **Simple Types:** If a match is determined, the handler is passed to the compiler to generate the specific IL for that field or argument.
* **Complex Types:** The handler is passed down to the **`TypeParsingInfo`**. It is made available to the sub-registry to be applied if a valid construction path is found.

### **Functional Control**

The engine assigns a handler based on the type's nature, which can be overridden by attributes:

* **`NullableColHandler`**: The default for reference types and `Nullable<T>`. It allows the `NULL` value to be passed through to the object.
* **`NotNullColHandler`**: The default for non-nullable structs. It is also used when the **`[NotNull]`** attribute is applied to a reference type or `Nullable<T>`, ensuring an exception is thrown if a `NULL` is encountered.
* **`[InvalidOnNull]`**: This instruction tells the compiled function to immediately exit the current construction path. It indicate that when that parameter is `null` all the item should be null, from that point the caller will handle if it accept nullable or not or leave the responsability to its caller.
* **Custom Implementations**:
* **`INullColHandler`**: Defines the logic for checking nulls and deciding the jump or exception behavior at execution time.
* **`INullColHandlerMaker`**: A factory used during metadata discovery to produce the specific `INullColHandler` for a parameter or member.

### **Registry Example**

#### **Source Code**

```csharp
public class Container<T> {
    public Container(
        [NotNull] string label, 
        [Alt("Item")] Item<T>? content
    ) { ... }
}

public class Item<T> {
    public Item(
        [InvalidOnNull] T id, 
        string description
    ) { ... }
}
```
#### **1. Registry Entry: `Container<T>`**

The engine has discovered and registered the following constructor for the `Container<T>` type.

**Constructor Signature:** `public Container(string label, Item<T>? content)`

| Parameter | Saved Type | Name Candidates | Null Handler |
| --- | --- | --- | --- |
| **`label`** | `string` | `["label"]` | **`NotNullColHandler`** |
| **`content`** | `Item<T>` | `["content", "Item"]` | **`NullableColHandler`** |

#### **2. Registry Entry: `Item<T>`**

The engine has discovered and registered the following constructor for the `Item<T>` type. This is stored as a reusable blueprint.

**Constructor Signature:** `public Item(T id, string description)`

| Parameter | Saved Type | Name Candidates | Null Handler |
| --- | --- | --- | --- |
| **`id`** | `T` (Generic) | `["id"]` | **`JumpIfNull`** |
| **`description`** | `string` | `["description"]` | **`NullableColHandler`** |

### **Manual Post-Creation Overrides**

The **`IDbTypeParserMatcher`** interface allows for direct modification of a matcher�s behavior after its initial creation. This enables fine-grained control over how a specific slot will negotiate with the schema or behave at execution time.

* **Property Swapping:** The **`NameComparer`** and **`NullColHandler`** properties are writable. You can replace the default strategies with custom implementations to change how columns are matched or how `NULL` values are processed.
* **Direct Configuration:** The **`AddAltName`** and **`SetInvalidOnNull`** methods provide a clean, high-level way to perform common adjustments�such as injecting aliases or switching to a "Jump" strategy.

> **Implementation Note:** The outcome of these modifications is entirely in the hands of the implementation. A custom matcher may ignore these calls or handle them through internal mechanisms that do not follow the standard `ParamInfo` logic.

### **Bulk Configuration via TypeParsingInfo**

The **`TypeParsingInfo`** instance provides methods to apply configurations across the registry based on a "default name." This ensures that specific naming or null-handling rules are applied consistently, even if the engine chooses between different construction paths or members.

* **`SetInvalidOnNull(string defaultName, bool invalidOnNull)`**: This method scans the **`PossibleConstructors`** collection. For every matcher where the parameter name matches `defaultName`, it calls that matcher's `SetInvalidOnNull` method.
* **`AddAltName(string defaultName, string nameToAdd)`**: This method applies to both **`PossibleConstructors`** and **`AvailableMembers`**. For every matcher where the parameter name matches `defaultName`, it calls that matcher's `AddAltName` method.

### **Customizing Negotiation**

Every type has a negotiation process that can be entirely replaced. By setting the `Matcher` property on the `TypeParsingInfo` instance, you can define a custom strategy for how that type should resolve itself against a schema.

`public IDbTypeParserInfoMatcher? Matcher { get; set; }`

If this property is provided, the engine delegates all matching logic for that type to your custom implementation every time the type is encountered.

---

## **The Negotiation Process**

When `GetParser<T>` is called, the engine first checks an internal cache for an existing function mapped to the exact current database schema. If a match is found, the cached `Func<DbDataReader, T>` is returned immediately. If not, the engine retrieves the `TypeParsingInfo` for the target type and initiates the **Negotiation Process**.

---

### **The Default Negotiation Logic**

If no custom `Matcher` is provided, the engine follows a structured, recursive search to find the best fit for the schema:

#### **1. Evaluating Entry Points**

The engine loops through the `PossibleConstructors` in their priority order. For each entry point, it attempts to "match" every parameter by calling its associated **`IDbTypeParserMatcher`**.

* **Recursive Matching:** A matcher may trigger deep recursion (e.g., if a parameter is itself a complex type) to successfully build its branch of the resolution tree.
* **Column Tracking:** As columns are matched, they are marked as "used" to ensure a single database column isn't mapped to multiple parameters within the same construction path.
* **Path Failure:** If any single parameter fails to match, the engine abandons that entry point and moves to the next one in the registry.

#### **2. Member Completion**

Once a valid entry point is found (all parameters matched), the engine checks if that path is authorized to **Complete with Members**:

* **Standard Paths:** If authorized, the engine attempts to match as many `AvailableMembers` as possible from the remaining unmatched columns.
* **Parameterless Fallback:** If no specific entry point with parameters matches, the engine checks for a parameterless constructor. If found, it populates the object solely using `AvailableMembers`.

#### **3. Local Match (Tree Nodes)**

During the recursive search, the engine determines if a specific type can be satisfied by the schema:

* **Failure:** If no entry point can satisfy the schema (and no parameterless fallback available), the type fails to match. This failure bubbles up to the parent caller, who may then try an alternative construction path.
* **Success:** A match is found. The engine creates a **Resolution Node** that encapsulates the chosen entry point, the tree branches for its parameters, and (if authorized) any matching branches for its members.

#### **4. Global Result (The Final Function)**

Once the process returns to the root type (from `GetParser`):

* **Global Success:** A complete resolution tree has been successfully constructed from the root down to every leaf. The engine traverses this tree to compile the optimized IL function, caches it for future use, and returns it.
* **Global Failure:** If the root target type cannot find any viable path to satisfy the schema, the engine throws an exception.
