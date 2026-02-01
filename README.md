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
* **Template:** `UPDATE Users SET Email = @Email, Phone = ?@Phone`
* **Result:** `UPDATE Users SET Email = @Email`

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
* **Result (if nothing is not provided):** `SELECT o.ID, o.Total FROM Orders o`
* **Result (if @Role is not provided):** `SELECT o.ID, o.Total FROM Orders o INNER JOIN Users u ON o.UserID = u.ID WHERE u.Role = @Role`
* **Result (if Name is not provided):** `SELECT o.ID, o.Total, u.Name FROM Orders o INNER JOIN Users u ON o.UserID = u.ID`

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

## Difference between `IBaseHandler` and `SpecialHandler`

### `IBaseHandler`

An `IBaseHandler` handles only the query string. It modifies the SQL by injecting values directly into the string through its `Handle` method.

* **Example:** `_N` is an `IBaseHandler` because it directly injects a number into the SQL query string without affecting other parts of the execution.

---

### `SpecialHandler`

A `SpecialHandler` implements `IBaseHandler` but goes beyond just modifying the query string. In addition to modifying the SQL being generated, it also interacts with the `DbCommand`. It can bind values to parameters or perform additional actions that affect how the SQL query is executed.

* **Example:** `_X` is a `SpecialHandler` because it not only spreads values across the query string but also assigns those values to individual `DbCommand` parameters.

---




---
---
---
---
---
---
---
---
---
---
---
---
---
---
### 1. Automatic Clause Handling
When a variable is marked as optional, the engine identifies the footprint of the condition. If the key is not used, the engine prunes the segment and ensures no dangling keywords remain.

* **Template:** `SELECT * FROM Users WHERE Name = ?@Name`
* **Result:** `SELECT * FROM Users`

---





























### 2. Clean Logical Chains
The engine manages logical operators within the footprint. You can write natural SQL without worrying about whether an `AND` or `OR` will be left hanging if a parameter is missing.

* **Template:** `SELECT * FROM Users WHERE IsActive = 1 AND Category = ?@Cat`
* **Result:** `SELECT * FROM Users WHERE IsActive = 1`



## 1. Foundation: Standard SQL
Standard SQL and parameters are treated as static text. If they are not wrapped in conditional markers, they are always preserved.

### Static Parameters
* **Template:** `SELECT * FROM Users WHERE ID = @ID`
* **Result:** `SELECT * FROM Users WHERE ID = @ID`

---

## 2. Dynamic Filtering: Optional Variables (`?@`)
The `?@` prefix tells the blueprint that the surrounding segment depends on that specific key. If the key is Not Used, the blueprint prunes the footprint.

### Basic Filter Pruning
* **Template:** `SELECT * FROM Users WHERE Group = 'Admin' AND Age > ?@MinAge`
* **Result:** `SELECT * FROM Users WHERE Group = 'Admin'`

### Automatic Keyword Management
The blueprint omits keywords like `WHERE` if the internal conditions are pruned.
* **Template:** `SELECT * FROM Users WHERE Name = ?@Name`
* **Result:** `SELECT * FROM Users`

### List Management (Commas)
Commas in lists are managed so that pruning an item doesn't leave a syntax error.
* **Template:** `UPDATE Users SET Name = @Name, ?@Email, ?@Phone WHERE ID = @ID`
* **Result:** `UPDATE Users SET Name = @Name WHERE ID = @ID`

---

## 3. Structural Control: Logic Markers (`/*...*/`)
Markers define segments based on keys without requiring a parameter at that specific location.

### Boolean Toggles (Flags)
Control a branch with a simple signal.
* **Template:** `SELECT * FROM Users WHERE IsActive = 1 /*ShowDeleted*/ OR IsDeleted = 1`
* **Result:** `SELECT * FROM Users WHERE IsActive = 1`

