# Investigation: E2E Testing Strategy for Isolated vs Distributed Modes

**Feature**: distributed-sharding
**Status**: In Progress
**Created**: 2025-12-22

## Problem Statement

We need a testing strategy that:
1. **Maximizes code reuse** - Same test logic for both isolated and distributed modes
2. **Provides thorough coverage** - Both modes exercised with comprehensive scenarios
3. **Supports mode-specific tests** - Some behaviors differ (e.g., cross-shard transactions)
4. **Maintains developer experience** - Easy to run, debug, and extend

## Current Test Infrastructure Analysis

### Existing Patterns (from `Ignixa.Api.E2ETests`)

| Component | Location | Purpose |
|-----------|----------|---------|
| `IgnixaApiFixture` | `_Infrastructure/IgnixaApiFixture.cs` | WebApplicationFactory with tenant config |
| `E2ETestCollection` | `_Infrastructure/Collections/` | xUnit collection for shared fixture |
| `CapabilityDrivenTestBase` | `_Infrastructure/Base/E2ETestBase.cs` | Base class with helpers |
| `SearchTestHarness` | `_Infrastructure/Harness/` | HTTP client wrapper |
| Tag-based isolation | Per-test GUID tags | Prevents test interference |

### Current Tenant Configuration

```csharp
// IgnixaApiFixture.cs - Current setup
["Tenants:Mode"] = "Isolated"
["Tenants:Configurations:0:TenantId"] = "0"      // System partition
["Tenants:Configurations:0:IsSystemPartition"] = "true"
["Tenants:Configurations:1:TenantId"] = "1"      // E2E test tenant
["Tenants:Configurations:1:DisplayName"] = "E2E Test Tenant"
```

## Proposed Approach: Parameterized Fixture Pattern

### Strategy Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                    Test Architecture                            │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌─────────────────┐        ┌─────────────────┐                │
│  │ IsolatedMode    │        │ DistributedMode │                │
│  │ ApiFixture      │        │ ApiFixture      │                │
│  │ (Tenant 1 only) │        │ (3 shards)      │                │
│  └────────┬────────┘        └────────┬────────┘                │
│           │                          │                          │
│           ▼                          ▼                          │
│  ┌─────────────────────────────────────────────┐               │
│  │         ModeAgnosticTestBase                │               │
│  │  - Shared test helpers                      │               │
│  │  - Tag-based isolation                      │               │
│  │  - Mode-aware assertions                    │               │
│  └─────────────────────────────────────────────┘               │
│           │                          │                          │
│           ▼                          ▼                          │
│  ┌──────────────────┐      ┌──────────────────┐                │
│  │ [Isolated Mode]  │      │ [Distributed]    │                │
│  │ Test Collection  │      │ Test Collection  │                │
│  └──────────────────┘      └──────────────────┘                │
│           │                          │                          │
│           └──────────┬───────────────┘                          │
│                      ▼                                          │
│  ┌─────────────────────────────────────────────┐               │
│  │         Shared Test Classes                 │               │
│  │  - CRUD operations                          │               │
│  │  - Search scenarios                         │               │
│  │  - Bundle transactions                      │               │
│  │  - Include/RevInclude                       │               │
│  └─────────────────────────────────────────────┘               │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### Implementation Design

#### 1. Abstract Base Fixture

```csharp
// _Infrastructure/Base/TenantModeApiFixture.cs
public abstract class TenantModeApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public abstract TenantMode Mode { get; }
    public abstract int[] AvailablePartitions { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(GetTenantConfiguration());
        });

        builder.ConfigureServices(services =>
        {
            // Common test configuration
            services.Configure<AuthenticationOptions>(o => o.Enabled = false);
            services.Configure<AuthorizationOptions>(o => o.Enabled = false);
        });
    }

    protected abstract Dictionary<string, string?> GetTenantConfiguration();

    public virtual async Task InitializeAsync()
    {
        // Initialize databases for all partitions
        foreach (var partitionId in AvailablePartitions)
        {
            await InitializePartitionAsync(partitionId);
        }
    }
}
```

