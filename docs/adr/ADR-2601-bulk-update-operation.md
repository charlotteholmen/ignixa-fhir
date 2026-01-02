# ADR-2601: $bulk-update Operation

## Status

Accepted

## Date

2026-01-01

## Context

Healthcare systems often need to apply bulk updates to FHIR resources. Common scenarios include:

- Adding security tags across all patient resources
- Updating metadata for regulatory compliance
- Bulk classification of resources
- Adding or updating extensions across resource types

The Azure Health Data Services FHIR API provides a `$bulk-update` operation for this purpose. We need to implement an equivalent capability following the same API contract for compatibility.

## Decision

### Operation Name

Use `$bulk-update` as the operation name (compatible with Azure's `$bulk-update`). This aligns with the Azure FHIR API naming convention.

### API Contract

**Endpoints:**
- `PATCH /$bulk-update` - System-level bulk update
- `PATCH /{ResourceType}/$bulk-update` - Type-scoped bulk update
- `GET /_bulk-update/{jobId}` - Poll job status
- `DELETE /_bulk-update/{jobId}` - Cancel job

**Headers:**
- `Prefer: respond-async` (required)
- `Content-Type: application/fhir+json`
- `Accept: application/fhir+json`

### Patch Operations Supported

| Operation | Behavior |
|-----------|----------|
| `replace` | Replace existing value; fails if element absent |
| `add` | Add if absent, replace if present (treated as `upsert` for idempotency) |
| `upsert` | Add if absent, replace if present (idempotent) |

**Note:** The `add` operation is supported and is treated identically to `upsert` to maintain idempotency. Both add-if-absent and replace-if-present semantics are applied.

We explicitly exclude `insert`, `move`, `delete` to maintain idempotency and prevent dangerous bulk deletions.

### Processing Model

1. **Single job constraint** - Only one bulk-update job may run at a time
2. **Batch size** - 1000 resources per transaction
3. **Parallelism** - Partition by resource type and surrogate ID range
4. **Partial success** - Valid patches commit; failed resources logged with reason
5. **Default resource types** - When no resource type is specified in the endpoint URL, the operation applies to: Patient, Observation, Condition, MedicationRequest, Encounter, Procedure

### Excluded Resource Types

- `SearchParameter` - Could invalidate search indexes
- `StructureDefinition` - Could break schema validation

Resources of these types are skipped and counted in `ResourceIgnoredCount`.

### Path Requirements

All patch paths must be fully qualified starting with the resource type:
- Valid: `Patient.meta.tag`, `Observation.status`
- Invalid: `meta.tag`, `status`

For common properties, use `Resource` prefix: `Resource.meta.tag`

### Search Integration

When query parameters are present, the operation:
1. Executes search with provided parameters
2. Applies patches only to matching resources
3. Respects `_include`/`_revinclude` for extended scope
4. Partitions work at continuation token boundaries

### Error Handling

| Scenario | Response |
|----------|----------|
| Job already running | 400 Bad Request |
| Invalid Parameters body | 400 Bad Request |
| Unsupported operation type | 400 Bad Request |
| Replace on missing element | Resource fails, job continues |
| Immutable field violation | Resource fails, job continues |
| Transient DB error | Retry with backoff |

### Response Format

**In Progress (202):**
```json
{
  "resourceType": "Parameters",
  "parameter": [
    { "name": "status", "valueCode": "in-progress" },
    { "name": "progress", "valueDecimal": 45.5 }
  ]
}
```

**Complete (200):**
```json
{
  "resourceType": "Parameters",
  "parameter": [
    { "name": "status", "valueCode": "completed" },
    { "name": "ResourceUpdatedCount", "part": [...] },
    { "name": "ResourceIgnoredCount", "part": [...] },
    { "name": "ResourcePatchFailedCount", "part": [...] },
    { "name": "Issues", "part": [...] }
  ]
}
```

## Consequences

### Positive

- **Azure compatibility** - Same API contract enables migration
- **Safety** - Limited operation set prevents bulk deletions
- **Performance** - Batch processing with parallel workers
- **Visibility** - Progress tracking via polling endpoint
- **Auditability** - Batch-level audit logging

### Negative

- **Single job limit** - May queue multiple users
- **No rollback** - Committed changes are permanent
- **Memory pressure** - Large batches require memory for resource clones

### Risks and Mitigations

| Risk | Mitigation |
|------|------------|
| Runaway job blocks others | Cancellation endpoint + timeout |
| Partial failure state | Detailed error reporting in response |
| Performance degradation | Configurable batch size + parallelism |

## Implementation Notes

### File Organization

```
src/Application/Ignixa.Api/Endpoints/BulkUpdateEndpoints.cs
src/Application/Ignixa.Application.BackgroundOperations/BulkUpdate/
  CreateBulkUpdateJobCommand.cs
  CreateBulkUpdateJobHandler.cs
  CreateBulkUpdateJobResult.cs
  Activities/
    GetBulkUpdateRangesActivity.cs
    BulkUpdateWorkerActivity.cs
    UpdateBulkUpdateProgressActivity.cs
  Models/
    BulkUpdateOrchestrationInput.cs
    BulkUpdateOrchestrationOutput.cs
    BulkUpdateWorkerInput.cs
    BulkUpdateWorkerOutput.cs
    GetBulkUpdateRangesInput.cs
    GetBulkUpdateRangesOutput.cs
    UpdateBulkUpdateProgressInput.cs
  Orchestrations/
    BulkUpdateOrchestration.cs
src/Application/Ignixa.Application.BackgroundOperations/Jobs/
  GetJobStatusHandler.cs (shared with Import/Export)
src/Application/Ignixa.Domain/Models/
  BulkUpdateJobDefinition.cs
  BulkUpdateJobProgress.cs
  BulkUpdateJobResult.cs
  BulkUpdateOperationDefinition.cs
  BulkUpdateIssue.cs
```

### DurableTask Orchestration

Follow existing export/import patterns:
1. Validate request and create job record
2. Partition work by resource type + ID range
3. Fan-out to parallel workers
4. Aggregate results
5. Mark job complete

### Testing Strategy

- Unit tests for patch parsing and validation
- Unit tests for worker activity (mock repository)
- Integration tests for full orchestration
- Edge cases: empty results, all failures, cancellation

## References

- [Azure FHIR Bulk Update](https://learn.microsoft.com/en-us/azure/healthcare-apis/fhir/fhir-bulk-update)
- [Microsoft FHIR Server ADR-2504](https://github.com/microsoft/fhir-server/blob/main/docs/arch/adr-2504-bulkupdate.md)
- [FHIR Patch Operation](https://hl7.org/fhir/http.html#patch)