### Dependency Links
Ties a structural block (like a `JOIN`) to a key used elsewhere in the query.
* **Template:** `SELECT u.*, /*@Grp*/ g.Name FROM Users u /*@Grp*/ JOIN Groups g ON g.ID = u.GID WHERE u.ID = @ID`
* **Result:** `SELECT u.* FROM Users u WHERE u.ID = @ID`

### Section Toggles (Keyword Blocks)
A marker placed immediately before a keyword captures that entire functional block.
* **Template:** `SELECT * FROM Users u /*WithLogs*/ LEFT JOIN Logs l ON l.UID = u.ID WHERE u.ID = 1`
* **Result:** `SELECT * FROM Users u WHERE u.ID = 1`

### Linear Logic Gates
Markers can combine multiple keys using `&` (AND) and `|` (OR), evaluated strictly left-to-right.
* **Template:** `SELECT * FROM Orders WHERE ID > 0 /*Admin|Manager*/ AND InternalNote IS NOT NULL`
* **Result:** `SELECT * FROM Orders WHERE ID > 0`

# RinkuLib

# RinkuLib: Logic-Agnostic SQL Templating

RinkuLib is a lightweight engine that replaces complex string concatenation with a declarative SQL template syntax. It allows you to define **Conditional Segments** anywhere in a query, ensuring the final string is structurally clean.

## The Core Philosophy
RinkuLib treats SQL as a structural map of **Segments** and **Anchors**. It is **Context-Agnostic**, functioning identically across all parts of a query—from `WITH` clauses and `JOINs` to `SELECT` lists and `HAVING` blocks. 

The engine uses conditional identifiers (`?@`, `/*...*/`) to fragment the template into segments. Beyond simple pruning, it utilizes **Handlers** to generate specific SQL injections (numeric literals, enumerable spreads, etc.) for cases where standard parameters are insufficient.

---

## 0. System Context & Integration

**RinkuLib** is a specialized string transformation engine. It is important to distinguish between the **Template Logic** (how the SQL is structured) and the **State Management** (how data and flags are tracked).

### Separation of Concerns
* **State Agnostic:** RinkuLib does not track which keys are "Used" or store variable values internally. It is a "pure" engine that receives a state map, then produces a processed string.
* **Command Isolation:** This library does not interact with the database or manage `DbCommand` objects directly.

### The Standard Workflow
While developers can implement their own custom providers, the library is designed to function as the core of a larger ecosystem:

* **`QueryCommand`**: A container that holds the parsed SQL template and the metadata for the `DbCommand` parameters. It serves as the blueprint to generate the complete `DbCommand` instance.
* **`QueryBuilder`**: The functional interface where you define query logic at runtime. You use:
    * `.Use("Key")`: To activate a Boolean **Flag**.
    * `.Use("Key", value)`: To provide data for a **Variable**.

---

## 1. Syntax Reference

| Token | Name | Behavior |
| :--- | :--- | :--- |
| `@Var` | **Required Variable** | Standard injection. Always expected to be present. |
| `?@Var` | **Optional Variable** | If `@Var` is Not Used, the bound **Segment** is pruned. |
| `/*@Var*/` | **Variable Marker** | **Dependency Link:** Ties a segment to the "Used" state of `@Var` elsewhere. |
| `/*Flag*/` | **Boolean Toggle** | A standalone key used to control a **Segment**. |
| `/*Toggle*/ KEYWORD` | **Section Toggle** | Placed **before** a Keyword (e.g., `JOIN`). Captures the entire block until the next Section Keyword. |
| `@Var_X` | **Suffix Handler** | **Flexibility Hatch:** Forwards a value to a **Handler (A-Z)**. |

* **Hybrid Usage:** A variable can be both **Handled and Optional** (e.g., `?@IDs_X`). If `@IDs` is Not Used, the segment is pruned. If it is Used, the Handler is invoked.
* **Linear Logic Gates:** Markers evaluate multiple dependencies using a strictly **linear, left-to-right** approach. Unlike traditional C#, there is no operator precedence. 
    * *Example:* `/*A|B&C*/` is the equivalent of `(A OR B) AND C`.
