# Investigation: Distributed Mode for Horizontal Sharding

**Feature**: distributed-sharding
**Status**: In Progress
**Created**: 2025-12-22

## Executive Summary

This investigation analyzes implementing `TenantMode.Distributed` to enable horizontal scaling of a single customer's FHIR data across multiple database shards. Unlike `Isolated` mode (separate customers), `Distributed` mode serves a single logical tenant whose data is partitioned across multiple physical stores for capacity and performance.

## Current Architecture Analysis

### Existing Multi-Tenancy Foundation

The codebase already has strong foundations for distributed sharding:

| Component | Location | Current State | Distributed Mode Ready? |
|-----------|----------|---------------|------------------------|
| `RequestPartition` | `Ignixa.Domain/Models/RequestPartition.cs` | Supports `PartitionIds[]` + `PartitionMode` enum | **Yes** - designed for multiple partitions |
| `IPartitionStrategy` | `Ignixa.Domain/Abstractions/IPartitionStrategy.cs` | Separate read/write methods | **Yes** - read can return multiple |
| `IQueryExecutionStrategy` | `Ignixa.Domain/Abstractions/IQueryExecutionStrategy.cs` | Interface exists | **Partial** - only `PassthroughExecutionStrategy` implemented |
| `IsolatedModePartitionStrategy` | `Ignixa.Application/Infrastructure/` | Always returns single partition | Serves as template |
| System Partition (0) | Reserved for transaction IDs, DurableTask | API blocked via middleware | **Yes** - shared metadata store |

### Key Insight: Read vs Write Separation

The `IPartitionStrategy` interface already distinguishes:

```csharp
// Read: May return MULTIPLE partition IDs for fanout
RequestPartition DetermineReadPartition(context, resourceType, queryParams);

// Write: ALWAYS returns SINGLE partition (atomicity requirement)
RequestPartition DetermineWritePartition(context, resource);
```

This design anticipates distributed mode where:
- **Reads** fan out across shards
- **Writes** route to exactly one shard (determined by sharding key)

### System Partition Usage

**Current Uses**:
- Transaction ID allocation (global sequence)
- DurableTask orchestration state
- Reserved partition ID (0) - blocked from API access

**Proposed Additional Uses for Distributed Mode**:
- FHIR Package storage (StructureDefinitions, SearchParameters, ValueSets)
- Conformance resource cache
- Shard configuration and health metadata
- Cross-shard reference resolution cache

## Approach: Distributed Mode Architecture

### Sharding Strategy

```
┌─────────────────────────────────────────────────────────────────┐
│                    Distributed Tenant (Logical)                 │
├─────────────────────────────────────────────────────────────────┤
│  Partition 0 (System)     │  Shared metadata, packages, txn IDs │
├───────────────────────────┼─────────────────────────────────────┤
│  Shard 1 (Partition 1)    │  Patients A-G + compartment data    │
├───────────────────────────┼─────────────────────────────────────┤
│  Shard 2 (Partition 2)    │  Patients H-N + compartment data    │
├───────────────────────────┼─────────────────────────────────────┤
│  Shard 3 (Partition 3)    │  Patients O-Z + compartment data    │
└───────────────────────────┴─────────────────────────────────────┘
```

### Sharding Key: Patient Compartment Co-location

**Primary Strategy**: Hash-based patient ID distribution

```csharp
public record ShardingConfiguration
{
    public ShardingKeyType KeyType { get; init; } = ShardingKeyType.PatientCompartment;
    public int ShardCount { get; init; }
    public required IReadOnlyList<int> ShardPartitionIds { get; init; }
}

public enum ShardingKeyType
{
    PatientCompartment,    // All patient data co-located
    ResourceType,          // Different types on different shards
    HashBased,             // Consistent hashing on resource ID
    DateRange              // Time-based partitioning
}
```

**Patient Compartment Co-location Rationale**:
- 90%+ of FHIR queries are patient-centric
- Eliminates cross-shard joins for typical workflows
- Matches clinical reality (patient data is accessed together)
- Compartment definition already exists in codebase (`R4CompartmentDefinitions.g.cs`)

### Query Execution Strategies

Based on the fanout broker ADR patterns, implement three execution strategies:

#### 1. Parallel Execution (Low Result Count Expected)

**Triggers**:
- Exact ID searches (`_id=123`)
- Specific identifier searches (`identifier=system|value`)
- Queries with `_sort` parameter (requires global sort)
- Chained search sub-queries
- Small `_count` values (1-10)

