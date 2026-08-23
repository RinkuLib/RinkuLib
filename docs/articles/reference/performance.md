# Performance

## What was tested

Rinku was compared with Dapper using the same database, SQL, provider, and result shape. The tests include command execution, mapping, streaming, buffering, nullable and single-result semantics, scalar operations, procedures, parameters, and transactions.

The [RinkuLib.Benchmarks project](https://github.com/RinkuLib/RinkuLib/tree/alpha/RinkuLib.Benchmarks) contains the benchmark definitions and setup.

## Commands and mapping

These cases cover command setup, database-facing values, and the first step of turning rows into application shapes.

| Test idea | Dapper<br>Mean / Allocated | Rinku<br>Mean (Ratio) / Allocated (Alloc Ratio) |
| --- | ---: | ---: |
| Executing a batch of commands | 95,128.7 us<br>225.2 KB | Command: 90,293.9 us (0.95×)<br>163.99 KB (0.73×) |
| Mapping a complex result shape | 546.9 us<br>15.63 KB | Command: 550.4 us (1.01×)<br>14.33 KB (0.92×)<br>SQL string: 539.3 us (0.99×)<br>14.33 KB (0.92×) |
| Managing the connection lifecycle asynchronously | 742.6 us<br>23.09 KB | Command: 708.0 us (0.96×)<br>22.21 KB (0.96×)<br>SQL string: 716.8 us (0.97×)<br>22.21 KB (0.96×) |
| Mapping a custom database type asynchronously | 691.1 us<br>19.5 KB | Command: 660.6 us (0.96×)<br>18.18 KB (0.93×) |
| Reading rows through a data reader asynchronously | 632.7 us<br>12.13 KB | Command: 633.2 us (1.00×)<br>11.54 KB (0.95×) |
| Reading rows through a data reader synchronously | 582.1 us<br>5.33 KB | Command: 586.2 us (1.01×)<br>4.73 KB (0.89×) |
| Mapping a dynamic result asynchronously | 599.5 us<br>22.71 KB | Command: 610.8 us (1.02×)<br>21.46 KB (0.94×)<br>SQL string: 602.0 us (1.01×)<br>21.38 KB (0.94×) |
| Projecting a dynamic result asynchronously without details | dynamic: 566.6 us<br>14.21 KB<br>raw dictionary: 567.7 us<br>13.44 KB | dynamic object: 559.3 us (0.99×)<br>12.14 KB (0.85×)<br>raw dictionary: 562.4 us (0.99×)<br>12.39 KB (0.87×) |
| Projecting a dynamic result asynchronously with details | dynamic: 615.2 us<br>21.17 KB<br>raw dictionary: 600.2 us<br>20.17 KB | dynamic object: 591.9 us (0.96×)<br>18.59 KB (0.88×)<br>raw dictionary: 591.9 us (0.96×)<br>19.1 KB (0.90×) |

## Execution and parameters

These cases compare non-query work, scalar execution, SQL expansion, and parameter handling.

| Test idea | Dapper<br>Mean / Allocated | Rinku<br>Mean (Ratio) / Allocated (Alloc Ratio) |
| --- | ---: | ---: |
| Executing a non-query asynchronously | 1,494.1 us<br>10.99 KB | Command: 1,458.1 us (0.98×)<br>9.87 KB (0.90×)<br>SQL string: 1,466.3 us (0.98×)<br>9.87 KB (0.90×) |
| Executing a non-query synchronously | 1,433.9 us<br>6.13 KB | Command: 1,415.4 us (0.99×)<br>5.02 KB (0.82×)<br>SQL string: 1,388.7 us (0.97×)<br>5.02 KB (0.82×) |
| Executing a scalar command asynchronously | 1,335.0 us<br>13.52 KB | Command: 1,298.1 us (0.97×)<br>13.01 KB (0.96×)<br>SQL string: 1,304.2 us (0.98×)<br>13.01 KB (0.96×) |
| Passing explicit string metadata | 567.7 us<br>13.75 KB | Command: 563.6 us (0.99×)<br>12.28 KB (0.89×) |
| Expanding an IN clause | 753.9 us<br>51.74 KB | Command: 741.6 us (0.98×)<br>47.75 KB (0.92×)<br>SQL string: 749.3 us (0.99×)<br>47.75 KB (0.92×) |
| Replacing a literal in SQL | 505.0 us<br>6.42 KB | numeric literal: 494.3 us (0.98×)<br>4.79 KB (0.75×)<br>parameterized count: 497.8 us (0.99×)<br>5.35 KB (0.83×) |
| Adding parameters manually | 549.7 us<br>16.32 KB | Command: 537.9 us (0.98×)<br>14.7 KB (0.90×) |
| Reading multiple result sets asynchronously | 647.1 us<br>33.59 KB | Command: 644.9 us (1.00×)<br>33.29 KB (0.99×)<br>SQL string: 641.9 us (0.99×)<br>33.29 KB (0.99×) |
| Reading multiple result sets synchronously | 587.7 us<br>26.2 KB | Command: 586.5 us (1.00×)<br>24.79 KB (0.95×)<br>SQL string: 578.5 us (0.98×)<br>24.79 KB (0.95×) |

## Relationships and row mapping

These cases test nested and one-to-many shapes, separate result sets, and polymorphic row selection.

| Test idea | Dapper<br>Mean / Allocated | Rinku<br>Mean (Ratio) / Allocated (Alloc Ratio) |
| --- | ---: | ---: |
| Folding joined rows into one-to-many objects | 10,897.3 us<br>3379.17 KB | tuples: 10,572.4 us (0.97×)<br>2674.6 KB (0.79×)<br>native: 10,418.8 us (0.96×)<br>2205.87 KB (0.65×) |
| Mapping one-to-many data from separate result sets | 11,127.2 us<br>2919 KB | Command: 9,375.4 us (0.84×)<br>2145.01 KB (0.73×) |
| Mapping ordered one-to-many data from separate result sets | 8,452.4 us<br>2758.45 KB | Command: 7,811.2 us (0.92×)<br>1984.74 KB (0.72×) |
| Handling output and return parameters asynchronously | 531.7 us<br>13.98 KB | Command: 527.7 us (0.99×)<br>12.57 KB (0.90×) |
| Selecting a parser for polymorphic rows | 489.3 us<br>6.95 KB | row parser selection: 480.7 us (0.98×)<br>6.33 KB (0.91×)<br>interface factory: 481.0 us (0.98×)<br>6.33 KB (0.91×) |

## Streaming and buffering

These cases vary how results are consumed and how many rows are returned.

| Test idea | Dapper<br>Mean / Allocated | Rinku<br>Mean (Ratio) / Allocated (Alloc Ratio) |
| --- | ---: | ---: |
| Streaming query results asynchronously (rowCount=50) | 1,251.4 us<br>323.87 KB | Command: 1,243.2 us (0.99×)<br>316.7 KB (0.98×)<br>SQL string: 1,224.5 us (0.98×)<br>316.7 KB (0.98×) |
| Streaming query results asynchronously (rowCount=5000) | 60,778.7 us<br>30488.39 KB | Command: 62,115.6 us (1.02×)<br>30132.88 KB (0.99×)<br>SQL string: 61,047.3 us (1.01×)<br>30132.8 KB (0.99×) |
| Buffering query results asynchronously (rowCount=50) | 1,248.9 us<br>323.61 KB | Command: 1,258.9 us (1.01×)<br>317.71 KB (0.98×)<br>SQL string: 1,230.4 us (0.99×)<br>317.71 KB (0.98×) |
| Buffering query results asynchronously (rowCount=5000) | 71,702.9 us<br>30623.25 KB | Command: 72,122.4 us (1.01×)<br>30270.99 KB (0.99×)<br>SQL string: 71,589.4 us (1.00×)<br>30269.54 KB (0.99×) |
| Buffering query results synchronously (rowCount=50) | 1,192.6 us<br>316.97 KB | Command: 1,209.4 us (1.01×)<br>311 KB (0.98×)<br>SQL string: 1,193.3 us (1.00×)<br>311 KB (0.98×) |
| Buffering query results synchronously (rowCount=5000) | 70,488.8 us<br>30613.8 KB | Command: 70,189.3 us (1.00×)<br>30261.7 KB (0.99×)<br>SQL string: 70,088.9 us (1.00×)<br>30261.88 KB (0.99×) |
| Streaming query results synchronously (rowCount=50) | 1,192.3 us<br>315.85 KB | Command: 1,223.3 us (1.03×)<br>309.95 KB (0.98×)<br>SQL string: 1,207.3 us (1.01×)<br>309.95 KB (0.98×) |
| Streaming query results synchronously (rowCount=5000) | 59,446.8 us<br>30480.28 KB | Command: 60,506.4 us (1.02×)<br>30126.01 KB (0.99×)<br>SQL string: 60,037.6 us (1.01×)<br>30126.01 KB (0.99×) |

## Result selection

These cases compare the semantics for nullable results, optional results, and single-result guarantees.

| Test idea | Dapper<br>Mean / Allocated | Rinku<br>Mean (Ratio) / Allocated (Alloc Ratio) |
| --- | ---: | ---: |
| Querying a nullable reference with a default asynchronously | 536.2 us<br>13.5 KB | Command: 549.6 us (1.03×)<br>12.24 KB (0.91×)<br>SQL string: 534.3 us (1.00×)<br>12.24 KB (0.91×) |
| Querying a nullable reference with a default synchronously | 488.0 us<br>6.56 KB | Command: 495.8 us (1.02×)<br>5.45 KB (0.83×)<br>SQL string: 500.0 us (1.02×)<br>5.45 KB (0.83×) |
| Querying a nullable reference asynchronously | 581.7 us<br>13.5 KB | Command: 528.9 us (0.91×)<br>12.24 KB (0.91×)<br>SQL string: 522.3 us (0.90×)<br>12.24 KB (0.91×) |
| Querying a nullable reference synchronously | 502.5 us<br>6.56 KB | Command: 497.0 us (0.99×)<br>5.45 KB (0.83×)<br>SQL string: 504.9 us (1.01×)<br>5.45 KB (0.83×) |
| Querying a nullable value asynchronously | 564.8 us<br>13.36 KB | Command: 561.3 us (0.99×)<br>12.07 KB (0.90×)<br>SQL string: 534.6 us (0.95×)<br>12.07 KB (0.90×) |
| Querying a nullable value synchronously | 493.2 us<br>6.53 KB | Command: 491.6 us (1.00×)<br>5.39 KB (0.83×)<br>SQL string: 503.8 us (1.02×)<br>5.39 KB (0.83×) |
| Querying one result or a default asynchronously | 606.5 us<br>22.65 KB | optional: 600.4 us (0.99×)<br>21.32 KB (0.94×)<br>optional nullable: 575.6 us (0.95×)<br>21.32 KB (0.94×)<br>optional: 583.0 us (0.96×)<br>21.32 KB (0.94×)<br>optional nullable: 584.0 us (0.96×)<br>21.32 KB (0.94×) |
| Querying one result or a default synchronously | 552.8 us<br>15.87 KB | optional: 541.0 us (0.98×)<br>14.69 KB (0.93×)<br>optional nullable: 544.2 us (0.98×)<br>14.69 KB (0.93×)<br>optional: 541.8 us (0.98×)<br>14.69 KB (0.93×)<br>optional nullable: 555.6 us (1.01×)<br>14.69 KB (0.93×) |
| Querying one result, enforcing a single row, or a default asynchronously | 771.7 us<br>22.88 KB | Command: 621.0 us (0.81×)<br>21.53 KB (0.94×)<br>SQL string: 588.4 us (0.77×)<br>21.46 KB (0.94×) |
| Querying one result, enforcing a single row, or a default synchronously | 556.2 us<br>15.87 KB | Command: 540.4 us (0.97×)<br>14.69 KB (0.93×)<br>SQL string: 535.9 us (0.96×)<br>14.69 KB (0.93×) |
| Querying exactly one result asynchronously | 594.5 us<br>22.79 KB | Command: 589.6 us (0.99×)<br>21.46 KB (0.94×)<br>SQL string: 596.6 us (1.00×)<br>21.46 KB (0.94×) |
| Querying exactly one result synchronously | 553.5 us<br>15.87 KB | Command: 548.8 us (0.99×)<br>14.69 KB (0.93×)<br>SQL string: 538.9 us (0.97×)<br>14.69 KB (0.93×) |
| Querying one result asynchronously | 600.9 us<br>22.65 KB | Command: 597.6 us (0.99×)<br>21.32 KB (0.94×)<br>SQL string: 600.4 us (1.00×)<br>21.32 KB (0.94×) |
| Querying one result synchronously | 547.0 us<br>15.87 KB | Command: 540.5 us (0.99×)<br>14.69 KB (0.93×)<br>SQL string: 543.8 us (0.99×)<br>14.69 KB (0.93×) |

## Scalar and database operations

These cases cover scalar sequences, conditional counts, procedures, table-valued parameters, transactions, and execution context.

| Test idea | Dapper<br>Mean / Allocated | Rinku<br>Mean (Ratio) / Allocated (Alloc Ratio) |
| --- | ---: | ---: |
| Reading a scalar sequence asynchronously | 1,863.6 us<br>202.22 KB | Command: 1,818.9 us (0.98×)<br>84.01 KB (0.42×)<br>SQL string: 1,877.8 us (1.01×)<br>84.02 KB (0.42×) |
| Counting rows with a fixed and conditional path using a parameter | 566.2 us<br>13.35 KB | fixed path: 565.8 us (1.00×)<br>11.91 KB (0.89×)<br>conditional path: 558.6 us (0.99×)<br>11.91 KB (0.89×) |
| Counting rows with a fixed and conditional path without a parameter | 1,337.2 us<br>12.22 KB | fixed path: 1,307.0 us (0.98×)<br>10.85 KB (0.89×)<br>conditional path: 1,334.8 us (1.00×)<br>11.2 KB (0.92×) |
| Reading a scalar result asynchronously | 1,321.8 us<br>12.22 KB | Command: 1,339.9 us (1.01×)<br>10.85 KB (0.89×)<br>SQL string: 1,356.2 us (1.03×)<br>10.85 KB (0.89×) |
| Calling a stored procedure asynchronously | 589.5 us<br>21.9 KB | Command: 577.8 us (0.98×)<br>21.14 KB (0.97×) |
| Passing a table-valued parameter asynchronously | 1,226.1 us<br>16.34 KB | Command: 1,223.0 us (1.00×)<br>14.84 KB (0.91×) |
| Applying timeout and cancellation context | 1,255.8 us<br>324.21 KB | Command: 1,256.1 us (1.00×)<br>318.31 KB (0.98×) |
| Executing inside a transaction synchronously | 1,473.8 us<br>6.61 KB | Command: 1,489.1 us (1.01×)<br>6.61 KB (1.00×) |

## Typed projection

These cases compare typed projection with and without additional details.

| Test idea | Dapper<br>Mean / Allocated | Rinku<br>Mean (Ratio) / Allocated (Alloc Ratio) |
| --- | ---: | ---: |
| Projecting a typed result asynchronously without details | 561.0 us<br>14.1 KB | Command: 564.2 us (1.01×)<br>12.16 KB (0.86×) |
| Projecting a typed result asynchronously with details | 587.5 us<br>21.04 KB | Command: 572.8 us (0.98×)<br>18.59 KB (0.88×) |
