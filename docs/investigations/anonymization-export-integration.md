# Investigation: De-Identification (DeId) Integration in Export Pipeline

**Feature**: export / de-identification
**Status**: Proposed
**Created**: 2026-04-08

---

## Executive Summary

This investigation analyzes how to integrate the Ignixa.Anonymizer library (de-identification functionality) into the bulk export pipeline (`$export` operation) using FHIR-native configuration management, avoiding the external blob storage dependency used in Microsoft's original FHIR server implementation.

**Terminology Note**: We use "de-identification" (DeId) as the primary term, as it's more accurate for HIPAA Safe Harbor compliance. The library namespace remains `Ignixa.Anonymizer` for backward compatibility, but user-facing resources and APIs use "DeIdentification".

**Key Finding**: The old Microsoft FHIR server approach stored de-identification configurations in Azure Blob Storage, requiring external infrastructure dependencies. By contrast, SQL-on-FHIR's ViewDefinition demonstrates a superior pattern: storing transformation configurations as first-class FHIR resources.

**Recommendation**: Create `DeIdentificationConfig` as a custom FHIR resource (similar to ViewDefinition) that can be stored, versioned, and managed through standard FHIR REST APIs. Keys are stored encrypted within the resource using tenant-specific encryption, avoiding external key vault dependencies.

**Estimated Effort**: 20-26 hours (2.5-3.25 days) over 3 phases

---

## Problem Statement

### Current State

**What Works Today**:
1. **Ignixa.Anonymizer Library** (ADR-2602): Standalone de-identification with FHIRPath-based rules
2. **Bulk Export Pipeline**: High-performance streaming export via DurableTask orchestration
3. **ViewDefinition Integration**: SQL-on-FHIR transformations during export (Parquet output)

**What's Missing**:
- No integration between Anonymizer (de-identification) and `$export` operation
- No FHIR-native way to store/manage de-identification configurations
- No support for `$export?_deid=true` or similar parameter

### Microsoft FHIR Server Approach (Legacy Pattern)

The Microsoft FHIR server's de-identified export relied on external blob storage:

```
Azure Data Factory Pipeline:
1. Export FHIR data to Blob Storage (source container)
2. Load de-identification config from Blob Storage
3. Run de-identification tool (batch process)
4. Write anonymized data to Blob Storage (destination container)
```

**Configuration Storage**:
```json
// Stored in: https://mystorageaccount.blob.core.windows.net/configs/de-identification-config.json
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
# 1. Create de-identification configuration as a FHIR resource
POST /DeIdentificationConfig
Content-Type: application/fhir+json
{
  "resourceType": "DeIdentificationConfig",
  "name": "hipaa-safe-harbor",
  "status": "active",
  "fhirPathRules": [
    {"path": "Patient.identifier", "method": "cryptoHash"},
    {"path": "Patient.birthDate", "method": "redact"}
  ],
  "parameters": {
    "cryptoHashKey": "base64-encrypted-key-here",
    "dateShiftKey": "base64-encrypted-key-here"
  }
}

# 2. Search for available configs
GET /DeIdentificationConfig?status=active

# 3. Reference in export request
POST /$export?_deid=hipaa-safe-harbor
Prefer: respond-async

# 4. Export pipeline streams through de-identification
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

We analyzed three approaches for integrating de-identification into the export pipeline:

### Approach A: External Configuration Files (Microsoft Pattern)

**Description**: Store de-identification configs in external storage (filesystem or blob storage).

**Architecture**:
```
Export Request: POST /$export?_deid=true
    ↓
Load config from: /configs/de-identification-config.json
    ↓
Export Pipeline → Anonymizer (inline) → Output
```

**Implementation**:
```csharp
// ExportWorkerActivity.cs
var configPath = Path.Combine(_configDirectory, "de-identification-config.json");
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

### Approach B: DeIdentificationConfig as Custom FHIR Resource ⭐ **RECOMMENDED**

**Description**: Create `DeIdentificationConfig` as a custom FHIR resource (similar to ViewDefinition from SQL-on-FHIR).

