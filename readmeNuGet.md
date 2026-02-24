# RinkuLib: A Modular Micro-ORM
RinkuLib is a micro-ORM built on top of **ADO.NET**. It separate any SQL construction from the c# structure and provide a declarative way to build them. The engine also has complex type mapping compatibility with multiple customization options.

The library is designed as two independent, highly customizable parts
* SQL command generation with flexible templating engine
* Complex type parsing with negotiation phase to use the most appropriate construction

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
builder.Use("@MinAge", 18);      // Will add everything related to the variable
builder.Use("@Grp", "Admin");    // Always added to the string and throw if not used
                                 // @Category not used so wont use anything related to that variable

// 3. EXECUTION: DB call (SQL Generation + Type Parsing Negotiation)
using DbConnection cnn = GetConnection();
// Generates the final SQL, assign the parameters and fetches the compiled parser delegate.
IEnumerable<User> users = builder.QueryAll<User>(cnn);

// Resulting SQL: SELECT ID, Name FROM Users WHERE Group = @Grp AND Age > @MinAge
```

### The reasons it exist: Separation of concern, Customization and flexibility

When dynamicaly building SQL, individual SQL segment must be able to make a valid SQL. You never see the whole picture until processing. By defining a template first, you can have your c# logic focussing on checking validity and then simply need to "inform" the builder of what you use. That way you can have total separation of concern and no matter where an item affect the SQL result, you can keep exactly the same logic ensuring SQL validity and letting you make oprimized SQL commands without any compromizes.
When mapping to a type, you rearely need a flat object as the logic item, has a deep, fully customizable, negotiation phase that lets you map the flat row result of the DB, to the multi level nesting of the c# type.


### The 3-Step Process

1.  **Interpretation (`QueryCommand`):** A reusable blueprint. The engine analyzes your SQL template to create a structural blueprint and sets up storage for parameter instructions cache and mapping functions cache.

2.  **State Definition (`QueryBuilder`):** A temporary struct. You create this for every database call to hold your specific parameters and true conditions. It acts as the bridge between your C# data and the command's blueprint.

3.  **Execution (`QueryX` methods):** The DB call using methods (such as `QueryAllAsync`, `QueryOne`, etc.). The engine takes the blueprint from Step 1 and the data from Step 2 to generate the finalized SQL and create the complete `DbCommand`. It then find the mots apropriate mapping function between the schema and the type.

### Rinku vs. Dapper: Performance Profile

| Feature Comparison | Mean | Ratio | Allocated | Alloc Ratio |
| --- | --- | --- | --- | --- |
| **1. Single Row (Sync)** |  |  |  |  |
| Rinku QueryOne | 1,163.3 us | 1.93 | 3.43 KB | 0.98 |
| Dapper QueryFirstOrDefault | 604.6 us | 1.00 | 3.51 KB | 1.00 |
| **2. Single Row (Async)** |  |  |  |  |
| Rinku QueryOneAsync | 1,214.7 us | 1.88 | 5.11 KB | 0.94 |
| Dapper QueryFirstOrDefaultAsync | 647.7 us | 1.00 | 5.46 KB | 1.00 |
| **3. Streaming (Async)** |  |  |  |  |
| Rinku QueryAllAsync | 750.1 us | 0.99 | **20.02 KB** | **0.80** |
| Dapper QueryUnbufferedAsync | 760.1 us | 1.00 | 24.87 KB | 1.00 |
| **4. Buffered (Async)** |  |  |  |  |
| Rinku QueryAllBufferedAsync | 774.0 us | 1.02 | **19.76 KB** | **0.80** |
| Dapper QueryAsync (Buffered) | 762.1 us | 1.00 | 24.64 KB | 1.00 |
| **5. Dynamic Objects** |  |  |  |  |
| Rinku QueryOne\<DynaObject\> | 1,232.9 us | 1.93 | 5.23 KB | 0.93 |
| Dapper QueryFirstOrDefault\<dynamic\> | 638.3 us | 1.00 | 5.61 KB | 1.00 |
| **6. Complex Mapping (Nested)** |  |  |  |  |
| Rinku QueryAllBufferedAsync\<Product\> | 650.6 us | 1.00 | **5.81 KB** | **0.95** |
| Dapper QueryAsync\<Product, Category, Product\> | 652.6 us | 1.00 | 6.10 KB | 1.00 |
| **7. Command Execution (Sync)** |  |  |  |  |
| Rinku ExecuteQuery | 1,747.4 us | 1.00 | 2.06 KB | 0.95 |
| Dapper Execute | 1,741.1 us | 1.00 | 2.18 KB | 1.00 |
| **8. Command Execution (Async)** |  |  |  |  |
| Rinku ExecuteQueryAsync | **1,773.7 us** | **0.95** | 3.67 KB | 0.97 |
| Dapper ExecuteAsync | 1,883.8 us | 1.00 | 3.80 KB | 1.00 |
| **9. Collection Params (IN)** |  |  |  |  |
| Rinku (@ids_X) | 693.9 us | 1.01 | **7.29 KB** | **0.91** |
| Dapper @ids | 687.4 us | 1.00 | 8.04 KB | 1.00 |


GitHub : https://github.com/adamtherrienmarois/RinkuLib