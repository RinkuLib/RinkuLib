# Exception rework plan

Working notes for replacing the current throw sites with a typed, coded exception system. Not published
documentation. Move or delete once the work lands.

Census below was taken from `RinkuLib/` on 2026-07-18, before any of the rework.

## Where it stands

Every exception the library raises is either a Rinku code or a BCL type a caller catches on purpose. The
census that backs that:

- 32 codes, all wired to a throw site, all with a doc entry, all with a resolving `HelpLink`.
- Bare `throw new Exception`: 0.
- 38 non-Rinku throws remain. The bar for keeping one is that a caller outside RinkuLib catches it by its
  BCL type today and a Rinku type would break them. Every remaining site is listed below against that
  bar, since "it is a contract" was too loose a category to hide behind.

### Every non-Rinku throw, and why

**Implementing a BCL interface, where the interface names the exception.** A consumer that never heard of
RinkuLib holds these through the interface. `RinkuException` derives from `Exception`, and C# has single
inheritance, so a Rinku type here cannot also be the type the contract names.

| site | interface | why the BCL type |
| --- | --- | --- |
| `WrappedBasicReader` 59, 144, 145, 159, 173 | `DbDataReader` | ADO consumers probe optional members with `catch (NotSupportedException)` |
| `WrappedBasicReader` 193 | `DbDataReader` | use after dispose is `ObjectDisposedException` everywhere in .NET |
| `DynaObject` 65, 76, 126, 133, 142, 149 | `IReadOnlyDictionary` | a missing key is `KeyNotFoundException`, which LINQ and serializers rely on |
| `LetterMap` 94 | dictionary semantics | same |
| `LetterMap` 144 | `Dictionary.Add` semantics | adding a duplicate key is `ArgumentException` |
| `TrackingEditListBase` 173, 184, 190, 198, 203, 209 | `IList`, `ICollection` | a wrong element type or a bad `CopyTo` is the `ArgumentException` family |
| `TrackingEditListBase` 233, 235, 237 | `IBindingList` | the interface documents `NotSupportedException` for sort and find |

**Indexers.** `DynaObject` 111, 117, `PooledArray` 99, `TrackingList` 26, 33, 95, 106. Indexing past the
end reads the same as indexing anything else in .NET, and the author lets the lower level throw rather
than re-checking, see [[deliberate-exception-passthrough]].

**Argument validation on a public entry point.** `IMapper` 263, 278, `AsciiMapperBuilder` 77,
`WrappedBasicReader` 46 for a null argument, `LetterMap` 70, `PooledArray` 53, 108 for a bad length or
character, `TypeExtensions` 153 for a reference type where `Nullable<T>` needs a value type. These say
"you passed the wrong thing", which is what the `ArgumentException` family means to every .NET caller.
Coding them would say the failure is about RinkuLib when it is about the call.

### What this pass moved

Four sites failed that bar and were converted rather than justified:

- `DynaObject` JSON read said `NotImplementedException`, which reads as "not built yet" for a permanent
  refusal. Now RINKU5007.
- `NotSetHandler.Handle` threw a bare `NotImplementedException` where reaching it means a handler spot
  rendered before binding, a library bug. Now RINKU9001.
- `AsciiMapperBuilder` threw `UnreachableException` where RINKU9001 already means exactly that, and says
  so with the report-it message.
- `SizedDbParamCache.Get` threw `ArgumentException` for a Rinku decision about which `DbType` carries a
  size. Now RINKU2006.

The internal band is one code rather than six. A caller does the same thing for every one of them, report
it, so splitting them would have been six doc entries carrying the same sentence and six anchors landing
on the same advice. The message names which invariant it was. This is the call already made for RINKU5003
over seven member failures and RINKU4002 over any shape's rule, and it is the test for whether a condition
earns a code of its own: does the reader do something different because of it.

Two of the invariants it covers have no test. The unfinished condition is unreachable from any input, and
the missing generated constructor needs a shape wider than the generated forms, which no reproduction has
been found for.

### Known defect, not fixed

`Copier<T>` builds its copy strategy in a static initializer, so a RINKU6002 refusal reaches the caller
wrapped in a `TypeInitializationException` and the code sits on the inner exception. This is the same
burial that `QueryCommand.GetAccessorCache` had, fixed there by rethrowing the inner with
`ExceptionDispatchInfo`. The same fix does not apply here, since the wrapping comes from static
initialization rather than a reflection call, so it needs the strategy build moved out of the type
initializer. That is a performance-sensitive path in a subsystem still being built, so it is left for the
Tracking pass. `CopyStrategyTests` reads through the wrapper and keeps passing either way.