* **Error Handling:**
    * **Standard Variables:** If a required `@Var` is not provided, the engine generates the string with the variable name. The **Data Provider** (database driver) will fail during execution.
    * **Handlers:** If a handled variable receives malformed data, or if a required handled variable is missing, the **Handler will fail** immediately during generation.

---

## 2. Built-in Handlers (`_Letter`)
The system supports up to 26 custom handlers (**A-Z**). Handlers fall into two categories depending on their implementation:
1. **Base Handlers:** Affect only the generated SQL string (e.g., injecting a literal).
2. **Special Handlers:** Affect both the `DbCommand` (adding new parameters) and the generated SQL string.

### Common Handlers:
* **_X (Collection/In):** For `IN` syntax. Traverses an `IEnumerable` and generates 1-based parameters.
    * *Example:* `ID IN (@IDs_X)` => `ID IN (@IDs_1, @IDs_2, ..., @IDs_N)`
* **_N (Number):** Injects an integer directly into the string.
    * *Example:* `LIMIT @Count_N` => `LIMIT 2`
* **_S (String):** Injects a string wrapped in single quotes.
    * *Example:* `, @Str_S,` => `, 'Value',`
* **_R (Raw):** Injects a string directly without wrapping. Used for keywords or complex fragments.

---

## 3. The Two-Phase Process

### Phase 1: Initialization (Structural Mapping)
The engine performs a single exhaustive pass to "shatter" the template:
1.  **Key Extraction:** Identifies every unique key used.
2.  **Fragmentation:** Fragments the query into **Segments** based on conditional footprints.
3.  **Boundary Mapping:** Pre-calculates exactly where a segment begins and ends based on anchors.
4.  **Metadata Association:** Links segments to keys and attaches **Handlers** and **Excess Metadata**.

### Phase 2: Generation (Pruning & Handling)
The engine iterates through the segments and appends only those whose conditions are **Used**. 
> **Note:** The engine uses pre-calculated metadata to instantly remove dangling items. If a segment is the last survivor in a section, its "excess" (like a trailing `AND` or an empty `WHERE`) is automatically trimmed. This ensures perfectly valid SQL and removes any need for `WHERE 1=1`.

---

## 4. Boundary Mapping (The Anchor Rules)
A **Segment** is the exact range of characters the engine extracts or prunes. Its boundaries are fixed to the indices of **Anchors** found in the template.

### The Start Point
The segment begins at the character index **directly following** the preceding Anchor. There is no gap; the very next character (often a space) is the first character of the segment.

* **Anchors:** Start of string, SQL Keywords, Operators, or Separators (`,`).
* **Whitespace:** If there is a space after a Keyword (e.g., `WHERE `), that space is the first character of the segment.

### The End Point
The "cut" finishes differently depending on what follows the segment:

* **At an Operator or Separator:** The segment ends at the **last character** of that token.
    * **Behavior:** The token is consumed (swallowed) into the segment.
    * **Example:** `... | ID = ?@ID AND| ...` — The `A`, `N`, and `D` are part of the segment.
* **At a Keyword or `)`:** The segment ends at the **index immediately preceding** that token.
    * **Behavior:** The token is excluded (remains in the query).
    * **Example:** `... AND| ID = ?@ID |ORDER BY ...` — The segment stops exactly before the `O` in `ORDER BY`. Any whitespace before `ORDER BY` is inside the segment.

---

## 5. Section Toggles (/**Toggle**/ KEYWORD)
A Section Toggle overrides standard anchor rules. The Keyword immediately following the toggle is treated as the start of the segment, and the next Keyword acts as the end boundary.

* **Inclusive Start:** The segment begins at the first character of the Keyword following the toggle.
* **Exclusive End:** The segment ends at the first character of the next Keyword string.
    * **Example:** `/*JoinCond*/INNER JOIN t ON ... JOIN` results in `|INNER JOIN t ON ... |JOIN`

---

## 6. Parentheses & Functional Growth
The way boundaries are mapped changes based on the nature of the parentheses encountered:

* **Subqueries:** Internal anchors (commas, `AND`) are respected. Mapping remains contained **inside** the parentheses.

* **Functional Units:** (Functions/math) Internal anchors are ignored. The segment **"grows"** to capture the entire expression, including preceding whitespace and the trailing connector.
    * *Example:* `SELECT * FROM u WHERE Name LIKE CONCAT('%', ?@Name, '%') AND ...`
    * Since `WHERE` is the preceding keyword anchor, the segment starts **immediately after `WHERE`** (including the space) and ends **after the trailing `AND`**.

---

## 7. Hierarchical Dependency
A hierarchy emerges naturally whenever a conditional segment is fully contained within the range of a larger parent condition.

* **Nesting Mechanisms:** Both **Subqueries** and **Section Toggles** define boundaries that can physically contain lower-level conditional items. If those internal items are conditional, their segments are created entirely within the parent's footprint.

**The Rule of Inheritance:** Because child segments are physically located within the parent's footprint, they are inherited. To include a "Child," **both the Parent and the Child** keys must be **Used**. If the Parent key is Not Used, the entire range is discarded and the child is never evaluated.

---

## 8. Logic Mechanisms
* **Implicit AND Logic:** When multiple conditional items appear within the same mapped segment, they share a footprint. This applies to multiple optional variables (`?@A ?@B`), mixed items (`?@A /*B*/`), or multiple markers. Every item in the segment must be **Used** to preserve it.

    * *Example:* `/*A*/ ?@B` => `A AND B` 
    * `/*A|B&C*/ ?@D` => `(A OR B) AND C AND D`
* **Context Joining (`&`):** Explicitly merges distinct SQL parts into a single shared footprint. 
    * *Example:* `StartDate >= ?@Start &AND EndDate <= ?@End AND`

---

## 9. Dynamic Projection (extractSelects)
When enabled, the `SELECT` list is treated as a collection of conditional segments.

* **Automatic Toggling:** Columns are included only if their alias/names is marked as **Used**.
* **Column Joining (`&,`):** Joins columns with a shared dependency. In `SELECT`, this functions as an **OR** relationship.
    * *Example:* `ID &, Name` => Requesting *either* includes both.

---
# Examples

### 1. Static Query
* **Template:** `SELECT ID, Username, Email FROM Users WHERE IsActive = 1`
* **Shattered View:** `|SELECT ID, Username, Email FROM Users WHERE IsActive = 1|`
* **Variables:** N/A
* **Result:** `SELECT ID, Username, Email FROM Users WHERE IsActive = 1`
* **Explanation:** No conditional markers or handled variables exist. The entire string is a single "Always" segment.

### 2. Optional Variable Filter
* **Template:** `SELECT ID, Username FROM Users WHERE IsActive = 1 AND Status = ?@Status`
* **Shattered View:** `|SELECT ID, Username FROM Users WHERE IsActive = 1 AND| Status = @Status|`
* **Variables:** `@Status` is **Not Provided**.
* **Result:** `SELECT ID, Username FROM Users WHERE IsActive = 1`
* **Explanation:** Segment 2 is skipped. Segment 1 is the last survivor; it identifies the trailing `AND` as excess and strips it.

### 3. Boolean Toggle (Keyword Cleanup)
* **Template:** `SELECT ID, Username, Email FROM Users WHERE /*ActiveOnly*/Active = 1 ORDER BY Username`
* **Shattered View:** `|SELECT ID, Username, Email FROM Users WHERE| Active = 1 |ORDER BY Username|`
* **Variables:** `ActiveOnly` is **Not Provided**.
* **Result:** `SELECT ID, Username, Email FROM Users ORDER BY Username`
* **Explanation:** Segment 2 is skipped. Segment 1 is followed by Segment 3. Since Segment 3 starts with a Keyword (`ORDER BY`), Segment 1 strips its trailing Keyword Excess (`WHERE`).

