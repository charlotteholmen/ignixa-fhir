# Investigation: Anonymization Integration in Export Pipeline

**Feature**: export / anonymization
**Status**: Proposed
**Created**: 2026-04-08

---

## Executive Summary

This investigation analyzes how to integrate the Ignixa.Anonymizer library into the bulk export pipeline (`$export` operation) using FHIR-native configuration management, avoiding the external blob storage dependency used in Microsoft's original FHIR server implementation.

**Key Finding**: The old Microsoft FHIR server approach stored anonymization configurations in Azure Blob Storage, requiring external infrastructure dependencies. By contrast, SQL-on-FHIR's ViewDefinition demonstrates a superior pattern: storing transformation configurations as first-class FHIR resources.

**Recommendation**: Create `AnonymizationConfig` as a custom FHIR resource (similar to ViewDefinition) that can be stored, versioned, and managed through standard FHIR REST APIs. This aligns with the ImplementationGuide loading pattern and leverages existing export infrastructure.

**Estimated Effort**: 24-32 hours (3-4 days) over 3 phases

---

## Problem Statement

### Current State

**What Works Today**:
1. **Ignixa.Anonymizer Library** (ADR-2602): Standalone anonymization with FHIRPath-based rules
2. **Bulk Export Pipeline**: High-performance streaming export via DurableTask orchestration
3. **ViewDefinition Integration**: SQL-on-FHIR transformations during export (Parquet output)

**What's Missing**:
- No integration between Anonymizer and `$export` operation
- No FHIR-native way to store/manage anonymization configurations
- No support for `$export?_anonymize=true` or similar parameter

### Microsoft FHIR Server Approach (Legacy Pattern)

The Microsoft FHIR server's de-identified export relied on external blob storage:

```
Azure Data Factory Pipeline:
1. Export FHIR data to Blob Storage (source container)
2. Load anonymization config from Blob Storage
3. Run anonymization tool (batch process)
4. Write anonymized data to Blob Storage (destination container)
```

**Configuration Storage**:
```json
// Stored in: https://mystorageaccount.blob.core.windows.net/configs/anonymization-config.json
{
  "fhirVersion": "R4",
  "fhirPathRules": [
    {"path": "Patient.identifier", "method": "cryptoHash"},
    {"path": "Patient.birthDate", "method": "redact"}
  ],
  "parameters": {
    "cryptoHashKey": "...",
    "dateShiftKey": "..."
  }
}
```

**Problems with This Approach**:
- ❌ External infrastructure dependency (Azure Blob Storage)
- ❌ No versioning or audit trail for config changes
- ❌ Not discoverable via FHIR REST API
- ❌ Requires separate deployment/management process
- ❌ Secrets stored in configuration files (security risk)
- ❌ Not multi-tenant friendly (global configs)
- ❌ Batch processing model (no streaming)

### Desired Workflow

**FHIR-Native Configuration Management**:
```bash
# 1. Create anonymization configuration as a FHIR resource
POST /AnonymizationConfig
Content-Type: application/fhir+json
{
  "resourceType": "AnonymizationConfig",
  "name": "hipaa-safe-harbor",
  "status": "active",
  "fhirPathRules": [
    {"path": "Patient.identifier", "method": "cryptoHash"},
    {"path": "Patient.birthDate", "method": "redact"}
  ]
}

# 2. Search for available configs
GET /AnonymizationConfig?status=active

# 3. Reference in export request
POST /$export?_anonymize=hipaa-safe-harbor
Prefer: respond-async

# 4. Export pipeline streams through anonymizer
# (Same high-performance streaming as current export)
```

**Benefits**:
- ✅ FHIR-native resource management (CRUD, search, versioning)
- ✅ Multi-tenant isolation (each tenant has own configs)
- ✅ Audit trail via resource history
- ✅ Discoverable via CapabilityStatement
- ✅ Secrets managed separately (not in config)
- ✅ Streaming integration (no batch overhead)
- ✅ Consistent with ViewDefinition pattern

---

## Approach Analysis

We analyzed three approaches for integrating anonymization into the export pipeline:

### Approach A: External Configuration Files (Microsoft Pattern)

**Description**: Store anonymization configs in external storage (filesystem or blob storage).

