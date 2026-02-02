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

# Templating Syntax

RinkuLib uses a SQL template to build a **structural blueprint**. This blueprint identifies **Conditional Segments** and their boundaries. When generating the final SQL, the blueprint prunes these segments if their associated keys are not used, while ensuring the resulting string maintains valid SQL syntax.

> **Data Provision Warning:** Standard parameters (e.g., `@ID`) are treated as static text by the blueprint. If a parameter's segment is preserved but no value is associated to it, the **Database Provider** will throw an error during execution. The blueprint only manages the *presence* of the parameter string, not its value.

### Guide to Examples
The following examples illustrate how the blueprint transforms the template when conditions are not met:
* **Template:** The source SQL template string.
* **Result:** The generated SQL when the conditional keys are **Not Used**.

---
## Base SQL Compatibility

RinkuLib templates are parsed linearly. If the engine reaches the end of the string without encountering any conditional markers, the template remains a single, unfragmented segment with no attached conditions. Consequently, any valid SQL is naturally a valid template.

Without markers, the parser identifies no optional boundaries, leaving the query intact.
* **Template:** `SELECT * FROM Users WHERE IsActive = 1`
* **Result:** `SELECT * FROM Users WHERE IsActive = 1`

Parameters are handled as part of the static text. Since no markers define them as conditional, the parser identifies no optional boundaries.
* **Template:** `UPDATE Products SET Stock = @Amount WHERE ProductID = @ID`
* **Result:** `UPDATE Products SET Stock = @Amount WHERE ProductID = @ID`

---

## Customizing the Variable Character

The engine identifies variables based on a prefix character. While `@` is the default, this is fully configurable.

* **Local Override:** When compiling a template, you can provide a specific `variableChar`. If you provide `:`, the engine will parse `:Var` or `?:Var` instead of the standard `@` syntax.
* **Global Default:** You can change the default prefix for the entire application by modifying the public static field: `QueryFactory.DefaultVariableChar` (Default: `'@'`)

Changing this globally ensures that all future tamplates are compiled using your preferred character without needing to specify it every time.

---
## Optional Variables (`?@Var`)

The `?` prefix designates a variable as optional. When this marker is used (e.g., `?@Var`), the engine determines the **footprint** (the surrounding segment) related to that variable and identifies it as the conditional area using the variable name (`@Var`) as the condition key. 

A key advantage of this system is that it automatically manages dangling keywords. You do not need to use "tricks" like `WHERE 1=1`. You can write a standard, valid SQL query as if every parameter will be used, then simply add the `?` marker to make a parameter—and its associated footprint—conditional.

> In RinkuLib, logical operators (`AND`, `OR`, etc.) are associated with the **preceding** variable. The operator is part of the footprint of the variable that comes *before* it. 
>
> If you have a template like `WHERE col1 = ?@Col1 OR col2 = ?@Col2 AND col3 = ?@Col3` and `@Col2` is Not Used:
> * **Correct Result:** `WHERE col1 = @Col1 OR col3 = @Col3` (The `AND` following `@Col2` was pruned).
> * **Incorrect Expectation:** `WHERE col1 = @Col1 AND col3 = @Col3` (This would happen if the operator belonged to the following variable).

When an optional variable is not used, any trailing logical operator is removed to maintain a valid sequence. 
* **Template:** `SELECT * FROM Users WHERE IsActive = 1 AND Name = ?@Name`
* **Result:** `SELECT * FROM Users WHERE IsActive = 1`

**Tip: Minimizing Fragmentation**.
It is preferable to keep non-conditional parts of the query together whenever possible. This allows the engine to treat static portions as a continuous block rather than breaking them into multiple segments to accommodate optional markers.
* **Preferred:** `SELECT * FROM Users WHERE IsActive = 1 AND Name = ?@Name` (2 segments)
* **Less Optimal:** `SELECT * FROM Users WHERE Name = ?@Name AND IsActive = 1` (3 segments)

The same logic applies to comma cleanup. If an optional segment is removed, the engine ensures no dangling separators remain.
* **Template:** `UPDATE Users SET Email = @Email, Phone = ?@Phone WHERE ID = @ID`
* **Result:** `UPDATE Users SET Email = @Email WHERE ID = @ID`

