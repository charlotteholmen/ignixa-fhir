# ViewDefinition/$viewdefinition-export Operation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose the SQL-on-FHIR v2 `ViewDefinition/$viewdefinition-export` operation as an asynchronous bulk export of a ViewDefinition to CSV/NDJSON/Parquet, reusing Ignixa's existing `$export` orchestration and 202→poll→200 status pattern.

**Architecture:** A new endpoint group (`ViewDefinitionExportEndpoints`) accepts the FHIR `Parameters` invocation (a stored `viewReference` + `_outputFormat` + optional `_since`/`patient`/`group`), maps it onto the **existing** `CreateExportJobCommand` (which already carries `ViewDefinitionId`) and DurableTask `ExportOrchestration`, and returns `202 Accepted` + `Content-Location` pointing at the **existing** export status endpoint. The only engine change is teaching the export worker to emit CSV and NDJSON for ViewDefinition jobs — today it is Parquet-only.

**Tech Stack:** .NET 9, Minimal API, Medino, DurableTask, `IExportStreamWriter` + `IBlobStorageClient`, `Ignixa.SqlOnFhir` evaluator, xunit + Shouldly + NSubstitute.

## Global Constraints

- Reuse the existing async pattern: `202` + `Content-Location` → `GET .../_export/{jobId}` returns `202` (running) or `200` with output manifest. **No 303 redirect.**
- No `Hl7.Fhir.*` in Application/DataLayer.
- Async methods take `CancellationToken cancellationToken`.
- One type per file; file-scoped namespaces; 4-space indent; nullable; warnings-as-errors.
- Existing format constants live in `Ignixa.Domain.Constants.ExportConstants`: `MediaTypeNdjson = "application/fhir+ndjson"`, `MediaTypeParquet = "application/vnd.apache.parquet"`. CSV constant is added in Task 1.
- ViewDefinition export resolves a **stored** view via `ViewDefinitionLoader` (by id). Inline `viewResource` for async export is **out of scope** (an async job would have to persist the inline view first) — documented in Task 7.
- Multi-view single-job export is **out of scope** for core tasks (the existing job model carries a single `ViewDefinitionId`); single-view per job. Multi-view is a follow-up in Task 7.
- Partition 0 blocked by `TenantResolutionMiddleware`; do not special-case.

## File Structure

- `src/Core/Ignixa.Abstractions/ExportConstants.cs` — add CSV media type (modify)
- `src/Application/Ignixa.Application.BackgroundOperations/Export/ExportOutputFormat.cs` — format-code ↔ media-type mapping (create)
- `src/DataLayer/Ignixa.DataLayer.BlobStorage/ViewDefinitionNdjsonExportStreamWriter.cs` — ViewDefinition → NDJSON (create)
- `src/DataLayer/Ignixa.DataLayer.BlobStorage/ViewDefinitionCsvExportStreamWriter.cs` — ViewDefinition → CSV (create)
- `src/Application/Ignixa.Application.BackgroundOperations/Export/Activities/ExportWorkerActivity.cs` — writer selection by format (modify)
- `src/Application/Ignixa.Api/Endpoints/ViewDefinitionExportEndpoints.cs` — HTTP surface (create)
- Tests mirror under `test/Ignixa.DataLayer.BlobStorage.Tests/`, `test/Ignixa.Application.Tests/`, `test/Ignixa.Api.Tests/`

> **Current state (from code audit):** `ViewDefinitionExportStreamWriter` (Parquet) wraps `ParquetExportStreamWriter`, which evaluates the ViewDefinition via `SqlOnFhirEvaluator.Evaluate(...)` and buffers `Dictionary<string,object?>` rows. The CSV/NDJSON writers in this plan reuse that exact evaluation step but serialize text instead of Parquet. `ExportWorkerActivity` selects the writer; today it picks `ViewDefinitionExportStreamWriter` whenever `ViewDefinitionId` is set (`ExportWorkerActivity.cs:98-126`).

---

### Task 1: CSV media type + output-format mapping

**Files:**
- Modify: `src/Core/Ignixa.Abstractions/ExportConstants.cs`
- Create: `src/Application/Ignixa.Application.BackgroundOperations/Export/ExportOutputFormat.cs`
- Test: `test/Ignixa.Application.Tests/Export/ExportOutputFormatTests.cs`

