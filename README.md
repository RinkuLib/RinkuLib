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

| Method                               | Mean       | Ratio | Allocated | Alloc Ratio |
|--------------------------------------|------------|-------|-----------|-------------|
| **1. Query one Sync**                                                               |
| Dapper_QueryFirst                    |   526.8 us |  1.00 |   3.66 KB |        1.00 |
| Rinku_QueryT                         |   508.6 us |  0.97 |   3.07 KB |        0.84 |
| **2. Query one (or default) Sync**                                                  |
| Dapper_QueryFirstOrDefault           |   521.3 us |  1.00 |   3.66 KB |        1.00 |
| Rinku_QueryOptionalT                 |   521.3 us |  1.00 |   3.07 KB |        0.84 |
| **3. Query one (single) Sync**                                                      |
| Dapper_QuerySingle                   |   510.0 us |  1.00 |   3.66 KB |        1.00 |
| Rinku_QuerySingleT                   |   512.0 us |  1.00 |   3.07 KB |        0.84 |
| **4. Query one Async**                                                              |
| Dapper_QueryFirstAsync               |   559.8 us |  1.00 |   5.71 KB |        1.00 |
| Rinku_QueryTAsync                    |   540.4 us |  0.97 |   4.91 KB |        0.86 |
| **5. Query one (or default) Async**                                                 |
| Dapper_QueryFirstOrDefaultAsync      |   554.5 us |  1.00 |   5.71 KB |        1.00 |
| Rinku_QueryOptionalTAsync            |   546.8 us |  0.99 |   4.91 KB |        0.86 |
| **6. Query one (single) Async**                                                     |
| Dapper_QuerySingleAsync              |   555.9 us |  1.00 |    5.8 KB |        1.00 |
| Rinku_QuerySingleTAsync              |   544.4 us |  0.98 |   5.01 KB |        0.86 |
| **7. Query Sync (Stream)**                                                          |
| Dapper_QueryUnbuffered               |   602.6 us |  1.00 |  20.84 KB |        1.00 |
| Rinku_QueryIEnumerable               |   601.1 us |  1.00 |  15.46 KB |        0.74 |
| **8. Query Buffered Sync**                                                          |
| Dapper_QueryBuffered                 |   603.0 us |  1.00 |  22.98 KB |        1.00 |
| Rinku_QueryList                      |   601.1 us |  1.00 |  17.52 KB |        0.76 |
| **9. Query Async (Stream)**                                                         |
| Dapper_QueryUnbufferedAsync          |   654.7 us |  1.00 |  22.87 KB |        1.00 |
| Rinku_StreamQueryAsync               |   643.4 us |  0.98 |  17.41 KB |        0.76 |
| **10. Query Buffered Async**                                                        |
| Dapper_QueryAsyncBuffered            |   653.4 us |  1.00 |  24.79 KB |        1.00 |
| Rinku_QueryAsyncList                 |   650.3 us |  1.00 |  19.36 KB |        0.78 |
| **11. Dynamic Async**                                                               |
| Dapper_QueryAsyncDynamic             |   570.5 us |  1.00 |   5.77 KB |        1.00 |
| Rinku_QueryAsyncDynaObject           |   570.0 us |  1.00 |   4.95 KB |        0.86 |
| **12. Complex Mapping**                                                             |
| Dapper_Complex                       |   587.2 us |  1.00 |   6.25 KB |        1.00 |
| Rinku_Complex                        |   581.1 us |  0.99 |   5.41 KB |        0.87 |
| **13. Execute Sync**                                                                |
| Dapper_Execute                       | 1,476.8 us |  1.00 |   2.33 KB |        1.00 |
| Rinku_Execute                        | 1,443.6 us |  0.98 |   1.76 KB |        0.76 |
| **14. Execute Async**                                                               |
| Dapper_ExecuteAsync                  | 1,511.7 us |  1.00 |   3.95 KB |        1.00 |
| Rinku_ExecuteAsync                   | 1,485.1 us |  0.98 |   3.37 KB |        0.85 |
| **15. IN Clause**                                                                   |
| Dapper_InClause                      |   604.3 us |  1.00 |   8.01 KB |        1.00 |
| Rinku_InClause                       |   592.1 us |  0.98 |   6.63 KB |        0.83 |

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
| **Fetch One Result (throw when no rows)** | `Query<T>` | `T` | `Task<T>` |
| **Fetch One Result or Default** | `Query<Optional<T>>` | `Optional<T>` (`T`) | `Task<Optional<T>>` |
| **Fetch One Single Result** | `Query<Single<T>>` | `Single<T>` (`T`) | `Task<Single<T>>` |
| **Fetch One Single Result that can be null** | `Query<MaybeNull<T>>` | `MaybeNull<T>` (`T`) | `Task<MaybeNull<T>>` |
| **Fetch One Single Result null struct** | `Query<T?>` | `T?` | `Task<T?>` |
| **Fetch Multiple Results** | `Query<List<T>>` | `List<T>` | `Task<List<T>>` |
| **Stream Multiple Results** | `Query<IEnumerable<T>>` | `IEnumerable<T>` | `Task<IEnumerable<T>>` |
| **Stream Multiple Results Async** | `StreamQueryAsync<T>` | N/A | `IAsyncEnumerable<T>` |
| **Get Reader** | `ExecuteReader` | `DbDataReader` | `Task<DbDataReader>` |
| **Get MultiReader** | `ExecuteMultiReader` | `MultiReader` | `Task<MultiReader>` |

> Multiple types like `Optional<T>`, `Single<T>`, `MaybeNull<T>` are implicilty convertible to `T`

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

The parameters are a bit different and may change from one method to the other.