**Flow**:
```
Client Request
     │
     ▼
┌──────────────┐
│ All Shards   │ ← Parallel requests
│ in parallel  │
└──────────────┘
     │
     ▼
┌──────────────┐
│ Result       │ ← Merge + deduplicate + global sort
│ Aggregator   │
└──────────────┘
     │
     ▼
Response Bundle
```

#### 2. Sequential Execution (High Result Count Expected)

**Triggers**:
- Broad text searches (`name=John`)
- Status-based searches (`status=active`)
- Date range searches
- Large `_count` values (>20)

**Flow**:
```
Client Request
     │
     ▼
┌──────────────┐
│ Shard 1      │ ← Query first shard
└──────────────┘
     │ (insufficient results?)
     ▼
┌──────────────┐
│ Shard 2      │ ← Continue to next
└──────────────┘
     │ (fill-factor 50% met?)
     ▼
Early Termination → Response Bundle
```

#### 3. Targeted Execution (Sharding Key in Query)

**Triggers**:
- Patient compartment queries (`Patient/123/*`)
- Queries with patient reference (`Observation?patient=Patient/123`)
- `$everything` operations

**Flow**:
```
Client Request (with patient ID)
     │
     ▼
┌──────────────┐
│ Hash Patient │ → Determine target shard
│ ID           │
└──────────────┘
     │
     ▼
┌──────────────┐
│ Single Shard │ ← Direct query (no fanout)
└──────────────┘
     │
     ▼
Response Bundle
```

### Cross-Shard Transaction Support (2PC)

The system partition (0) stores the transaction table, enabling Two-Phase Commit (2PC) for bundles that span multiple shards.

**Architecture**:
```
System Partition (0)
├── TransactionState table
│   ├── TransactionId (global sequence)
│   ├── State (Pending, Prepared, Committed, Aborted)
│   ├── ParticipatingShards[]
│   ├── CreatedAt, PreparedAt, CompletedAt
│   └── Timeout settings
├── Packages/Conformance resources
└── Shard health metadata

Shard 1-N
├── Resource data with TransactionId FK
├── PendingWrites table (held until commit)
└── TransactionParticipant state
```

**2PC Flow for Cross-Shard Bundle**:
```
Phase 1: PREPARE
┌──────────────────────────────────────────────────────────────────┐
│ 1. Allocate TransactionId from System Partition                  │
│ 2. Determine target shards for each entry                        │
│ 3. Send PREPARE to each shard with entries                       │
│ 4. Each shard validates + writes to PendingWrites                │
│ 5. Each shard responds PREPARED or ABORT                         │
│ 6. Record state in TransactionState table                        │
└──────────────────────────────────────────────────────────────────┘
                              │
                              ▼
Phase 2: COMMIT (if all PREPARED) or ABORT (if any failed)
┌──────────────────────────────────────────────────────────────────┐
│ 7. Send COMMIT/ABORT to all participating shards                 │
│ 8. Each shard moves PendingWrites → Resources (or deletes)       │
│ 9. Update TransactionState to Committed/Aborted                  │
│ 10. Return Bundle response with outcomes                         │
└──────────────────────────────────────────────────────────────────┘
```

**Transaction Coordinator**:
```csharp
public class DistributedTransactionCoordinator
{
    private readonly ISystemPartitionRepository _systemRepo;
    private readonly IShardRepositoryFactory _shardFactory;

    public async Task<BundleResponse> ExecuteBundleAsync(
        Bundle bundle,
        CancellationToken ct)
    {
        // 1. Allocate transaction ID
        var txnId = await _systemRepo.AllocateTransactionIdAsync(ct);

        // 2. Group entries by target shard
        var shardEntries = GroupEntriesByTargetShard(bundle.Entry);

        // 3. Record transaction state
        await _systemRepo.CreateTransactionAsync(new TransactionRecord
        {
            TransactionId = txnId,
            State = TransactionState.Pending,
            ParticipatingShards = shardEntries.Keys.ToList(),
            Timeout = TimeSpan.FromSeconds(30)
        }, ct);

        try
        {
            // Phase 1: PREPARE
            var prepareResults = await Task.WhenAll(
                shardEntries.Select(kv => PrepareShardAsync(txnId, kv.Key, kv.Value, ct)));

            if (prepareResults.All(r => r.Success))
            {
                // Phase 2: COMMIT
                await _systemRepo.UpdateTransactionStateAsync(txnId, TransactionState.Prepared, ct);
                await Task.WhenAll(shardEntries.Keys.Select(s => CommitShardAsync(txnId, s, ct)));
                await _systemRepo.UpdateTransactionStateAsync(txnId, TransactionState.Committed, ct);
                return BuildSuccessResponse(prepareResults);
            }
            else
            {
                // Phase 2: ABORT
                await Task.WhenAll(shardEntries.Keys.Select(s => AbortShardAsync(txnId, s, ct)));
                await _systemRepo.UpdateTransactionStateAsync(txnId, TransactionState.Aborted, ct);
                return BuildFailureResponse(prepareResults);
            }
        }
        catch (Exception ex)
        {
            // Recovery: mark for cleanup by background process
            await _systemRepo.UpdateTransactionStateAsync(txnId, TransactionState.Aborted, ct);
            throw;
        }
    }
}
```