**Architecture**:
```
Export Request: POST /$export?_anonymize=true
    ↓
Load config from: /configs/anonymization-config.json
    ↓
Export Pipeline → Anonymizer (inline) → Output
```

**Implementation**:
```csharp
// ExportWorkerActivity.cs
var configPath = Path.Combine(_configDirectory, "anonymization-config.json");
var configJson = await File.ReadAllTextAsync(configPath, cancellationToken);
var anonymizerOptions = AnonymizerOptionsLoader.Load(configJson);
var anonymizerEngine = new AnonymizerEngine(anonymizerOptions, ...);

// Process each resource through anonymizer
await foreach (var resource in searchService.SearchStreamAsync(...))
{
    var anonymizedResource = await anonymizerEngine.AnonymizeAsync(resource, cancellationToken);
    await writer.WriteResourceAsync(anonymizedResource, cancellationToken);
}
```

**Feasibility**: ✅ Simple but problematic

**Pros**:
- ✅ Simple implementation (file I/O only)
- ✅ No database schema changes
- ✅ Works with existing Anonymizer library

**Cons**:
- ❌ External infrastructure dependency
- ❌ No versioning or audit trail
- ❌ Not discoverable via FHIR API
- ❌ Secrets in config files (security risk)
- ❌ Requires manual file deployment
- ❌ Not multi-tenant friendly
- ❌ Doesn't follow FHIR patterns

**Complexity**: 2/5 (simple but wrong approach)

**Time Estimate**: 8-12 hours

**Recommendation**: ❌ Not recommended - doesn't address core problem

---

### Approach B: AnonymizationConfig as Custom FHIR Resource ⭐ **RECOMMENDED**

**Description**: Create `AnonymizationConfig` as a custom FHIR resource (similar to ViewDefinition from SQL-on-FHIR).

**Architecture**:
```
Admin: Create AnonymizationConfig resource
POST /AnonymizationConfig → Store in FHIR repository
    ↓
User: Request export with anonymization
POST /$export?_anonymize=hipaa-safe-harbor
    ↓
Export Pipeline:
1. Load AnonymizationConfig from repository
2. Initialize AnonymizerEngine with config
3. Stream resources through anonymizer
4. Write anonymized output
```

**Resource Definition**:
```json
{
  "resourceType": "AnonymizationConfig",
  "id": "hipaa-safe-harbor",
  "meta": {
    "versionId": "2",
    "lastUpdated": "2026-04-08T10:00:00Z"
  },
  "url": "http://example.org/fhir/AnonymizationConfig/hipaa-safe-harbor",
  "name": "hipaa_safe_harbor",
  "title": "HIPAA Safe Harbor Configuration",
  "status": "active",
  "description": "HIPAA Safe Harbor de-identification rules",
  "fhirVersion": ["4.0.1", "4.3.0", "5.0.0"],
  "fhirPathRules": [
    {
      "path": "Patient.identifier",
      "method": "cryptoHash",
      "description": "Hash patient identifiers for linkage"
    },
    {
      "path": "Patient.birthDate",
      "method": "redact",
      "description": "Redact full birth dates (HIPAA Safe Harbor)"
    },
    {
      "path": "Patient.address.postalCode",
      "method": "redact",
      "description": "Redact zip codes to 3 digits"
    },
    {
      "path": "nodesByType('HumanName')",
      "method": "redact",
      "description": "Remove all names"
    }
  ],
  "parameters": {
    "enablePartialDatesForRedact": true,
    "enablePartialAgesForRedact": true,
    "enablePartialZipCodesForRedact": true,
    "restrictedZipCodeTabulationAreas": ["036", "059", "102"]
  },
  "keyReference": {
    "cryptoHashKey": "vault://keys/anonymization-hash-key",
    "dateShiftKey": "vault://keys/anonymization-dateshift-key"
  }
}
```

**Implementation**:

**1. AnonymizationConfig Resource Model**:
```csharp
// File: src/Ignixa.Domain/Models/AnonymizationConfig.cs
public record AnonymizationConfig
{
    public required string ResourceType { get; init; } = "AnonymizationConfig";
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Status { get; init; } // active | draft | retired
    public string? Description { get; init; }
    public List<string>? FhirVersion { get; init; }
    public required List<FhirPathRule> FhirPathRules { get; init; }
    public ParameterConfiguration? Parameters { get; init; }
    public KeyReference? KeyReference { get; init; }
}

public record FhirPathRule
{
    public required string Path { get; init; }
    public required string Method { get; init; }
    public string? Description { get; init; }
}

public record KeyReference
{
    // Reference to external key vault (not stored directly in config)
    public string? CryptoHashKey { get; init; }
    public string? DateShiftKey { get; init; }
    public string? EncryptKey { get; init; }
}
```

**2. AnonymizationConfigLoader**:
```csharp
// File: src/Ignixa.Application/Features/Export/AnonymizationConfigLoader.cs
public class AnonymizationConfigLoader
{
    private readonly IFhirRepository _repository;
    private readonly ILogger<AnonymizationConfigLoader> _logger;

    public async Task<ISourceNode?> LoadAnonymizationConfigAsync(
        int tenantId,
        string configId,
        CancellationToken cancellationToken)
    {
        var resourceKey = new ResourceKey("AnonymizationConfig", configId);
        var searchResult = await _repository.GetAsync(
            tenantId,
            resourceKey,
            cancellationToken);

        if (searchResult == null)
        {
            _logger.LogWarning(
                "AnonymizationConfig not found: TenantId={TenantId}, ConfigId={ConfigId}",
                tenantId,
                configId);
            return null;
        }

        return searchResult.Resource;
    }
}
```

**3. Export Pipeline Integration**:
```csharp
// File: src/Ignixa.Application.BackgroundOperations/Export/Activities/ExportWorkerActivity.cs

// Add to ExecuteAsync method (after ViewDefinition check):
IAnonymizerEngine? anonymizerEngine = null;

if (!string.IsNullOrEmpty(input.AnonymizationConfigId))
{
    _logger.LogInformation(
        "Loading AnonymizationConfig: Job={JobId}, ConfigId={ConfigId}",
        input.JobId,
        input.AnonymizationConfigId);

    // Load config from repository
    var configNode = await _anonymizationConfigLoader.LoadAnonymizationConfigAsync(
        input.TenantId,
        input.AnonymizationConfigId,
        CancellationToken.None);

    if (configNode == null)
    {
        throw new InvalidOperationException(
            $"AnonymizationConfig '{input.AnonymizationConfigId}' not found");
    }

    // Parse config JSON into AnonymizerOptions
    var configJson = configNode.ToJson();
    var anonymizerOptions = AnonymizerOptionsLoader.Load(configJson);

    // Resolve keys from vault (not stored in config)
    await ResolveKeysFromVaultAsync(anonymizerOptions, input.TenantId, cancellationToken);

    // Initialize anonymizer engine
    var fhirVersion = FhirSpecificationExtensions.FromVersionString(tenantConfig.FhirVersion);
    var structureProvider = _fhirVersionContext.GetSchemaProvider(fhirVersion, input.TenantId);

    anonymizerEngine = new AnonymizerEngine(
        anonymizerOptions,
        structureProvider,
        _loggerFactory);
}

// Consumer: Process resources (add anonymization step)
await foreach (var resource in channel.Reader.ReadAllAsync(CancellationToken.None))
{
    SearchEntryResult processedResource = resource;

    // Apply anonymization if configured
    if (anonymizerEngine != null)
    {
        var element = resource.Resource.ToElement(structureProvider);
        var anonymizedElement = await anonymizerEngine.AnonymizeAsync(
            element,
            CancellationToken.None);

        // Convert back to SearchEntryResult
        processedResource = new SearchEntryResult(
            ResourceType: resource.ResourceType,
            ResourceId: resource.ResourceId,
            VersionId: resource.VersionId,
            LastUpdated: resource.LastUpdated,
            Resource: anonymizedElement.ToSourceNode(),
            SurrogateId: resource.SurrogateId);
    }

    // Write (possibly anonymized) resource
    await writer.WriteResourceAsync(processedResource, CancellationToken.None);
    resourcesExported++;
}
```