The engine also manages the presence of the clause keywords themselves. If an optional variable is not provided, the engine ensures the SQL remains valid by removing the clause if it becomes empty.
* **Template:** `SELECT * FROM Users WHERE Name = ?@Name ORDER BY Name`
* **Result:** `SELECT * FROM Users ORDER BY Name`

This cleanup is not performed on an individual condition basis; rather, the engine determines whether to include a keyword only if there is content actually being used within that clause. If a clause turns out to be empty, the keyword is removed.
* **Template:** `SELECT Category FROM Users GROUP BY Category HAVING AVG(Salary) > ?@MinSalary AND COUNT(*) > ?@MinCount`
* **Result (if neither provided):** `SELECT Category FROM Users GROUP BY Category`

These conditional segments can be placed anywhere in the query. For example, within a Common Table Expression:
* **Template:** `WITH ActiveUsers AS (SELECT * FROM Users WHERE Dept = ?@Dept) SELECT * FROM ActiveUsers`
* **Result:** `WITH ActiveUsers AS (SELECT * FROM Users) SELECT * FROM ActiveUsers`

Similarly, they can be used within subqueries:
* **Template:** `SELECT * FROM (SELECT * FROM Users WHERE Dept = ?@Dept) AS Sub`
* **Result:** `SELECT * FROM (SELECT * FROM Users) AS Sub`

The same logic applies to JOIN conditions. If the optional part is unused, the trailing operator is removed and the rest of the join remains intact.
* **Template:** `SELECT * FROM Orders o JOIN Users u ON o.UserID = u.ID AND u.Role = ?@Role`
* **Result:** `SELECT * FROM Orders o JOIN Users u ON o.UserID = u.ID`

A conditional may span across deeper context.
* **Template:** `SELECT * FROM Users WHERE ?@ManagerId = (SELECT ManagerId FROM Departments WHERE Departments.ID = Users.DeptID)`
* **Result:** `SELECT * FROM Users`

When multiple optional variables are used within the same segment, they function with a "all-or-nothing" logic. If any one of the variables in that segment is not provided, the entire segment is discarded.
* **Template:** `SELECT * FROM Users WHERE Name = ?@FirstName + ' ' + ?@LastName`
* **Result (if only @FirstName is provided):** `SELECT * FROM Users`

This ensures that partial or invalid expressions are never left behind in the final query. However, when a segment contains both a required (`@`) and an optional (`?@`) variable, the entire segment becomes conditional based on the optional marker.

* **Template:** `SELECT * FROM Users WHERE FullName = @FirstName + ' ' + ?@LastName`

**Behavior:**
1. **If `@LastName` is missing:** The entire segment is removed, resulting in `SELECT * FROM Users`.
2. **If `@LastName` is provided:** The segment is kept, resulting in `SELECT * FROM Users WHERE FullName = @FirstName + ' ' + @LastName`. However, `@FirstName` must still be provided in your parameters, or the SQL execution will fail.

This is useful when you are certain that two values always exist together. It allows you to remove a redundant condition check by letting one optional marker control the inclusion of multiple parameters.

Conditionals can also be nested. When a conditional spans a deeper context and contains another conditional inside it, the outer one determines whether the inner one is ever evaluated.
* **Template:** `SELECT * FROM Users WHERE ?@ManagerId = (SELECT ManagerId FROM Departments WHERE ID = Users.DeptID AND Location = ?@Location)`

* **Result (if @ManagerId is not provided):** `SELECT * FROM Users` (even if `@Location` is used)

* **Result (if @ManagerId is provided, but @Location is not):** `SELECT * FROM Users WHERE @ManagerId = (SELECT ManagerId FROM Departments WHERE ID = Users.DeptID)`

Sometimes two conditions are logically related and should only be used together. To treat multiple conditions as a single conditional segment, a logical connector can be prefixed with `&`. When used this way, the connected conditions are evaluated as one unit and are either fully included or fully removed.

* **Template:** `SELECT * FROM Events WHERE Date > ?@MinDate &AND Date < ?@MaxDate`

