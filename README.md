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

👉 **[Read the Full Templating & Syntax Documentation](TemplatingSyntaxDoc.md)**

---

### 2. Mapping Engine (Recursive Negotiation)

A **Tree of Responsibility** that reconciles the database schema with your C# types to compile optimized mapping delegates.

* **Schema Negotiation:** Dynamically maps database columns to complex types, handling non-1-to-1 relationships and varied data shapes without manual configuration.
* **Decomposable Process:** Every step of the mapping is a "plug-in" point, allowing you to inject custom logic into specific branches or leaf-nodes of the object graph.

👉 **[Read the Full Mapping Engine & Registry Documentation](MappingEngineDoc.md)**