**Architecture**:
```
Admin: Create DeIdentificationConfig resource
POST /DeIdentificationConfig → Store in FHIR repository
    ↓
User: Request export with de-identification
POST /$export?_deid=hipaa-safe-harbor
    ↓
Export Pipeline:
1. Load DeIdentificationConfig from repository
2. Initialize AnonymizerEngine with config
3. Stream resources through anonymizer
4. Write anonymized output
```

**Resource Definition**:
```json
{
  "resourceType": "DeIdentificationConfig",
  "id": "hipaa-safe-harbor",
  "meta": {
    "versionId": "2",
    "lastUpdated": "2026-04-08T10:00:00Z"
  },
  "url": "http://example.org/fhir/DeIdentificationConfig/hipaa-safe-harbor",
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
    "restrictedZipCodeTabulationAreas": ["036", "059", "102"],
    "cryptoHashKey": "ENC:AES256:base64-encrypted-value-here==",
    "dateShiftKey": "ENC:AES256:base64-encrypted-value-here=="
  }
}
```

**Implementation**:

**1. DeIdentificationConfig Resource Model**:
```csharp
// File: src/Ignixa.Domain/Models/DeIdentificationConfig.cs
public record DeIdentificationConfig
{
    public required string ResourceType { get; init; } = "DeIdentificationConfig";
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Status { get; init; } // active | draft | retired
    public string? Description { get; init; }
    public List<string>? FhirVersion { get; init; }
    public required List<FhirPathRule> FhirPathRules { get; init; }
    public ParameterConfiguration? Parameters { get; init; }
}

public record FhirPathRule
{
    public required string Path { get; init; }
    public required string Method { get; init; }
    public string? Description { get; init; }
}

// Note: Keys are stored encrypted in ParameterConfiguration.CryptoHashKey/DateShiftKey
// Format: "ENC:AES256:base64-encrypted-value"
// Decryption uses tenant-specific encryption key (managed by server)
```

**2. DeIdentificationConfigLoader**:
```csharp
// File: src/Ignixa.Application/Features/Export/DeIdentificationConfigLoader.cs
public class DeIdentificationConfigLoader
{
    private readonly IFhirRepository _repository;
    private readonly ITenantEncryptionService _encryptionService;
    private readonly ILogger<DeIdentificationConfigLoader> _logger;

    public async Task<AnonymizerOptions?> LoadDeIdentificationConfigAsync(
        int tenantId,
        string configId,
        CancellationToken cancellationToken)
    {
        var resourceKey = new ResourceKey("DeIdentificationConfig", configId);
        var searchResult = await _repository.GetAsync(
            tenantId,
            resourceKey,
            cancellationToken);

        if (searchResult == null)
        {
            _logger.LogWarning(
                "DeIdentificationConfig not found: TenantId={TenantId}, ConfigId={ConfigId}",
                tenantId,
                configId);
            return null;
        }

        // Parse config JSON into AnonymizerOptions
        var configJson = searchResult.Resource.ToJson();
        var anonymizerOptions = AnonymizerOptionsLoader.Load(configJson);

        // Decrypt keys using tenant-specific encryption
        await DecryptKeysAsync(anonymizerOptions.Parameters, tenantId, cancellationToken);

        return anonymizerOptions;
    }

    private async Task DecryptKeysAsync(
        ParameterConfiguration parameters,
        int tenantId,
        CancellationToken cancellationToken)
    {
        // Decrypt cryptoHashKey if encrypted
        if (parameters.CryptoHashKey?.StartsWith("ENC:") == true)
        {
            parameters.CryptoHashKey = await _encryptionService.DecryptAsync(
                tenantId,
                parameters.CryptoHashKey,
                cancellationToken);
        }

        // Decrypt dateShiftKey if encrypted
        if (parameters.DateShiftKey?.StartsWith("ENC:") == true)
        {
            parameters.DateShiftKey = await _encryptionService.DecryptAsync(
                tenantId,
                parameters.DateShiftKey,
                cancellationToken);
        }

        // Decrypt encryptKey if encrypted
        if (parameters.EncryptKey?.StartsWith("ENC:") == true)
        {
            parameters.EncryptKey = await _encryptionService.DecryptAsync(
                tenantId,
                parameters.EncryptKey,
                cancellationToken);
        }
    }
}

// Tenant-specific encryption service (uses DPAPI or similar per-tenant keys)
public interface ITenantEncryptionService
{
    Task<string> EncryptAsync(int tenantId, string plaintext, CancellationToken cancellationToken);
    Task<string> DecryptAsync(int tenantId, string ciphertext, CancellationToken cancellationToken);
}
```