### 4. Functional Footprint (Segment Growth)
* **Template:** `SELECT ID, u.Name FROM Users u WHERE u.Name LIKE CONCAT('%', ?@Name, '%')`
* **Shattered View:** `|SELECT ID, u.Name FROM Users u WHERE| u.Name LIKE CONCAT('%', @Name, '%')|`
* **Variables:** `@Name` is **Not Provided**.
* **Result:** `SELECT ID, u.Name FROM Users u`
* **Explanation:** The engine identifies the parentheses. Since they do not belong to a subquery, the segment **grows** to include the preceding identifier and operator (`u.Name LIKE`). Because the optional variable `@Name` is missing, the entire grown footprint is discarded.

### 5. Implicit AND (Shared Footprint)
* **Template:** `SELECT ID, Name FROM Products WHERE Price * ?@Modifier > ?@Minimum`
* **Shattered View:** `|SELECT ID, Name FROM Products WHERE| Price * @Modifier > @Minimum|`
* **Variables:** `@Modifier` is **Provided**, `@Minimum` is **Not Provided**.
* **Result:** `SELECT ID, Name FROM Products`
* **Explanation:** Multiple optional variables in a single shattered segment create a shared dependency. Because one is missing, the entire segment fails.

### 6. Context Joining (`&`)
* **Template:** `SELECT * FROM Products WHERE Price IS NOT NULL &AND Price > ?@MinPrice`
* **Shattered View:** `|SELECT * FROM Products WHERE| Price IS NOT NULL AND Price > @MinPrice|`
* **Variables:** `@MinPrice` is **Not Provided**.
* **Result:** `SELECT * FROM Products`
* **Explanation:** The `&` operator attaches the `AND` to the footprint of `@MinPrice`. Since `@MinPrice` is missing, the entire conditional segment is discarded.

### 7. Section Toggle (Join Dependency)
* **Template:** `SELECT p.ID, p.Name FROM Products p /*@VendorName*/INNER JOIN Vendors v ON v.ID = p.VendorID WHERE p.IsActive = 1 AND v.VendorName = ?@VendorName`
* **Shattered View:** `|SELECT p.ID, p.Name FROM Products p |INNER JOIN Vendors v ON v.ID = p.VendorID |WHERE p.IsActive = 1 AND| v.VendorName = @VendorName|`
* **Variables:** `@VendorName` is **Not Provided**.
* **Result:** `SELECT p.ID, p.Name FROM Products p WHERE p.IsActive = 1`
* **Explanation:** Segment 2 fails because the toggle variable is missing. Segment 4 also fails, causing Segment 3 to strip its trailing `AND`.

### 8. Linear Logic AND Gate
* **Template:** `SELECT ID, Username, Email, /*Internal&Authorized*/SocialSecurityNumber FROM Users`
* **Shattered View:** `|SELECT ID, Username, Email,| SocialSecurityNumber |FROM Users|`
* **Variables:** `Internal` is **Provided**, `Authorized` is **Not Provided**.
* **Result:** `SELECT ID, Username, Email FROM Users`
* **Explanation:** The `&` gate requires all specified keys to be present. Since `Authorized` is missing, Segment 2 is skipped. Segment 1 strips its trailing comma.

### 9. Atomic Subquery (Footprint Extension)
* **Template:** `SELECT ID, Name FROM Users WHERE /*@ActionType*/(SELECT Count(*) FROM Actions WHERE UserID = ID AND Type = @ActionType) > 0`
* **Shattered View:** `|SELECT ID, Name FROM Users WHERE| (SELECT Count(*) FROM Actions WHERE UserID = ID AND Type = @ActionType) > 0|`
* **Variables:** `@ActionType` is **Not Provided**.
* **Result:** `SELECT ID, Name FROM Users`
* **Explanation:** Using `?@ActionType` would only shatter the inner filter. The comment `/*@ActionType*/` is used to **extend the footprint** to the entire subquery, ensuring the logic is removed atomically if the variable is missing.

