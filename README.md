# RinkuLib: A Modular Micro-ORM

RinkuLib is a micro-ORM built on top of **ADO.NET** that provides a declarative approach to SQL generation and object mapping. It replaces manual string concatenation with a structural blueprint and utilizes an IL-based recursive parser to negotiate and compile high-speed data mapping.

The library is designed as two independent, highly customizable parts—one for SQL command generation and another for complex type parsing—integrated into a unified, seamless workflow.

---

## 🚀 Quick Start

```csharp
// 1. INTERPRETATION: The blueprint (SQL Generation Part)
// Define the template once; the structure is analyzed and cached.
string sql = "SELECT ID, Name FROM Users WHERE Group = @Grp AND Age > ?@MinAge AND Cat = ?@Category";
QueryCommand query = new QueryCommand(sql);

// 2. STATE DEFINITION: The transient builder (State Data)
// Create a builder for a specific database trip.
QueryBuilder builder = query.StartBuilder();
builder.Use("@Grp", "Admin");    // Required: Fails if missing.
builder.Use("@MinAge", 18);      // Optional: Used, so segment is preserved.
                                 // @Category: NOT used, so segment is pruned.

// 3. EXECUTION: Unified process (SQL Generation + Type Parsing Negotiation)
// RinkuLib generates the final SQL and fetches the compiled parser delegate.
using DbConnection cnn = GetConnection();
IEnumerable<User> users = builder.QueryMultiple<User>(cnn);

// Resulting SQL: SELECT ID, Name FROM Users WHERE Group = @Grp AND Age > @MinAge
```

### The Philosophy: A Cohesive, Recursive Toolkit

RinkuLib is a full-process data tool designed as a series of interconnected, independent modules that work together for a seamless ORM experience. Its "open" architecture is built on a Tree of Responsibility—a nested hierarchy where every major process is decomposable into specialized sub-processes. This ensures you are never "locked in"; you can override a high-level engine, parameterize a mid-level branch, or inject logic into a single leaf-node, allowing you to "plug in" at any level of granularity without breaking the chain.


### The 3-Step Process

1.  **Interpretation (`QueryCommand`):** This is the **one-time setup**. The engine analyzes your SQL template to create a structural blueprint and sets up storage for parameter instructions and mapping functions—both of which are specialized and cached during actual usage.

2.  **State Definition (`QueryBuilder`):** This is the **temporary data container**. You create this for every database call to hold your specific parameters and true conditions. It acts as the bridge between your C# data and the command's blueprint.

3.  **Execution (`QueryX` methods):** This is the **final operation**. Using methods (such as `QueryMultipleAsync`, `QueryFirst`, etc.), the engine takes the blueprint from Step 1 and the data from Step 2 to generate the finalized SQL. It then negotiates for the compiled mapping function—either fetching or generating the most appropriate construction process for the current database schema—to turn the results into your C# objects.

---

## 🏗 The Core Engines

RinkuLib is built on two independent systems. For the full technical specifications, see the dedicated documentation for each:

### 1. Templating Syntax (SQL Segmentation)

The engine analyzes your SQL to create a **Structural Blueprint**, fragmenting the query into "Footprints" that are preserved or pruned based on the presence of data.

* **Conditional Markers:** Uses `?@Var` and `/*...*/` to define optional segments—from parameters to entire clauses—ensuring valid SQL syntax after pruning.
* **Structural Handlers:** Special suffixes like `_N` (Numeric), `_X` (Collection spreading), and `_R` (Raw injection) to adjust the query's physical structure at runtime.