**3. Export Pipeline Integration**:
```csharp
// File: src/Ignixa.Application.BackgroundOperations/Export/Activities/ExportWorkerActivity.cs

// Add to ExecuteAsync method (after ViewDefinition check):
IAnonymizerEngine? anonymizerEngine = null;

if (!string.IsNullOrEmpty(input.DeIdentificationConfigId))
{
    _logger.LogInformation(
        "Loading DeIdentificationConfig: Job={JobId}, ConfigId={ConfigId}",
        input.JobId,
        input.DeIdentificationConfigId);

    // Load config from repository (keys are decrypted automatically)
    var anonymizerOptions = await _deIdentificationConfigLoader.LoadDeIdentificationConfigAsync(
        input.TenantId,
        input.DeIdentificationConfigId,
        CancellationToken.None);

    if (anonymizerOptions == null)
    {
        throw new InvalidOperationException(
            $"DeIdentificationConfig '{input.DeIdentificationConfigId}' not found");
    }

    // Initialize anonymizer engine
    var fhirVersion = FhirSpecificationExtensions.FromVersionString(tenantConfig.FhirVersion);
    var structureProvider = _fhirVersionContext.GetSchemaProvider(fhirVersion, input.TenantId);

    anonymizerEngine = new AnonymizerEngine(
        anonymizerOptions,
        structureProvider,
        _loggerFactory);
}

// Consumer: Process resources (add de-identification step)
await foreach (var resource in channel.Reader.ReadAllAsync(CancellationToken.None))
{
    SearchEntryResult processedResource = resource;

    // Apply de-identification if configured
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

// Add _deid parameter to $export
endpoints.MapPost("/$export", async (
    HttpContext context,
    IMediator mediator,
    CancellationToken cancellationToken) =>
{
    var queryParams = context.Request.Query;
    var anonymizeConfigId = queryParams["_deid"].FirstOrDefault();

    var command = new CreateExportJobCommand(
        // ... existing parameters ...
        DeIdentificationConfigId: anonymizeConfigId
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

**Description**: Add a dedicated `$anonymize` operation for on-demand de-identification (separate from export).

**Architecture**:
```
POST /Patient/$anonymize?config=hipaa-safe-harbor
    ↓
