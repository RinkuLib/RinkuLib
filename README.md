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
IEnumerable<User> users = builder.Query<IEnumerable<User>>(cnn);
List<User> users = builder.Query<List<User>>(cnn);
User user = builder.Query<User>(cnn);

// Resulting SQL: SELECT ID, Name FROM Users WHERE Group = @Grp AND Age > @MinAge
```

## The reasons it exist: Separation of concern, Customization and flexibility

When dynamicaly building SQL, individual SQL segment must be able to make a valid SQL. You never see the whole picture until processing. By defining a template first, you can have your c# logic focussing on checking validity and then simply need to "inform" the builder of what you use. That way you can have total separation of concern and no matter where an item affect the SQL result, you can keep exactly the same logic ensuring SQL validity and letting you make oprimized SQL commands without any compromizes.
When mapping to a type, you rearely need a flat object as the logic item, has a deep, fully customizable, negociation phase that lets you map the flat row result of the DB, to the multi level nesting of the c# type.
In truth, originaly it was meant as an extensions to `Dapper`, but the blueprint engine had to create the whole `DbCommand` to be efficient, so I made the mapping part.


### The 3-Step Process

1.  **Interpretation (`QueryCommand`):** A reusable blueprint. The engine analyzes your SQL template to create a structural blueprint and sets up storage for parameter instructions cache and mapping functions cache.

2.  **State Definition (`QueryBuilder`):** A temporary struct. You create this for every database call to hold your specific parameters and true conditions. It acts as the bridge between your C# data and the command's blueprint.

3.  **Execution (`Query` / `ExecuteX` methods):** The DB call using methods (such as `QueryAsync`, `Query`, `Execute`, `ExecuteReader`, etc.). The engine takes the blueprint from Step 1 and the data from Step 2 to generate the finalized SQL and create the complete `DbCommand`. It then find the mots apropriate mapping function between the schema and the type.

---

### Rinku vs. Dapper: Performance Profile

| Method                            | Mean       | Ratio | Allocated | Alloc Ratio |
|-----------------------------------|------------|-------|-----------|-------------|
| **1. Single Row (Sync)**          |            |       |           |             |
| Dapper_QueryFirstOrDefault        |   533.7 us |  1.00 |   3.66 KB |        1.00 |
| Rinku_QueryT                      |   512.3 us |  0.96 |   3.07 KB |        0.84 |
| **2. Single Row (Async)**         |            |       |           |             |
| Dapper_QueryFirstOrDefaultAsync   |   579.6 us |  1.00 |   5.61 KB |        1.00 |
| Rinku_QueryTAsync                 |   552.4 us |  0.96 |   4.81 KB |        0.86 |
| **3. Streaming (Sync)**           |            |       |           |             |
| Dapper_QueryUnbuffered            |   631.6 us |  1.00 |  20.84 KB |        1.00 |
| Rinku_QueryIEnumerable            |   632.2 us |  1.00 |  15.46 KB |        0.74 |
| **4. Buffered (Sync)**            |            |       |           |             |
| Dapper_QueryBuffered              |   626.8 us |  1.00 |  22.98 KB |        1.00 |
| Rinku_QueryList                   |   623.2 us |  0.99 |  17.52 KB |        0.76 |
| **5. Streaming (Async)**          |            |       |           |             |
| Dapper_QueryUnbufferedAsync       |   688.4 us |  1.00 |  22.87 KB |        1.00 |
| Rinku_StreamQueryAsync            |   691.4 us |  1.00 |  24.51 KB |        1.07 |
| **6. Buffered (Async)**           |            |       |           |             |
| Dapper_QueryAsyncBuffered         |   691.6 us |  1.00 |  24.79 KB |        1.00 |
| Rinku_QueryAsyncList              |   656.9 us |  0.95 |  19.36 KB |        0.78 |
| **7. Dynamic Objects**            |            |       |           |             |
| Dapper_QueryAsyncDynamic          |   565.4 us |  1.00 |   5.77 KB |        1.00 |
| Rinku_QueryAsyncDynaObject        |   556.3 us |  0.98 |   4.95 KB |        0.86 |
| **8. Complex Mapping (Nested)**   |            |       |           |             |
| Dapper_Complex                    |   570.1 us |  1.00 |   6.25 KB |        1.00 |
| Rinku_Complex                     |   570.6 us |  1.00 |   5.41 KB |        0.87 |
| **9. Command Execution (Sync)**   |            |       |           |             |
| Dapper_Execute                    | 1,518.6 us |  1.00 |   2.33 KB |        1.00 |
| Rinku_Execute                     | 1,516.0 us |  1.00 |   1.76 KB |        0.76 |
| **10. Command Execution (Async)** |            |       |           |             |
| Dapper_ExecuteAsync               | 1,624.6 us |  1.00 |   3.95 KB |        1.00 |
| Rinku_ExecuteAsync                | 1,587.9 us |  0.98 |   3.37 KB |        0.85 |
| **11. Collection Params (IN)**    |            |       |           |             |
| Dapper_InClause                   |   604.8 us |  1.00 |   8.01 KB |        1.00 |
| Rinku_InClause                    |   598.5 us |  0.99 |   6.38 KB |        0.80 |

---

## Templating Syntax (SQL generation)

The engine analyzes your SQL to create a **reusable blueprint**, fragmenting the query into "footprints" that are preserved or pruned based on the presence of data.

* **Conditional Markers:** Uses `?@Var` and `/*...*/` to define optional segments, parameters or even entire clauses and ensure valid SQL syntax.
* **Structural Handlers:** Special suffixes like `_N` (Numeric), `_X` (Collection spreading), and `_R` (Raw injection) to influence the result using runtime data.

**[Read the Full Templating Syntax Documentation](https://github.com/RinkuLib/RinkuLib/blob/main/TemplatingSyntaxDoc.md)**

---

## Mapping Engine (Complex type Negotiation)

Stores metadata about types that are customizable and will be used during negociation to generate an optimal IL-compiled mapping function from type to schema.

* **Schema Negotiation:** Out of the box, it can map complex types via various ctors, factory methods, members or even a mix of both. It even handle recursive types.
* **Customization:** Almost everything is public and let you adjust the negociation registery how you see fit.

**[Read the Full Mapping Engine Documentation](https://github.com/RinkuLib/RinkuLib/blob/main/MappingEngineDoc.md)**

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

**[Read the Full QueryCommand Documentation](https://github.com/RinkuLib/RinkuLib/blob/main/QueryCommandDoc.md)**

---
## State Initialization: The Builders

To make a call to the DB, you need a **state**, this is where the builders are used. The **state** determines which optional SQL segments are preserved and provides the actual data for parameters.

There are two types of builder via `StartBuilder()` extensions for different needs.

### `QueryBuilder` (Single-trip queries)

**Use Case:** Most of the time when you dont want to keep a `DbCommand` alive.

```csharp
var builder = userCmd.StartBuilder();
builder.Use("@id", 10);
var user = builder.Query<User>(cnn);
```

### `QueryBuilderCommand<T>` (Multiple call)
**Use Case:** Mainly for batch processing when you dont want to remake a `DbCommand` each time.

```csharp
using var sqlCmd = new SqlCommand();
var builder = userCmd.StartBuilder(sqlCmd);

