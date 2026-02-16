# RinkuLib: A Modular Micro-ORM
[![Rinku](https://img.shields.io/nuget/v/Rinku)](https://www.nuget.org/packages/Rinku/)
[![Rinku](https://img.shields.io/nuget/dt/Rinku)](https://www.nuget.org/packages/Rinku/)


RinkuLib is a micro-ORM built on top of **ADO.NET**. It separate any SQL construction from the c# structure and provide a declarative way to build them. The engine also has complex type mapping compability with multiple customization options.

The library is designed as two independent, highly customizable parts
* SQL command generation with flexible templating engine
* Complex type parsing with negociation phase to use the most appropriate construction

---

## Quick Start

```csharp
// 1. INTERPRETATION: The blueprint (SQL Generation Part)
// Define the template once to analyzed and cached the sql generation conditions
string sql = "SELECT ID, Name FROM Users WHERE Group = @Grp AND Cat = ?@Category AND Age > ?@MinAge";
QueryCommand query = new QueryCommand(sql);

// 2. STATE DEFINITION: The transient builder (State Data)
// Create a builder for a specific database trip
QueryBuilder builder = query.StartBuilder();
builder.Use("@MinAge", 18);    // Will add everything related to the variable
builder.Use("@Grp", "Admin");  // Always added to the string and throw if not used
                               // @Category is not used so nothing related to it will be use

// 3. EXECUTION: DB call (SQL Generation + Type Parsing Negotiation)
using DbConnection cnn = GetConnection();
// Generates the final SQL, assign the parameters and fetches the compiled parser delegate.
IEnumerable<User> users = builder.QueryMultiple<User>(cnn);

// Resulting SQL: SELECT ID, Name FROM Users WHERE Group = @Grp AND Age > @MinAge
```

## The reasons it exist: Separation of concern, Customization and flexibility

When dynamicaly building SQL, individual SQL segment must be able to make a valid SQL. You never see the whole picture until processing. By defining a template first, you can have your c# logic focussing on checking validity and then simply need to "inform" the builder of what you use. That way you can have total separation of concern and no matter where an item affect the SQL result, you can keep exactly the same logic ensuring SQL validity and letting you make oprimized SQL commands without any compromizes.
When mapping to a type, you rearely need a flat object as the logic item, has a deep, fully customizable, negociation phase that lets you map the flat row result of the DB, to the multi level nesting of the c# type.
In truth, originaly it was meant as an extensions to `Dapper`, but the blueprint engine had to create the whole `DbCommand` to be efficient, so I made the mapping part.


### The 3-Step Process

1.  **Interpretation (`QueryCommand`):** A reusable blueprint. The engine analyzes your SQL template to create a structural blueprint and sets up storage for parameter instructions cache and mapping functions cache.

2.  **State Definition (`QueryBuilder`):** A temporary struct. You create this for every database call to hold your specific parameters and true conditions. It acts as the bridge between your C# data and the command's blueprint.

3.  **Execution (`QueryX` methods):** The DB call using methods (such as `QueryMultipleAsync`, `QueryFirst`, etc.). The engine takes the blueprint from Step 1 and the data from Step 2 to generate the finalized SQL and create the complete `DbCommand`. It then find the mots apropriate mapping function between the schema and the type.

---

### Templating Syntax (SQL generation)

The engine analyzes your SQL to create a **reusable blueprint**, fragmenting the query into "footprints" that are preserved or pruned based on the presence of data.

* **Conditional Markers:** Uses `?@Var` and `/*...*/` to define optional segments, parameters or even entire clauses and ensure valid SQL syntax.
* **Structural Handlers:** Special suffixes like `_N` (Numeric), `_X` (Collection spreading), and `_R` (Raw injection) to influence the result using runtime data.

**[Read the Full Templating Syntax Documentation](https://github.com/adamtherrienmarois/RinkuLib/blob/main/TemplatingSyntaxDoc.md)**

---

### Mapping Engine (Complex type Negotiation)

Stores metadata about types that are customizable and will be used during negociation to generate an optimal IL-compiled mapping function from type to schema.

* **Schema Negotiation:** Out of the box, it can map complex types via various ctors, factory methods, members or even a mix of both. It even handle recursive types.
* **Customization:** Almost everything is public and let you adjust the negociation registery how you see fit.

**[Read the Full Mapping Engine Documentation](https://github.com/adamtherrienmarois/RinkuLib/blob/main/MappingEngineDoc.md)**

---

## Interpretation: Creating a QueryCommand

The `QueryCommand` is a reusable object, that cache every info related to any possible variations of a command. You define it once from a specific SQL template, and it becomes the host all variation of the commands.

```csharp
// Parsing happens once at instantiation
var userCmd = new QueryCommand("SELECT * FROM Users WHERE ID = ?@id AND Age > ?@minAge");
```

When you create the command, it processes the template and generate an optimized internal structures. Refer to the templating syntax to see all the options, but from now on, you dont have to consider SQL anymore.

The `QueryCommand` stores the following key elements:

* **`Mapper`:** An indexer that maps parameter/conditions names (like `@id` or `IsInvalid`) to a specific integer index.
    > Thoses indexes are used internaly, external tools like the `QueryBuilder` uses the `Mapper` to place data into the correct slots of the state array.
* **`QueryText`:** It contains the logic for building the query string, where conditions refer directly to indices corresponding with `Mapper`.
* **`QueryParameters`:** A collection of `DbParamInfo` objects which store the metadata required to create `DbParameter` objects.
* **Parser Cache:** A specialized cache that stores compiled IL-functions for mapping database results to C# types based on the schema used.
* **Type Accessor Cache:** A specialized cache that stores compiled IL-functions for parameter object to generate the `DbCommand`.

**[Read the Full QueryCommand Documentation](https://github.com/adamtherrienmarois/RinkuLib/blob/main/QueryCommandDoc.md)**

---
## State Initialization: The Builders

To make a call to the DB, you need a **state**, this is where the builders are used. The **state** determines which optional SQL segments are preserved and provides the actual data for parameters.

There are two types of builder via `StartBuilder()` extensions for different needs.

### `QueryBuilder` (Single-trip queries)

**Use Case:** Most of the time when you dont want to keep a `DbCommand` alive.

```csharp
var builder = userCmd.StartBuilder();
builder.Use("@id", 10);
var user = builder.QueryFirst<User>(cnn);
```

### `QueryBuilderCommand<T>` (Multiple call)
**Use Case:** Mainly for batch processing when you dont want to remake a `DbCommand` each time.

```csharp
using var sqlCmd = new SqlCommand();
var builder = userCmd.StartBuilder(sqlCmd);

foreach(var val in dataList) {
    builder.Use("@val", val);
    builder.ExecuteQuery(cnn); // Reuses the internal command object
}
```
#### One step building
> In the futur there will be to use an object as parameter letting you directly call the QueryX with the parameter object and the blueprint
> (proof of concept, UserFilters will controll the usage of the variables and will inform the template to make the correct SQL)
> ```csharp
> var user = userCmd.QuerySingle<User>(userFilters, cnn);
> ```
But for now, you can use a type to auto populate a builder
```csharp
// will return a builder with the non null prop/field of the object that match the builder to used (with value)
var builder = userCmd.StartBuilderWith(obj);

var builder = userCmd.StartBuilderWith(ref largeStruct);

// You can manualy set multiple using a Span<ValueTupe<string, object>>
var builder = userCmd.StartBuilder([("@Name", "John"), ("IsActive", true)]);

// Equivalent also available once the builder is made
builder.UseWith(obj);
builder.UseWith(ref largeStruct);
builder.Use([("@Name", "John"), ("IsActive", true)]);
```

---
## Execution: QueryX via QueryBuilder

The `QueryX` extension methods handle the entire database "trip." They generate the final SQL from your template, synchronize parameters, and execute the command in one step.
`QueryX` comes in three primary operations, available in both **Synchronous** and **Asynchronous** versions.

| Goal | Method | Sync Return | Async Return |
| --- | --- | --- | --- |
| **Update/Delete/Insert** | `ExecuteQuery` | `int` | `Task<int>` |
| **Fetch Single Row** | `QuerySingle<T>` | `T?` | `Task<T?>` |
| **Stream Multiple Rows** | `QueryMultiple<T>` | `IEnumerable<T>` | `IAsyncEnumerable<T>` |

```csharp
var user = builder.QuerySingle<User>(cnn);
var users = builder.QueryMultiple<User>(cnn);
var (user, supervisor) = await builder.QuerySingleAsync<(User, Supervisor)>(cnn);
var cboItems = await builder.QueryMultipleAsync<KeyValuePair<int, string>>(cnn, null, null, ct);
var id = builder.QuerySingle<int>(cnn);
var names = await builder.QueryMultipleAsync<string>(cnn);
var nbAffected = builder.ExecuteQuery(cnn, trans);
```

All builder methods use a consistent signature for managing the database context:

* **`cnn`**: The `DbConnection` (or `IDbConnection`) to use.
* **`transaction`**: *(Optional)* The transaction to associate with the command.
* **`timeout`**: *(Optional)* Overrides the default command timeout.
* **`ct`**: *(Async only)* A `CancellationToken` for the task lifecycle.

The equivalent methods for `QueryBuilderCommand` does not take any parameters (except the `ct`), and does not reconfigure the associated `DbCommand` since managed by the builder.

---

## Direct Execution via DbCommand

In the spirit of modularity, the mapping engine is accesible using any `DbCommand` instance.

The same extensions are provided directly on the the `DbCommand` (there is also support for `IDbCommand`)

| Goal | Method | Sync Return | Async Return |
| --- | --- | --- | --- |
| **Update/Delete/Insert** | `ExecuteQuery` | `int` | `Task<int>` |
| **Fetch Single Row** | `QuerySingle<T>` | `T?` | `Task<T?>` |
| **Stream Multiple Rows** | `QueryMultiple<T>` | `IEnumerable<T>` | `IAsyncEnumerable<T>` |

The parameters are a bit different since the `DbCommand` should allready been properly made.

* **`disposeCommand`**: A boolean (defaults to **`true`**).
    * If **`true`**: The command is automatically disposed after execution or when the reader has ended.
    * If **`false`**: The command is not disposed, allowing it to be reused after the call.
* **`ct`**: *(Async only)* A `CancellationToken` for the task lifecycle.