* **Result (if only @MinDate is provided):** `SELECT * FROM Events`
* **Result (if both are provided):** `SELECT * FROM Events WHERE Date > @MinDate AND Date < @MaxDate`

The same grouping can be applied with `OR`. A grouped connector may combine an optional condition with a static one, and the entire group is included or removed together.
* **Template:** `SELECT * FROM Users WHERE Role = 'Admin' &OR Role = ?@Role`

* **Result (if @Role is not provided):** `SELECT * FROM Users`
* **Result (if @Role is provided):** `SELECT * FROM Users WHERE Role = 'Admin' OR Role = @Role`

The `&` can also be used for comma-separated segments.
* **Template:** `UPDATE Users SET Status = 'Active' &, Email = ?@Email, Name = @Name WHERE ID = @ID`
* **Result:** `UPDATE Users SET Name = @Name WHERE ID = @ID`

A conditional inside parentheses, but that is not in a subquery takes a footprint outside the parentheses.
* **Template:** `SELECT * FROM Users WHERE Name LIKE CONCAT('%', ?@Name, '%') AND IsActive = 1 ORDER BY Name`
* **Result:** `SELECT * FROM Users WHERE IsActive = 1 ORDER BY Name`

The same logic applies inside parentheses used in an expression. The footprint of a conditional inside parentheses that are not containing subqueries, includes the entire condition.
* **Template:** `SELECT * FROM Orders WHERE (Total * ?@Multiplier) > 100`
* **Result:** `SELECT * FROM Orders`

Parentheses can also be used to group multiple conditions. A conditional inside such parentheses controls the entire group.
* **Template:** `SELECT * FROM Orders WHERE (Status = 'Shipped' AND ?@MinTotal < Total)`
* **Result:** `SELECT * FROM Orders`

---
## Conditional Markers (`/*...*/`)

The `/*...*/` markers are the core mechanism for defining conditional footprints.  

Functionally, `?@Var` behaves like a shorthand for `/*@Var*/@Var`, with one exception: the footprint of `?@Var` automatically grows outside parentheses that are not subquery parentheses.  

Using `/*...*/` directly lets you do everything `?@Var` can do, and more—**except that it treats every parenthesis like a subquery parenthesis**. Instead, it lets you place the conditional **at the level of parentheses you need**, rather than letting the system automatically decide, giving you precise control over the "growth".

A conditional variable inside a subquery can control a segment outside it by placing the `/*...*/` marker at the level of parentheses you want to affect.  
* **Template:** `SELECT * FROM Users WHERE /*@DeptId*/DeptID = (SELECT ID FROM Departments WHERE ID = @DeptId)`
* **Result** `SELECT * FROM Users`

A conditional variable can be contained inside non-subquery parentheses by placing the `/*...*/` marker inside them, preventing automatic growth outside the parentheses.
* **Template:** `SELECT * FROM Tasks WHERE Status = @Status AND (AssignedTo = @AssignedTo1 OR AssignedTo = @AssignedTo2 OR /*@Priority*/Priority = @Priority)`
* **Result:** `SELECT * FROM Tasks WHERE Status = @Status AND (AssignedTo = @AssignedTo1 OR AssignedTo = @AssignedTo2)`

> In an `INSERT`, the first-level parentheses of the column list and the `VALUES` list are not “growthable” by `?@Var`.

A single conditional variable can affect multiple places in a query. For example, you can have an optional value in an `INSERT` also control its corresponding column.
* **Template:** `INSERT INTO Orders (ID, Amount, /*@Discount*/ Discount) VALUES (@ID, @Amount, ?@Discount)`
* **Result:** `INSERT INTO Orders (ID, Amount) VALUES (@ID, @Amount)`

What you’ve been using as `/*@Var*/` is actually just a special case of the `/*...*/` mechanism.  

Using a comment with a variable (for example `/*@Name*/`) works the same as any other `/*...*/` comment: it makes the segment conditional. The only difference is that the engine also verifies that the variable exists somewhere in the query.  