#### 2. Isolated Mode Fixture

```csharp
// _Infrastructure/Fixtures/IsolatedModeApiFixture.cs
public class IsolatedModeApiFixture : TenantModeApiFixture
{
    public override TenantMode Mode => TenantMode.Isolated;
    public override int[] AvailablePartitions => [0, 1]; // System + single tenant

    protected override Dictionary<string, string?> GetTenantConfiguration() => new()
    {
        ["Tenants:Mode"] = "Isolated",

        // System partition
        ["Tenants:Configurations:0:TenantId"] = "0",
        ["Tenants:Configurations:0:IsSystemPartition"] = "true",
        ["Tenants:Configurations:0:IsActive"] = "true",
        ["Tenants:Configurations:0:Storage:Type"] = "SqlEntityFramework",

        // Single test tenant
        ["Tenants:Configurations:1:TenantId"] = "1",
        ["Tenants:Configurations:1:DisplayName"] = "E2E Isolated Tenant",
        ["Tenants:Configurations:1:IsActive"] = "true",
        ["Tenants:Configurations:1:Storage:Type"] = "SqlEntityFramework",
    };
}
```

#### 3. Distributed Mode Fixture

```csharp
// _Infrastructure/Fixtures/DistributedModeApiFixture.cs
public class DistributedModeApiFixture : TenantModeApiFixture
{
    public override TenantMode Mode => TenantMode.Distributed;
    public override int[] AvailablePartitions => [0, 1, 2, 3]; // System + 3 shards

    protected override Dictionary<string, string?> GetTenantConfiguration() => new()
    {
        ["Tenants:Mode"] = "Distributed",

        // System partition (packages, transaction state)
        ["Tenants:Configurations:0:TenantId"] = "0",
        ["Tenants:Configurations:0:IsSystemPartition"] = "true",
        ["Tenants:Configurations:0:IsActive"] = "true",
        ["Tenants:Configurations:0:Storage:Type"] = "SqlEntityFramework",
        ["Tenants:Configurations:0:Storage:ConnectionString"] = GetShardConnectionString(0),

        // Shard 1
        ["Tenants:Configurations:1:TenantId"] = "1",
        ["Tenants:Configurations:1:DisplayName"] = "Shard 1",
        ["Tenants:Configurations:1:IsActive"] = "true",
        ["Tenants:Configurations:1:Storage:Type"] = "SqlEntityFramework",
        ["Tenants:Configurations:1:Storage:ConnectionString"] = GetShardConnectionString(1),

        // Shard 2
        ["Tenants:Configurations:2:TenantId"] = "2",
        ["Tenants:Configurations:2:DisplayName"] = "Shard 2",
        ["Tenants:Configurations:2:IsActive"] = "true",
        ["Tenants:Configurations:2:Storage:Type"] = "SqlEntityFramework",
        ["Tenants:Configurations:2:Storage:ConnectionString"] = GetShardConnectionString(2),

        // Shard 3
        ["Tenants:Configurations:3:TenantId"] = "3",
        ["Tenants:Configurations:3:DisplayName"] = "Shard 3",
        ["Tenants:Configurations:3:IsActive"] = "true",
        ["Tenants:Configurations:3:Storage:Type"] = "SqlEntityFramework",
        ["Tenants:Configurations:3:Storage:ConnectionString"] = GetShardConnectionString(3),

        // Sharding configuration
        ["DistributedSharding:LogicalTenantId"] = "1",
        ["DistributedSharding:ShardingKey"] = "PatientCompartment",
        ["DistributedSharding:Shards:0:PartitionId"] = "1",
        ["DistributedSharding:Shards:0:HashRangeStart"] = "0",
        ["DistributedSharding:Shards:0:HashRangeEnd"] = "341",
        ["DistributedSharding:Shards:1:PartitionId"] = "2",
        ["DistributedSharding:Shards:1:HashRangeStart"] = "342",
        ["DistributedSharding:Shards:1:HashRangeEnd"] = "682",
        ["DistributedSharding:Shards:2:PartitionId"] = "3",
        ["DistributedSharding:Shards:2:HashRangeStart"] = "683",
        ["DistributedSharding:Shards:2:HashRangeEnd"] = "1023",
    };

    private static string GetShardConnectionString(int shardId)
    {
        var baseConn = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION_STRING")
            ?? "Server=localhost;User Id=sa;Password=YourPassword123;TrustServerCertificate=true;";
        return $"{baseConn}Database=FhirTest_Shard{shardId}_{Guid.NewGuid():N};";
    }
}
```