**4. Export Endpoint Enhancement**:
```csharp
// File: src/Ignixa.Api/Endpoints/ExportEndpoints.cs

// Add _anonymize parameter to $export
endpoints.MapPost("/$export", async (
    HttpContext context,
    IMediator mediator,
    CancellationToken cancellationToken) =>
{
    var queryParams = context.Request.Query;
    var anonymizeConfigId = queryParams["_anonymize"].FirstOrDefault();

    var command = new CreateExportJobCommand(
        // ... existing parameters ...
        AnonymizationConfigId: anonymizeConfigId
    );

    var result = await mediator.SendAsync(command, cancellationToken);
    return Results.Accepted($"/$export-status/{result.JobId}");
});
```

**Feasibility**: ✅ Highly feasible and architecturally sound

**Pros**:
- ✅ FHIR-native resource management (CRUD, search, versioning)
- ✅ Multi-tenant isolation (standard repository isolation)
- ✅ Audit trail via resource history
- ✅ Discoverable via CapabilityStatement
- ✅ Secrets managed externally (key vault references)
- ✅ Streaming integration (no batch overhead)
- ✅ Consistent with ViewDefinition pattern
- ✅ Works with existing Anonymizer library
- ✅ Supports IG loading pattern (future)

**Cons**:
- ❌ Requires custom resource support (but ViewDefinition investigation already covers this)
- ❌ Requires StructureDefinition for validation (can reuse IG loading infrastructure)
- ❌ Moderate implementation complexity

**Complexity**: 3/5 (moderate - leverages existing patterns)

**Time Estimate**: 24-32 hours (3-4 days)

**Recommendation**: ✅ **HIGHLY RECOMMENDED** - FHIR-native, scalable, secure

---

### Approach C: Built-in `$anonymize` Operation

**Description**: Add a dedicated `$anonymize` operation for on-demand anonymization (separate from export).

**Architecture**:
```
POST /Patient/$anonymize?config=hipaa-safe-harbor
    ↓
Load AnonymizationConfig
    ↓
Search all Patient resources
    ↓
Anonymize each resource
    ↓
Return Bundle with anonymized resources
```

**Implementation**:
```csharp
// File: src/Ignixa.Api/Endpoints/AnonymizationEndpoints.cs
endpoints.MapPost("/{resourceType}/$anonymize", async (
    string resourceType,
    HttpContext context,
    IMediator mediator,
    CancellationToken cancellationToken) =>
{
    var configId = context.Request.Query["config"].FirstOrDefault();

    var command = new AnonymizeResourcesCommand(
        ResourceType: resourceType,
        AnonymizationConfigId: configId
    );

    var result = await mediator.SendAsync(command, cancellationToken);
    return Results.Ok(result.Bundle);
});
```

**Feasibility**: ✅ Viable but lower priority

**Pros**:
- ✅ On-demand anonymization without export
- ✅ RESTful operation
- ✅ Useful for testing/debugging configs

**Cons**:
- ❌ Not part of FHIR core spec (custom operation)
- ❌ Requires additional implementation beyond export
- ❌ Less common use case than export-time anonymization
- ❌ May encourage storing de-identified data in server

**Complexity**: 3/5 (moderate)

**Time Estimate**: 16-24 hours

**Recommendation**: ⚠️ Optional enhancement - implement after export integration

---

## Recommended Approach: AnonymizationConfig as FHIR Resource

### Decision Rationale

**Why This is the Best Choice**:

1. **Alignment with SQL-on-FHIR Pattern**:
   - ViewDefinition already demonstrates this pattern
   - Reuses IG loading infrastructure (from ViewDefinition investigation)
   - Both are "transformation configurations as FHIR resources"

2. **FHIR-Native Benefits**:
   - Standard CRUD operations (POST, GET, PUT, DELETE)
   - Search support (by name, status, version)
   - Resource versioning and history
   - Multi-tenant isolation
   - CapabilityStatement advertisement

3. **Security Advantages**:
   - Secrets stored in key vault (not in config)
   - Configuration separated from credentials
   - Audit trail for config changes
   - Per-tenant access control

4. **Integration Simplicity**:
   - Minimal changes to export pipeline
   - Reuses existing repository infrastructure
   - Streaming-friendly (no batch overhead)
   - Compatible with ViewDefinition (can use both)