## The bands, as landed

The first banding in this file was guessed before the phases were clear, and it grouped by subsystem
folder rather than by what a run had done when it failed. It was redone once every condition was known.
The band now follows the stage reached, which is what tells a reader where to look.

| band | family | raised while | codes |
| --- | --- | --- | --- |
| 1### | `RinkuTemplateException` | reading the template | 8 |
| 2### | `RinkuBindingException` | preparing a command from it | 5 |
| 3### | `RinkuMappingException` | building a parser for the target type | 1 |
| 4### | `RinkuReadException` | reading a result through that parser | 5 |
| 5### | `RinkuConfigurationException` | configuring a type | 6 |
| 9### | `RinkuInternalException` | an invariant did not hold | 1 |

31 codes, densely numbered with no gaps. What the redo changed:

- The old `0###` and `1###` split put template parsing beside command binding while handler rendering sat
  with connection state. Rendering a handler is part of preparing a command, so `2###` holds it.
- The old `2###` mixed building a parser with reading rows through one. Those are the two halves a reader
  is trying to tell apart, so they are now `3###` and `4###`.
- The old `3###` value band folded into reading, where a conversion failure and a null refusal belong
  together.
- The emission band held two codes that were not the same thing. Attribute misuse is a configuration
  mistake and moved to `5###`, the missing generated constructor is a library limit and moved to `9###`.
- `NestingTooDeep` and `UnbalancedParenthesis` became `ScopeTooDeep` and `UnbalancedScope`, since `CASE`
  and `BEGIN` sit on the same counter and the old names sent readers looking for parentheses.

Renumbering cost nothing in the tests, which assert `ErrorCodes.X` rather than literals. That is the seam
working as intended, and it is why the constants are worth keeping even where a literal would read fine.

## Status

The system is in place and the first families are migrated. What landed:

- `RinkuException` base with `Code` and a `HelpLink` onto the error reference, plus the seven family
  classes, in `RinkuLib/Exceptions/`.
- `ErrorCodes` holding every code, one constant per condition, with the summary that seeds its doc entry.
- `Refuse` raising the conditions checked from many places, and the four typed conditions that carry
  structured facts, `RinkuNoConnectionException`, `RinkuNoParserException`, `RinkuNoRowsException`,
  `RinkuTooManyRowsException`.
- 24 codes migrated: the whole template family, the connection guard (27 sites to one), the mapping
  family, value access, registration, and the internal invariants listed below.
- `NullValueAssignmentException` and `RequiredHandlerValueException` reparented, names kept, codes added.
- `docs/articles/reference/errors.md` written and in the reference toc.
- `ErrorCodeTests` guarding the catalog itself: codes unique, `RINKU####` shaped, inside a defined band,
  and an internal invariant naming itself as a library bug.
- Tests assert codes through `Refusals`, not message text.

Decisions taken: single `RinkuException` root rather than per-family BCL bases, and RINKU4001 aligned so
every info refuses an unusable type identically. Both forcing tests are gone, replaced by
`Every_info_refuses_an_unusable_type_the_same_way`.

Bare `throw new Exception` went from 69 to 1. The remaining one is
`Tracking/TrackingEditListBase.cs:23`, left alone because Tracking is still WIP and wants its own pass.

Codes added after the first migration: RINKU0009 (a marker naming a variable the query does not contain,
which was a bare throw), RINKU1005 (a handler handed a value it cannot render, which was an uncoded
`InvalidCastException`), RINKU9004 (emitter arity, which was `throw new Exception()` with no message at
all), RINKU9005 (a reflection lookup of a runtime internal came back empty). RINKU0007 retired, the
condition moved to RINKU9003 once it turned out to be a library bug rather than a template one.

The earlier draft of this table is kept below for the record. Every row in it is now wired.