**Interfaces:**
- Produces: `ExportConstants.MediaTypeCsv`; `ExportOutputFormat.ToMediaType(string code)` and `ExportOutputFormat.FileExtension(string mediaType)` consumed by Tasks 3, 4.

- [ ] **Step 1: Write the failing test**

```csharp
using Ignixa.Application.BackgroundOperations.Export;
using Ignixa.Domain.Constants;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Export;

public class ExportOutputFormatTests
{
    [Theory]
    [InlineData("csv", "text/csv")]
    [InlineData("ndjson", "application/fhir+ndjson")]
    [InlineData("parquet", "application/vnd.apache.parquet")]
    public void GivenSpecFormatCode_WhenMappingToMediaType_ThenReturnsExpected(string code, string expected)
        => ExportOutputFormat.ToMediaType(code).ShouldBe(expected);

    [Fact]
    public void GivenUnknownCode_WhenMapping_ThenThrowsBadRequest()
        => Should.Throw<BadRequestException>(() => ExportOutputFormat.ToMediaType("xml"));

    [Theory]
    [InlineData("text/csv", ".csv")]
    [InlineData(ExportConstants.MediaTypeNdjson, ".ndjson")]
    [InlineData(ExportConstants.MediaTypeParquet, ".parquet")]
    public void GivenMediaType_WhenFileExtension_ThenReturnsExpected(string mediaType, string expected)
        => ExportOutputFormat.FileExtension(mediaType).ShouldBe(expected);
}
```

- [ ] **Step 2: Run to verify it fails** — `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter ExportOutputFormatTests` → FAIL (types missing).

- [ ] **Step 3: Implement**

Add to `ExportConstants.cs`:
```csharp
/// <summary>MIME type for CSV output (SQL-on-FHIR flat export).</summary>
public const string MediaTypeCsv = "text/csv";
```

`ExportOutputFormat.cs`:
```csharp
using Ignixa.Domain.Constants;

namespace Ignixa.Application.BackgroundOperations.Export;

/// <summary>Maps SQL-on-FHIR OutputFormatCodes (json/ndjson/csv/parquet) to media types and file extensions.</summary>
public static class ExportOutputFormat
{
    public static string ToMediaType(string code) => code switch
    {
        "csv" => ExportConstants.MediaTypeCsv,
        "ndjson" => ExportConstants.MediaTypeNdjson,
        "parquet" => ExportConstants.MediaTypeParquet,
        _ => throw new BadRequestException($"Unsupported _outputFormat '{code}'. Supported: csv, ndjson, parquet")
    };

    public static string FileExtension(string mediaType) => mediaType switch
    {
        ExportConstants.MediaTypeCsv => ".csv",
        ExportConstants.MediaTypeNdjson => ".ndjson",
        ExportConstants.MediaTypeParquet => ".parquet",
        _ => throw new BadRequestException($"Unsupported output media type '{mediaType}'")
    };
}
```

> Confirm `BadRequestException`'s namespace while implementing (it's used in `CreateExportJobHandler`); add the `using`.

- [ ] **Step 4: Run to verify it passes.**

- [ ] **Step 5: Commit** — `git commit -m "feat(sof-export): csv media type + output-format mapping"`

---

### Task 2: ViewDefinition CSV/NDJSON export stream writers

**Files:**
- Create: `src/DataLayer/Ignixa.DataLayer.BlobStorage/ViewDefinitionNdjsonExportStreamWriter.cs`
- Create: `src/DataLayer/Ignixa.DataLayer.BlobStorage/ViewDefinitionCsvExportStreamWriter.cs`
- Test: `test/Ignixa.DataLayer.BlobStorage.Tests/ViewDefinitionTextExportStreamWriterTests.cs`

**Interfaces:**
- Consumes: `IExportStreamWriter` (the same interface `ViewDefinitionExportStreamWriter` implements — `WriteResourceAsync(SearchEntryResult, CancellationToken)`, `BytesWritten`, `IAsyncDisposable`), `SqlOnFhirEvaluator`, `IBlobStorageClient`, `ISchema`/schema provider.
- Produces: two writer classes selected in Task 3.

