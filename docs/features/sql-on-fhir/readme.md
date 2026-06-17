# Feature: SQL on FHIR v2

**Status**: Partial Implementation
**Created**: 2026-06-17

## Problem Statement

[SQL on FHIR v2](https://build.fhir.org/ig/FHIR/sql-on-fhir-v2/) defines a tabular
projection of FHIR data (`ViewDefinition`) plus a normative HTTP API and, as of
v2.1.0-pre, a `SQLQuery` component for running SQL over those projections. Ignixa
has a mature **ViewDefinition evaluation engine** and a **CLI**, but almost no
**server-side HTTP surface** and **zero SQLQuery support**. This feature area
tracks closing those gaps and keeping pace with the moving spec.

## Current Implementation (2026-06-17)

| Component | State | Where |
|-----------|-------|-------|
| ViewDefinition evaluation engine | Substantive | `src/Core/Ignixa.SqlOnFhir/` |
| Output writers (CSV/NDJSON/Parquet) | Done | `src/Core/Ignixa.SqlOnFhir.Writers/` |
| CLI (`run`/`preview`/`validate`, batch) | Done | `tools/Ignixa.SqlOnFhir.Cli/` |
| Official conformance harness | Done (pinned v2.0.0 tests) | `test/Ignixa.SqlOnFhir.Tests/OfficialSqlOnFhirTestRunner.cs` |
| `$export` with `_viewDefinition` (Parquet only) | Partial | `src/Application/Ignixa.Api/Endpoints/ExportEndpoints.cs` |
| `$viewdefinition-run` server operation | **Missing** | — |
| `$viewdefinition-export` (spec-shaped, async 303) | **Missing** | — |
| CapabilityStatement advertising SoF | **Missing** | — |
| SQLQuery profile + `$sqlquery-run` / `$sqlquery-export` | **Missing** | — |

The submodule `test/.../sql-on-fhir-tests` is pinned at upstream `7765c2b`
(2026-04-29). The live continuous build is **v2.1.0-pre**, which is ahead of the
pin — notably it adds `$sqlquery-export` and the derived ViewDefinition profiles.

## Constraints

- ViewDefinition evaluation is **FHIRPath-based row flattening**, not SQL. Ignixa
  is *not* a SQL query engine today. SQLQuery (`$sqlquery-run`) needs a real SQL
  execution surface — this is the central architectural fork (see investigation).
- Layer rules apply: `Hl7.Fhir.*` must not leak into Application/DataLayer; server
  operations go through Minimal API `*Endpoints.cs` + Medino handlers.
- Arbitrary SQL execution (SQLQuery) is a security boundary — parameter binding
  MUST be parameterized; string interpolation is forbidden by the spec.
- ViewDefinition / Library are not first-class stored FHIR resources today;
  `viewReference`/`queryReference` resolution needs a storage/registry answer.

## ViewDefinition Coverage Cross-Check

Against the [functional model](https://build.fhir.org/ig/FHIR/sql-on-fhir-v2/StructureDefinition-ViewDefinition.html).
Verified against source, not just the model classes.

| Feature | Supported | Evidence |
|---------|-----------|----------|
| `resource`, `where`, `constant` | ✅ | `Models/ViewDefinition.cs`, `WhereClause.cs`, `ViewConstant.cs` |
| `select` (multiple groups), nested `select` | ✅ | `Parsing/ViewDefinitionExpressionParser.cs:188` |
| `column` name/path/type/description/`collection` | ✅ | `Models/ViewColumnDefinition.cs` |
| `column.tag` | ✅ | `Models/ColumnTag.cs`; parser :231 |
| `forEach`, `forEachOrNull` | ✅ | `Models/SelectGroup.cs:30,37` |
| `unionAll` | ✅ | `Expressions/ViewDefinitionExpression.cs:38` |
| `select.repeat` | ✅ | `SqlOnFhirEvaluationVisitor.cs:202` |
| `%rowIndex` | ✅ | `SqlOnFhirEvaluationVisitor.cs:127` (injected last, unshadowable) |
| `%resource` / root threading | ✅ | `SqlOnFhirEvaluationVisitor.cs:118` (`RootResource`) |
| `getResourceKey()` / `getReferenceKey()` | ✅ | `Ignixa.FhirPath/Evaluation/Functions/FhirSpecificFunctions.cs:157,207` |
| `constant` override via caller variables | ✅ | `SqlOnFhirEvaluator.Evaluate(..., variables)` |
| `fhirVersion`, `profile`, `where.description` | ⚠️ metadata only | `Models/ViewDefinition.cs:39,45` — parsed, not enforced |
| `column.isRequired` enforcement | ⚠️ metadata only | present in model; empty-value rejection not enforced |
| **ShareableViewDefinition** profile | ❌ | derived profile (declared `fhirVersion`) not implemented |
| **TabularViewDefinition** profile | ❌ | scalar-only constraint profile not implemented |
| v2.1.0-pre test suite | ❌ | embedded tests still v2.0.0 |

**Verdict on ViewDefinition:** the core engine is in good shape — the previously
suspected gaps (`getResourceKey`, `%resource`) are actually implemented in the
FhirPath layer. Remaining ViewDefinition work is *conformance polish* (derived
profiles, `isRequired` enforcement, refreshing the pinned test suite), not engine
gaps. The substantive new work is server-side, captured in the two investigations
below.

## Investigations

| Investigation | Status | Summary |
|--------------|--------|---------|
| [sqlquery-component](investigations/sqlquery-component.md) | In Progress | SQLQuery Library profile + `$sqlquery-run`/`$sqlquery-export`; requires a SQL execution engine |
| [http-api-operations](investigations/http-api-operations.md) | In Progress | `$viewdefinition-run` + spec-shaped `$viewdefinition-export` (async 303) + CapabilityStatement |

## Implementation Plans

| Plan | Operation | Notes |
|------|-----------|-------|
| [2026-06-17-sqlonfhir-viewdefinition-run](../../superpowers/plans/2026-06-17-sqlonfhir-viewdefinition-run.md) | `ViewDefinition/$run` | Sync streaming (json/ndjson/csv), inline + server data, full blob viewReference resolution. No pagination — streams `_limit`-capped results. |
| [2026-06-17-sqlonfhir-viewdefinition-export](../../superpowers/plans/2026-06-17-sqlonfhir-viewdefinition-export.md) | `ViewDefinition/$viewdefinition-export` | Async, reuses existing `$export` 202→poll→200 pattern; adds csv/ndjson to the Parquet-only ViewDefinition export path. |

## Related (pre-existing, now superseded by this area)

- `docs/features/search/investigations/sql-on-fhir.md` — stale 2025-11 planning doc (predates the implemented engine)
- `docs/features/serialization/investigations/viewdefinition-support.md` — serialization-layer view support

## Decision

*No ADR yet - investigations in progress*
