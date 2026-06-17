# Investigation: SQLQuery component (`$sqlquery-run` / `$sqlquery-export`)

**Feature**: sql-on-fhir
**Status**: In Progress
**Created**: 2026-06-17

## Scope

Support the **SQLQuery** component added to SQL on FHIR v2 in v2.1.0-pre. This is
*entirely new* surface for Ignixa — zero implementation exists today. It is
distinct from ViewDefinition: ViewDefinition *flattens* a resource into rows;
SQLQuery *runs SQL* (joins, aggregates, filters, parameters) **across multiple
ViewDefinition outputs treated as tables**.

Three new artifacts in the spec:

1. **`SQLQuery` profile** — a `Library` (type `sql-query`) holding one SQL query,
   its ViewDefinition dependencies (`relatedArtifact` `depends-on` + `label` =
   table name), declared `parameter`s (`use=in`), and the SQL itself (base64 in
   `content.data`, plain text in the `sql-text` extension). Plus `@annotation`
   comment syntax for generating these Libraries from `.sql` files.
2. **`$sqlquery-run`** — synchronous execution. System / type / instance level
   (`POST [base]/$sqlquery-run`, `/Library/$sqlquery-run`,
   `/Library/{id}/$sqlquery-run`). Returns `Binary` for `json|ndjson|csv|parquet`,
   or a FHIR `Parameters` resource (rows as repeating `row` parameters) for
   `_format=fhir`.
3. **`$sqlquery-export`** — asynchronous variant (live build, ahead of our pin).

## The Central Problem

**Ignixa is not a SQL engine.** `SqlOnFhirEvaluator` is an in-memory FHIRPath row
flattener. `$sqlquery-run` requires executing arbitrary SQL — `SELECT ... FROM
patient JOIN bp ON ... WHERE x = :param` — against tables that are the
materialized outputs of ViewDefinitions. Nothing in the current pipeline can do
that. This is the architectural fork the investigation exists to resolve.

Execution flow the spec mandates:

```
resolve SQLQuery Library
  → for each relatedArtifact depends-on: resolve ViewDefinition
  → materialize each ViewDefinition output as a TABLE named after `label`
  → bind `parameters` (nested Parameters resource) to :name placeholders  ← parameterized, no string interp
  → execute the Library's SQL against those tables
  → return rows (Binary | Parameters with SQL→FHIR type mapping)
```

## Approach Options (the SQL engine fork)

### Option 1 — Embedded DuckDB (in-process)

Materialize ViewDefinition rows into DuckDB (in-memory or temp file), run the
Library SQL there, stream results out.

| Pros | Cons |
|------|------|
| Purpose-built for analytics SQL over columnar/flat data — the ecosystem default | New native dependency (DuckDB.NET); platform/packaging surface |
| Reads Parquet/Arrow directly — our writers already produce Parquet | Another SQL dialect to document for `content.contentType;dialect=` variants |
| In-process, no external infra; matches the "ViewDefinition runner" persona | Memory pressure for large materializations (mitigate: temp-file mode) |
| Strong parameterized-query support (injection guard for free) | |

### Option 2 — Reuse SQL Server (existing DataLayer)

Materialize ViewDefinition output into temp tables in the existing SQL Server,
run the Library SQL there via parameterized `SqlCommand`.

| Pros | Cons |
|------|------|
| No new engine dependency; reuses connection/auth infra | T-SQL dialect ≠ portable SoF SQL; clients writing ANSI SQL hit dialect drift |
| Familiar operational story | Couples an analytics feature to the transactional store (contention, tenancy) |
| | Temp-table lifecycle + multi-tenant isolation is fiddly |
| | Cosmos backend gets nothing — feature becomes SQL-Server-only |

### Option 3 — SQLite (mirror the reference implementation)

What upstream `sof-js` does: materialize into SQLite temp tables, execute, return.

| Pros | Cons |
|------|------|
| Byte-for-byte alignment with the reference impl → easiest conformance | SQLite SQL dialect; weaker analytics performance at scale |
| Tiny embedded dependency, trivial parameterization | Not a long-term analytics engine; likely a stepping stone |
| Fast path to passing the official `$sqlquery-run` tests | |

### Cross-cutting (any option)

- **SQLQuery Library storage/resolution** — `queryReference` and the dependent
  `relatedArtifact` ViewDefinitions must be resolvable. Shared decision with the
  [HTTP API investigation](http-api-operations.md). Library *is* a real FHIR
  resource (unlike ViewDefinition), so package-management canonical storage is a
  natural fit.