Both writers mirror `ParquetExportStreamWriter`'s evaluation (parse `resource.ResourceBytes` → `ToElement(schemaProvider)` → `evaluator.Evaluate(viewNavigator, element)` → rows) but serialize rows as text to a blob-backed stream instead of Parquet.

- [ ] **Step 1: Write the failing test** (feed two Patients through a Patient view, assert NDJSON line count / CSV header)

```csharp
[Fact]
public async Task GivenTwoResources_WhenNdjsonViewExport_ThenWritesTwoLines()
{
    // Arrange: in-memory IBlobStorageClient capturing the written stream;
    // viewNavigator parsed from a Patient ViewDefinition; schemaProvider = FhirVersion.R4.GetSchemaProvider().
    await using var writer = new ViewDefinitionNdjsonExportStreamWriter(
        blobStorage, "out.ndjson", viewNavigator, schemaProvider, NullLoggerFactory.Instance);

    // Act
    await writer.WriteResourceAsync(PatientEntry("p1"), CancellationToken.None);
    await writer.WriteResourceAsync(PatientEntry("p2"), CancellationToken.None);
    await writer.DisposeAsync();

    // Assert
    var text = capturedBlob.AsUtf8String().TrimEnd('\n');
    text.Split('\n').Length.ShouldBe(2);
}
```

- [ ] **Step 2: Run to verify it fails** — types missing.