5. **Future-Proof**:
   - Works with IG loading pattern
   - Can be packaged in custom IGs
   - Supports FHIR R6 Additional Resources pattern

### Trade-offs

**What We Gain**:
- ✅ FHIR-native configuration management
- ✅ Multi-tenant, versioned, auditable configs
- ✅ Secure secret management
- ✅ Streaming export integration
- ✅ Discoverable via REST API

**What We Sacrifice**:
- ❌ Implementation complexity (3-4 days vs. simple file approach)
- ❌ Requires custom resource support (but ViewDefinition already needs this)
- ❌ Requires StructureDefinition for validation

**Assessment**: The trade-offs are acceptable. The upfront investment pays dividends in security, maintainability, and FHIR compliance.

---

## Implementation Plan

### Phase 1: AnonymizationConfig Resource Foundation (12-16 hours)

**Goal**: Enable AnonymizationConfig as a FHIR resource with CRUD operations

**Tasks**:
1. **Create AnonymizationConfig Domain Model** (2-3 hours)
   - Record types for AnonymizationConfig, FhirPathRule, KeyReference
   - Serialization/deserialization support

2. **Create AnonymizationConfigLoader** (3-4 hours)
   - Load configs from repository
   - Parse JSON into AnonymizerOptions
   - Key resolution from vault

3. **Create AnonymizationConfig StructureDefinition** (3-4 hours)
   - Define resource structure
   - Validation rules
   - Register with IG loading (if available) or CompositeSchemaProvider

4. **Test CRUD Operations** (4-5 hours)
   - Create, read, update, delete configs
   - Versioning and history
   - Multi-tenant isolation

**Deliverables**:
- `src/Ignixa.Domain/Models/AnonymizationConfig.cs`
- `src/Ignixa.Application/Features/Export/AnonymizationConfigLoader.cs`
- `src/Ignixa.Application/Features/Export/KeyVaultResolver.cs`
- `docs/rest/anonymization-config.http` (REST examples)
- `test/Ignixa.Api.Tests/AnonymizationConfigCrudTests.cs`

**Testing**:
```csharp
[Fact]
public async Task GivenAnonymizationConfig_WhenCreating_ThenStoresInRepository()
{
    // Arrange
    var config = new AnonymizationConfig
    {
        ResourceType = "AnonymizationConfig",
        Id = "test-config",
        Name = "test_config",
        Status = "active",
        FhirPathRules = [
            new FhirPathRule { Path = "Patient.identifier", Method = "cryptoHash" }
        ]
    };

    // Act
    var response = await _client.PostAsync("/AnonymizationConfig", JsonContent.Create(config));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.Created);
    var location = response.Headers.Location;
    location.Should().NotBeNull();
}
```

---

### Phase 2: Export Pipeline Integration (8-12 hours)

**Goal**: Integrate anonymization into export worker activity

**Tasks**:
1. **Modify ExportWorkerInput** (1-2 hours)
   - Add AnonymizationConfigId parameter
   - Update orchestration to pass config ID

2. **Enhance ExportWorkerActivity** (4-6 hours)
   - Load AnonymizationConfig from repository
   - Initialize AnonymizerEngine with config
   - Apply anonymization in streaming pipeline
   - Handle errors and logging

3. **Update Export Endpoint** (2-3 hours)
   - Add `_anonymize` query parameter
   - Pass config ID to CreateExportJobCommand
   - Validate config exists before starting job

4. **E2E Export Tests** (2-3 hours)
   - Test export with anonymization
   - Verify anonymized output
   - Test error handling

**Deliverables**:
- `src/Ignixa.Application.BackgroundOperations/Export/Models/ExportWorkerInput.cs` (modified)
- `src/Ignixa.Application.BackgroundOperations/Export/Activities/ExportWorkerActivity.cs` (modified)
- `src/Ignixa.Api/Endpoints/ExportEndpoints.cs` (modified)
- `test/Ignixa.Api.Tests/ExportWithAnonymizationTests.cs`