#### 4. Test Collections

```csharp
// _Infrastructure/Collections/IsolatedModeCollection.cs
[CollectionDefinition("Isolated Mode")]
public class IsolatedModeCollection : ICollectionFixture<IsolatedModeApiFixture>
{
    public const string Name = "Isolated Mode";
}

// _Infrastructure/Collections/DistributedModeCollection.cs
[CollectionDefinition("Distributed Mode")]
public class DistributedModeCollection : ICollectionFixture<DistributedModeApiFixture>
{
    public const string Name = "Distributed Mode";
}
```

#### 5. Mode-Agnostic Test Base

```csharp
// _Infrastructure/Base/ModeAgnosticTestBase.cs
public abstract class ModeAgnosticTestBase<TFixture> : CapabilityDrivenTestBase
    where TFixture : TenantModeApiFixture
{
    protected TFixture Fixture { get; }
    protected TenantMode Mode => Fixture.Mode;

    protected ModeAgnosticTestBase(TFixture fixture) : base(fixture)
    {
        Fixture = fixture;
    }

    /// <summary>
    /// Skip test if current mode doesn't match requirement.
    /// </summary>
    protected void RequireMode(TenantMode requiredMode)
    {
        if (Mode != requiredMode)
        {
            throw new SkipException($"Test requires {requiredMode} mode, current mode is {Mode}");
        }
    }

    /// <summary>
    /// Assert that resources are distributed across shards (distributed mode only).
    /// </summary>
    protected async Task AssertDistributedAcrossShardsAsync(ResourceJsonNode[] resources)
    {
        if (Mode != TenantMode.Distributed)
            return; // No-op in isolated mode

        var shardDistribution = await GetShardDistributionAsync(resources);
        shardDistribution.Keys.Count.ShouldBeGreaterThan(1,
            "Resources should be distributed across multiple shards");
    }

    /// <summary>
    /// Get the shard distribution of resources (for validation in distributed mode).
    /// </summary>
    protected async Task<Dictionary<int, List<string>>> GetShardDistributionAsync(
        ResourceJsonNode[] resources)
    {
        // Query internal endpoint or check resource metadata
        // Returns: { shardId: [resourceId1, resourceId2, ...] }
        throw new NotImplementedException();
    }
}
```

### Test Class Patterns

#### Pattern 1: Shared Tests Run in Both Modes