### 10. Collection Handler (`_X`)
* **Template:** `SELECT * FROM Tasks WHERE CategoryID IN (?@Cats_X)`
* **Shattered View:** `|SELECT * FROM Tasks WHERE| CategoryID IN (|@Cats_X|)|`
* **Variables:** `@Cats` = `[10, 20]`.
* **Result:** `SELECT * FROM Tasks WHERE CategoryID IN (@Cats_1, @Cats_2)`
* **Explanation:** `_X` identifies a handled variable for enumerable expansion. It generates individual parameters within the parenthetical segment.

### 11. Passenger Dependency (Segment Enclosure)
* **Template:** `SELECT Name FROM Products ORDER BY ID OFFSET ?@Skip_N ROWS FETCH NEXT @Take_N ROWS ONLY`
* **Shattered View:** `|SELECT Name FROM Products ORDER BY ID OFFSET| @Skip_N ROWS FETCH NEXT @Take_N ROWS ONLY|`
* **Variables:** `@Skip` = 50, `@Take` = **Not Provided**.
* **Result:** **Exception Thrown**
* **Explanation:** `FETCH` is not a keyword anchor, so `@Skip_N` and `@Take_N` are in one segment. Because `@Skip` is provided, the segment activates. However, `@Take_N` is a handled variable (via `_N`) and is **not** marked as optional (`?`). Its absence triggers an exception.

### 12. Raw Injection Handler (`_R`)
* **Template:** `SELECT ID, Name FROM @Table_R WHERE IsActive = 1`
* **Shattered View:** `|SELECT ID, Name FROM |@Table_R| WHERE IsActive = 1|`
* **Variables:** `@Table` = "Logs".
* **Result:** `SELECT ID, Name FROM Logs WHERE IsActive = 1`
* **Explanation:** `_R` identifies a handled variable for raw text injection (e.g., table names).

### 13. Dynamic Projection & Grouping
* **Template:** `SELECT /*Agg*/COUNT(*) AS Total&, SUM(Price) AS Revenue, p.CategoryName, /*NotAgg*/p.BrandName&, p.ID FROM Products p WHERE p.IsActive = 1 /*Agg*/GROUP BY p.CategoryName, p.BrandName`
* **Shattered View:** `|SELECT| COUNT(*) AS Total&, SUM(Price) AS Revenue,| p.CategoryName,| p.BrandName, p.ID |FROM Products p WHERE p.IsActive = 1 |GROUP BY p.CategoryName, p.BrandName|`
* **Variables:** `Agg` is **Not Provided**, `NotAgg` is **Provided**.
* **Result:** `SELECT p.CategoryName, p.BrandName, p.ID FROM Products p WHERE p.IsActive = 1`
* **Explanation:** `Agg` segments are discarded. Segment 3 keeps its comma because the active Segment 4 follows it.

### 14. Column Joining (`&,`)
* **Template:** `SELECT ID, Username, /*IncludeAddress*/City&, Street&, ZipCode FROM Users`
* **Shattered View:** `|SELECT ID, Username,| City, Street, ZipCode |FROM Users|`
* **Variables:** `IncludeAddress` is **Provided**.
* **Result:** `SELECT ID, Username, City, Street, ZipCode FROM Users`
* **Explanation:** The flag activates the segment. The `&,` logic ensures the comma joins the static projection to the dynamic block.

### 15. UPDATE List Cleanup
* **Template:** `UPDATE Users SET LastModified = GETDATE(), Username = ?@Username, Email = ?@Email WHERE ID = @ID`
* **Shattered View:** `|UPDATE Users SET LastModified = GETDATE(),| Username = @Username,| Email = @Email |WHERE ID = @ID|`
* **Variables:** `@Email` is **Not Provided**.
* **Result:** `UPDATE Users SET LastModified = GETDATE(), Username = @Username WHERE ID = @ID`
* **Explanation:** Segment 2 is now the last survivor of the `SET` block and strips its trailing comma.

