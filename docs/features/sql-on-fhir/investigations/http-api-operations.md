# Investigation: SQL-on-FHIR HTTP API (server operations)

**Feature**: sql-on-fhir
**Status**: In Progress
**Created**: 2026-06-17

## Scope

Enhance the **HTTP API** chapter of SQL on FHIR v2 — the normative server
operations — to spec conformance. This is distinct from the [SQLQuery
component](sqlquery-component.md). The spec defines three things a conformant SoF
server exposes:

1. **`$viewdefinition-run`** (a.k.a. `$run`) — synchronous, real-time evaluation
   of a ViewDefinition. Type level (`POST/GET [base]/ViewDefinition/$run`) and
   instance level (`.../ViewDefinition/{id}/$run`).
2. **`$viewdefinition-export`** — asynchronous bulk export of one or more
   ViewDefinitions to CSV/NDJSON/Parquet, using the simplified async pattern
   (`202 Accepted` → poll → **`303 See Other`** → result URL).
3. **CapabilityStatement** advertising the supported operations, output formats,
   reference formats, and known ViewDefinitions.

## Current State

- The evaluator (`SqlOnFhirEvaluator`) and writers (CSV/NDJSON/Parquet) already
  produce exactly the output these operations need. The gap is the **HTTP edge**,
  not the engine.
- The only server surface today is `$export` with an optional `_viewDefinition`
  parameter, restricted to `_outputFormat=application/fhir+parquet`
  (`ExportEndpoints.cs:154-165`). ViewDefinitions are resolved from blob via
  `Ignixa.DataLayer.BlobStorage/ViewDefinitionLoader.cs` during the background job.
- There is **no** `ViewDefinition/$run`, no spec-shaped `$viewdefinition-export`
  operation envelope, no `Parameters`-based invocation, no OperationOutcome error
  contract, and **no CapabilityStatement entry**. A conformance client pointed at
  Ignixa cannot discover or invoke SoF.

## Spec Requirements (the contract to hit)

### `$viewdefinition-run`
- **Invocation**: GET (query params only) and POST (`Parameters` body). Type +
  instance level. Instance level infers `viewReference` from the path.
- **Inputs**: `viewReference` *xor* `viewResource` (type level; neither at
  instance level), `_format` (`json|ndjson|csv|parquet`), `header` (CSV),
  `patient`, `group`, `_since`, `_limit`, `resource` (inline data, alternative to
  server data), `source` (external).
- **viewReference resolution**: relative URL, canonical (`url|version`), or
  absolute. Server documents which it supports in CapabilityStatement.
- **Output**: `Binary` (`return`) in the requested format; MAY use chunked
  transfer encoding for large sets; JSON form is an array of objects.
- **Errors**: `200/400/404/422/500`, each 4xx/5xx carrying an `OperationOutcome`
  (e.g. unsupported param → 400 `not-supported`; bad ViewDefinition → 422
  `invalid`; missing view → 404).

### `$viewdefinition-export`
- Async: `Prefer: respond-async` → `202` + `Content-Location` status URL.
- Poll: `202` while running (MAY include interim), **`303 See Other` +
  `Location`** on completion (the `unify-async` redirect-only pattern — replaces
  inline-result polling).
- Multi-view input, output to file storage, `OutputFormatCodes` value set.
- Cancellation via `DELETE` on the status URL (normativity still being settled
  upstream — `unify-async/review.md`).

### CapabilityStatement
- `/metadata` advertises the operations, supported `_format` values, supported
  reference formats, and discoverable ViewDefinitions.

## Approach

Build a dedicated `ViewDefinitionEndpoints.cs` (Minimal API, per layer rules)
backed by Medino handlers, reusing the export orchestration that already exists.
Three sub-pieces, sequenced by effort and value:

### Phase A — `$viewdefinition-run` (synchronous)

```
POST /ViewDefinition/$run          ← Parameters{viewResource|viewReference, _format, filters, resource[]}
GET  /ViewDefinition/{id}/$run     ← infers viewReference, query-param filters
        ↓ ViewDefinitionRunEndpoints (API)
        ↓ RunViewDefinitionQuery / Handler (Application)
        ↓ resolve view → SqlOnFhirEvaluator.EvaluateBatch → IFileWriter(format) → stream
```

- **Reuse**: `SqlOnFhirEvaluator`, the three writers, the FHIR `Parameters`
  parsing already used by other operations, and the search/compartment layer for
  `patient`/`group`/`_since`/`_limit` (these map onto existing search predicates —
  do **not** reinvent compartment logic).
- **New**: `Parameters` ↔ run-request mapping, `viewReference` resolution (see
  open question), format/Accept negotiation, streaming `IResult`, and the
  OperationOutcome error contract.