**Recovery**:
- Background service scans for stale `Pending` transactions (timeout exceeded)
- Queries each shard for `Prepared` state
- Completes commit or abort based on majority/all responses
- Cleans up `PendingWrites` on shards

**Performance Considerations**:
- Single-shard bundles bypass 2PC (no coordination needed)
- Most bundles are patient-centric → single shard → no 2PC overhead
- Cross-shard bundles (~10% of traffic) incur 2PC latency (~50-100ms extra)

### Component Design

#### DistributedModePartitionStrategy

```csharp
public class DistributedModePartitionStrategy : IPartitionStrategy
{
    private readonly ShardingConfiguration _config;
    private readonly IShardRouter _router;

    public RequestPartition DetermineReadPartition(
        PartitionResolutionContext context,
        string? resourceType,
        IReadOnlyDictionary<string, string> queryParams)
    {
        // Check if query can be targeted to single shard
        if (TryExtractShardingKey(queryParams, out var shardKey))
        {
            var targetShard = _router.RouteToShard(shardKey);
            return new RequestPartition
            {
                PartitionIds = [targetShard],
                Mode = PartitionMode.Distributed
            };
        }

        // Fan out to all shards
        return new RequestPartition
        {
            PartitionIds = _config.ShardPartitionIds,
            Mode = PartitionMode.Distributed
        };
    }

    public RequestPartition DetermineWritePartition(
        PartitionResolutionContext context,
        ResourceJsonNode resource)
    {
        // Extract sharding key from resource
        var shardKey = ExtractShardingKey(resource);
        var targetShard = _router.RouteToShard(shardKey);

        return new RequestPartition
        {
            PartitionIds = [targetShard],
            Mode = PartitionMode.Distributed
        };
    }
}
```

#### FanoutExecutionStrategy

```csharp
public class FanoutExecutionStrategy : IQueryExecutionStrategy
{
    private readonly IExecutionStrategyAnalyzer _analyzer;
    private readonly ISearchServiceFactory _searchFactory;
    private readonly IResultAggregator _aggregator;

    public async IAsyncEnumerable<SearchEntryResult> SearchStreamAsync<TSearchOptions>(
        RequestPartition partition,
        TSearchOptions searchOptions,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var strategy = _analyzer.DetermineStrategy(searchOptions);

        IAsyncEnumerable<SearchEntryResult> results = strategy switch
        {
            ExecutionMode.Parallel => ExecuteParallelAsync(partition, searchOptions, ct),
            ExecutionMode.Sequential => ExecuteSequentialAsync(partition, searchOptions, ct),
            ExecutionMode.Targeted => ExecuteTargetedAsync(partition, searchOptions, ct),
            _ => throw new InvalidOperationException()
        };

        await foreach (var result in results.WithCancellation(ct))
        {
            yield return result;
        }
    }
}
```

#### Distributed Continuation Token

```csharp
public record DistributedContinuationToken
{
    public required IReadOnlyDictionary<int, string?> ShardTokens { get; init; }
    public required string SortCriteria { get; init; }
    public required int PageSize { get; init; }
    public required IReadOnlyList<int> ExhaustedShards { get; init; }
}
```

### Cross-Shard Reference Resolution

For includes/revIncludes and chained searches, two strategies (from fanout broker ADR):

#### Passthrough Resolution (Co-located References)

Assumes referenced resources exist on same shard as referencing resources. Optimal for patient-centric sharding.

```csharp
// Patient and their Observations on same shard
GET /Observation?patient=Patient/123&_include=Observation:patient
// → Query single shard, include resolves locally
```

#### Distributed Resolution (Cross-Shard References)

For resources that may reference across shards (e.g., Practitioner, Organization):

```csharp
// Phase 1: Get Observations from patient's shard
// Phase 2: Extract Practitioner references
// Phase 3: Query Practitioner shard(s) for referenced IDs
```

### System Partition for Shared Resources

**Package Storage (Partition 0)**:

```
dbo.PackageResource (System Partition)
├── StructureDefinitions
├── SearchParameters
├── ValueSets
├── CodeSystems
├── CapabilityStatement templates
└── ViewDefinitions
```

**Benefits**:
- Single source of truth for conformance resources
- No duplication across shards
- Simplified package management
- Consistent schema across all shards

**Access Pattern**:
```csharp
// Conformance resources loaded from system partition
var structureDef = await _systemPartitionRepo.GetByCanonicalAsync(canonical);

// Clinical data from distributed shards
var patient = await _shardRepo.GetResourceAsync("Patient", id);
```

## Tradeoffs

| Pros | Cons |
|------|------|
| Horizontal scaling for 100M+ resources | Increased query complexity |
| Patient-centric co-location eliminates most cross-shard queries | 2PC coordination overhead for cross-shard bundles |
| Existing architecture designed for this | New continuation token format |
| System partition provides single source for packages | Shard rebalancing complexity |
| Cross-shard transactions via 2PC (system partition coordination) | Higher operational overhead |
| Transparent to API consumers | Result merging memory pressure |
| Parallel execution for throughput | |

## Alignment

- [x] Follows layer rules (API -> App -> Domain -> Data)
- [x] F5 Developer Experience (single-shard mode for development)
- [x] FHIR spec compliance (search semantics preserved)
- [x] Consistent with existing patterns (`IPartitionStrategy`, `IQueryExecutionStrategy`)
- [ ] Requires new `FanoutExecutionStrategy` implementation
- [ ] Requires `DistributedModePartitionStrategy` implementation
- [ ] Requires shard router and result aggregator

## Evidence

### Codebase Analysis

**Existing Foundation**:
- `RequestPartition.PartitionIds` already supports multiple IDs
- `PartitionMode.Distributed` enum value exists but unused
- `PassthroughExecutionStrategy` validates single partition (pattern to extend)
- `CompositeSearchServiceFactory` routes by tenant (extend for shards)
- `TenantConfiguration` has storage configuration per partition

**Fanout Broker ADR (prior art)**:
- Execution strategy analyzer pattern
- Parallel vs sequential decision matrix
- Continuation token aggregation design
- Include/RevInclude resolution strategies
- Chained search handling

### Industry Patterns

| System | Sharding Approach | Reference Resolution |
|--------|-------------------|---------------------|
| Google Spanner | Hash-based with interleaving | Co-located hierarchies |
| CockroachDB | Range-based with locality | Distributed joins |
| Cosmos DB | Partition key routing | Cross-partition queries |
| HAPI FHIR | Not supported | N/A |
| Microsoft FHIR Server | Not supported | N/A |

### FHIR Specification Considerations

- Compartment definitions (`CompartmentDefinition` resource) provide natural sharding boundaries
- Patient compartment includes: `Account`, `AllergyIntolerance`, `Appointment`, `CarePlan`, `Claim`, `Condition`, `Consent`, `Coverage`, `Device`, `DiagnosticReport`, `DocumentReference`, `Encounter`, `ExplanationOfBenefit`, `Goal`, `ImagingStudy`, `Immunization`, `Invoice`, `List`, `MedicationAdministration`, `MedicationDispense`, `MedicationRequest`, `MedicationStatement`, `NutritionOrder`, `Observation`, `Patient`, `Procedure`, `Provenance`, `RequestGroup`, `RiskAssessment`, `Schedule`, `ServiceRequest`, `Specimen`, `SupplyDelivery`, `VisionPrescription`

## Implementation Roadmap

### Phase 1: Foundation (4-6 weeks)

| Task | Complexity | Description |
|------|------------|-------------|
| **FS-001** Shard configuration model | S | `ShardingConfiguration`, `ShardInfo` records |
| **FS-002** Shard router interface | S | `IShardRouter` with consistent hashing |
| **FS-003** Distributed partition strategy | M | `DistributedModePartitionStrategy` implementation |
| **FS-004** Execution strategy analyzer | M | Determine parallel/sequential/targeted |
| **FS-005** Update `TenantConfiguration` | S | Add sharding settings per tenant |

### Phase 2: Query Execution (6-8 weeks)

| Task | Complexity | Description |
|------|------------|-------------|
| **FS-006** Parallel execution | M | Task-based concurrent shard queries |
| **FS-007** Sequential execution | L | Server-by-server with early termination |
| **FS-008** Targeted execution | S | Direct routing when sharding key known |
| **FS-009** Result aggregator | M | Merge, deduplicate, global sort |
| **FS-010** Distributed continuation tokens | L | Per-shard token aggregation |