| site | condition | proposed |
| --- | --- | --- |
| `DbParsing/ColumnUsage.cs` 23, 33 | checkpoint length mismatch | RINKU9002 |
| `DbParsing/DefaultTypeParsingInfo.cs` 53, 75, 171, 182 | construction or member from a foreign generic | RINKU4002 |
| `DbParsing/DefaultTypeParsingInfo.cs` 168 | construction result not assignable | RINKU4003 |
| `DbParsing/DynaObject.cs` 34 | mapper length mismatch | new 3### code |
| `DbParsing/DynaObjectTypeMatcher.cs` 121, 148, 161 | generated ctor not found | RINKU5002 |
| `DbParsing/TypeExtensions.cs` 153 | nullable ctor on a reference type | RINKU4005 |
| `DbParsing/TypeExtensions.cs` 171 | nullable ctor not found | new 4### code |
| `Queries/QueryFactory.cs` 150 | comment condition names an unknown variable | new 0### code |
| `Queries/SpecialHandlers.cs` 126 | value not set or not saved | RINKU1004 |
| `Tracking/TrackingEditListBase.cs` 23 | no value at index | new code, Tracking is WIP |
| `TypeAccessing/ForBoolCondAttribute.cs` 20 | attribute on the wrong member type | RINKU5001 |
| `TypeAccessing/NotNullOrWhitespaceAttribute.cs` 19 | attribute on the wrong member type | RINKU5001 |
| `TypeAccessing/WrappedBasicReader.cs` 18, 23 | reflection lookup of a BCL internal failed | RINKU9### |

The non-Rinku throws were then audited one by one rather than by type. Three groups came out of it.

**Contract obligations, left alone.** `WrappedBasicReader` owes `DbDataReader` its `NotSupportedException`
and `ObjectDisposedException`, `DynaObject` owes `IReadOnlyDictionary` its `KeyNotFoundException` and its
indexers an `IndexOutOfRangeException`, `LetterMap` and `PooledArray` owe the same, and
`TrackingEditListBase` owes `IBindingList` a `NotSupportedException` from `ApplySort`, `RemoveSort`, and
`Find`. A caller catches these by their BCL type on purpose. Converting them would break the contract.

**Rinku decisions wearing a BCL type, converted.** A member with no setter, a member that is not a field,
property, or method, a construction whose result does not match the target, all were
`InvalidOperationException`, `ArgumentException`, or `NotImplementedException` while being the library
deciding something is unusable. Now RINKU4003 and RINKU4004, joining the sites that already carried those
codes.

**A wrong BCL type, corrected.** `Mapper.GetOneKeyMapper`, `GetTwoKeyMapper`, and `AsciiMapperBuilder`
threw `NullReferenceException` for a null key argument, which is indistinguishable from a real
dereference bug. Now `ArgumentNullException` naming the parameter, no code, since the BCL type says it
precisely.

Left for the Tracking pass, which is WIP: its `Copier` and `CollectionCopier` reflection failures, and the
one remaining bare `Exception`.

An earlier draft called these "a missing guard or a leak" and slated them for wrapping. That was wrong.
The author lets a lower level throw rather than re-checking at every level, most often for index access
where the collection already validates the bound, so re-guarding costs performance and repeats a check
whose message was already accurate. See [[deliberate-exception-passthrough]].

What earns a code is that the raw error cannot tell the user what they did wrong, not that it is uncoded.

| raw error | verdict |
| --- | --- |
| unknown handler suffix, was `KeyNotFoundException` | coded, a SQL typo surfaced as "The given key was not present in the dictionary", naming neither the suffix nor the variable |
| accessor attribute misuse, was `TargetInvocationException` | unwrapped, the message underneath was already good and merely buried |
| an index the caller passed is out of range | leave it, the runtime message is apt |
| a collection lookup the caller drives | leave it |

Work through the rest case by case against that test rather than as a batch.

## Original state

143 throw sites. By type:

| count | type |
| --- | --- |
| 69 | `Exception` |
| 15 | `InvalidOperationException` |
| 12 | `NotSupportedException` |
| 12 | `ArgumentException` |
| 7 | `KeyNotFoundException` |
| 7 | `IndexOutOfRangeException` |
| 4 | `RequiredHandlerValueException` |
| 4 | `NotImplementedException` |
| 4 | `ArgumentOutOfRangeException` |
| 3 | `NullReferenceException` |
| 3 | `MissingMethodException` |
| 1 each | `UnreachableException`, `ObjectDisposedException`, `ArgumentNullException` |

The 69 bare `Exception` throws by area: TypeAccessing 20, DbParsing 18, Commands 17, Queries 12,
Tracking 1, Tools 1.

Two typed exceptions already exist and are the model to generalize:

- `NullValueAssignmentException` (`DbParsing/DbItemParser.cs`), carries parent type, parameter type,
  parameter name.
- `RequiredHandlerValueException` (`Queries/QueryText.cs`), carries the variable index.

`NullValueAssignmentException` is the reference shape: a dedicated type, constructor parameters that are
the facts of the failure, and a message composed from them rather than assembled at the call site.

### What the census exposes

- `"no connections was set with the command"` is repeated **27 times** across `DBCommandExtensions`,
  `BaseTypeParser`, and `EnumerableTypeParser`. One condition, 27 literals.
- `"The query provided more result than required for the single item"` appears 4 times.
- `"Cannot add a possible construction from a generic type other then the target type"` appears 3 times.
- `"should not happend"` is an internal assertion that reached a user-facing throw. Typo included.
- Seven `KeyNotFoundException`, seven `IndexOutOfRangeException`, and three `NullReferenceException` reach
  callers. One was coded this session (unknown handler suffix), the rest are unaudited and most are
  expected to stay as they are, per principle 3.

## Principles

1. The failure names what failed. Every exception carries the specific thing (type, column, slot,
   variable, suffix) as a property, not only inside a formatted string.
2. One condition, one throw site. A condition raised in 27 places gets a helper, so the message and code
   live once.
3. A raw exception from a lower level is left alone unless it fails to say what the caller did wrong.
   Wrapping an out-of-range index the caller passed adds nothing and costs a check on a hot path, see
   [[deliberate-exception-passthrough]].
4. Structured before pretty. Properties first so callers can branch, message second so humans can read.

## Code scheme

Format `RINKU####`, four digits, banded by subsystem. Bands leave room to grow.

| band | subsystem | roughly |
| --- | --- | --- |
| 0001-0999 | Query template parsing | `QueryExtracter`, `QueryFactory`, handler suffixes, conditions |
| 1000-1999 | Parameter binding and commands | `DBCommandExtensions`, param caches, connection state |
| 2000-2999 | Type negotiation and parser construction | `TypeParser`, `TypeParsingInfo`, construction paths |
| 3000-3999 | Row reading and value conversion | null rules, `Caster`, `DynaObject` access |
| 4000-4999 | Registration and configuration API | `AddMember`, `AddPossibleConstruction`, `ValidateCanUseType` |
| 5000-5999 | Emission and codegen | `Generator`, accessor emitters, attribute misuse |
| 9000-9999 | Internal invariants | conditions that mean a library bug, not user error |

The code goes on the exception as a property and is prefixed to the message, so a search for the code
finds both the throw site and the doc page.

## Exception hierarchy

```
RinkuException (abstract)                       Code, plus a link to the doc anchor
├── RinkuQueryTemplateException                 0001-0999, carries the query text and offset
├── RinkuBindingException                       1000-1999, carries the variable or parameter name
├── RinkuMappingException                       2000-2999, carries the target type and the schema
│   └── NullValueAssignmentException            existing, keep the name, give it a code
├── RinkuValueException                          3000-3999, carries source value and target type
├── RinkuRegistrationException                  4000-4999, carries the type and the member
├── RinkuEmissionException                      5000-5999, carries the attribute or member
└── RinkuInternalException                      9000-9999, "this is a bug, please report"
```

Keep `NullValueAssignmentException` and `RequiredHandlerValueException` as names. They are already
descriptive and already asserted in tests. Reparent them and add codes.

Base class carries at minimum:

```csharp
public abstract class RinkuException : Exception {
    public string Code { get; }
    public string HelpLink { get; }   // docs anchor built from Code
}
```

## Catalog

First pass. Conditions confirmed by instrumentation this session are marked "seen". The rest are from the
census and need their message and behaviour confirmed before a code is committed.

### Query template, 0001-0999

| code | condition | today |
| --- | --- | --- |
| RINKU0001 | Query shorter than two characters | `"invalid query ..., must contains at least 2 letters"` (seen) |
| RINKU0002 | Comment or condition left unclosed | `"comment unclosed"`, 2 sites |
| RINKU0003 | Parenthesis or case nesting past 64 | `"cannot have more than 64 level deep..."` |
| RINKU0004 | Too many closing parentheses | `"too many closing parentesis / cases"` |
| RINKU0005 | Whitespace-only condition key | `"Cannot have a whitespace condition ..."` |
| RINKU0006 | Unknown handler suffix letter | fixed this session, was `KeyNotFoundException` (seen) |
| RINKU0007 | Condition never finished | `"conditions {cond} was not finished [..]"` |
| RINKU0008 | Dynamic-projection-only construct used elsewhere | `"The ... may only be used in a dynamic projection context"` |