**Testing**:
```csharp
[Fact]
public async Task GivenExportWithAnonymization_WhenExporting_ThenOutputIsAnonymized()
{
    // Arrange
    await CreateAnonymizationConfig("hipaa-safe-harbor");
    await CreateTestPatients(count: 100);

    // Act
    var response = await _client.PostAsync(
        "/$export?_type=Patient&_anonymize=hipaa-safe-harbor",
        null);

    // Wait for export to complete
    var jobId = ExtractJobIdFromLocation(response.Headers.Location);
    await WaitForExportCompletion(jobId);

    // Assert
    var exportedFiles = await GetExportedFiles(jobId);
    var patients = await ParseNdjsonFile<Patient>(exportedFiles["Patient"]);

    // Verify anonymization was applied
    patients.Should().AllSatisfy(p =>
    {
        p.Identifier.Should().AllSatisfy(id =>
            id.Value.Should().MatchRegex("^[a-f0-9]{64}$")); // Hashed
        p.BirthDate.Should().BeNull(); // Redacted
        p.Name.Should().BeEmpty(); // Redacted
    });
}
```

---

### Phase 3: Key Management & Security (4-8 hours)

**Goal**: Secure secret management for anonymization keys

**Tasks**:
1. **Implement KeyVaultResolver** (3-4 hours)
   - Resolve key references from Azure Key Vault
   - Support multiple key types (hash, dateshift, encrypt)
   - Caching and performance optimization

2. **Security Enhancements** (2-3 hours)
   - Ensure keys never logged
   - Validate key permissions
   - Audit key usage

3. **Documentation** (1-2 hours)
   - Key management guide
   - Security best practices
   - Example configurations

**Deliverables**:
- `src/Ignixa.Application/Features/Export/KeyVaultResolver.cs`
- `docs/user-guides/anonymization-key-management.md`
- `docs/rest/anonymization-config.http` (updated with key examples)

**Key Vault Integration**:
```csharp
public class KeyVaultResolver
{
    private readonly SecretClient _secretClient;
    private readonly ILogger<KeyVaultResolver> _logger;

    public async Task ResolveKeysAsync(
        AnonymizerOptions options,
        int tenantId,
        CancellationToken cancellationToken)
    {
        // Resolve cryptoHashKey reference
        if (options.KeyReference?.CryptoHashKey?.StartsWith("vault://") == true)
        {
            var keyName = ExtractKeyName(options.KeyReference.CryptoHashKey);
            var secret = await _secretClient.GetSecretAsync(keyName, cancellationToken: cancellationToken);

            options.Parameters.CryptoHashKey = secret.Value.Value;

            _logger.LogInformation(
                "Resolved cryptoHashKey from vault: TenantId={TenantId}, KeyName={KeyName}",
                tenantId,
                keyName);
        }

        // Similar for dateShiftKey, encryptKey...
    }
}
```

---

## File Modifications

### Files to Create

**Application Layer** (~800 lines total):

1. **AnonymizationConfig.cs** (~150 lines)
   - Domain model for AnonymizationConfig resource

2. **AnonymizationConfigLoader.cs** (~200 lines)
   - Load configs from repository
   - Parse into AnonymizerOptions

3. **KeyVaultResolver.cs** (~150 lines)
   - Resolve key references from Azure Key Vault

4. **AnonymizationConfigStructureDefinition.json** (~200 lines)
   - StructureDefinition for validation

5. **ExportWithAnonymizationTests.cs** (~300 lines)
   - E2E tests for export with anonymization

**Documentation** (~600 lines total):

6. **anonymization-config.http** (~200 lines)
   - REST API examples

7. **anonymization-key-management.md** (~400 lines)
   - Key management guide

### Files to Modify

1. **ExportWorkerInput.cs** (add ~10 lines)
   - Add AnonymizationConfigId property

2. **ExportWorkerActivity.cs** (add ~60 lines)
   - Load and apply anonymization

3. **ExportEndpoints.cs** (add ~20 lines)
   - Add _anonymize query parameter

4. **CreateExportJobCommand.cs** (add ~10 lines)
   - Add AnonymizationConfigId property

5. **ExportOrchestrationInput.cs** (add ~10 lines)
   - Pass config ID to workers

**Total New Code**: ~1,400 lines
**Total Modified Code**: ~110 lines
**Total**: ~1,510 lines

---

## Security Considerations

