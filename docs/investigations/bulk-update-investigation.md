# Investigation: $bulk-update Operation

**Date:** 2026-01-01
**Status:** In Progress
**Feature:** FHIR $bulk-update / $bulk-update Operation

## Summary

Implement the `$bulk-update` operation as defined by Microsoft Azure Health Data Services, enabling bulk updates to FHIR resources using FHIR Patch semantics.

## Sources

1. [Azure Healthcare APIs - FHIR Bulk Update](https://learn.microsoft.com/en-us/azure/healthcare-apis/fhir/fhir-bulk-update)
2. [Microsoft FHIR Server ADR-2504](https://github.com/microsoft/fhir-server/blob/main/docs/arch/adr-2504-bulkupdate.md)

## API Contract

### Endpoints

| Method | URL Pattern | Description |
|--------|-------------|-------------|
| PATCH | `/$bulk-update` | System-level bulk patch |
| PATCH | `/{ResourceType}/$bulk-update` | Type-scoped bulk patch |
| GET | `/_bulk-update/{jobId}` | Poll job status |
| DELETE | `/_bulk-update/{jobId}` | Cancel job |

### Request Headers

| Header | Value | Required |
|--------|-------|----------|
| Content-Type | application/fhir+json | Yes |
| Accept | application/fhir+json | Yes |
| Prefer | respond-async | Yes |

### Request Body

```json
{
  "resourceType": "Parameters",
  "parameter": [
    {
      "name": "operation",
      "part": [
        { "name": "type", "valueCode": "replace" },
        { "name": "path", "valueString": "Patient.meta.tag" },
        { "name": "value", "valueCoding": {
            "system": "http://example.org/tags",
            "code": "reviewed"
          }
        }
      ]
    }
  ]
}
```

### Supported Patch Operations

| Operation | Description |
|-----------|-------------|
| `replace` | Replace existing value (fails if not present) |
| `upsert` | Add if absent, replace if present (idempotent) |

**NOT Supported:** `add`, `insert`, `move`, `delete`

### Response

**Initial (202 Accepted):**
```http
HTTP/1.1 202 Accepted
Content-Location: https://server/_bulk-update/{jobId}
```

**Polling (200 OK when complete):**
```json
{
  "resourceType": "Parameters",
  "parameter": [
    {
      "name": "status",
      "valueCode": "completed"
    },
    {
      "name": "ResourceUpdatedCount",
      "part": [
        { "name": "Patient", "valueInteger64": 1500 },
        { "name": "Observation", "valueInteger64": 3200 }
      ]
    },
    {
      "name": "ResourceIgnoredCount",
      "part": [
        { "name": "StructureDefinition", "valueInteger64": 45 }
      ]
    },
    {
      "name": "ResourcePatchFailedCount",
      "part": [
        { "name": "Patient", "valueInteger64": 3 }
      ]
    }
  ]
}
```

## Constraints

1. **One job at a time** - Reject if job already running (400 Bad Request)
2. **Excluded resource types** - `SearchParameter`, `StructureDefinition` (skip with IgnoredCount)
3. **Path format** - Must start with ResourceType (e.g., `Patient.meta.tag`)
4. **Immutable fields** - Cannot update `id`, `meta.versionId`, `meta.lastUpdated`
5. **No rollback** - Committed updates are permanent
6. **Partial success** - Valid updates commit; failures logged separately

## Implementation Design

### Architecture Alignment

Following existing patterns from `$export` and `$import`:

1. **Endpoint** (`BulkUpdateEndpoints.cs`) - Route registration
2. **Command/Handler** - `CreateBulkUpdateJobCommand`, `GetBulkUpdateJobStatusQuery`
3. **Orchestration** (`BulkUpdateOrchestration.cs`) - DurableTask workflow
4. **Activities:**
   - `GetBulkUpdateRangesActivity` - Partition resources by surrogate ID ranges
   - `BulkUpdateWorkerActivity` - Apply patches in batches

### Processing Flow

```
1. Client submits PATCH request with Parameters body
2. Endpoint validates request, creates job record
3. Returns 202 with Content-Location
4. Orchestration partitions work (by type + surrogate ID range)
5. Workers process in parallel:
   a. Fetch batch of resources (1000 at a time)
   b. Apply patch to each resource in memory
   c. Save via MergeResources (batch insert)
   d. Track success/failure counts
6. Aggregates results
7. Job marked complete
```

### Search Integration

When query parameters present:
- Execute search to find matching resources
- Apply patches only to resources matching path prefix
- Partition by continuation token boundaries

### Batching Strategy

- **Batch size:** 1000 resources per transaction (matches ADR spec)
- **Parallelism:** Worker count based on resource type ranges
- **Transaction boundary:** Each batch is atomic

### Error Handling

| Scenario | Behavior |
|----------|----------|
| Replace on missing element | Resource fails, others succeed |
| Invalid patch syntax | Job rejected (400) |
| Immutable field violation | Resource fails, others succeed |
| Transient DB error | Retry with backoff |
| Job already running | Reject (400) |

## File Structure

```
src/
  Application/
    Ignixa.Api/
      Endpoints/
        BulkUpdateEndpoints.cs           # Route registration
    Ignixa.Application/
      Features/
        BulkUpdate/
          CreateBulkUpdateJobCommand.cs
          CreateBulkUpdateJobHandler.cs
          GetBulkUpdateJobStatusQuery.cs
          GetBulkUpdateJobStatusHandler.cs
          CancelBulkUpdateJobCommand.cs
          CancelBulkUpdateJobHandler.cs
          BulkUpdateJobResult.cs
    Ignixa.Application.BackgroundOperations/
      BulkUpdate/
        Orchestrations/
          BulkUpdateOrchestration.cs
        Activities/
          GetBulkUpdateRangesActivity.cs
          BulkUpdateWorkerActivity.cs
        Models/
          BulkUpdateJob.cs
          BulkUpdateRange.cs
          BulkUpdateWorkerResult.cs
```

## Questions for ADR

1. Should we support JSON Patch (RFC 6902) in addition to FHIR Patch?
   - **Decision:** No, FHIR Patch only (aligns with Azure spec)

2. Should we allow multiple concurrent jobs?
   - **Decision:** No, single job at a time (aligns with Azure spec)

3. Should we emit audit events per resource or per batch?
   - **Decision:** Per batch (performance, matches bulk-delete pattern)

4. Should we support `_include`/`_revinclude` in search?
   - **Decision:** Yes (Azure spec supports it)

## Next Steps

1. Create ADR documenting decisions
2. Implement command/handlers
3. Implement orchestration and activities
4. Add endpoints
5. Write tests