Load DeIdentificationConfig
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
        DeIdentificationConfigId: configId
    );

    var result = await mediator.SendAsync(command, cancellationToken);
    return Results.Ok(result.Bundle);
});
```

**Feasibility**: ✅ Viable but lower priority

**Pros**:
- ✅ On-demand de-identification without export
- ✅ RESTful operation
- ✅ Useful for testing/debugging configs

**Cons**:
- ❌ Not part of FHIR core spec (custom operation)
- ❌ Requires additional implementation beyond export
- ❌ Less common use case than export-time de-identification
- ❌ May encourage storing de-identified data in server

**Complexity**: 3/5 (moderate)

**Time Estimate**: 16-24 hours

**Recommendation**: ⚠️ Optional enhancement - implement after export integration

---

## Recommended Approach: DeIdentificationConfig as FHIR Resource

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

### Phase 1: DeIdentificationConfig Resource Foundation (12-16 hours)

**Goal**: Enable DeIdentificationConfig as a FHIR resource with CRUD operations

**Tasks**:
1. **Create DeIdentificationConfig Domain Model** (2-3 hours)
   - Record types for DeIdentificationConfig, FhirPathRule, KeyReference
   - Serialization/deserialization support

2. **Create DeIdentificationConfigLoader** (3-4 hours)
   - Load configs from repository
   - Parse JSON into AnonymizerOptions
   - Key resolution from vault

3. **Create DeIdentificationConfig StructureDefinition** (3-4 hours)
   - Define resource structure
   - Validation rules
   - Register with IG loading (if available) or CompositeSchemaProvider

4. **Test CRUD Operations** (4-5 hours)
   - Create, read, update, delete configs
   - Versioning and history
   - Multi-tenant isolation

**Deliverables**:
- `src/Ignixa.Domain/Models/DeIdentificationConfig.cs`
- `src/Ignixa.Application/Features/Export/DeIdentificationConfigLoader.cs`
- `src/Ignixa.Application/Features/Export/TenantEncryptionService.cs`
- `docs/rest/de-identification-config.http` (REST examples)
- `test/Ignixa.Api.Tests/DeIdentificationConfigCrudTests.cs`

**Testing**:
```csharp
[Fact]
public async Task GivenDeIdentificationConfig_WhenCreating_ThenStoresInRepository()
{
    // Arrange
    var config = new DeIdentificationConfig
    {
        ResourceType = "DeIdentificationConfig",
        Id = "test-config",
        Name = "test_config",
        Status = "active",
        FhirPathRules = [
            new FhirPathRule { Path = "Patient.identifier", Method = "cryptoHash" }
        ]
    };

    // Act
    var response = await _client.PostAsync("/DeIdentificationConfig", JsonContent.Create(config));

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.Created);
    var location = response.Headers.Location;
    location.Should().NotBeNull();
}
```

---

### Phase 2: Export Pipeline Integration (8-12 hours)

**Goal**: Integrate de-identification into export worker activity

**Tasks**:
1. **Modify ExportWorkerInput** (1-2 hours)
   - Add DeIdentificationConfigId parameter
   - Update orchestration to pass config ID

2. **Enhance ExportWorkerActivity** (4-6 hours)
   - Load DeIdentificationConfig from repository
   - Initialize AnonymizerEngine with config
   - Apply de-identification in streaming pipeline
   - Handle errors and logging

3. **Update Export Endpoint** (2-3 hours)
   - Add `_deid` query parameter
   - Pass config ID to CreateExportJobCommand
   - Validate config exists before starting job

4. **E2E Export Tests** (2-3 hours)
   - Test export with de-identification
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
    await CreateDeIdentificationConfig("hipaa-safe-harbor");
    await CreateTestPatients(count: 100);

    // Act
    var response = await _client.PostAsync(
        "/$export?_type=Patient&_deid=hipaa-safe-harbor",
        null);

    // Wait for export to complete
    var jobId = ExtractJobIdFromLocation(response.Headers.Location);
    await WaitForExportCompletion(jobId);

    // Assert
    var exportedFiles = await GetExportedFiles(jobId);
    var patients = await ParseNdjsonFile<Patient>(exportedFiles["Patient"]);

    // Verify de-identification was applied
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

### Phase 3: Key Encryption & Security (4-6 hours)

**Goal**: Implement tenant-specific key encryption (no external dependencies)

**Tasks**:
1. **Implement TenantEncryptionService** (3-4 hours)
   - Use .NET Data Protection API (DPAPI) or AES-256 with tenant-specific keys
   - Keys derived from tenant ID + server secret (configured once at startup)
   - Support encryption/decryption with "ENC:AES256:" prefix

2. **Security Enhancements** (1-2 hours)
   - Ensure plaintext keys never logged
   - Validate encrypted key format
   - Audit key usage

3. **Documentation** (1-2 hours)
   - Key management guide
   - Security best practices
   - Example configurations

**Deliverables**:
- `src/Ignixa.Application/Features/Export/TenantEncryptionService.cs`
- `docs/user-guides/deid-key-management.md`
- `docs/rest/de-identification-config.http` (updated with encrypted key examples)

**Tenant Encryption Implementation**:
```csharp
public class TenantEncryptionService : ITenantEncryptionService
{
    private readonly byte[] _serverMasterKey; // Configured at startup from appsettings
    private readonly ILogger<TenantEncryptionService> _logger;

    public async Task<string> EncryptAsync(
        int tenantId,
        string plaintext,
        CancellationToken cancellationToken)
    {
        // Derive tenant-specific key from master key + tenant ID
        var tenantKey = DeriveKey(_serverMasterKey, tenantId);

        // Encrypt using AES-256-GCM
        using var aes = new AesGcm(tenantKey);
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];

        aes.Encrypt(nonce, Encoding.UTF8.GetBytes(plaintext), ciphertext, tag);

