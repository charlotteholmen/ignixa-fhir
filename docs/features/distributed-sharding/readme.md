# Feature: Distributed Sharding

**Status**: Exploring
**Created**: 2025-12-22

## Problem Statement

As FHIR data volumes grow, a single database instance becomes a bottleneck for both storage capacity and query throughput. Distributed sharding enables horizontal scaling by partitioning data across multiple database nodes while maintaining FHIR query semantics and transactional guarantees where required.

## Constraints

- Must preserve FHIR search semantics across shards
- Multi-tenant isolation must be maintained (tenant data cannot leak across shards)
- Reference integrity within compartments (Patient, Encounter) should be co-located
- Cross-shard queries must be transparent to API consumers
- Transaction boundaries may be limited to single-shard operations
- Must work with existing partition-based multi-tenancy model

## Key Architecture Points

### Current Foundation (Already Exists)

The codebase already has strong foundations designed for distributed sharding:

| Component | Location | Status |
|-----------|----------|--------|
| `RequestPartition` with `PartitionIds[]` | `Ignixa.Domain/Models/RequestPartition.cs` | Ready - supports multiple partitions |
| `PartitionMode.Distributed` enum | `Ignixa.Domain/Models/RequestPartition.cs` | Defined but unused |
| `IPartitionStrategy` (read/write separation) | `Ignixa.Domain/Abstractions/` | Interface ready |
| `IQueryExecutionStrategy` | `Ignixa.Domain/Abstractions/` | Interface ready |
| System Partition (0) | Reserved for metadata | API blocked, available for packages |

### Proposed Approach

1. **Sharding Key**: Patient compartment-based co-location (90%+ queries are patient-centric)
2. **System Partition**: Store FHIR packages, conformance resources, transaction state
3. **Query Execution**: Three modes - Parallel (fanout), Sequential (early termination), Targeted (single shard)
4. **Write Routing**: Single-shard writes are direct; cross-shard bundles use 2PC via system partition

## Investigations

| Investigation | Status | Summary |
|--------------|--------|---------|
| [distributed-mode](investigations/distributed-mode.md) | Complete | Core architecture: partition strategy, execution modes, 2PC transactions, system partition usage |
| [well-architected-review](investigations/well-architected-review.md) | Complete | Azure WAF review: 5 critical issues, 7 high-priority recommendations, 10-week timeline |
| [testing-strategy](investigations/testing-strategy.md) | Complete | E2E testing for isolated vs distributed: parameterized fixtures, shared test code, mode-specific tests |
| [fanoutbroken-old](investigations/fanoutbroken-old.md) | Reference | Prior art from similar codebase: execution strategy patterns, continuation tokens |

## Well-Architected Assessment

| Pillar | Score | Key Finding |
|--------|-------|-------------|
| Reliability | 4/10 | Need circuit breakers, cross-shard transaction prevention |
| Security | 5/10 | Need shard authorization policy, audit logging |
| Cost Optimization | 6/10 | Need streaming merge with bounded buffers |
| Operational Excellence | 4/10 | Need distributed tracing across shards |
| Performance Efficiency | 6/10 | Patient compartment co-location is excellent |

**Overall**: Fair - Strong design foundation, requires ~10 weeks hardening before production.

## Implementation Phases

### Phase 1: Critical Fixes (2 weeks) - REQUIRED BEFORE DEPLOYMENT
- [ ] Per-shard circuit breakers with graceful degradation
- [ ] Shard-level authorization policy (`IShardAuthorizationPolicy`)
- [ ] 2PC transaction coordinator (`DistributedTransactionCoordinator`)
- [ ] Distributed tracing with Activity API
- [ ] Streaming merge with bounded memory buffers

### Phase 2: Core Implementation (6-8 weeks)
- [ ] `DistributedModePartitionStrategy`
- [ ] `FanoutExecutionStrategy` (parallel, sequential, targeted)
- [ ] Distributed continuation tokens
- [ ] Cross-shard include/revinclude resolution
- [ ] Chained search support

### Phase 3: Production Readiness (4-6 weeks)
- [ ] Shard health monitoring
- [ ] Performance optimization
- [ ] Load testing (10/50/100 shards)
- [ ] Operations documentation

## Decision

*Pending completion of Phase 1 critical fixes before ADR approval*

### Recommendation

**Proceed with implementation** - the architecture is designed for this. The existing `IPartitionStrategy`, `IQueryExecutionStrategy`, and `RequestPartition` abstractions explicitly anticipate distributed mode.

**Critical Success Factors**:
1. Patient compartment co-location is essential (eliminates 90%+ cross-shard queries)
2. System partition must handle all shared metadata (packages, transaction state)
3. Cross-shard transactions supported via 2PC (system partition coordination)
4. Circuit breakers required for production reliability