### Secret Management

**DO NOT store secrets in AnonymizationConfig resources**:
```json
{
  "resourceType": "AnonymizationConfig",
  "keyReference": {
    "cryptoHashKey": "vault://keys/tenant-1-hash-key",  // ✅ Reference only
    "dateShiftKey": "vault://keys/tenant-1-dateshift-key"
  }
}
```

**Key Resolution at Runtime**:
1. Export worker loads AnonymizationConfig
2. KeyVaultResolver resolves key references
3. Keys injected into AnonymizerEngine (in-memory only)
4. Keys never logged or persisted

**Key Rotation**:
- **Challenge**: Anonymization requires determinism for linkage
- **Solution**: Use versioned key names (`tenant-1-hash-key-v2`)
- **Trade-off**: Old data can't be re-linked to new anonymizations

### Access Control

**Per-Tenant Keys**:
- Each tenant has separate keys in Key Vault
- Worker activity resolves tenant-specific keys
- Multi-tenant isolation maintained

**RBAC for AnonymizationConfig**:
- Only admins can create/update configs
- Users can read configs (metadata only, not keys)
- Export operations require special permissions

---

## Integration with ViewDefinition

**Can Use Both Together**:
```bash
# Export with both ViewDefinition transformation AND anonymization
POST /$export?_type=Patient&_outputFormat=application/vnd.apache.parquet&_viewDefinition=patient-demographics&_anonymize=hipaa-safe-harbor
```

**Processing Order**:
```
Search Results → ViewDefinition Transform → Anonymization → Parquet Output
```

**Implementation**:
```csharp
// ExportWorkerActivity.cs processing pipeline
await foreach (var resource in channel.Reader.ReadAllAsync(...))
{
    SearchEntryResult processedResource = resource;

    // Step 1: Apply ViewDefinition (if specified)
    if (writer is ViewDefinitionExportStreamWriter viewDefWriter)
    {
        // ViewDefinition handles transformation internally
        // (already implemented)
    }

    // Step 2: Apply Anonymization (if specified)
    if (anonymizerEngine != null)
    {
        var element = processedResource.Resource.ToElement(structureProvider);
        var anonymizedElement = await anonymizerEngine.AnonymizeAsync(element, ...);
        processedResource = ConvertToSearchEntryResult(anonymizedElement);
    }

    // Step 3: Write output
    await writer.WriteResourceAsync(processedResource, ...);
}
```

---

## Comparison to Microsoft FHIR Server

| Aspect | Microsoft FHIR Server | Ignixa Recommendation |
|--------|----------------------|----------------------|
| **Config Storage** | Azure Blob Storage | FHIR repository (AnonymizationConfig resource) |
| **Discoverability** | Not discoverable | REST API (GET /AnonymizationConfig) |
| **Versioning** | Manual file management | FHIR resource versioning |
| **Multi-Tenancy** | Global configs | Per-tenant configs |
| **Secret Management** | Secrets in config files | Key Vault references |
| **Integration** | Azure Data Factory (batch) | Streaming export pipeline |
| **Performance** | Batch processing overhead | Streaming (no overhead) |
| **FHIR Compliance** | External pattern | FHIR-native resources |

---

## Alternative: SQL-on-FHIR ViewDefinition for De-identification

**Could ViewDefinition Handle Anonymization?**

ViewDefinition supports column transformations, but has limitations for de-identification:

**What ViewDefinition CAN Do**:
```json
{
  "resourceType": "ViewDefinition",
  "select": [{
    "column": [
      {
        "name": "patient_id_hash",
        "path": "id.hashSHA256()",  // If FHIRPath extension exists
        "type": "string"
      },
      {
        "name": "age_group",
        "path": "birthDate.toQuantity('years').value div 10 * 10",
        "type": "integer",
        "description": "Age generalized to decade"
      }
    ]
  }]
}
```

**What ViewDefinition CANNOT Do**:
- ❌ HMAC-SHA256 with secret keys (not in FHIRPath spec)
- ❌ AES encryption
- ❌ Date shifting with deterministic offsets
- ❌ HIPAA Safe Harbor rules (restricted ZCTAs)
- ❌ Security tagging (meta.security labels)