Using a comment without a variable simply marks the segment as conditional with a custom key. It works the same way a parameter does in controlling the final output, but now you can control it **without needing an actual value**.
* **Template:** `SELECT * FROM Tasks WHERE Status = 'Open' AND /*HighPriority*/ Priority = 'High'`
* **Result:** `SELECT * FROM Tasks WHERE Status = 'Open'`

A `/*...*/` marker can make a column in a `SELECT` conditional. If the condition is not used, the column is removed from the final query.
* **Template:** `SELECT ID, Name, /*ShowSalary*/ Salary FROM Users`
* **Result:** `SELECT ID, Name FROM Users`

A forced boundary can be introduced using the `???` marker.
It acts like a logical separator that conditionals cannot cross, without adding any syntax to the final query.
> Use with caution, as it overrides the engine’s automatic boundary detection.
This is mainly useful when the first column in a `SELECT` is conditional, but query modifiers (such as `DISTINCT`) must remain unaffected.

* **Template:** `SELECT DISTINCT /*ShowID*/ ID, Name FROM Users`
* **Result:** `SELECT Name FROM Users`

By inserting a `???` marker, you can manually separate `DISTINCT` from the conditional column.
* **Template:** `SELECT DISTINCT ??? /*ShowId*/ ID, Name FROM Users`
* **Result:** `SELECT DISTINCT Name FROM Users`

A forced boundary can also be used in the opposite direction: to make a query modifier conditional **without affecting the first column**.
* **Template:** `SELECT /*UseDistinct*/ DISTINCT ??? ID, Name FROM Users`
* **Result:** `SELECT ID, Name FROM Users`

A `/*...*/` marker and a `?@Var` in the same segment act like two `?` variables: if either is not used, the entire segment is removed.
* **Template:** `SELECT * FROM Users WHERE /*IsAdmin*/ ?@MinSalary <= Salary AND ID = @ID`
* **Result:** `SELECT * FROM Users WHERE ID = @ID`

If you need a comment in the final query (for hints or notes) without making it a conditional segment, start the comment with `~`. The engine will **not** treat it as a `/*...*/` marker.
* **Template:** `/*~This is a hint*/SELECT ID, Name FROM Users`
* **Result:** `/*This is a hint*/ SELECT ID, Name FROM Users`

You can combine multiple conditions in a single `/*...*/` marker using `|` (OR) and `&` (AND). The comment is read **linearly**, from left to right, without operator precedence. For example, `/*A|B&C*/` is interpreted as `(A OR B) AND C`.  
* **Template:** `SELECT * FROM Users WHERE /*IsAdmin|IsManager&Active*/ Salary > 50000`
* **Result:** `SELECT * FROM Users`

There is a special usage of `/*...*/`: if placed directly before a SQL clause (like a `JOIN`), the entire clause is treated as a conditional segment.
* **Template:** `SELECT o.ID, o.Total FROM Orders o /*FilterUsers*/ JOIN Users u ON o.UserID = u.ID WHERE u.Role = ?@Role`
* **Result:** `SELECT o.ID, o.Total FROM Orders o`

You can combine multiple conditions in a single `/*...*/` marker for a conditional join. If **any** of the conditions is triggered, the join is included.

* **Template:** `SELECT o.ID, o.Total, /*Name*/u.Name FROM Orders o /*@Role|Name*/INNER JOIN Users u ON o.UserID = u.ID WHERE u.Role = ?@Role`
* **Result (if nothing is provided):** `SELECT o.ID, o.Total FROM Orders o`
* **Result (if `@Role` is provided):** `SELECT o.ID, o.Total FROM Orders o INNER JOIN Users u ON o.UserID = u.ID WHERE u.Role = @Role`
* **Result (if `Name` is provided):** `SELECT o.ID, o.Total, u.Name FROM Orders o INNER JOIN Users u ON o.UserID = u.ID`

When using a `CASE` expression, `WHEN`, `THEN`, and `ELSE` are treated as **section keywords**.
Making only the `WHEN` conditional can lead to an invalid or unintended query, because the `THEN` part remains.

