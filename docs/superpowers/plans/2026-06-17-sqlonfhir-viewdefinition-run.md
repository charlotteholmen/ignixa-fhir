# ViewDefinition/$run Operation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose the SQL-on-FHIR v2 `ViewDefinition/$run` operation as a synchronous, streaming FHIR server operation that flattens FHIR resources into tabular output (json/ndjson/csv).

**Architecture:** A new Minimal API endpoint group (`ViewDefinitionRunEndpoints`) parses the FHIR `Parameters` invocation, resolves the ViewDefinition (inline `viewResource`, or stored `viewReference`/instance-level via `ViewDefinitionLoader`), then a Medino handler (`RunViewDefinitionQuery`/`Handler`) feeds resources — either inline `resource` params or server data via the search-stream layer — through the existing `SqlOnFhirEvaluator` and streams the resulting rows to the HTTP response in the requested format. No pagination: the operation streams the full result set (chunked), optionally capped by `_limit`.

**Tech Stack:** .NET 9, Minimal API, Medino (mediator), `Ignixa.SqlOnFhir` evaluator, `Ignixa.Serialization` JSON nodes, xunit + Shouldly + NSubstitute.

## Global Constraints

- No `Hl7.Fhir.*` in Application/DataLayer — use `Ignixa.*` (`ParametersJsonNode`, `ResourceJsonNode`, `OperationOutcomeJsonNode`).
- Async methods take a `CancellationToken cancellationToken` (full name, never `ct`).
- One type per file. File-scoped namespaces. 4-space indent. Nullable enabled. Warnings-as-errors.
- Minimal API in `*Endpoints.cs`; no MVC controllers.
- Query/Command = immutable `record : IRequest<TResult>`; handler = `class : IRequestHandler<TReq,TRes>` with `HandleAsync(request, cancellationToken)`.
- Output formats for sync `$run`: `json` (array), `ndjson`, `csv`. Parquet sync output is **out of scope** (use `$viewdefinition-export` for Parquet) — documented in Task 9.
- Error responses are `OperationOutcome` with spec status codes: 400 (bad/unsupported param), 404 (view not found), 422 (invalid ViewDefinition), 500 (processing).
- Partition 0 is blocked by `TenantResolutionMiddleware` — do not special-case it here.

## File Structure

- `src/Application/Ignixa.Application/Features/SqlOnFhir/RunViewDefinitionQuery.cs` — request record
- `src/Application/Ignixa.Application/Features/SqlOnFhir/RunViewDefinitionResult.cs` — result (row stream + resolved column order)
- `src/Application/Ignixa.Application/Features/SqlOnFhir/RunViewDefinitionHandler.cs` — evaluation handler
- `src/Application/Ignixa.Api/Endpoints/Support/ViewDefinitionRowStreamFormatter.cs` — rows → Stream (json/ndjson/csv)
- `src/Application/Ignixa.Api/Endpoints/ViewDefinitionRunEndpoints.cs` — HTTP surface
- `src/Application/Ignixa.Api/Endpoints/Support/ViewReferenceResolver.cs` — relative/canonical/absolute resolution
- Tests mirror under `test/Ignixa.Application.Tests/Features/SqlOnFhir/` and `test/Ignixa.Api.Tests/Infrastructure/`

> **Design note (DRY tradeoff):** the Core writers in `Ignixa.SqlOnFhir.Writers` are file-path coupled (they open a `FileStream` from an `outputPath`), so they cannot stream to the HTTP response without a refactor. This plan adds a small response-stream formatter for the sync path and leaves the Core writers untouched. A future cleanup could unify them on a `Stream` sink; out of scope here.

---

### Task 1: RunViewDefinitionQuery + Result types

**Files:**
- Create: `src/Application/Ignixa.Application/Features/SqlOnFhir/RunViewDefinitionQuery.cs`
- Create: `src/Application/Ignixa.Application/Features/SqlOnFhir/RunViewDefinitionResult.cs`
- Test: `test/Ignixa.Application.Tests/Features/SqlOnFhir/RunViewDefinitionQueryTests.cs`