### 16. INSERT Column Dependency
* **Template:** `INSERT INTO Users (Username, /*@Email*/Email) VALUES (@Username, ?@Email)`
* **Shattered View:** `|INSERT INTO Users (Username,| Email|) VALUES (@Username,| @Email|)|`
* **Variables:** `@Email` is **Not Provided**.
* **Result:** `INSERT INTO Users (Username) VALUES (@Username)`
* **Explanation:** Segments 2 and 4 are removed. Segments 1 and 3 strip their commas to maintain valid list syntax.

### 17. Multi-Column Insert
* **Template:** `INSERT INTO Profiles (UserID, /*Details*/Bio&, Website&, AvatarURL) VALUES (@UID, /*Details*/@Bio&, @Web&, @Img)`
* **Shattered View:** `|INSERT INTO Profiles (UserID,| Bio, Website, AvatarURL|) VALUES (@UID,| @Bio, @Web, @Img|)|`
* **Variables:** `Details` is **Provided**.
* **Result:** `INSERT INTO Profiles (UserID, Bio, Website, AvatarURL) VALUES (@UID, @Bio, @Web, @Img)`
* **Explanation:** One flag controls multiple segments to synchronize column and value lists.

### 18. DELETE AND Cleanup
* **Template:** `DELETE FROM Logs WHERE LogDate < GETDATE() - 30 AND /*PurgeOldOnly*/IsArchived = 1`
* **Shattered View:** `|DELETE FROM Logs WHERE LogDate < GETDATE() - 30 AND| IsArchived = 1|`
* **Variables:** `PurgeOldOnly` is **Not Provided**.
* **Result:** `DELETE FROM Logs WHERE LogDate < GETDATE() - 30`
* **Explanation:** Segment 2 is skipped. Segment 1 identifies the trailing `AND` and strips it.

### 19. Dynamic Order By (Clause Cleanup)
* **Template:** `SELECT * FROM Products WHERE IsActive = 1 /*@Sort*/ORDER BY @Sort_R ?@Dir_R`
* **Shattered View:** `|SELECT * FROM Products WHERE IsActive = 1 |ORDER BY| @Sort_R @Dir_R|`
* **Variables:** `@Sort` is **Provided**, `@Dir` is **Not Provided**.
* **Result:** `SELECT * FROM Products WHERE IsActive = 1`
* **Explanation:** Missing `@Dir` discards the entire footprint segment `@Sort_R @Dir_R`. This leaves `ORDER BY` empty, so it is stripped by the keyword cleanup logic.

# Technical Documentation: The Recursive Parser Engine

## Overview

The **Recursive Parser Engine** is a Parser Factory that generates optimized `Func<DbDataReader, T>` delegates through a two-step process:

1.  **Registration Phase**: Mapping components and configurations are defined within the engine’s registry.
2.  **Negotiation Phase**: The engine evaluates the database schema against the target object graph to emit the final IL delegate.

The tool is designed to provide high granularity for custom logic insertion points, allowing for precise control over the registered items used during the negotiation phase.

---

## Part 1: The Building Blocks (`TypeParsingInfo`)
The engine maps every type to a `TypeParsingInfo` instance, which acts as the authoritative metadata registry.

### 1. Construction Options (with Smart Prioritization)
This is a registry of valid "entry points" (constructors or factory methods) for creating the type.

* **Discovery & Acceptance Criteria (Automatic):** Registers **public** constructors and **public, static, non-generic** factory methods returning the exact target type.
* **Manual Additions (Explicit Registration):** Allows for non-public paths (internal/private).
    * **Requirement for Generics:** If the target type is generic, a manual factory method **must be generic and possess the exact same generic signature** as the target type to allow for specialization during the generation phase.
* **Path Viability & Known Types:** A path is only viable if all parameters are **Known Types** (types with their own `TypeParsingInfo`, `IReadable` implementations, or Basic Types).
    * **Basic Types:** `string`, `int`, `DateTime`, `bool`, `long`, `decimal`, `Guid`, `object`, `float`, `double`, `char`, `byte`, `short`, all **Enums**, and all **Unsigned equivalents**.