```csharp
// Search/Basic/BasicSearchTests.Isolated.cs
[Collection(IsolatedModeCollection.Name)]
public class BasicSearchTests_Isolated : BasicSearchTestsBase<IsolatedModeApiFixture>
{
    public BasicSearchTests_Isolated(IsolatedModeApiFixture fixture) : base(fixture) { }
}

// Search/Basic/BasicSearchTests.Distributed.cs
[Collection(DistributedModeCollection.Name)]
public class BasicSearchTests_Distributed : BasicSearchTestsBase<DistributedModeApiFixture>
{
    public BasicSearchTests_Distributed(DistributedModeApiFixture fixture) : base(fixture) { }
}

// Search/Basic/BasicSearchTestsBase.cs
public abstract class BasicSearchTestsBase<TFixture> : ModeAgnosticTestBase<TFixture>
    where TFixture : TenantModeApiFixture
{
    protected BasicSearchTestsBase(TFixture fixture) : base(fixture) { }

    [Fact]
    public async Task GivenPatients_WhenSearchedByCity_ThenReturnsMatching()
    {
        // Arrange
        var tag = Guid.NewGuid().ToString();
        var patient = CreatePatient().FromSeattle().WithTag(tag).Build();
        await Harness.CreateResourcesAsync([patient]);

        // Act
        var results = await Harness.SearchAsync("Patient", $"address-city=Seattle&_tag={tag}");

        // Assert
        results.Length.ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(SearchParameterData))]
    public async Task SearchWithVariousParameters(string query, int expectedCount)
    {
        // Same test logic works for both modes
    }
}
```

#### Pattern 2: Mode-Specific Tests

```csharp
// Distributed/CrossShardTransactionTests.cs
[Collection(DistributedModeCollection.Name)]
public class CrossShardTransactionTests : ModeAgnosticTestBase<DistributedModeApiFixture>
{
    public CrossShardTransactionTests(DistributedModeApiFixture fixture) : base(fixture) { }

    [Fact]
    public async Task GivenCrossShardBundle_WhenPosted_ThenAllResourcesCreated()
    {
        RequireMode(TenantMode.Distributed); // Skip in isolated mode

        // Arrange: Create patients that will route to different shards
        var patient1 = CreatePatient().WithId("patient-shard1").Build(); // Hash → Shard 1
        var patient2 = CreatePatient().WithId("patient-shard2").Build(); // Hash → Shard 2

        var bundle = new BundleBuilder()
            .AddTransaction(patient1, HttpMethod.Post)
            .AddTransaction(patient2, HttpMethod.Post)
            .Build();

        // Act
        var response = await Harness.PostBundleAsync(bundle);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var resultBundle = await Harness.ParseBundleResponseAsync(response);
        resultBundle.Entry.Count.ShouldBe(2);
        resultBundle.Entry.ShouldAllBe(e => e.Response.Status == "201");

        // Verify distributed across shards
        var createdResources = await Task.WhenAll(
            Harness.GetResourceAsync("Patient", "patient-shard1"),
            Harness.GetResourceAsync("Patient", "patient-shard2"));
        await AssertDistributedAcrossShardsAsync(createdResources);
    }

    [Fact]
    public async Task GivenCrossShardBundle_WhenOneShardFails_ThenAllRolledBack()
    {
        RequireMode(TenantMode.Distributed);

        // Arrange: First entry valid, second entry will fail validation
        var validPatient = CreatePatient().WithId("valid-patient").Build();
        var invalidPatient = CreatePatient().WithInvalidData().Build();

        var bundle = new BundleBuilder()
            .AddTransaction(validPatient, HttpMethod.Post)
            .AddTransaction(invalidPatient, HttpMethod.Post)
            .Build();

        // Act
        var response = await Harness.PostBundleAsync(bundle);

        // Assert: Transaction should be atomic - all fail
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Neither patient should exist
        var getResponse = await Client.GetAsync("/Patient/valid-patient");
        getResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
```

#### Pattern 3: Distributed Query Verification