        // Format: ENC:AES256:base64(nonce||tag||ciphertext)
        var combined = nonce.Concat(tag).Concat(ciphertext).ToArray();
        return $"ENC:AES256:{Convert.ToBase64String(combined)}";
    }

    public async Task<string> DecryptAsync(
        int tenantId,
        string ciphertext,
        CancellationToken cancellationToken)
    {
        if (!ciphertext.StartsWith("ENC:AES256:"))
        {
            throw new InvalidOperationException("Invalid encrypted key format");
        }

        // Extract base64 payload
        var payload = Convert.FromBase64String(ciphertext.Substring(11));

        // Extract nonce, tag, ciphertext
        var nonceSize = AesGcm.NonceByteSizes.MaxSize;
        var tagSize = AesGcm.TagByteSizes.MaxSize;
        var nonce = payload[..nonceSize];
        var tag = payload.Skip(nonceSize).Take(tagSize).ToArray();
        var encrypted = payload.Skip(nonceSize + tagSize).ToArray();

        // Derive tenant-specific key
        var tenantKey = DeriveKey(_serverMasterKey, tenantId);

        // Decrypt using AES-256-GCM
        using var aes = new AesGcm(tenantKey);
        var plaintext = new byte[encrypted.Length];
        aes.Decrypt(nonce, encrypted, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }

    private static byte[] DeriveKey(byte[] masterKey, int tenantId)
    {
        // Use HKDF to derive tenant-specific key
        var info = Encoding.UTF8.GetBytes($"tenant:{tenantId}");
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, 32, null, info);
    }
}
```

**Configuration** (appsettings.json):
```json
{
  "DeIdentification": {
    "MasterEncryptionKey": "base64-encoded-256-bit-key-here"
  }
}
```

**Key Generation** (one-time setup):
```bash
# Generate a 256-bit master key
dotnet user-secrets set "DeIdentification:MasterEncryptionKey" "$(openssl rand -base64 32)"
```

---

## File Modifications

### Files to Create

**Application Layer** (~800 lines total):

1. **DeIdentificationConfig.cs** (~150 lines)
   - Domain model for DeIdentificationConfig resource

2. **DeIdentificationConfigLoader.cs** (~250 lines)
   - Load configs from repository
   - Parse into AnonymizerOptions
   - Decrypt keys using TenantEncryptionService

3. **TenantEncryptionService.cs** (~150 lines)
   - Encrypt/decrypt keys using AES-256-GCM
   - Derive tenant-specific keys from master key

4. **DeIdentificationConfigStructureDefinition.json** (~200 lines)
   - StructureDefinition for validation

5. **ExportWithAnonymizationTests.cs** (~300 lines)
   - E2E tests for export with de-identification

**Documentation** (~600 lines total):

6. **de-identification-config.http** (~200 lines)
   - REST API examples

7. **de-identification-key-management.md** (~400 lines)
   - Key management guide

### Files to Modify

1. **ExportWorkerInput.cs** (add ~10 lines)
   - Add DeIdentificationConfigId property

2. **ExportWorkerActivity.cs** (add ~60 lines)
   - Load and apply de-identification

3. **ExportEndpoints.cs** (add ~20 lines)
   - Add _deid query parameter

4. **CreateExportJobCommand.cs** (add ~10 lines)
   - Add DeIdentificationConfigId property

5. **ExportOrchestrationInput.cs** (add ~10 lines)
   - Pass config ID to workers

**Total New Code**: ~1,450 lines
**Total Modified Code**: ~110 lines
**Total**: ~1,560 lines

**Reduced Complexity**: No external dependencies (Azure Key Vault, etc.)

---

## Security Considerations

### Secret Management

**Store keys encrypted in DeIdentificationConfig resources**:
```json
{
  "resourceType": "DeIdentificationConfig",
  "parameters": {
    "cryptoHashKey": "ENC:AES256:base64-encrypted-value",  // ✅ Encrypted at rest
    "dateShiftKey": "ENC:AES256:base64-encrypted-value"
  }
}
```

**Key Encryption at Runtime**:
1. Admin creates DeIdentificationConfig with plaintext keys via API
2. Server encrypts keys using tenant-specific encryption before storing
3. Export worker loads DeIdentificationConfig and decrypts keys automatically
4. Keys used in-memory only, never logged or persisted in plaintext

**Key Derivation**:
- **Master Key**: Single server-wide secret (configured once in appsettings/user-secrets)
- **Tenant Keys**: Derived using HKDF(master_key, tenant_id)
- **Rotation**: Change master key + re-encrypt all configs (or use versioned master keys)

**No External Dependencies**:
- ✅ No Azure Key Vault required
- ✅ No external secret management service
- ✅ Uses built-in .NET cryptography (AES-256-GCM + HKDF)
- ✅ Tenant isolation via key derivation

### Access Control

**Per-Tenant Keys**:
- Each tenant has separate keys in Key Vault
- Worker activity resolves tenant-specific keys
- Multi-tenant isolation maintained

**RBAC for DeIdentificationConfig**:
- Only admins can create/update configs
- Users can read configs (metadata only, not keys)
- Export operations require special permissions

---

## Integration with ViewDefinition

**Can Use Both Together**:
```bash
# Export with both ViewDefinition transformation AND de-identification
POST /$export?_type=Patient&_outputFormat=application/vnd.apache.parquet&_viewDefinition=patient-demographics&_deid=hipaa-safe-harbor
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
| **Config Storage** | Azure Blob Storage | FHIR repository (DeIdentificationConfig resource) |
| **Discoverability** | Not discoverable | REST API (GET /DeIdentificationConfig) |
| **Versioning** | Manual file management | FHIR resource versioning |
| **Multi-Tenancy** | Global configs | Per-tenant configs |
| **Secret Management** | Secrets in config files | Keys encrypted in resource (tenant-specific encryption) |
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