* **Template (incorrect):** `SELECT CASE WHEN Role = ?@SpecialRole THEN 'S' WHEN Role = 'Admin' THEN 'A' ELSE 'U' END AS UserType FROM Users`
* **Result:** `SELECT CASE THEN 'S' WHEN Role = 'Admin' THEN 'A' ELSE 'U' END AS UserType FROM Users`

To correctly remove a conditional WHEN, the corresponding THEN must also be marked with the same condition.

* **Template (correct):** `SELECT CASE Role = ?@SpecialRole /*@SpecialRole*/THEN 'S' WHEN Role = 'Admin' THEN 'A' ELSE 'U' END AS UserType FROM Users`
* **Result:** `SELECT CASE WHEN Role = 'Admin' THEN 'A' ELSE 'U' END AS UserType FROM Users`

---

## Dynamic Projection (`?SELECT`)

Prefixing a `SELECT` keyword with `?` enables **dynamic projection**.

When enabled, the projected columns of that `SELECT` are extracted into individual conditional segments, using the column name as the key.
Dynamic projection only affects the column list of the `SELECT` where `?` is used.

The columns created by the **first** dynamic projection are considered as "select" conditions

* **Template:** `?SELECT ID, Name FROM Users`
* **Equivalent to:** `SELECT /*ID*/ID, /*Name*/Name FROM Users`

Each column can then be independently included or removed based on its condition.

* **Template:** `?SELECT ID, Name FROM Users`
* **Result (`Name` is provided):** `SELECT Name FROM Users`

The `?` prefix can also be used on a `SELECT` at a lower level, such as inside a CTE.

* **Template:** `WITH U AS (?SELECT ID, Name, Salary FROM Users) SELECT * FROM U`
* **Result (`Name` is provided):** `WITH U AS (SELECT Name FROM Users) SELECT * FROM U`

Dynamic projection can be used on multiple `SELECT` statements, such as in a `UNION`.
When column names match, their conditions are **shared**, allowing the projection to stay in sync.

* **Template:** `?SELECT ID, Name FROM Users UNION ALL ?SELECT ID, Name FROM ArchivedUsers`
* **Result (`Name` is provided):** `SELECT Name FROM Users UNION ALL SELECT Name FROM ArchivedUsers`

If column names do not match, only the first `?SELECT` defines the dynamic projection.
Columns from subsequent `?SELECT` statements with non-matching names are treated as normal conditional segments.

* **Template:** `?SELECT ID, Name FROM Users UNION ALL ?SELECT UserId, FullName FROM ArchivedUsers`
* **Result (`Name` is provided):** `SELECT Name FROM Users UNION ALL SELECT FROM ArchivedUsers`

This can be useful when multiple sources expose equivalent data under different column names, and you want to control which one participates in the union.

* **Template:** `?SELECT ID, Name FROM Users UNION ALL ?SELECT ID, Name AS DifferentName, UserName FROM DifferentUsers`
* **Result (`Name` and `UserName` are provided):** `SELECT Name FROM Users UNION ALL SELECT UserName FROM DifferentUsers`

Because dynamic projection turns projected columns into conditional segments, any modifier that appears before the first column may become part of that column’s condition.

If the modifier is not separated by a forced boundary, it will be removed together with the first column.

* **Template:** `?SELECT DISTINCT ID, Name FROM Users`
* **Result:** `SELECT Name FROM Users`

To prevent this, insert a `???` marker to isolate the modifier from the projected columns.

* **Template:** `?SELECT DISTINCT ??? ID, Name FROM Users`
* **Result:** `SELECT DISTINCT Name FROM Users`

When dynamic projection is used, each column creates a condition. Joining columns causes their **conditions to share the same footprint**.

In the case of a joined footprint created inside a dynamic projection, the conditions are evaluated as an implicit **OR**, instead of the usual implicit **AND**.

* **Template:** `?SELECT ID, FirstName&, LastName FROM Users`

* **Result (only `FirstName` is used):** `SELECT FirstName, LastName FROM Users`

---
## Handlers (`_Letter`)

Sometimes, you need a query to change based on more than just true/false conditions. You may want the query to be dynamically adjusted by an actual value, like changing table names or column names at runtime.