**Interfaces:**
- Produces: `RunViewDefinitionQuery` (record) and `RunViewDefinitionResult` (record) consumed by Task 2's handler and Task 5's endpoint.

- [ ] **Step 1: Write the failing test**

```csharp
using Ignixa.Application.Features.SqlOnFhir;
using Ignixa.Serialization.Models;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Features.SqlOnFhir;

public class RunViewDefinitionQueryTests
{
    [Fact]
    public void GivenInlineViewAndResources_WhenConstructed_ThenPropertiesAreSet()
    {
        // Arrange
        var view = new ResourceJsonNode();
        var resources = new List<ResourceJsonNode> { new() };

        // Act
        var query = new RunViewDefinitionQuery(
            TenantId: 1,
            ViewDefinition: view,
            InlineResources: resources,
            Format: "ndjson",
            IncludeCsvHeader: true,
            PatientReference: null,
            GroupReferences: null,
            Since: null,
            Limit: 10);

        // Assert
        query.TenantId.ShouldBe(1);
        query.Format.ShouldBe("ndjson");
        query.Limit.ShouldBe(10);
        query.InlineResources!.Count.ShouldBe(1);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter RunViewDefinitionQueryTests`
Expected: FAIL — `RunViewDefinitionQuery` does not exist (compile error).

- [ ] **Step 3: Write the request and result records**

`RunViewDefinitionQuery.cs`:
```csharp
using Ignixa.Serialization.Models;
using Medino;

namespace Ignixa.Application.Features.SqlOnFhir;

/// <summary>
/// Synchronous ViewDefinition/$run invocation. ViewDefinition is already resolved
/// (inline or loaded) by the endpoint; the handler only evaluates it.
/// </summary>
public record RunViewDefinitionQuery(
    int TenantId,
    ResourceJsonNode ViewDefinition,
    IReadOnlyList<ResourceJsonNode>? InlineResources,
    string Format,
    bool IncludeCsvHeader,
    string? PatientReference,
    IReadOnlyList<string>? GroupReferences,
    DateTimeOffset? Since,
    int? Limit) : IRequest<RunViewDefinitionResult>;
```

`RunViewDefinitionResult.cs`:
```csharp
namespace Ignixa.Application.Features.SqlOnFhir;

/// <summary>
/// Result of a ViewDefinition/$run. Rows are lazily enumerable so the endpoint
/// can stream them to the response without buffering the full set.
/// </summary>
public record RunViewDefinitionResult(
    IAsyncEnumerable<Dictionary<string, object?>> Rows);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter RunViewDefinitionQueryTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Application/Ignixa.Application/Features/SqlOnFhir/ test/Ignixa.Application.Tests/Features/SqlOnFhir/RunViewDefinitionQueryTests.cs
git commit -m "feat(sof): add RunViewDefinitionQuery/Result contracts"
```

---

### Task 2: RunViewDefinitionHandler — inline-resource evaluation

**Files:**
- Create: `src/Application/Ignixa.Application/Features/SqlOnFhir/RunViewDefinitionHandler.cs`
- Modify: `src/Application/Ignixa.Api/Registrations/ApplicationServicesRegistration.cs` (register handler — find the Medino handler registration block)
- Test: `test/Ignixa.Application.Tests/Features/SqlOnFhir/RunViewDefinitionHandlerTests.cs`

**Interfaces:**
- Consumes: `RunViewDefinitionQuery`, `SqlOnFhirEvaluator.EvaluateBatch(ISourceNavigator, IEnumerable<IElement>, IReadOnlyDictionary<string,string>?)`, `IFhirVersionContext.GetSchemaProvider(...)`.
- Produces: `RunViewDefinitionHandler : IRequestHandler<RunViewDefinitionQuery, RunViewDefinitionResult>`.