### Phase 3: Advanced Features (6-8 weeks)

| Task | Complexity | Description |
|------|------------|-------------|
| **FS-011** Cross-shard includes | L | Passthrough and distributed resolution |
| **FS-012** Chained search support | L | Multi-phase chain resolution |
| **FS-013** Distributed sorting | L | Global sort with pagination |
| **FS-014** Resolution cache | M | Cache for cross-shard reference lookups |

### Phase 4: System Partition Integration (4-6 weeks)

| Task | Complexity | Description |
|------|------------|-------------|
| **FS-015** Package storage migration | M | Move packages to system partition |
| **FS-016** Conformance resource sharing | M | Shared schema across shards |
| **FS-017** Transaction ID coordination | S | Global sequence from partition 0 |
| **FS-018** Shard health monitoring | M | Track shard availability/latency |

### Phase 5: Production Readiness (4-6 weeks)

| Task | Complexity | Description |
|------|------------|-------------|
| **FS-019** Circuit breaker per shard | L | Polly-based resilience |
| **FS-020** Performance monitoring | M | Per-shard metrics, query tracing |
| **FS-021** Load testing | L | 10/50/100+ shard configurations |
| **FS-022** Documentation | M | Operations guide, configuration reference |

### Phase 6: Data Management (Future)

| Task | Complexity | Description |
|------|------------|-------------|
| **FS-023** Shard rebalancing | XL | Move patients between shards |
| **FS-024** Shard split/merge | XL | Subdivide or combine shards |
| **FS-025** Backup coordination | L | Cross-shard consistent backups |

## Configuration Example

```json
{
  "Tenants": {
    "Mode": "Distributed",
    "Configurations": [
      {
        "TenantId": 0,
        "DisplayName": "System Partition",
        "IsSystemPartition": true,
        "IsActive": true,
        "Storage": {
          "Type": "SqlEntityFramework",
          "ConnectionString": "Server=sql;Database=FhirSystem;..."
        }
      }
    ],
    "DistributedSharding": {
      "LogicalTenantId": 1,
      "DisplayName": "Acme Healthcare",
      "FhirVersion": "R4",
      "ShardingKey": "PatientCompartment",
      "Shards": [
        {
          "ShardId": 1,
          "PartitionId": 1,
          "HashRangeStart": 0,
          "HashRangeEnd": 341,
          "Storage": {
            "Type": "SqlEntityFramework",
            "ConnectionString": "Server=sql-shard1;Database=FhirShard1;..."
          }
        },
        {
          "ShardId": 2,
          "PartitionId": 2,
          "HashRangeStart": 342,
          "HashRangeEnd": 682,
          "Storage": {
            "Type": "SqlEntityFramework",
            "ConnectionString": "Server=sql-shard2;Database=FhirShard2;..."
          }
        },
        {
          "ShardId": 3,
          "PartitionId": 3,
          "HashRangeStart": 683,
          "HashRangeEnd": 1023,
          "Storage": {
            "Type": "SqlEntityFramework",
            "ConnectionString": "Server=sql-shard3;Database=FhirShard3;..."
          }
        }
      ],
      "QueryExecution": {
        "ParallelThreshold": 10,
        "SequentialFillFactor": 0.5,
        "ChainedSearchTimeout": "00:00:15",
        "IncludeResolution": "Passthrough",
        "ChainedSearchResolution": "Distributed",
        "MaxCrossShardReferences": 1000
      }
    }
  }
}
```

## Verdict

**Recommendation**: Proceed with implementation

The architecture is designed for this. The existing `IPartitionStrategy`, `IQueryExecutionStrategy`, and `RequestPartition` abstractions explicitly anticipate distributed mode. The fanout broker ADR provides battle-tested patterns for execution strategies and result aggregation.

**Critical Success Factors**:
1. Patient compartment co-location is essential for performance
2. System partition must handle all shared metadata (packages, transaction state)
3. Cross-shard transactions supported via 2PC with system partition coordination
4. Start with 3-shard configuration, validate patterns before scaling further

**Alternative Approaches Worth Considering**:
1. **Range-based sharding by patient ID prefix** - Simpler routing but potential hotspots
2. **Time-based sharding** - Good for append-heavy workloads, bad for patient queries
3. **Resource-type sharding** - Forces cross-shard queries for most operations

## Related Investigations

- [ ] `shard-rebalancing.md` - Moving patients between shards
- [ ] `global-search-indexes.md` - Elasticsearch/OpenSearch for cross-shard search
- [ ] `testing-strategy.md` - E2E testing for isolated vs distributed modes