- **SQL→FHIR type mapping** for `_format=fhir` (ISO 9075 → `value[x]`): integer
  family → `valueInteger`/`valueInteger64`, decimal/real/float → `valueDecimal`,
  char → `valueString`, date/time/timestamp → `valueDate`/`valueTime`/
  `valueDateTime`/`valueInstant`, etc. Unsupported types → `422`. NULL → omit part.
- **Parameter binding alignment** — the spec moved (`align-sqlquery-run-with-cql`)
  from custom `parameter` parts to a nested `Parameters` resource (CQL `$evaluate`
  style). Implement the *new* shape; the pinned submodule still shows the old one.
- **Annotation builder** (`@name`, `@param`, `@relatedDependency`, ...) — optional
  tooling to generate SQLQuery Libraries from annotated `.sql`. Lower priority; the
  CLI is the natural home, not the server.

## Tradeoffs (feature-level)

| Pros | Cons |
|------|------|
| Unlocks real analytics (joins/aggregates across views) — the headline SoF v2.1 capability | Largest net-new surface in this feature area; a genuine engine decision |
| Library is a real FHIR resource → storage/resolution is tractable | Arbitrary SQL execution is a serious security boundary (injection, resource exhaustion, multi-tenant isolation) |
| Option 1/3 keep it in Core, off the transactional store | Dialect management (`content` variants) is ongoing maintenance |
| ViewDefinition materialization reuses the existing evaluator + Parquet writer | `$sqlquery-export` (async) compounds with the unsettled `unify-async` 303 work |

## Alignment

- [x] Follows layer rules (engine in Core; operation via `*Endpoints.cs` + handler)
- [x] Developer Experience (inline `queryResource` + inline data = zero-setup ad-hoc SQL)
- [x] FHIR spec compliance (new normative component)
- [ ] Consistent with existing patterns — **introduces a new dependency class (a SQL engine)**; not consistent by definition, hence the option analysis

## Weakest-Link Analysis

1. **SQL execution security** — the least reliable component is arbitrary
   client-supplied SQL. Parameter binding MUST be parameterized (spec: string
   interpolation forbidden). Beyond injection: query timeouts, row/memory caps,
   and per-tenant isolation of materialized tables. Get this wrong and it's the
   whole system's reliability ceiling.
2. **Materialization cost** — every `$sqlquery-run` re-flattens all dependent
   ViewDefinitions before the SQL runs. For large data this dominates latency. A
   materialization cache / `$sqlquery-export` async path is the pressure valve.
3. **Dialect fidelity** — if the chosen engine's dialect diverges from what
   clients write, queries silently misbehave. The upstream `sql-portability`
   skill (sqlglot transpilation) exists precisely for this; lean on it.

## Reversibility

Medium-low. The engine choice is the sticky decision — it shapes packaging,
performance, and dialect docs. Mitigation: isolate it behind an
`ISqlQueryEngine` abstraction in Core so Option 3 (SQLite, conformance-first) can
be swapped for Option 1 (DuckDB, performance) without touching the operation
layer. Choosing the abstraction now is cheap; choosing the wrong concrete engine
without one is expensive.

## Evidence

- Spec: `StructureDefinition-SQLQuery-intro.md` / `-notes.md` (profile, aliasing,
  parameters, annotations), `OperationDefinition-SQLQueryRun-intro.md` / `-notes.md`
  (endpoints, execution flow, SQL→FHIR type mapping, error contract).
- Upstream in-flight: `openspec/changes/improve-sqlquery-profile`,
  `implement-sqlquery-run-server` (reference server uses SQLite temp tables — issue
  #332), `align-sqlquery-run-with-cql` (Parameters-resource params — issue #318).
- Live build v2.1.0-pre adds `$sqlquery-export` (async) beyond our submodule pin.
- Reference impl: Brian Kaney's `sql-fhir-library-builder`; reference server
  `sof-js/src/server/` (materialize → SQLite → format).
- Code: no SQLQuery anywhere (grep clean); `SqlOnFhirEvaluator` + Parquet writer
  are the materialization building blocks.

## Verdict

*Pending evaluation.* Leaning: **`ISqlQueryEngine` abstraction in Core, SQLite
first** (Option 3) to land conformance against the official tests cheaply, with
**DuckDB (Option 1) as the production engine** behind the same interface. Reject
Option 2 (SQL Server) — coupling analytics SQL to the transactional store loses
the Cosmos backend and invites contention/dialect problems. Resolve Library/
ViewDefinition storage jointly with the [HTTP API investigation](http-api-operations.md).
Defer the `@annotation` builder and `$sqlquery-export` to follow-ups.