👉 **[Read the Full Templating Syntax Documentation](https://github.com/adamtherrienmarois/RinkuLib/blob/main/TemplatingSyntaxDoc.md)**

---

### 2. Mapping Engine (Recursive Negotiation)

A **Tree of Responsibility** that reconciles the database schema with your C# types to compile optimized mapping delegates.

* **Schema Negotiation:** Dynamically maps database columns to complex types, handling non-1-to-1 relationships and varied data shapes without manual configuration.
* **Decomposable Process:** Every step of the mapping is a "plug-in" point, allowing you to inject custom logic into specific branches or leaf-nodes of the object graph.

👉 **[Read the Full Mapping Engine Documentation](https://github.com/adamtherrienmarois/RinkuLib/blob/main/MappingEngineDoc.md)**

---

## Creating a QueryCommand

The `QueryCommand` is a long-lived orchestrator. You define it once for a specific SQL template, and it becomes the host for all structural and performance logic.

```csharp
// Parsing happens once at instantiation
var userCmd = new QueryCommand("SELECT * FROM Users WHERE ID = ?@id AND Age > ?@minAge");

```

When you create the command, it processes the template and generates optimized internal structures. It is designed for versatility; by using optional markers (`?@`), this single instance can generate different SQL permutations based on the data provided.

The `QueryCommand` acts as a container for the results of the template parsing. It stores the following key elements:

* **`Mapper`:** A specialized indexer that maps parameter names (like `@id`) to a specific integer index. While the command uses indices internally, the Mapper allows external tools like the `QueryBuilder` to place data into the correct slots of the state array.
* **`QueryText`:** This is the finalized blueprint of your SQL. It contains the logic for building the query string, where conditions refer directly to indices corresponding with `Mapper`.
* **`QueryParameters`:** A collection of `DbParamInfo` objects, in order matching to `Mapper`, which store the metadata required to create `DbParameter` objects.
* **Parser Cache:** A specialized cache that stores compiled IL-functions for mapping database results to C# types based on the returned schema.

👉 **[Read the Full QueryCommand Documentation](https://github.com/adamtherrienmarois/RinkuLib/blob/main/QueryCommandDoc.md)**

---
## State Initialization: The Builders

After defining a `QueryCommand`, you must initialize its **State**. This transient phase determines which optional SQL segments are preserved and provides the actual data for parameters.

RinkuLib offers two builder types via `StartBuilder()` extensions to handle different execution patterns:

---

### 1. `QueryBuilder` (Transient)

Ideal for **single-trip queries**. It is a lightweight state container used to generate SQL and execute a command once.

* **Use Case:** Standard API calls or one-off fetches.
* **Logic:** Created, populated, and executed in a single scope.

```csharp
var builder = userCmd.StartBuilder();
builder.Use("@id", 10);
var user = builder.QueryFirst<User>(cnn);
```

### 2. `QueryBuilderCommand<T>` (Managed)

Designed for **repetitive execution** and high-performance loops. It wraps an existing `IDbCommand` to minimize object allocation.

* **Use Case:** Batch processing or importing large datasets.
* **Logic:** Maintains a persistent `IDbCommand` while you update state/parameters between executions.

```csharp
using var sqlCmd = new SqlCommand();
var builder = userCmd.StartBuilder(sqlCmd);

foreach(var val in dataList) {
    builder.Use("@val", val);
    builder.Execute(cnn); // Reuses the internal command object
}
```

---
## Execution via QueryBuilder

The `QueryBuilder` extensions handle the entire database "trip." They synthesize the final SQL from your template, synchronize parameters, and execute the command in one step.

### 1. The Core Operations

RinkuLib provides three primary operations, available in both **Synchronous** and **Asynchronous** versions.

| Goal | Method | Sync Return | Async Return |
| --- | --- | --- | --- |
| **Update/Delete/Insert** | `ExecuteQuery` | `int` | `Task<int>` |
| **Fetch Single Row** | `QuerySingle<T>` | `T?` | `Task<T?>` |
| **Stream Multiple Rows** | `QueryMultiple<T>` | `IEnumerable<T>` | `IAsyncEnumerable<T>` |

---

### 2. Standard vs. Manual Mapping

The query methods come in two distinct flavors depending on how you want to handle the data transformation:

* **Automatic Mapping:** When you call these methods, RinkuLib uses its **Mapping Engine** to negotiate with the database schema. It dynamically generates a high-speed IL-parser (via `GetParser`) that matches the returned columns to your object properties.

```csharp
// Standard: Mapping Engine negotiates the parser automatically
var user = builder.QuerySingle<User>(cnn);
```

---

### 3. Shared Execution Parameters

All builder methods use a consistent signature for managing the database context:

* **`cnn`**: The `DbConnection` (or `IDbConnection`) to use.
* **`transaction`**: *(Optional)* The transaction to associate with the command.
* **`timeout`**: *(Optional)* Overrides the default command timeout.
* **`ct`**: *(Async only)* A `CancellationToken` for the task lifecycle.
---

### 4. `QueryBuilderCommand`
The equivalent methods for `QueryBuilderCommand` does not take any parameters (except the `ct`), and does not reconfigure the associated `DbCommand`.

---

## Direct Execution via DbCommand

These extensions transform a standard `DbCommand` into a high-speed data mapper. They are used when you have a pre-configured command and need explicit control over how data is retrieved and how the command object is managed.

### 1. Execution & Mapping Methods

| Goal | Method | Sync Return | Async Return |
| --- | --- | --- | --- |
| **Update/Delete/Insert** | `ExecuteQuery` | `int` | `Task<int>` |
| **Fetch Single Row** | `QuerySingle<T>` | `T?` | `Task<T?>` |
| **Stream Multiple Rows** | `QueryMultiple<T>` | `IEnumerable<T>` | `IAsyncEnumerable<T>` |

---

### 2. Extension Parameters

Every method in the table above utilizes a consistent set of parameters.
* **`disposeCommand`**: A boolean (defaults to **`true`**).
    * If **`true`**: The command is automatically disposed after execution or when the result stream is exhausted.
    * If **`false`**: The command is not disposed, allowing it to be accessed or reused after the call.
* **`ct`**: *(Async only)* A `CancellationToken` for the task lifecycle.