### Binding and commands, 1000-1999

| code | condition | today |
| --- | --- | --- |
| RINKU1001 | Command carries no connection | bare `Exception`, **27 sites** (seen) |
| RINKU1002 | Required handler variable not set | `RequiredHandlerValueException`, keep name |
| RINKU1003 | No valid parameter at index | `"there is no valid parameter at index {i}"` (seen) |
| RINKU1004 | Parameter index out of range | `ArgumentOutOfRangeException` from the collection (seen) |
| RINKU1005 | Value not set or not saved | `"the value was not set or not saved"` |

RINKU1001 is the single highest-value change in the plan. One helper, 27 call sites collapse.

### Mapping and negotiation, 2000-2999

| code | condition | today |
| --- | --- | --- |
| RINKU2001 | No construction path satisfies the schema | `"cannot make the parser for {T} with the schema (...)"` (seen) |
| RINKU2002 | Query returned no rows for a non-optional shape | `"No values were returned from the query"` (seen) |
| RINKU2003 | More rows than a `Single<>` shape allows | `"The query provided more result than required..."`, 4 sites (seen) |
| RINKU2004 | Null into a slot that refuses it | `NullValueAssignmentException`, keep name (seen) |
| RINKU2005 | Recursion stopped without consuming a column | reaches RINKU2001 today |

RINKU2001 is the most common user-facing failure and deserves the richest payload: target type, the
schema it was offered, and ideally which slot ran out of candidates. That last part is what turns
"cannot make the parser" into something a user can act on without reading the negotiation source.

### Value access, 3000-3999

| code | condition | today |
| --- | --- | --- |
| RINKU3001 | Cannot convert source value to target type | `"Unable to parse from {v} (object : {t}) to {T}"` (seen) |
| RINKU3002 | Cannot read column by index as type | `"Unable to get value at index {i} of type {T}"` (seen) |
| RINKU3003 | Cannot read column by name as type | `"Unable to get value for {key} of type {T}"`, 2 sites (seen) |

### Registration, 4000-4999

| code | condition | today |
| --- | --- | --- |
| RINKU4001 | Info does not support the requested type | **split**: `InvalidOperationException` on base and ctor infos, `ArgumentException` on dyna (seen) |
| RINKU4002 | Construction from a foreign generic type | `"Cannot add a possible construction from a generic type..."`, 3 sites |
| RINKU4003 | Construction result not assignable to target | `"the expected type is {T} but the provided type ... is {U}"` |
| RINKU4004 | Member is not a field, property, or method | `"The member must be a field, property or method"` |
| RINKU4005 | Nullable ctor on a non-value type | `"type must be a value type in order to have a nullable ctor"` |

RINKU4001 is blocked on an open decision, below.

### Emission, 5000-5999

| code | condition | today |
| --- | --- | --- |
| RINKU5001 | Attribute applied to the wrong member type | good message, was wrapped in `TargetInvocationException`, unwrap fixed this session (seen) |
| RINKU5002 | Generated ctor not found | `"the ctor for {T} with {n} arguments cannot be found"`, 2 sites |

### Internal, 9000-9999

| code | condition | today |
| --- | --- | --- |
| RINKU9001 | Unreachable state | `"should not happend"`, `UnreachableException` |
| RINKU9002 | Checkpoint length mismatch | `"must be the same length expected:{a} actual:{b}"`, 2 sites |

These should say plainly that reaching them is a library bug and ask for a report. They are not user
errors and should not read like one.

## Doc page shape

Eventual home: `docs/articles/reference/errors.md`, one anchor per code so `HelpLink` can point at it.
Per the docs style, show the failing case before explaining it.

````markdown
## RINKU2001, no construction path satisfies the schema

The negotiation tried every construction path on the type and none of them could be filled from the
columns the query returned.

```csharp
public record Track(int Id, string Name, int Code);   // Code has no default

// Columns: Id | Name
var track = cmd.Query<Track>(cnn);   // RINKU2001
```

`Code` is required and no column matches it, so no path is satisfiable.

Ways out:

- Give the slot a default equal to the type default, `int Code = 0`, which makes it optional.
- Return the column the slot needs.
- Attach an `IFallbackParserGetter` to supply the value, see [construction paths](...).
````

Each entry: what happened, the smallest reproduction, why, then the ways out. The "ways out" section is
the part that makes the code worth having.

## Migration order

Ordered so each step is independently shippable and testable.

1. Add `RinkuException` base, the `Code` property, and the doc-anchor `HelpLink`. Nothing throws it yet.
2. RINKU1001, the connection guard. 27 sites to one helper. Largest cleanup, lowest risk, already has
   test coverage from this session in `NoConnectionTests`.
3. RINKU2001 and RINKU2003, the two most common mapping failures. `Refusals.NoParserFor` already routes
   ten test sites through one seam, so this step touches one test file.
4. Reparent `NullValueAssignmentException` and `RequiredHandlerValueException`, assign codes.
5. Query template family, 0001-0008.
6. Registration family, after the RINKU4001 decision.
7. Review the raw throws case by case against principle 3, coding only the ones whose message cannot tell
   the caller what they did wrong. Expect most to stay as they are.
8. Internal family, and fix the `"should not happend"` typo on the way past.
9. Write `errors.md` as the codes land, not after.

## Test impact

`RinkuLib.Tests/Infrastructure/Refusals.cs` exists to absorb this rework. It asserts the shape of a
refusal, that it happened and that it names the thing that failed, not the exact wording. Ten sites route
through it today.

When codes land, `Refusals` gains a code check and the ten sites keep working unchanged:

```csharp
public static Exception NoParserFor<T>(Action build) {
    var ex = Assert.ThrowsAny<Exception>(build);
    Assert.Contains(TypeToken(typeof(T)), ex.Message);
    return ex;
}
```

becomes an assertion on `RinkuMappingException` and `Code == "RINKU2001"`.

Tests that still assert message fragments directly, and will need a pass when the wording changes:

- `NoConnectionTests`, the `NoConnection` constant, one place
- `QueryTests`, `"No values were returned"` and `"more result than required"`
- `ParserQueryRoadsTests` and `CollectionAndWrapperTests`, `"more result than required"`
- `RegistrationApiTests`, three `ValidateCanUseType` fragments
- `HandlerRenderingTests`, `"_Q"` and `"@V"`
- `AccessorEmitterVariantTests`, attribute and type names

All of these were chosen to assert short semantic fragments rather than whole messages, so a rewording
that keeps naming the offending thing will not break them.

## Open decisions

1. **RINKU4001 exception type.** `ValidateCanUseType` throws `InvalidOperationException` from the base
   and ctor infos and `ArgumentException` from the dyna info, for the same class of failure. Two failing
   tests in `RegistrationApiTests` state the two options, `ValidateCanUseType_refuses_with_an_InvalidOperationException`
   and `ValidateCanUseType_refuses_with_an_ArgumentException`. Both fail today. Pick one, align the infos,
   delete the other test.
2. **Do the new types keep deriving from the closest BCL type?** `RinkuRegistrationException : ArgumentException`
   preserves existing catch blocks; deriving everything from `RinkuException : Exception` is cleaner but
   is a breaking change for anyone catching `ArgumentException` today.
3. **Does `Code` go in the message text?** Prefixing `RINKU2001: ` makes logs greppable and makes the doc
   findable from a stack trace alone. It also means every message-fragment assertion in the tests keeps
   working, since they match on fragments rather than prefixes.
4. **`MakeInfoAt` bad index.** Currently `ArgumentOutOfRangeException` from the underlying collection,
   bypassing the library's own `"there is no valid parameter at index {i}"` guard. Decide whether a bad
   index and a present-but-invalid parameter are one condition or two.

## Not yet audited

The test audit that produced this plan covered roughly half the `Assert.ThrowsAny<Exception>` sites.
Still uninstrumented, and therefore possibly hiding conditions missing from the catalog above:

- `ParserQueryRoadsTests`, the `BadCmd` cluster, 6 sites
- `LegacyReaderTests`, 3 sites
- `CommandsSurfaceTests`, 3 sites
- `AccessorEmitterVariantTests:165`, `CasterTests:305`, `RegistrationApiTests:434` and `:439`

Instrumenting these before assigning final codes is worth the pass. The method that found the three bugs
this session: replace the assertion with a capture of the actual exception type and message, run, then
compare what is thrown against what the test name claims.