```csharp
// Distributed/FanoutQueryTests.cs
[Collection(DistributedModeCollection.Name)]
public class FanoutQueryTests : ModeAgnosticTestBase<DistributedModeApiFixture>
{
    public FanoutQueryTests(DistributedModeApiFixture fixture) : base(fixture) { }

    [Fact]
    public async Task GivenPatientsOnMultipleShards_WhenSearchAll_ThenReturnsFromAllShards()
    {
        RequireMode(TenantMode.Distributed);

        // Arrange: Create patients that hash to different shards
        var tag = Guid.NewGuid().ToString();
        var patients = Enumerable.Range(0, 30)
            .Select(i => CreatePatient()
                .WithId($"patient-{i:D3}")
                .WithTag(tag)
                .Build())
            .ToArray();

        await Harness.CreateResourcesAsync(patients);

        // Act: Search without sharding key - should fan out
        var results = await Harness.SearchAsync("Patient", $"_tag={tag}&_count=100");

        // Assert
        results.Length.ShouldBe(30);

        // Verify results came from multiple shards
        await AssertDistributedAcrossShardsAsync(results);
    }

    [Fact]
    public async Task GivenPatientOnSpecificShard_WhenSearchByPatientRef_ThenTargetsSingleShard()
    {
        RequireMode(TenantMode.Distributed);

        // Arrange
        var tag = Guid.NewGuid().ToString();
        var patient = CreatePatient().WithId("target-patient").WithTag(tag).Build();
        var observations = Enumerable.Range(0, 10)
            .Select(i => CreateObservation()
                .ForPatient("target-patient")
                .WithTag(tag)
                .Build())
            .ToArray();

        await Harness.CreateResourceAsync(patient);
        await Harness.CreateResourcesAsync(observations);

        // Act: Search with patient reference - should target single shard
        var results = await Harness.SearchAsync(
            "Observation",
            $"patient=Patient/target-patient&_tag={tag}");

        // Assert: All observations should be on same shard as patient
        results.Length.ShouldBe(10);
        // Note: In distributed mode, patient-centric queries should be efficient
    }
}
```

### Test Organization

```
test/Ignixa.Api.E2ETests/
├── _Infrastructure/
│   ├── Base/
│   │   ├── TenantModeApiFixture.cs        (abstract base)
│   │   ├── ModeAgnosticTestBase.cs        (test base with mode helpers)
│   │   └── CapabilityDrivenTestBase.cs    (existing, unchanged)
│   ├── Collections/
│   │   ├── IsolatedModeCollection.cs
│   │   └── DistributedModeCollection.cs
│   ├── Fixtures/
│   │   ├── IsolatedModeApiFixture.cs
│   │   └── DistributedModeApiFixture.cs
│   └── Harness/
│       └── SearchTestHarness.cs           (existing, unchanged)
│
├── Shared/                                 (mode-agnostic test bases)
│   ├── Search/
│   │   ├── BasicSearchTestsBase.cs
│   │   ├── DateSearchTestsBase.cs
│   │   ├── TokenSearchTestsBase.cs
│   │   └── ChainedSearchTestsBase.cs
│   ├── Operations/
│   │   ├── CrudTestsBase.cs
│   │   ├── BundleTestsBase.cs
│   │   └── ConditionalTestsBase.cs
│   └── Include/
│       ├── IncludeTestsBase.cs
│       └── RevIncludeTestsBase.cs
│
├── Isolated/                               (isolated mode test implementations)
│   ├── Search/
│   │   ├── BasicSearchTests.cs            ([Collection("Isolated Mode")])
│   │   └── DateSearchTests.cs
│   └── Operations/
│       └── CrudTests.cs
│
├── Distributed/                            (distributed mode test implementations)
│   ├── Search/
│   │   ├── BasicSearchTests.cs            ([Collection("Distributed Mode")])
│   │   ├── DateSearchTests.cs
│   │   └── FanoutQueryTests.cs            (distributed-specific)
│   ├── Operations/
│   │   ├── CrudTests.cs
│   │   └── CrossShardTransactionTests.cs  (distributed-specific)
│   └── Sharding/
│       ├── ShardRoutingTests.cs           (verify patient co-location)
│       └── ContinuationTokenTests.cs      (verify multi-shard paging)
│
└── Ignixa.Api.E2ETests.csproj
```

### Running Tests

