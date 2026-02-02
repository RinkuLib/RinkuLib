
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
> Changes made to the mappers are global. It is recommended to configure your custom mappings during application startup before any templates are compiled.

#### Internal Initialization
If you are using the default `QueryCommand` class to manage your query blueprints, you don't need to manually wire these together. When `QueryCommand` compiles a template internally, it automatically references these two registries to:

Identify the handler segments.

Initialize the appropriate handler logic for both string manipulation and parameter binding.

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