This task wires the **inline `resource`** path (no server data — that's Task 4). Server-data branch throws `NotSupportedException` for now, replaced in Task 4.

- [ ] **Step 1: Write the failing test**

```csharp
using Ignixa.Application.Features.SqlOnFhir;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Specification;            // FhirVersion / schema provider access
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Features.SqlOnFhir;

public class RunViewDefinitionHandlerTests
{
    private const string PatientView = """
    {"resourceType":"ViewDefinition","resource":"Patient",
     "select":[{"column":[
        {"name":"id","path":"getResourceKey()"},
        {"name":"gender","path":"gender"}]}]}
    """;

    private const string Patient = """
    {"resourceType":"Patient","id":"p1","gender":"female"}
    """;

    [Fact]
    public async Task GivenInlineViewAndResource_WhenHandling_ThenEmitsOneRow()
    {
        // Arrange
        var versionContext = Substitute.For<IFhirVersionContext>();
        versionContext.GetSchemaProvider(Arg.Any<FhirSpecification>(), Arg.Any<int>())
            .Returns(FhirVersion.R4.GetSchemaProvider());
        var handler = new RunViewDefinitionHandler(versionContext, NullLogger<RunViewDefinitionHandler>.Instance);

        var query = new RunViewDefinitionQuery(
            TenantId: 1,
            ViewDefinition: JsonSourceNodeFactory.Parse<ResourceJsonNode>(PatientView)!,
            InlineResources: new[] { JsonSourceNodeFactory.Parse<ResourceJsonNode>(Patient)! },
            Format: "ndjson", IncludeCsvHeader: true,
            PatientReference: null, GroupReferences: null, Since: null, Limit: null);

        // Act
        var result = await handler.HandleAsync(query, CancellationToken.None);
        var rows = new List<Dictionary<string, object?>>();
        await foreach (var row in result.Rows) rows.Add(row);

        // Assert
        rows.Count.ShouldBe(1);
        rows[0]["id"].ShouldBe("Patient/p1");
        rows[0]["gender"].ShouldBe("female");
    }
}
```

> If `JsonSourceNodeFactory.Parse<T>(string)` is not the exact generic form, use the byte/stream overload shown in `ViewDefinitionLoader` (`JsonSourceNodeFactory.Parse(bytes)`); confirm the schema-provider accessor name against `IFhirVersionContext` while implementing — the reference doc shows `GetSchemaProvider(fhirSpec, tenantId)`.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter RunViewDefinitionHandlerTests`
Expected: FAIL — `RunViewDefinitionHandler` does not exist.

- [ ] **Step 3: Write the handler**

```csharp
using Ignixa.Abstractions;             // IElement, ISourceNavigator
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.SqlOnFhir.Evaluation;
using Medino;
using Microsoft.Extensions.Logging;

namespace Ignixa.Application.Features.SqlOnFhir;

public class RunViewDefinitionHandler(
    IFhirVersionContext versionContext,
    ILogger<RunViewDefinitionHandler> logger)
    : IRequestHandler<RunViewDefinitionQuery, RunViewDefinitionResult>
{
    public Task<RunViewDefinitionResult> HandleAsync(
        RunViewDefinitionQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var schemaProvider = versionContext.GetSchemaProvider(
            versionContext.ResolveFhirVersion(request.TenantId), request.TenantId);
        var viewNavigator = request.ViewDefinition.ToSourceNavigator();
        var evaluator = new SqlOnFhirEvaluator();

        if (request.InlineResources is null)
        {
            // Server-data source — implemented in Task 4.
            throw new NotSupportedException("Server-data $run not yet implemented");
        }

        var elements = request.InlineResources
            .Select(r => r.ToElement(schemaProvider))
            .ToList();

        var rows = StreamRows(evaluator, viewNavigator, elements, request.Limit);
        return Task.FromResult(new RunViewDefinitionResult(rows));
    }

    private static async IAsyncEnumerable<Dictionary<string, object?>> StreamRows(
        SqlOnFhirEvaluator evaluator,
        ISourceNavigator viewNavigator,
        IReadOnlyList<IElement> elements,
        int? limit)
    {
        var emitted = 0;
        foreach (var row in evaluator.EvaluateBatch(viewNavigator, elements))
        {
            if (limit.HasValue && emitted >= limit.Value) yield break;
            emitted++;
            yield return row;
            await Task.Yield();
        }
    }
}
```

> `versionContext.ResolveFhirVersion(tenantId)` is illustrative — use whatever the codebase exposes to get the tenant's `FhirSpecification` (the export worker uses `FhirSpecificationExtensions.FromVersionString(tenantConfig.FhirVersion)`). Match that pattern; the test stubs `GetSchemaProvider` so the exact resolver call is an implementation detail to confirm.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter RunViewDefinitionHandlerTests`
Expected: PASS.

- [ ] **Step 5: Register the handler**

In `ApplicationServicesRegistration.cs`, next to existing handler registrations:
```csharp
builder.RegisterType<RunViewDefinitionHandler>()
    .As<IRequestHandler<RunViewDefinitionQuery, RunViewDefinitionResult>>();
```

- [ ] **Step 6: Build + commit**

Run: `dotnet build src/Application/Ignixa.Application/Ignixa.Application.csproj`
Expected: 0 warnings, 0 errors.
```bash
git add -A && git commit -m "feat(sof): RunViewDefinitionHandler evaluates inline resources"
```

---

### Task 3: ViewDefinitionRowStreamFormatter (rows → Stream)

**Files:**
- Create: `src/Application/Ignixa.Api/Endpoints/Support/ViewDefinitionRowStreamFormatter.cs`
- Test: `test/Ignixa.Api.Tests/Infrastructure/ViewDefinitionRowStreamFormatterTests.cs`

**Interfaces:**
- Produces: `static Task WriteAsync(IAsyncEnumerable<Dictionary<string,object?>> rows, string format, bool csvHeader, Stream output, CancellationToken)` consumed by Task 5.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Text;
using Ignixa.Api.Endpoints.Support;
using Shouldly;
using Xunit;

namespace Ignixa.Api.Tests.Infrastructure;

public class ViewDefinitionRowStreamFormatterTests
{
    private static async IAsyncEnumerable<Dictionary<string, object?>> Rows()
    {
        yield return new Dictionary<string, object?> { ["id"] = "p1", ["gender"] = "female" };
        yield return new Dictionary<string, object?> { ["id"] = "p2", ["gender"] = null };
        await Task.CompletedTask;
    }

    [Fact]
    public async Task GivenRows_WhenNdjson_ThenOneJsonObjectPerLine()
    {
        using var ms = new MemoryStream();
        await ViewDefinitionRowStreamFormatter.WriteAsync(Rows(), "ndjson", true, ms, CancellationToken.None);
        var text = Encoding.UTF8.GetString(ms.ToArray()).TrimEnd('\n');
        var lines = text.Split('\n');
        lines.Length.ShouldBe(2);
        lines[0].ShouldContain("\"id\":\"p1\"");
    }

    [Fact]
    public async Task GivenRows_WhenCsvWithHeader_ThenHeaderThenValues()
    {
        using var ms = new MemoryStream();
        await ViewDefinitionRowStreamFormatter.WriteAsync(Rows(), "csv", true, ms, CancellationToken.None);
        var lines = Encoding.UTF8.GetString(ms.ToArray()).Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        lines[0].ShouldBe("id,gender");
        lines[1].ShouldBe("p1,female");
        lines[2].ShouldBe("p2,");          // null -> empty cell
    }

    [Fact]
    public async Task GivenRows_WhenJson_ThenJsonArray()
    {
        using var ms = new MemoryStream();
        await ViewDefinitionRowStreamFormatter.WriteAsync(Rows(), "json", true, ms, CancellationToken.None);
        var text = Encoding.UTF8.GetString(ms.ToArray());
        text.TrimStart()[0].ShouldBe('[');
        text.TrimEnd()[^1].ShouldBe(']');
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Ignixa.Api.Tests/Ignixa.Api.Tests.csproj --filter ViewDefinitionRowStreamFormatterTests`
Expected: FAIL — type does not exist.

- [ ] **Step 3: Implement the formatter**

```csharp
using System.Text;
using System.Text.Json;

namespace Ignixa.Api.Endpoints.Support;

/// <summary>
/// Streams ViewDefinition/$run rows to an output stream in json/ndjson/csv.
/// CSV column order is fixed from the first row's keys (spec: first row defines columns).
/// </summary>
public static class ViewDefinitionRowStreamFormatter
{
    public static async Task WriteAsync(
        IAsyncEnumerable<Dictionary<string, object?>> rows,
        string format,
        bool csvHeader,
        Stream output,
        CancellationToken cancellationToken)
    {
        switch (format)
        {
            case "ndjson": await WriteNdjsonAsync(rows, output, cancellationToken); break;
            case "csv": await WriteCsvAsync(rows, csvHeader, output, cancellationToken); break;
            case "json": await WriteJsonAsync(rows, output, cancellationToken); break;
            default: throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported format");
        }
    }

    private static async Task WriteNdjsonAsync(
        IAsyncEnumerable<Dictionary<string, object?>> rows, Stream output, CancellationToken ct)
    {
        await using var writer = new StreamWriter(output, new UTF8Encoding(false), leaveOpen: true);
        await foreach (var row in rows.WithCancellation(ct))
            await writer.WriteLineAsync(JsonSerializer.Serialize(row).AsMemory(), ct);
        await writer.FlushAsync(ct);
    }

    private static async Task WriteCsvAsync(
        IAsyncEnumerable<Dictionary<string, object?>> rows, bool header, Stream output, CancellationToken ct)
    {
        await using var writer = new StreamWriter(output, new UTF8Encoding(false), leaveOpen: true);
        string[]? columns = null;
        await foreach (var row in rows.WithCancellation(ct))
        {
            if (columns is null)
            {
                columns = row.Keys.ToArray();
                if (header) await writer.WriteLineAsync(string.Join(",", columns.Select(EscapeCsv)));
            }
            await writer.WriteLineAsync(string.Join(",",
                columns.Select(c => EscapeCsv(row.TryGetValue(c, out var v) ? v?.ToString() ?? "" : ""))));
        }
        await writer.FlushAsync(ct);
    }

    private static async Task WriteJsonAsync(
        IAsyncEnumerable<Dictionary<string, object?>> rows, Stream output, CancellationToken ct)
    {
        await using var json = new Utf8JsonWriter(output);
        json.WriteStartArray();
        await foreach (var row in rows.WithCancellation(ct))
            JsonSerializer.Serialize(json, row);
        json.WriteEndArray();
        await json.FlushAsync(ct);
    }

    private static string EscapeCsv(string value)
        => value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Ignixa.Api.Tests/Ignixa.Api.Tests.csproj --filter ViewDefinitionRowStreamFormatterTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat(sof): row stream formatter for json/ndjson/csv"
```

---

### Task 4: Server-data source (search stream) + filters

**Files:**
- Modify: `src/Application/Ignixa.Application/Features/SqlOnFhir/RunViewDefinitionHandler.cs`
- Test: `test/Ignixa.Application.Tests/Features/SqlOnFhir/RunViewDefinitionServerDataTests.cs`

**Interfaces:**
- Consumes: the existing search-stream surface (`IQueryExecutionStrategy.SearchStreamAsync(RequestPartition, SearchOptions, CancellationToken)` returning `IAsyncEnumerable<SearchEntryResult>`, per `PatientEverythingHandler`) and `ISearchOptionsBuilderFactory` to build `SearchOptions` from `patient`/`group`/`_since`/`_limit`.

When `InlineResources is null`, stream resources of the ViewDefinition's `resource` type from the server, applying filters, and evaluate per resource.

- [ ] **Step 1: Write the failing test** (stub the execution strategy to return two Patients)

```csharp
[Fact]
public async Task GivenServerData_WhenHandling_ThenEvaluatesEachStreamedResource()
{
    // Arrange: stub IQueryExecutionStrategy to yield 2 Patient SearchEntryResults,
    // stub partition strategy + context like PatientEverythingHandlerTests.SetupDefaultMocks().
    // (See test/Ignixa.Application.Tests/Features/PatientEverything/PatientEverythingHandlerTests.cs
    //  lines 37-89 for the exact NSubstitute setup to copy.)
    // query.InlineResources = null, Limit = null.

    // Act
    var result = await handler.HandleAsync(query, CancellationToken.None);
    var rows = new List<Dictionary<string, object?>>();
    await foreach (var row in result.Rows) rows.Add(row);

    // Assert
    rows.Count.ShouldBe(2);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter RunViewDefinitionServerDataTests`
Expected: FAIL — currently throws `NotSupportedException`.

- [ ] **Step 3: Implement server-data branch**

Inject `IQueryExecutionStrategy`, `IPartitionStrategy`, `ISearchOptionsBuilderFactory`, `IFhirRequestContextAccessor` into the handler (mirror `PatientEverythingHandler`'s constructor). Replace the `NotSupportedException` branch:
```csharp
var resourceType = request.ViewDefinition.MutableNode["resource"]?.GetValue<string>()
    ?? throw new BadRequestException("ViewDefinition.resource is required");

var searchOptions = BuildSearchOptions(resourceType, request);   // applies _since/_limit/patient/group
var partition = partitionStrategy.DetermineReadPartition(/* like PatientEverythingHandler */);

var rows = StreamServerRows(evaluator, viewNavigator, schemaProvider, resourceType,
    partition, searchOptions, request.Limit, cancellationToken);
return new RunViewDefinitionResult(rows);
```
with:
```csharp
private async IAsyncEnumerable<Dictionary<string, object?>> StreamServerRows(
    SqlOnFhirEvaluator evaluator, ISourceNavigator viewNavigator, IFhirSchemaProvider schemaProvider,
    string resourceType, RequestPartition partition, SearchOptions searchOptions, int? limit,
    [EnumeratorCancellation] CancellationToken cancellationToken)
{
    var emitted = 0;
    await foreach (var entry in executionStrategy.SearchStreamAsync(partition, searchOptions, cancellationToken))
    {
        var element = JsonSourceNodeFactory.Parse(entry.ResourceBytes)!.ToElement(schemaProvider);
        foreach (var row in evaluator.Evaluate(viewNavigator, element))
        {
            if (limit.HasValue && emitted >= limit.Value) yield break;
            emitted++;
            yield return row;
        }
    }
}
```
`BuildSearchOptions` uses `ISearchOptionsBuilderFactory.Create(fhirSpec, tenantId)`, sets `_since`→`_lastUpdated` filter, `_limit`→`MaxItemCount`, and `patient`/`group`→compartment expression. For unsupported combinations, throw `BadRequestException` (mapped to 400 in Task 5).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter RunViewDefinitionServerDataTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat(sof): server-data source + patient/group/_since/_limit filters for $run"
```

---

### Task 5: ViewReferenceResolver

**Files:**
- Create: `src/Application/Ignixa.Api/Endpoints/Support/ViewReferenceResolver.cs`
- Test: `test/Ignixa.Api.Tests/Infrastructure/ViewReferenceResolverTests.cs`

**Interfaces:**
- Consumes: `ViewDefinitionLoader.LoadViewDefinitionAsync(int tenantId, string id, CancellationToken)`.
- Produces: `Task<ResourceJsonNode> ResolveAsync(int tenantId, string reference, CancellationToken)` consumed by Task 6.

Handles the spec's three forms: relative (`ViewDefinition/123` → id `123`), canonical (`http://x/ViewDefinition/123|1.0.0` → id segment), absolute (treated as relative-by-id for now; remote fetch is out of scope — throw `NotSupportedException` mapped to 400).

- [ ] **Step 1: Write the failing test**

```csharp
[Theory]
[InlineData("ViewDefinition/abc", "abc")]
[InlineData("abc", "abc")]
[InlineData("http://h/fhir/ViewDefinition/abc|1.0.0", "abc")]
public async Task GivenReference_WhenResolving_ThenLoadsExpectedId(string reference, string expectedId)
{
    // Arrange
    var loader = Substitute.For<IViewDefinitionLoader>();
    loader.LoadViewDefinitionAsync(1, expectedId, Arg.Any<CancellationToken>())
        .Returns(/* a ResourceJsonNode-backed navigator parsed from a minimal ViewDefinition */);
    var resolver = new ViewReferenceResolver(loader);

    // Act
    var view = await resolver.ResolveAsync(1, reference, CancellationToken.None);

    // Assert
    view.ShouldNotBeNull();
    await loader.Received(1).LoadViewDefinitionAsync(1, expectedId, Arg.Any<CancellationToken>());
}
```

> Note: `ViewDefinitionLoader` returns `ISourceNavigator`. If the resolver must hand back a `ResourceJsonNode` for the formatter/handler, parse once and carry the navigator instead — adjust the produced type to `ISourceNavigator` and have Task 2's handler accept either. Confirm `IViewDefinitionLoader` interface exists; if the class is concrete, extract an interface in this task.

- [ ] **Step 2: Run to verify it fails** — `dotnet test ... --filter ViewReferenceResolverTests` → FAIL.

- [ ] **Step 3: Implement** parsing of the id segment (strip `|version`, take segment after last `/`), delegate to the loader; throw `NotSupportedException` for absolute remote URLs.

- [ ] **Step 4: Run to verify it passes.**

- [ ] **Step 5: Commit** — `git commit -m "feat(sof): viewReference resolution (relative/canonical)"`

---

### Task 6: ViewDefinitionRunEndpoints (HTTP surface)

**Files:**
- Create: `src/Application/Ignixa.Api/Endpoints/ViewDefinitionRunEndpoints.cs`
- Modify: the endpoint aggregator that calls `MapOperationEndpoints()` (the `MapIgnixaEndpoints` extension) — add `MapViewDefinitionRunEndpoints()`
- Test: `test/Ignixa.Api.Tests/Infrastructure/ViewDefinitionRunEndpointsTests.cs`

**Interfaces:**
- Consumes: `RunViewDefinitionQuery`/`Handler`, `ViewReferenceResolver`, `ViewDefinitionRowStreamFormatter`, `ParametersJsonNode`, `OperationOutcomeJsonNode`, `FhirResults`.

Routes (type + instance, GET + POST), mirroring `OperationEndpoints` style:
```csharp
group.MapPost("/ViewDefinition/$run", HandleRunTypeLevel);
group.MapGet("/ViewDefinition/{id}/$run", HandleRunInstanceLevelGet);
group.MapPost("/ViewDefinition/{id}/$run", HandleRunInstanceLevelPost);
```

Behaviour:
- Negotiate format: `_format` query/param wins, else `Accept` header (`application/json`→json, `application/x-ndjson`→ndjson, `text/csv`→csv). Unsupported → 400 `not-supported`.
- POST: parse body as `ParametersJsonNode` via `JsonSourceNodeFactory.ParseAsync<ParametersJsonNode>`. Extract `viewResource` (inline) xor `viewReference`; repeated `resource` params → `InlineResources`. Instance level: infer view from `{id}` via resolver; reject `viewResource`/`viewReference` (400 per spec).
- Map exceptions: `BadRequestException`→400 `invalid`/`not-supported`; view-not-found (`InvalidOperationException` from loader)→404 `not-found`; ViewDefinition parse/eval failure→422 `invalid`; else 500 `processing`. Use `CreateOperationOutcome(...)`.
- On success: set `Content-Type` for the format, then stream via `ViewDefinitionRowStreamFormatter.WriteAsync(result.Rows, format, csvHeader, response.Body, ct)`. Return a custom streaming `IResult` (write directly to `HttpContext.Response.Body`; enables chunked transfer).

- [ ] **Step 1: Write the failing test** — minimal happy-path integration test using inline view + inline resource, asserting 200 + ndjson body. Follow `OperationEndpointsPatientEverythingTests` for the WebApplicationFactory/test-server pattern.

```csharp
[Fact]
public async Task GivenInlineViewAndResource_WhenPostRun_ThenReturnsNdjsonRows()
{
    var parameters = """
    {"resourceType":"Parameters","parameter":[
      {"name":"_format","valueCode":"ndjson"},
      {"name":"viewResource","resource":{"resourceType":"ViewDefinition","resource":"Patient",
        "select":[{"column":[{"name":"id","path":"getResourceKey()"},{"name":"gender","path":"gender"}]}]}},
      {"name":"resource","resource":{"resourceType":"Patient","id":"p1","gender":"female"}}
    ]}
    """;
    var response = await client.PostAsync("/tenant/1/ViewDefinition/$run",
        new StringContent(parameters, Encoding.UTF8, "application/fhir+json"));
    response.StatusCode.ShouldBe(HttpStatusCode.OK);
    var body = await response.Content.ReadAsStringAsync();
    body.ShouldContain("\"id\":\"Patient/p1\"");
}
```

- [ ] **Step 2: Run to verify it fails** — endpoint not mapped → 404.

- [ ] **Step 3: Implement the endpoints + register** `MapViewDefinitionRunEndpoints()` in the aggregator.

- [ ] **Step 4: Run to verify it passes.**

- [ ] **Step 5: Build full solution** — `dotnet build All.sln` → 0 warnings, 0 errors.

- [ ] **Step 6: Commit** — `git commit -m "feat(sof): ViewDefinition/$run endpoints (type + instance)"`

---

### Task 7: Error-contract conformance tests

**Files:**
- Test: `test/Ignixa.Api.Tests/Infrastructure/ViewDefinitionRunErrorTests.cs`

Cover the spec's documented error cases (from `OperationDefinition-ViewDefinitionRun-notes.md`): missing required param (400 `required`), invalid ViewDefinition FHIRPath (422 `invalid`), unknown instance id (404 `not-found`), unsupported `_format=xml` (400 `not-supported`), both `viewResource`+`viewReference` (400 `invalid`).

- [ ] **Step 1–N:** one `[Fact]` per case; assert status code and `OperationOutcome.issue[0].code`. Run, confirm green, commit `test(sof): $run error-contract coverage`.

---

### Task 8: CapabilityStatement advertisement

**Files:**
- Modify: the metadata/CapabilityStatement builder (search for where `/metadata` is assembled)
- Test: extend the capability test to assert the `ViewDefinition` `$run` operation appears.

- [ ] Add an `operation` entry for `$run` on the `ViewDefinition` resource with the operation canonical URL, and document supported `_format` values. TDD: assert presence in `/metadata`. Commit.

---

### Task 9: Docs

**Files:**
- Modify: `docs/site/docs/core-sdk/sql-on-fhir.md` and `docs/features/sql-on-fhir/readme.md`

- [ ] Document the new server operation, supported formats (json/ndjson/csv; **Parquet via `$viewdefinition-export`, not sync `$run`**), filters, and the no-pagination/streaming model. Update the feature readme's HTTP API row from "Missing" to "Implemented (sync run)". Commit `docs(sof): document ViewDefinition/$run`.

---

## Self-Review

- **Spec coverage:** type+instance ✅ (T6), GET+POST ✅ (T6), viewResource/viewReference/instance-infer ✅ (T5/T6), inline `resource` ✅ (T2), server data ✅ (T4), `_format` json/ndjson/csv ✅ (T3/T6), `header` ✅ (T3), patient/group/_since/_limit ✅ (T4), OperationOutcome error matrix ✅ (T7), CapabilityStatement ✅ (T8). `source` (external) and Parquet sync output intentionally deferred — noted in T9.
- **Type consistency:** `RunViewDefinitionQuery`/`RunViewDefinitionResult` names stable across T1/T2/T4/T6; `WriteAsync` signature stable T3↔T6; evaluator returns `Dictionary<string,object?>` rows throughout.
- **Open confirmations flagged inline:** `IFhirVersionContext` FHIR-version resolver method name; `JsonSourceNodeFactory.Parse<T>` generic vs byte overload; whether `IViewDefinitionLoader` interface exists or must be extracted (T5). These are lookups, not design gaps.

## Execution Handoff

Plan saved. Two execution options — **(1) Subagent-Driven (recommended)**: fresh subagent per task with review between; **(2) Inline Execution**: batch with checkpoints. Which approach?