Handlers solve this problem with the syntax `@Var_Letter`, where `_Letter` is a single letter representing a specific value. This allows the query to adjust its structure based on runtime values, giving you more control over the generated SQL.

> The `_Letter` does not add to the variable name. In the example, `@Index_N` means the variable is handled from having `_Letter`, and the `N` is the handler being used. The variable itself is `@Index`.

> If a handled variable value is required but not provided, the error is raised during query generation, unlike normal variables that throws during the call to the db. This is because the handler needs the value to generate the correct SQL string.

The `_N` handler is used to inject a numeric value directly into the SQL string. It expects the value to be a number and will place it where needed in the query.
* **Template:** `SELECT * FROM Users ORDER BY @Index_N`
* **Result (if `@Index` is set to 3):** `SELECT * FROM Users ORDER BY 3`

The `_S` handler is used to inject a string literal directly into the SQL string. It expects the value to be a string and will escape it using single quotes (`''`) to ensure it is properly formatted for SQL.
* **Template:** `SELECT * FROM Users WHERE Name = @Name_S`
* **Result (if `@Name` is set to `John`):** `SELECT * FROM Users WHERE Name = 'John'`

The `_R` handler allows you to inject raw SQL directly into the query. It is used when you need to modify the structure of the SQL, such as specifying table names or column names dynamically. It expects a string value and directly inserts it into the query without escaping it.
* **Template:** `SELECT * FROM @Table_R WHERE Status = 'Active'`
* **Result (if `@Table` is set to `'Users'`):** `SELECT * FROM Users WHERE Status = 'Active'`

> **Warning:** When using the `_R` handler, be careful about injecting untrusted values directly into your query. Always ensure that only fully controlled and sanitized values are passed to prevent SQL injection. You can use this handler to inject any SQL structure, giving you flexibility, but with great power comes great responsibility.

The `_X` handler is used to inject a collection of values into your SQL query. It expects an `IEnumerable` (such as a list or array) and will generate a series of parameters based on the number of items in the collection. Each item is represented by `@Var_1`, `@Var_2`, ..., `@Var_N`, where `N` is the index of the item.
* **Template:** `SELECT * FROM Users WHERE ID IN (@IDs_X)`
* **Result (if `@IDs` is set to `[10, 20, 30]`):** `SELECT * FROM Users WHERE ID IN (@ID_1, @ID_2, @ID_3)`

An handled variable can also be used with optional parameters.
* **Template:** `SELECT * FROM Tasks WHERE CategoryID IN (?@Cats_X)`
* **Result (if `@Cats` is set to `[1, 2, 3]`):** `SELECT * FROM Tasks WHERE CategoryID IN (@Cat_1, @Cat_2, @Cat_3)`
* **Result (if `@Cats` is **not** provided):** `SELECT * FROM Tasks`

* **Template:** `SELECT Name FROM Products ORDER BY ID OFFSET ?@Skip_N ROWS FETCH NEXT @Take_N ROWS ONLY`
* **Result (if `@Skip` is set to `10` and `@Take` is set to `20`):** `SELECT Name FROM Products ORDER BY ID OFFSET 10 ROWS FETCH NEXT 20 ROWS ONLY`
* **Result (if `@Skip` is not provided):** `SELECT Name FROM Products ORDER BY ID`

> The keyword `FETCH` is not considered as a keyword, meaning that the footprint of `@Skip` cover it

### Difference between `IBaseHandler` and `SpecialHandler`

#### `IBaseHandler`

An `IBaseHandler` handles only the query string. It modifies the SQL by injecting values directly into the string through its `Handle` method.

* **Example:** `_N` is an `IBaseHandler` because it directly injects a number into the SQL query string without affecting other parts of the execution.

---

#### `SpecialHandler`

A `SpecialHandler` implements `IBaseHandler` but goes beyond just modifying the query string. In addition to modifying the SQL being generated, it also interacts with the `DbCommand`. It can bind values to parameters or perform additional actions that affect how the SQL query is executed.

* **Example:** `_X` is a `SpecialHandler` because it not only spreads values across the query string but also assigns those values to individual `DbCommand` parameters.

### Customizing Handlers

