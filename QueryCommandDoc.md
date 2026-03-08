# **QueryCommand**

The `QueryCommand` is a long-lived orchestrator. You define it once for a specific SQL template, and it becomes the host for all structural and performance logic.

```csharp
// Parsing happens once at instantiation
var userCmd = new QueryCommand("SELECT * FROM Users WHERE ID = ?@id AND Age > ?@minAge");

```
## Making the DbCommand

The `QueryCommand` is responsible for the actual assembly of the database request. When execution is triggered, it takes the state array (`object[]`) sets a `DbCommand`:

1. **Parameter Injection:** For every active index it uses the corresponding **`DbParamInfo`** entry in the **`QueryParameters`** to make a `DbParameter`, ensuring the command is correctly initialized for the query variables.
2. **SQL Parsing:** The engine utilizes the **`QueryText`** and the provided state to evaluate which segments to use and generate the final SQL and set it to the `DbCommand`.

---

## Parameter Specialization

The `QueryCommand` uses a "Learn-on-Use" strategy to move from a generic configuration to one optimized for your specific database provider.

### The Initial State: Inferred

Initially, every parameter in the registry is an `InferredDbParamInfo`.

* On the first use, RinkuLib creates a standard `DbParameter` and assigns the value without specifying types or sizes.
* The `DbCommand` is executed, allowing the provider to resolve the parameter requirements.

### The Learning Mechanism (`IDbParamInfoGetter`)

Immediately after execution (right after `ExecuteReader` or `ExecuteNonQuery`), the `QueryCommand` attempts to "learn" the specific requirements for the parameters used. It utilizes a pluggable system to extract this metadata:

```csharp
public interface IDbParamInfoGetter {
    public static readonly List<ParamInfoGetterMaker> ParamGetterMakers = [];
    // Returns the specialized DbParamInfo for the parameter at index 'i'
    DbParamInfo MakeInfoAt(int i);
}

// The Maker: Checks the command type to decide if it can handle extraction
public delegate bool ParamInfoGetterMaker(IDbCommand cmd, out IDbParamInfoGetter getter);
```

### How Specialization is Resolved

The engine iterates through a global list of `ParamGetterMakers`:

1. **Type Checking:** A custom Maker checks if the `cmd` is a specific type (e.g., `if (cmd is SqlCommand sqlCmd)`).
2. **Specialized Extraction:** If it matches, the Maker may return a custom `IDbParamInfoGetter` that can use provider-specific features (like internal metadata properties) to build a more accurate `DbParamInfo`.
3. **The Fallback:** If no specialized maker claims the command, the engine uses the **`DefaultParamCache`**. This fallback directly inspects the standard `DbParameter` properties (`Type`, `Size`) from the `IDbCommand`.

### Persistence & Manual Overrides

Once a `DbParamInfo` is captured, it is **cached permanently** in the `QueryCommand` registry.

* **Static Optimization:** Subsequent calls skip the learning phase and use the specialized info to create perfectly configured parameters instantly.
* **Manual Control:** You can update the cache manually at any time, even before the first call, to ensure the engine uses your preferred configuration.

```csharp
// Manually update the cache for a parameter by name
userCmd.UpdateParamCache("@Name", TypedDbParamCache.Get(DbType.AnsiStringFixedLength, 1000));
```

---

## The Parser Cache

The Parser Cache handles the transformation of `DbDataReader` rows into C# objects. It is a **lazy, reactive system** that only populates when the engine is asked to parse an object from a result set.

### Logical Bypass

For non-SELECT queries (like `INSERT`, `UPDATE`, or `DELETE`), the Parser Cache is completely ignored. Because these operations do not produce a result schema, the engine never triggers the "Negotiation" phase.

### The First "Hit" (Populating the Cache)

When a query returns data, the `QueryCommand` captures the **first available delegate** it encounters for that schema:

* **Engine Generated:** The engine calls `GetParser` to generate a specialized IL-function for the schema. This generated function is then saved into the cache.

### Fixed vs. Dynamic Projection

The storage behavior is determined by the template's complexity:

* **Standard (Fixed):** By default, the `QueryCommand` holds **one parsing function**. It assumes the schema is stable and reuses this single delegate for all future calls.
* **Dynamic Projection:** If the template was flagged during parsing as having a dynamic result shape, the cache expands to store **multiple functions**, mapping each unique database schema to its own specialized delegate.