**Conclusion**: ViewDefinition is for data transformation, not cryptographic anonymization. AnonymizationConfig is the right abstraction.

---

## ADR Recommendation

**Yes**, this warrants an Architecture Decision Record.

**Recommended ADR**:
```
File: docs/adr/ADR-2603-anonymization-export-integration.md

Title: ADR-2603: AnonymizationConfig Resource for Export Pipeline Integration

Status: Proposed → Accepted (after implementation)

Context:
- Ignixa.Anonymizer library exists (ADR-2602) but not integrated with export
- Microsoft FHIR server used external blob storage for configs (problematic)
- Need FHIR-native configuration management
- SQL-on-FHIR ViewDefinition demonstrates custom resource pattern

Decision:
Create AnonymizationConfig as a custom FHIR resource to:
1. Store anonymization configurations in FHIR repository
2. Enable CRUD operations via REST API
3. Integrate with export pipeline via _anonymize parameter
4. Manage secrets via Key Vault references (not in config)
5. Support multi-tenant isolation and versioning

Consequences:
Positive:
- FHIR-native resource management
- Audit trail via resource history
- Discoverable via CapabilityStatement
- Secure secret management (Key Vault)
- Streaming integration (no batch overhead)
- Consistent with ViewDefinition pattern

Negative:
- Implementation complexity (3-4 days)
- Requires custom resource support
- Requires StructureDefinition for validation

Implementation:
- See docs/investigations/anonymization-export-integration.md
- 3 phases over 24-32 hours
- Leverages existing IG loading pattern

References:
- ADR-2602: FHIR Anonymizer Library
- ViewDefinition Investigation: docs/features/serialization/investigations/viewdefinition-support.md
- Microsoft FHIR Server De-ID Export: https://learn.microsoft.com/en-us/azure/healthcare-apis/fhir/deidentified-export
```

---

## References

**Ignixa Documentation**:
- ADR-2602: FHIR Anonymizer Library (`docs/adr/adr-2602-anonymizer-library.md`)
- ViewDefinition Investigation (`docs/features/serialization/investigations/viewdefinition-support.md`)
- Export Pipeline (`src/Application/Ignixa.Application.BackgroundOperations/Export/`)

**Microsoft FHIR Server**:
- De-identified Export: https://learn.microsoft.com/en-us/azure/healthcare-apis/fhir/deidentified-export
- FHIR-Tools-for-Anonymization: https://github.com/microsoft/FHIR-Tools-for-Anonymization
- Azure Data Factory Integration: https://github.com/microsoft/FHIR-Tools-for-Anonymization/blob/master/docs/FHIR-azure-data-factory-integration.md

**SQL-on-FHIR**:
- ViewDefinition Spec: https://build.fhir.org/ig/FHIR/sql-on-fhir-v2/StructureDefinition-ViewDefinition.html
- SQL-on-FHIR v2 IG: https://build.fhir.org/ig/FHIR/sql-on-fhir-v2/

**HIPAA & De-identification**:
- HIPAA Safe Harbor: https://www.hhs.gov/hipaa/for-professionals/privacy/special-topics/de-identification/index.html
- GDPR Article 6: https://gdpr-info.eu/art-6-gdpr/

---

## Conclusion

**Recommended Approach**: AnonymizationConfig as Custom FHIR Resource

This approach:
1. **Solves the core problem**: FHIR-native config management (no external blob storage)
2. **Aligns with existing patterns**: Consistent with ViewDefinition and IG loading
3. **Provides security**: Secrets managed via Key Vault, not in configs
4. **Enables streaming**: No batch overhead, integrates directly with export pipeline
5. **Future-proof**: Works with IG loading pattern, supports FHIR R6
6. **Reasonable effort**: 3-4 days (24-32 hours)

**Next Steps**:
1. Create ADR-2603-anonymization-export-integration.md
2. Implement Phase 1 (AnonymizationConfig resource foundation)
3. Implement Phase 2 (Export pipeline integration)
4. Implement Phase 3 (Key management & security)
5. Document and deploy

**Decision**: Proceed with AnonymizationConfig as custom FHIR resource, integrated into export pipeline via `_anonymize` parameter.

---

**End of Investigation**