The mapping of letters to specific handlers is entirely flexible. While `N`, `S`, `R`, and `X` are the defaults, you can modify, add, or remove mappings to suit your needs.

#### The Handler Mappers

Handlers are registered in two separate mappers depending on their complexity. Both mappers use a `char` as a key and a factory delegate as the value:

| Mapper | Register Type | Purpose |
| --- | --- | --- |
| `QueryFactory.BaseHandlerMapper` | `LetterMap<HandlerGetter<IQuerySegmentHandler>>` | Only modify the **SQL string** (e.g., `_N`, `_S`). |
| `SpecialHandler.SpecialHandlerGetter` | `LetterMap<HandlerGetter<SpecialHandler>>` | Also interact with the **`DbCommand`** (e.g., `_X`). |

##### Mapping Rules:

* **Case Insensitivity:** Letters are treated the same regardless of case (`@Var_n` is the same as `@Var_N`).
* **Slot Limit:** There are **26 available slots** (A-Z). Non-letter characters are not permitted.
* **Delegates:** Both mappers use a factory delegate to instantiate the handler :
  * `public delegate T HandlerGetter<out T>(string Name) where T : IQuerySegmentHandler;`
  * `Name` corresponds to the variable name if you need to use it during the handling like `_X` does


#### Implementation & Registration

To register a custom handler, you assign a delegate to the desired letter in the corresponding mapper.

```csharp
QueryFactory.BaseHandlerMapper['D'] = _ => new LegacyDateHandler();
SpecialHandler.SpecialHandlerGetter['P'] = name => new EncryptionHandler(name);
```

#### Internal Initialization
If you are using the default `QueryCommand` class to manage your query blueprints, you don't need to manually wire these together. When `QueryCommand` compiles a template internally, it automatically references these two registries to:

Identify the handler segments.

Initialize the appropriate handler logic for both string manipulation and parameter binding.

> Changes made to the mappers are global. It is recommended to configure your custom mappings during application startup before any templates are compiled.

---

## Compilation Output and Key Mapping

Compiling a template does more than identify segments and conditions.
The compilation process also produces a **key map**, represented by a `Mapper` object.

The `Mapper` stores every condition key discovered during parsing **without duplication** and assigns each key a stable index.
All key comparisons are performed in a **case-insensitive** manner.

This mapping is used internally to efficiently resolve which conditions are used when generating the final SQL.

Keys are registered in a deterministic order during compilation:

1. **Dynamic projection (SELECT) conditions**
2. **Comment-based conditions without variables** (`/*...*/`)
3. **Variables** (required and optional)
4. **Special handler variables** (required and optional)
5. **Base handler variables** (required and optional)

>Markers of the form `/*@Var*/` do not register a key since the referenced `@Var` is already registered as a variable.

---

# The Mapping Engine: Registry & Negotiation

The Mapping Engine is a system for defining how C# types should be interpreted. Its sole purpose is to produce an optimized `Func<DbDataReader, T>` based on a specific database schema.

As a developer, you interact with the **Metadata Cache**—a registry of rules that the engine builds as it encounters types—which it later uses to **Negotiate** with the database to generate the final function.

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
That’s the most accurate way to frame it. The logic doesn't change based on depth; it is a consistent, recursive process where every `ParamInfo` simply applies the **Column Modifier** it received, regardless of whether that modifier is currently empty or already contains three layers of prefixes.
---

## **ParamInfo: The Names**

The **`INameComparer`** manages a list of candidate names (the parameter/member name plus any **`[Alt(string altName)]`** names). This list is used to negotiate with the schema, but it never acts in isolation—it always operates through a **Column Modifier**. The Column Modifier is essentially a cumulative prefix that is passed down the construction tree. At the root level, this modifier is empty. As the engine moves deeper into nested types, the modifier grows.

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
* **`[JumpIfNull]`**: This instruction tells the compiled function to immediately exit the current construction path. The "jump" moves execution to a predetermined jump-point, which usually bubbles up to the first nullable parent in the object graph. This allows the engine to return a `null` for a higher-level object rather than throwing an exception or returning a partially-filled instance.
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
        [JumpIfNull] T id, 
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
