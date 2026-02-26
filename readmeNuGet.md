# RinkuLib: A Modular Micro-ORM
RinkuLib is a micro-ORM built on top of **ADO.NET**. It separate any SQL construction from the c# structure and provide a declarative way to build them. The engine also has complex type mapping compatibility with multiple customization options.

The library is designed as two independent, highly customizable parts
* SQL command generation with flexible templating engine
* Complex type parsing with negotiation phase to use the most appropriate construction

---

## Quick Start

```csharp
// 1. INTERPRETATION: The blueprint (Create once and reuse throughout the app)
// Define the template once to analyzed and cached the sql generation conditions
string sql = "SELECT ID, Name FROM Users WHERE Group = @Grp AND Cat = ?@Category AND Age > ?@MinAge";
public static readonly QueryCommand usersQuery = new QueryCommand(sql);

public QueryBuilder GetBuilder(QueryCommand queryCmd) {
    // 2. STATE DEFINITION: A temporary builder (Does not manage DbConnection or DbCommand)
    // Create a builder for a specific database trip
    // Identify which variables are used and their values
    QueryBuilder builder = queryCmd.StartBuilder();
    builder.Use("@MinAge", 18);      // Will add everything related to the variable
    builder.Use("@Grp", "Admin");    // Always added to the string and throw if not used
                        // @Category not used so wont use anything related to that variable
    return builder;
}

public IEnumerable<User> GetUsers(QueryBuilder builder) {
    // 3. EXECUTION: DB call (SQL Generation + Type Parsing Negotiation)
    using DbConnection cnn = GetConnection();
    // Uses the QueryCommand and the values in the builder to create the DbCommand and parse the result
    IEnumerable<User> users = builder.QueryAll<User>(cnn);
    return users;
}

// Resulting SQL: SELECT ID, Name FROM Users WHERE Group = @Grp AND Age > @MinAge
```

### The reasons it exist: Separation of concern, Customization and flexibility

When dynamicaly building SQL, individual SQL segment must be able to make a valid SQL. You never see the whole picture until processing. By defining a template first, you can have your c# logic focussing on checking validity and then simply need to "inform" the builder of what you use. That way you can have total separation of concern and no matter where an item affect the SQL result, you can keep exactly the same logic ensuring SQL validity and letting you make oprimized SQL commands without any compromizes.
When mapping to a type, you rearely need a flat object as the logic item, has a deep, fully customizable, negotiation phase that lets you map the flat row result of the DB, to the multi level nesting of the c# type.


### The 3-Step Process

1.  **Interpretation (`QueryCommand`):** A reusable blueprint. The engine analyzes your SQL template to create a structural blueprint and sets up storage for parameter instructions cache and mapping functions cache.

2.  **State Definition (`QueryBuilder`):** A temporary struct. You create this for every database call to hold your specific parameters and true conditions. It acts as the bridge between your C# data and the command's blueprint.

3.  **Execution (`QueryX` / `ExecuteX` methods):** The DB call using methods (such as `QueryAllAsync`, `QueryOne`, `Execute`, etc.). The engine takes the blueprint from Step 1 and the data from Step 2 to generate the finalized SQL and create the complete `DbCommand`. It then find the mots apropriate mapping function between the schema and the type.

GitHub : https://github.com/adamtherrienmarois/RinkuLib