```bash
# Run all tests (both modes)
dotnet test test/Ignixa.Api.E2ETests

# Run only isolated mode tests
dotnet test test/Ignixa.Api.E2ETests --filter "Collection=Isolated Mode"

# Run only distributed mode tests
dotnet test test/Ignixa.Api.E2ETests --filter "Collection=Distributed Mode"

# Run specific test category in both modes
dotnet test test/Ignixa.Api.E2ETests --filter "FullyQualifiedName~BasicSearch"

# Run with SQL Server (required for distributed mode)
TEST_SQL_CONNECTION_STRING="Server=localhost;..." dotnet test test/Ignixa.Api.E2ETests
```

### CI/CD Pipeline

```yaml
# .github/workflows/e2e-tests.yml
jobs:
  e2e-isolated:
    runs-on: ubuntu-latest
    services:
      sqlserver:
        image: mcr.microsoft.com/mssql/server:2022-latest
    steps:
      - name: Run Isolated Mode Tests
        run: dotnet test --filter "Collection=Isolated Mode"
        env:
          TEST_SQL_CONNECTION_STRING: ${{ secrets.SQL_CONNECTION }}

  e2e-distributed:
    runs-on: ubuntu-latest
    services:
      sqlserver:
        image: mcr.microsoft.com/mssql/server:2022-latest
    steps:
      - name: Run Distributed Mode Tests
        run: dotnet test --filter "Collection=Distributed Mode"
        env:
          TEST_SQL_CONNECTION_STRING: ${{ secrets.SQL_CONNECTION }}
```

## Tradeoffs

| Pros | Cons |
|------|------|
| Maximum code reuse (shared base classes) | Slightly more complex test organization |
| Clear separation of mode-specific tests | Two test runs in CI (isolated + distributed) |
| Easy to add new shared tests | Requires 4 databases for distributed mode tests |
| Mode-aware assertions | Initial setup overhead |
| Follows existing xUnit patterns | |

## Alignment

- [x] Uses existing xUnit collection/fixture patterns
- [x] Maintains `CapabilityDrivenTestBase` compatibility
- [x] Tag-based isolation works in both modes
- [x] SearchTestHarness unchanged
- [x] BDD naming convention preserved
- [ ] Requires new fixture classes
- [ ] Requires reorganization of existing tests

## Implementation Roadmap

### Phase 1: Infrastructure (1-2 days)

1. Create `TenantModeApiFixture` abstract base
2. Create `IsolatedModeApiFixture` (mostly copy of existing)
3. Create `DistributedModeApiFixture` (new)
4. Create collection definitions
5. Create `ModeAgnosticTestBase<T>`

### Phase 2: Migration (2-3 days)

1. Move existing tests to `Shared/` as base classes
2. Create thin derived classes in `Isolated/`
3. Create thin derived classes in `Distributed/`
4. Verify all existing tests pass in isolated mode

### Phase 3: Distributed-Specific Tests (3-5 days)

1. Cross-shard transaction tests
2. Fanout query verification tests
3. Shard routing validation tests
4. Continuation token tests for multi-shard paging
5. Circuit breaker/failure scenario tests

### Phase 4: CI Integration (1 day)

1. Update CI pipeline to run both modes
2. Add distributed mode to required checks
3. Document test running instructions

## Verdict

**Recommendation**: Proceed with parameterized fixture pattern

This approach:
- Maximizes test code reuse (90%+ shared between modes)
- Provides clear organization for mode-specific tests
- Uses familiar xUnit patterns
- Enables parallel development of distributed features

**Alternative Considered**: Single fixture with runtime mode detection
- Rejected because: Harder to debug, slower test runs (all tests would wait for distributed setup even when not needed)

## Related Files

- `test/Ignixa.Api.E2ETests/_Infrastructure/IgnixaApiFixture.cs` - Current fixture to extend
- `test/Ignixa.Api.E2ETests/_Infrastructure/Base/E2ETestBase.cs` - Current base class
- `docs/features/distributed-sharding/investigations/distributed-mode.md` - Core architecture