foreach(var val in dataList) {
    builder.Use("@val", val);
    builder.Execute(cnn); // Reuses the internal command object
}
```
#### One step building
There are ways to initialize the state in one step by using object instances with the state values:
```csharp
public record class StateParameters(int? MinSalary, string? DeptName, [property:NotNullOrWhitespace] string? EmployeeStatus) {
    public int OtherField = 32;
    [ForBoolCond] public bool Year;
}
```
By default, it match the members with variables in SQL (`@DeptName` instead of `DeptName`), if you want to correspond to a boolean condition, you must use the `[ForBoolCond]` attribute and the member must be of type bool.
There are also options to modify the "usage" condition or the returned value using attributes inheriting `AccessorEmiterHandler` (eg, `NotNullOrWhitespace` will only use if the value is not null nor whitespace)
```csharp
var user = userCmd.Query<User>(cnn, new StateParameters(10, "Marketing", "  ") { Year = true });
```
It is also possible to use parameter objects with a builder if you want to modify the values before executing
```csharp
var builder = userCmd.StartBuilder(); // or .StartBuilder(sqlCmd);
builder.UseWith(stateParams);
builder.Use("Year");
var users = builder.Query<IEnumerable<User>>(cnn);
```

---
## Execution: QueryX via QueryBuilder

The `Query` and `ExecuteX` extension methods handle the entire database "trip." They generate the final SQL from your template, synchronize parameters, and execute the command in one step.
Here are the extensions they are available in both **Synchronous** and **Asynchronous** versions.

| Goal | Method | Sync Return | Async Return |
| --- | --- | --- | --- |
| **Update/Delete/Insert** | `Execute` | `int` | `Task<int>` |
| **Update/Delete/Insert** | `ExecuteScalar<T>` | `T` | `Task<T>` |
| **Fetch Single Row** | `Query<T>` | `T?` | `Task<T?>` |
| **Fetch Multiple Rows** | `Query<List<T>>` | `List<T>` | `Task<List<T>>` |
| **Stream Multiple Rows** | `Query<IEnumerable<T>>` | `IEnumerable<T>` | `Task<IEnumerable<T>>` |
| **Stream Multiple Rows Async** | `StreamQueryAsync<T>` | N/A | `IAsyncEnumerable<T>` |
| **Get Reader** | `ExecuteReader` | `DbDataReader` | `Task<DbDataReader>` |
| **Get MultiReader** | `ExecuteMultiReader` | `MultiReader` | `Task<MultiReader>` |

> `Query<T>` returns `T?` but for manualy handled cases like `Query<List<T>>` or `Query<IEnumerable<T>>` the return value will never be null unless manualy changed. The possible null value can safely be ignored in thoses cases

```csharp
var user = builder.Query<User>(cnn);
var users = builder.Query<IEnumerable<User>>(cnn);
var (user, supervisor) = await builder.QueryAsync<(User, Supervisor)>(cnn);
var cboItems = await builder.StreamQueryAsync<KeyValuePair<int, string>>(cnn, null, null, ct);
var id = builder.Query<int>(cnn);
var names = await builder.QueryAsync<List<string>>(cnn);
var nbAffected = builder.Execute(cnn, trans);
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
| **Update/Delete/Insert** | `Execute` | `int` | `Task<int>` |
| **Update/Delete/Insert** | `ExecuteScalar<T>` | `T` | `Task<T>` |
| **Fetch Single Row** | `Query<T>` | `T?` | `Task<T?>` |
| **Fetch Multiple Rows** | `Query<List<T>>` | `List<T>` | `Task<List<T>>` |
| **Stream Multiple Rows** | `Query<IEnumerable<T>>` | `IEnumerable<T>` | `Task<IEnumerable<T>>` |
| **Stream Multiple Rows Async** | `StreamQueryAsync<T>` | N/A | `IAsyncEnumerable<T>` |
| **Get Reader** | `ExecuteReader` | `DbDataReader` | `Task<DbDataReader>` |
| **Get MultiReader** | `ExecuteMultiReader` | `MultiReader` | `Task<MultiReader>` |

The parameters are a bit different and may change from one method to the other.