- **`resource` (inline data) path**: when the client supplies resources in the
  body, bypass storage entirely and evaluate in-memory — this is the
  authoring/debugging use case and is the cheapest correctness win.

### Phase B — `$viewdefinition-export` (spec-shaped async)

- Wrap the **existing** export orchestration (`ExportOrchestration`,
  DurableTask) in the spec's operation envelope. Lift the Parquet-only
  restriction to CSV/NDJSON/Parquet (writers already exist).
- Implement the **303 redirect** completion pattern. This is a *new* status-endpoint
  shape; align it with — but keep distinct from — the FHIR Bulk Data `$export`
  status endpoint (which returns `200` on completion). Watch the open upstream
  items in `unify-async/review.md` (result-URL lifetime, cancellation normativity)
  before committing to normative wording.

### Phase C — CapabilityStatement + conformance harness

- Advertise operations/formats/reference-formats in `/metadata`.
- Wire the spec's HTTP test cases (`sof-js/tests/server/*`) into the existing test
  project so the server surface is covered, not just the evaluator.

## Tradeoffs

| Pros | Cons |
|------|------|
| Engine + writers already exist — Phase A is mostly an HTTP adapter | `viewReference` resolution forces a ViewDefinition storage/registry decision |
| `$export` orchestration already exists — Phase B is an envelope + format unlock | 303 async pattern is still in flux upstream; risk of churn if we adopt early |
| Filters reuse existing search/compartment code | `patient`/`group` semantics are SHALL-strict (must not leak out-of-compartment rows) — needs careful test coverage |
| Closes the conformance gap that makes Ignixa undiscoverable as a SoF server | New public HTTP surface = new security/authorization surface (multi-tenancy, partition 0 rules apply) |
| Inline-`resource` path needs no storage and lands the debugging use case fast | Streaming large results through Minimal API needs backpressure care |

## Alignment

- [x] Follows layer rules (`*Endpoints.cs` → Medino handler → Core engine)
- [x] Developer Experience (inline-`resource` `$run` works with zero setup)
- [x] FHIR spec compliance (this *is* the conformance work)
- [x] Consistent with existing patterns (reuses export orchestration, writers, search)

## Weakest-Link Analysis

1. **viewReference / ViewDefinition storage** — the operations assume the server
   can resolve a stored ViewDefinition. Today they live in blob
   (`ViewDefinitionLoader`) for export. Options: (a) keep blob/artifact registry
   and resolve canonical+version there; (b) store ViewDefinitions as canonical
   resources via package management. This decision is shared with the SQLQuery
   investigation (which needs the same for `queryReference`) — resolve it once.
2. **Async 303 pattern stability** — adopting normative wording before upstream
   settles (`unify-async`) risks rework. Mitigation: implement behaviour, keep the
   conformance assertions behind the pinned submodule version, bump deliberately.
3. **Compartment leakage** — `patient`/`group` are SHALL-strict. The weakest link
   is silently returning rows outside the requested compartment. Must be tested
   adversarially.

## Reversibility

High for Phase A (a new endpoint; remove if unused). Medium for Phase B (the 303
contract is client-visible — versioned via CapabilityStatement). Low cost to be
wrong on viewReference storage *if* we share the decision with SQLQuery rather
than solving it twice.

## Open Questions

- Where do stored ViewDefinitions live, and how is canonical+version resolved?
  (Shared with [sqlquery-component](sqlquery-component.md).)
- Adopt the 303 async pattern now, or ship Phase B against the current pinned
  inline-result pattern and migrate when upstream finalizes?
- Do we expose `$run` at system level for inline-`resource` only (no stored view),
  matching the authoring use case, before tackling stored-view resolution?

## Evidence

- Spec: `input/pagecontent/operations.md`,
  `OperationDefinition-ViewDefinitionRun-notes.md` (full param tables, error
  contract, examples), `unify-async/review.md` (303 pattern + open items).
- Live build v2.1.0-pre artifacts: `$viewdefinition-run`, `$viewdefinition-export`
  (confirmed via `artifacts.html`).
- Code: `ExportEndpoints.cs` (current `_viewDefinition` param, Parquet-only),
  `ViewDefinitionLoader.cs` (current resolution), `SqlOnFhirEvaluator`, writers.

## Verdict

*Pending evaluation.* Recommended ordering: **Phase A inline-`resource` `$run`**
(fastest conformance + DX win, no storage dependency) → resolve ViewDefinition
storage → Phase A stored-view resolution → Phase B export envelope → Phase C
CapabilityStatement + HTTP conformance tests.