* **Specificity & Ordering:** Prioritizes the order of appearance but moves **More Specific** signatures ahead.
    * **Specificity Rule:** A signature is more specific if it has **equal or more parameters AND every parameter is the same type or a more specific implementation**.

### 2. Member Metadata
* **Scope:** Includes **public editable Fields** and **Properties**.
* **Conditional Completion:** Only the **parameterless constructor** authorizes member completion by default. Other paths must be explicitly configured to allow it.
* **Post-Instantiation Overwriting:** Authorized member population occurs after construction, potentially overwriting values set by the constructor if the schema provides a match for that member.

### 3. Custom Orchestration
Developers can replace the default strategy with a custom implementation to take full control over the **Tree Generation** process and the **Matching Conditions**.

---

## Part 2: The Atomic Unit: The Matching Item
A **Matching Item** (representing a parameter, field, or property) is a definition of how a specific member should behave during negotiation.

### The Default Implementation
* **Name & Alternative Names:** Defines the primary identity and alternatives which constitute the **Name Comparer** used to recognize columns in the reader.
* **Type Context:** Stores the target C# type, including generic placeholders to be resolved during generation.
* **Nullability Handling & Structural Anchors:** Defines the intended reaction to nullable columns (to be used later during Step B: Emission).

---

## Part 3: The Generation Phase (Default Process)

### Step A: Recursive Negotiation & Tree Generation
The engine attempts to build an **Emission Tree**. If no custom matching logic is registered, it proceeds with the **Default Matching Implementation** described below.

#### 1. Type-Level Negotiation (Default)
Determines how to instantiate a complex type by evaluating available paths:
* **Path Selection:** Iterates through **Construction Options** in prioritized order.
* **Path Validation:** Triggers a **Member-Level Negotiation** for every parameter in the signature.
* **Commitment:** A path is selected only if **every** parameter successfully resolves to a node. If any parameter fails to generate a node, the entire path is discarded.
* **The "Null" Result:** If all paths for a type are exhausted without a match, the negotiation returns **null**. The parent node interprets this result—usually by discarding its current path.
* **Member Enrichment:** If a path is chosen and authorizes it, the engine negotiates for **Member Metadata** to append additional setters to the resulting node.



#### 2. Member-Level Negotiation (Default)
Handles the schema reconciliation for a specific parameter, field, or property:
* **Type Specialization:** The engine first resolves the member's type. If generic, it uses the **Parent's Closed Type** to resolve placeholders into a concrete **Closed Type**.
* **Complex Type Detection:** The engine attempts to retrieve `TypeParsingInfo` for the closed type.
    * **If Complex:** It passes handling recursively to the associated **Type-Level Negotiation** call, passing the closed type, the **Column Modifier** (using its Name Comparer as a prefix), and its **Nullability Handle Info**.
    * **If Terminal:** It searches for a column matching its **Name Comparer** (within the current modifier context) and its **Type**. If a match is found, it creates a node using the found index; otherwise, it returns **null**.

#### 3. Tree Termination
Recursion stops at **Terminal Points** (Basic Types or custom implementations that handle their own parsing).

---

### Step B: Emission & Jump Points
The engine traverses the negotiated tree to emit optimized IL.

#### 1. Jump Point Management
The engine maintains a **Stack of Recovery Locations** (Jump Points). Each complex node represents a potential return point for the logic flow.

#### 2. Branch Pruning (The Nullability Handle)
The **Nullability Handling Info** assigned during negotiation dictates the emitted IL:

* **JumpIfNull:** Emits a branch instruction to skip instantiation and "jump" to the parent's recovery point if the column is `DBNull`.
* **Nullable:** Emits code to return `null` or `default` and continues execution.
* **Required (No Check):** Emits a direct cast. This will throw an exception if the column is `DBNull`.



---

## Caching and Scope
* **Function Caching:** Compiled delegates are cached against the **Target Type + Database Schema signature**.
* **Schema Evolution:** Changes in SQL columns or aliases trigger a new **Negotiation Phase** to rebuild the tree, ensuring the IL is always optimized for the current data shape.