**Conclusion**: ViewDefinition is for data transformation, not cryptographic de-identification. DeIdentificationConfig is the right abstraction.

---

## ADR Recommendation

**Yes**, this warrants an Architecture Decision Record.

**Recommended ADR**:
```
File: docs/adr/ADR-2603-de-identification-export-integration.md

Title: ADR-2603: DeIdentificationConfig Resource for Export Pipeline Integration

Status: Proposed → Accepted (after implementation)

Context:
- Ignixa.Anonymizer library exists (ADR-2602) but not integrated with export
- Microsoft FHIR server used external blob storage for configs (problematic)
- Need FHIR-native configuration management
- SQL-on-FHIR ViewDefinition demonstrates custom resource pattern

Decision:
Create DeIdentificationConfig as a custom FHIR resource to:
1. Store de-identification configurations in FHIR repository
2. Enable CRUD operations via REST API
3. Integrate with export pipeline via _deid parameter
4. Manage secrets using tenant-specific encryption (no external dependencies)
5. Support multi-tenant isolation and versioning

Consequences:
Positive:
- FHIR-native resource management
- Audit trail via resource history
- Discoverable via CapabilityStatement
- Secure key storage (encrypted at rest, tenant-specific)
- Streaming integration (no batch overhead)
- Consistent with ViewDefinition pattern
- No external dependencies (Key Vault, blob storage, etc.)

Negative:
- Implementation complexity (2.5-3.25 days)
- Requires custom resource support
- Requires StructureDefinition for validation
- Requires server-side master encryption key management

Implementation:
- See docs/investigations/anonymization-export-integration.md (now using "de-identification" terminology)
- 3 phases over 20-26 hours
- Uses tenant-specific encryption (AES-256-GCM + HKDF)
- No external dependencies required

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

**Recommended Approach**: DeIdentificationConfig as Custom FHIR Resource

This approach:
1. **Solves the core problem**: FHIR-native config management (no external blob storage)
2. **Aligns with existing patterns**: Consistent with ViewDefinition and IG loading
3. **Provides security**: Secrets encrypted at rest using tenant-specific keys (no external dependencies)
4. **Enables streaming**: No batch overhead, integrates directly with export pipeline
5. **Future-proof**: Works with IG loading pattern, supports FHIR R6
6. **Reasonable effort**: 2.5-3.25 days (20-26 hours)

**Next Steps**:
1. Create ADR-2603-de-identification-export-integration.md
2. Implement Phase 1 (DeIdentificationConfig resource foundation)
3. Implement Phase 2 (Export pipeline integration)
4. Implement Phase 3 (Key management & security)
5. Document and deploy

**Decision**: Proceed with DeIdentificationConfig as custom FHIR resource, integrated into export pipeline via `_deid` parameter.

---

**End of Investigation**
