# Investigation: SQL-on-FHIR v2.1 conformance — 13 failing tests

**Feature**: sql-on-fhir
**Status**: In Progress
**Created**: 2026-06-17

## Context

The conformance submodule was re-pointed from `FHIR/sql-on-fhir-v2` (the IG repo, which
removed its embedded suite) to `FHIR/sql-on-fhir.js` (the reference implementation, where the
shared suite now lives), pinned at `70c0566`. The suite grew from 134 → **144** tests.

Running the existing evaluator against it: **131/144 pass, 13 fail**. The failures are recorded
honestly in `test_report.json` (the published conformance number is real), but are listed in the
`KnownConformanceFailures` allowlist in `OfficialSqlOnFhirTestRunner` so the build gate stays green.
This investigation scopes closing those 13 so the allowlist can be emptied.

> The allowlist is xfail, not suppression: a listed case that starts passing **fails** the build
> (forcing its removal), and a not-listed failure fails the build (catches regressions). The report
> always shows the true pass/fail.

## Approach

Fix the 13 in the Core evaluator (not the test harness). They cluster into three independent gaps,
each fixable on its own and worth a separate task/PR:

### 1. `repeat` semantics — 9 failures (the meaty one)
`repeat.json`: `repeat inside forEach`, `repeat inside repeat`, `repeat inside forEachOrNull`,
`sibling repeats at top level`, `sibling repeats inside forEach`,
`top-level repeat with sibling forEach containing repeat`,
`forEach with repeat with forEach (triple nesting)`, `repeat inside repeat inside repeat`,
`multi-path repeat inside forEach`.

Symptoms: we **over-produce rows** (`expected 2 got 3`, `expected 1 got 5`, `expected 8 got 12`)
and **mis-index siblings** (`linkIdA` expected `g1`, got `g1.1`). The v2.1 suite added 12 `repeat`
cases (7 → 19) that pin down nesting/sibling/cartesian behavior our implementation predates.

Core location: `src/Core/Ignixa.SqlOnFhir/Evaluation/SqlOnFhirEvaluationVisitor.cs` (the `select.repeat`
handling). Likely root cause: how `repeat` expands and how its rows combine with sibling `select`/
`forEach` groups (cross-join cardinality) and how `%rowIndex`-style indexing applies under nesting.

### 2. `join()` over an empty collection — 3 failures
`fn_join.json`: `join with comma`, `join with empty value`, `join with no value - default to no separator`.

Symptom: spec/reference expects `null` for a join over an empty input; we emit empty string `''`
(`expected 'null', got ''`). A targeted fix in the FhirPath `join` implementation: empty input →
empty result (null cell), not `""`.

Core location: `src/Core/Ignixa.FhirPath/Evaluation/Functions/CollectionFunctions.cs`.

### 3. `highBoundary()` on a time — 1 failure
`fn_boundary.json`: `time highBoundary`. Symptom: `expected '12:34:00.999', got '12:34:59.999'`.
We fill the seconds component to `59.999` where the spec expects the boundary at `00.999` for the
given precision — a precision/units bug in the time branch of `highBoundary`.

Core location: `src/Core/Ignixa.FhirPath/Evaluation/Functions/BoundaryFunctions.cs`.

## Tradeoffs

| Pros | Cons |
|------|------|
| Closes real spec-conformance gaps; raises published number toward 144/144 | `repeat` is non-trivial; risk of regressing the 131 currently-passing cases |
| Each gap is independent — can land as 3 small TDD PRs | Touches Core (FhirPath + SqlOnFhir), broader blast radius than the test-only report PR |
| xfail allowlist already encodes the exact targets; report proves progress | Upstream suite is still moving (v2.1.0-pre); pin may need re-bumping mid-fix |

## Alignment

- [x] Follows architectural layering rules (fixes live in Core, consumed by the evaluator)
- [x] Developer Experience (the allowlist + report make remaining gaps visible at a glance)
- [x] Specification compliance (the entire point — conform to the shared suite)
- [x] Consistent with existing patterns (FhirPath functions, evaluation visitor)

## Evidence

- Re-point + run measured 131/144 at `sql-on-fhir.js@70c0566`; the 13 failures and their reasons
  come straight from the generated `test_report.json`.
- Same 13 reproduced at two different upstream `main` commits — they are stable engine gaps, not
  test flake or suite churn.
- The reference behavior is encoded in the suite JSON (`tests/repeat.json`, `tests/fn_join.json`,
  `tests/fn_boundary.json`) and the reference engine `sof-js/src/index.js`; use these as the oracle
  when writing the failing tests first.
- Existing `repeat` support is documented in the feature readme as "✅ `select.repeat`" against the
  *older* suite — i.e. it passed v2.0 but not the clarified v2.1 cases. This is conformance polish,
  not greenfield.

## Suggested execution (separate tasks, TDD per the upstream constitution)

1. **`join()`-empty → null** (smallest, 3 tests) — quick win, isolates the FhirPath fix.
2. **`highBoundary()` time precision** (1 test) — isolated FhirPath fix.
3. **`repeat` nesting/sibling semantics** (9 tests) — the substantive one; do last, with the suite
   cases driving it. May warrant its own sub-investigation if the row-combination model needs rework.

As each task lands, delete the corresponding entries from `KnownConformanceFailures`; the xfail guard
fails the build if a fixed case is left listed, keeping the allowlist honest.

## Verdict

*Pending evaluation* — scoping complete; the 13 are well-understood and partitioned into 3
independent fixes. Recommend starting with `join`/`highBoundary` (4 tests, low risk) to validate the
workflow, then tackling `repeat`.