- [ ] **Step 3: Implement** both writers implementing `IExportStreamWriter`. Reuse the evaluation block verbatim from `ParquetExportStreamWriter.WriteResourceWithViewDefinitionAsync` (`ParquetExportStreamWriter.cs:143-179`); replace the buffer/flush-to-Parquet with: NDJSON → `JsonSerializer.Serialize(row)` per line to the blob append stream; CSV → header from first row keys then escaped values (reuse the escaping rule from the run plan's `ViewDefinitionRowStreamFormatter`). Track `BytesWritten`. Open the blob output stream via `IBlobStorageClient` the same way `ParquetExportStreamWriter`'s constructor does.

> DRY note: the CSV escaping + NDJSON row serialization is identical to the sync `$run` formatter. If the run plan landed first, extract `RowTextSerializer` into a shared spot (`Ignixa.SqlOnFhir.Writers`) and call it from both. Otherwise inline here and leave a `// TODO unify with $run formatter` — flag in the PR.

- [ ] **Step 4: Run to verify it passes.**

- [ ] **Step 5: Commit** — `git commit -m "feat(sof-export): ViewDefinition CSV/NDJSON stream writers"`

---

### Task 3: Writer selection by OutputFormat in the worker

**Files:**
- Modify: `src/Application/Ignixa.Application.BackgroundOperations/Export/Activities/ExportWorkerActivity.cs` (the `ViewDefinitionId`-set branch, ~lines 98-126)
- Test: `test/Ignixa.Application.Tests/Export/ExportWorkerActivityViewDefinitionFormatTests.cs`

**Interfaces:**
- Consumes: `ExportWorkerInput` (carries `ViewDefinitionId`; needs the job's `OutputFormat` — thread it through if not already present), `ExportOutputFormat`, the three writers.

> **Prerequisite check:** `ExportWorkerInput` (per audit) does NOT carry `OutputFormat`. Add an `OutputFormat` field to `ExportWorkerInput` and populate it from `ExportCoordinatorInput.OutputFormat` in `ExportOrchestration` (`ExportOrchestration.cs:82-105`, where `ExportWorkerInput` is constructed). The file extension there must come from `ExportOutputFormat.FileExtension(...)` instead of the current hard-coded `fileExtension`.

- [ ] **Step 1: Write the failing test** — given `OutputFormat=text/csv` + a `ViewDefinitionId`, the activity selects the CSV writer (assert via a seam: factory method `CreateViewDefinitionWriter(format, ...)` returns the right concrete type).

- [ ] **Step 2: Run to verify it fails.**

- [ ] **Step 3: Implement** — extract `IExportStreamWriter CreateViewDefinitionWriter(string mediaType, string outputPath, ISourceNavigator viewDefNode, ISchema schemaProvider)` selecting:
  - `MediaTypeParquet` → `ViewDefinitionExportStreamWriter` (existing)
  - `MediaTypeNdjson` → `ViewDefinitionNdjsonExportStreamWriter`
  - `MediaTypeCsv` → `ViewDefinitionCsvExportStreamWriter`
  Replace the hard-coded Parquet writer construction in the `ViewDefinitionId` branch with this factory, keyed on `input.OutputFormat`.

- [ ] **Step 4: Run to verify it passes.**

- [ ] **Step 5: Build** `dotnet build All.sln` → 0/0. **Commit** — `git commit -m "feat(sof-export): select ViewDefinition writer by output format"`

---

### Task 4: ViewDefinitionExportEndpoints (async kickoff)

**Files:**
- Create: `src/Application/Ignixa.Api/Endpoints/ViewDefinitionExportEndpoints.cs`
- Modify: the `MapIgnixaEndpoints` aggregator — add `MapViewDefinitionExportEndpoints()`
- Test: `test/Ignixa.Api.Tests/Infrastructure/ViewDefinitionExportEndpointsTests.cs`

**Interfaces:**
- Consumes: `CreateExportJobCommand` (existing — `{TenantId, ResourceTypes, Since, TypeFilters, OutputFormat, ViewDefinitionId, GroupId}`), `IMediator`, `ParametersJsonNode`, `OperationOutcomeJsonNode`, `ExportOutputFormat`.

Routes (type + instance), POST only (Parameters body), with `Prefer: respond-async` honored:
```csharp
group.MapPost("/ViewDefinition/$viewdefinition-export", HandleExportTypeLevel);
group.MapPost("/ViewDefinition/{id}/$viewdefinition-export", HandleExportInstanceLevel);
```

Behaviour:
- Parse `Parameters`: `viewReference` (type level; the stored view id) or infer from `{id}` (instance level). Reject inline `viewResource` with 400 `not-supported` (out of scope — see constraints). Read `_outputFormat` (default `ndjson`), `_since`, `patient`, `group`.
- Map `_outputFormat` code → media type via `ExportOutputFormat.ToMediaType` (400 on unsupported).
- Build `CreateExportJobCommand` with `ViewDefinitionId` = resolved id, `OutputFormat` = media type, `ResourceTypes = []` (handler auto-derives from the view — `CreateExportJobHandler.cs:75-89`), `Since`, `GroupId`.
- `await mediator.SendAsync(command, ct)` → `result.JobId`. Return `202 Accepted` + `Content-Location: {scheme}://{host}/tenant/{tenantId}/_export/{jobId}` (the **existing** status endpoint — `ExportEndpoints.cs:341`). Mirror the existing `Results.Accepted(statusUrl, new { jobId, status = "queued" })` shape exactly.
- `BadRequestException` from the handler (e.g. view/resourceType mismatch) → 400 `OperationOutcome`.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task GivenStoredViewReference_WhenPostExport_ThenReturns202WithContentLocation()
{
    var parameters = """
    {"resourceType":"Parameters","parameter":[
      {"name":"viewReference","valueReference":{"reference":"ViewDefinition/patient-demo"}},
      {"name":"_outputFormat","valueCode":"csv"}
    ]}
    """;
    var response = await client.PostAsync("/tenant/1/ViewDefinition/$viewdefinition-export",
        new StringContent(parameters, Encoding.UTF8, "application/fhir+json"));

    response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    response.Headers.GetValues("Content-Location").First().ShouldContain("/tenant/1/_export/");
}
```

> The test harness must register a stubbed `IMediator` (or in-memory) returning a `CreateExportJobResult { JobId = "job-1", Status = "Queued" }` so the kickoff path is exercised without a real orchestration backend — follow the existing API test fixture for service stubbing.

- [ ] **Step 2: Run to verify it fails** — route not mapped → 404.

- [ ] **Step 3: Implement endpoints + register** `MapViewDefinitionExportEndpoints()`.

- [ ] **Step 4: Run to verify it passes.**

- [ ] **Step 5: Commit** — `git commit -m "feat(sof-export): ViewDefinition/$viewdefinition-export async kickoff"`

---

### Task 5: Status manifest content-type per format

**Files:**
- Modify: `src/Application/Ignixa.Api/Endpoints/ExportEndpoints.cs` — `BuildOutputManifestFromResultAsync` (~lines 434-461)
- Test: `test/Ignixa.Api.Tests/Infrastructure/ExportStatusManifestTests.cs`

The existing manifest emits `{ type, url }` per output file. For SQL-on-FHIR conformance, the completed-status manifest should also reflect the output `type` as the ViewDefinition/resource and is format-agnostic (URL extension already encodes format from Task 3). Confirm the manifest is returned with `200` on completion (existing behaviour, `ExportEndpoints.cs:360-375`).

- [ ] **Step 1: Write the failing test** — simulate a completed job whose result lists a `.csv` output; assert the status endpoint returns `200` and the manifest `output[].url` ends in `.csv`. (Stub `IBlobStorageClient.GetBlobUrlAsync` to echo the path.)

- [ ] **Step 2–4:** run-fail, implement any gap (likely none beyond confirming extension flows through), run-pass.

- [ ] **Step 5: Commit** — `git commit -m "test(sof-export): status manifest covers csv/ndjson outputs"`

---

### Task 6: Error + cancellation conformance tests

**Files:**
- Test: `test/Ignixa.Api.Tests/Infrastructure/ViewDefinitionExportErrorTests.cs`

Cover: unsupported `_outputFormat` (400 `not-supported`), inline `viewResource` rejected (400 `not-supported`), unknown view id surfaced from the handler (400/404 per `CreateExportJobHandler` behaviour), and `DELETE /tenant/{id}/_export/{jobId}` cancellation returns `204` (reuses existing `CancelExportAsync`, `ExportEndpoints.cs:406`).

- [ ] One `[Fact]` per case; assert status + `OperationOutcome.issue[0].code` where applicable. Run, green, commit `test(sof-export): error + cancellation coverage`.

---

### Task 7: CapabilityStatement + docs + follow-up notes

**Files:**
- Modify: CapabilityStatement builder; `docs/site/docs/core-sdk/sql-on-fhir.md`; `docs/features/sql-on-fhir/readme.md`

- [ ] Advertise `$viewdefinition-export` on `ViewDefinition` in `/metadata` with supported `_outputFormat` values (csv/ndjson/parquet). TDD: assert presence.
- [ ] Document the operation, the reuse of the `$export` 202→poll→200 pattern, and explicitly record the **deferred** items: inline `viewResource` async export, multi-view single-job export, and the `$sqlquery-export` async variant (separate component). Update the feature readme's `$viewdefinition-export` row to "Implemented (single stored view; csv/ndjson/parquet)".
- [ ] Commit `docs(sof): document $viewdefinition-export + deferred scope`.

---

## Self-Review

- **Spec coverage:** async kickoff + 202/Content-Location ✅ (T4), poll→200 manifest ✅ (reused, T5), csv/ndjson/parquet ✅ (T1-T3), `_since`/`patient`/`group` ✅ (T4, via existing command fields), cancellation ✅ (T6, reused), CapabilityStatement ✅ (T7). Deferred and **explicitly logged** (not silently dropped): inline-view async export, multi-view per job, `$sqlquery-export`.
- **Reuse integrity:** `CreateExportJobCommand`/`ExportOrchestration`/status+cancel endpoints unchanged except the additive `ExportWorkerInput.OutputFormat` field and writer-selection factory (T3). No change to the legacy `$export?_viewDefinition` route's Parquet-only validation — back-compat preserved.
- **Type consistency:** `ExportOutputFormat.ToMediaType/FileExtension` used identically in T3/T4; the three writer types named consistently T2↔T3; `OutputFormat` threaded coordinator→worker in T3.
- **Prerequisite flagged:** `ExportWorkerInput` lacks `OutputFormat` today — Task 3 adds it and updates the single construction site in `ExportOrchestration`. Without this, the worker can't pick a non-Parquet writer.

## Execution Handoff

Plan saved. Two execution options — **(1) Subagent-Driven (recommended)**: fresh subagent per task with review between; **(2) Inline Execution**: batch with checkpoints. Which approach?

> Sequencing across both plans: the `$run` plan's `ViewDefinitionRowStreamFormatter` (CSV escaping + NDJSON serialization) and this plan's Task 2 writers share serialization logic. If both are executed, do the `$run` plan first and extract a shared `RowTextSerializer` into `Ignixa.SqlOnFhir.Writers` so Task 2 reuses it (DRY).
