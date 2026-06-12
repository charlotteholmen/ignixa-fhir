# ADR 2606: Vector Search for Semantic FHIR Queries

## Status

Proposed

## Context

FHIR search is purely structural: token matches, string prefixes, date ranges, reference chains. This works well for coded data but fails when clinicians need semantic similarity — "find patients with conditions similar to X" or "retrieve observations whose narrative resembles Y." The Microsoft FHIR Server is exploring vector search integration (see `personal/bkowitz/vector-search-implementation` branch), and SQL Server 2025 now provides native vector capabilities (`VECTOR` data type, `VECTOR_DISTANCE`, DiskANN indexes).

**Problem:** How do we introduce vector/embedding-based search into Ignixa without disrupting the existing search pipeline, violating layer boundaries, or coupling to a single embedding provider?

## Decision

Integrate vector search as a **new search parameter type** that flows through the existing expression tree and query builder pipeline, backed by SQL Server 2025's native vector support.

```mermaid
flowchart TB
    subgraph "Write Path"
        A[Resource Create/Update] --> B[ElementSearchIndexer]
        B --> C[VectorSearchParameterRowGenerator]
        C --> D["EmbeddingProvider (External API)"]
        D --> E["VectorSearchParam Table (VECTOR column)"]
    end
    subgraph "Read Path"
        F["GET /Observation?_vector=..."] --> G[SearchOptionsBuilder]
        G --> H[VectorSearchExpression]
        H --> I[SearchExpressionQueryBuilder]
        I --> J["VECTOR_DISTANCE(cosine, @input, Embedding)"]
        J --> K[Results ranked by similarity]
    end
```

### Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| New `VectorSearchParam` table with SQL Server `VECTOR(1536)` column | Native storage; no serialization overhead; enables DiskANN indexing |
| `IEmbeddingProvider` interface in Domain layer | Decouples from any specific model (OpenAI, Azure AI, local); allows per-tenant configuration |
| Async embedding generation at index time (write path) | Keeps read path fast; embeddings computed once per resource version |
| Custom search parameter prefix `_vector` | Non-standard FHIR extension; avoids collision with spec-defined parameters |
| Similarity threshold + top-K as query parameters | `_vector.threshold=0.8&_vector.count=10` — controls precision/recall tradeoff |
| DiskANN index opt-in (not default) | Preview limitations: table becomes read-only while index exists; only viable for read-heavy tenants |
| File-based backend: skip vector search (return OperationOutcome) | Vector search requires a vector-capable backend; file system gracefully degrades |

### Schema Addition

```sql
CREATE TABLE dbo.VectorSearchParam (
    ResourceTypeId      SMALLINT        NOT NULL,
    ResourceSurrogateId BIGINT          NOT NULL,
    SearchParamId       SMALLINT        NOT NULL,
    Embedding           VECTOR(1536)    NOT NULL,
    ModelId             VARCHAR(128)    NOT NULL,
    CONSTRAINT FK_VectorSearchParam_Resource
        FOREIGN KEY (ResourceTypeId, ResourceSurrogateId)
        REFERENCES dbo.Resource (ResourceTypeId, ResourceSurrogateId)
);
```

### Extension Points

- **`IEmbeddingProvider`**: `Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken)` — implementations for Azure OpenAI, local models, or custom pipelines
- **`IVectorExtractionStrategy`**: Determines which text to embed per resource type (e.g., `Condition.code.text + Condition.note`, `Observation.code.display + Observation.valueString`)
- **Configuration per tenant**: Tenants opt-in via `SearchParameter` resources with type `vector` (custom extension)

### F5 Developer Experience

- Local dev: `IEmbeddingProvider` has an in-memory stub that returns deterministic hash-based vectors (no external API calls)
- SQL Server LocalDB or Docker: `VECTOR` type available in SQL Server 2025 Developer Edition
- File system backend: Vector parameters silently skipped during indexing; queries return `OperationOutcome` with informational issue

## Consequences

### Positive

- Enables semantic/similarity search over clinical narratives and coded concepts
- Follows existing Ignixa patterns (row generator, expression tree, query builder)
- Provider-agnostic: swap embedding models without schema changes
- Per-tenant opt-in: no impact on tenants that don't enable vector parameters
- Native SQL Server integration avoids external vector database dependency

### Negative

- SQL Server 2025 required (vector type not available in older versions)
- DiskANN index preview limitations: read-only table constraint forces architectural choices (separate table, index rebuild workflow)
- Embedding generation adds write latency (mitigated by async/background processing)
- Model drift: re-embedding required when switching embedding models (reindex operation)
- Non-standard FHIR: `_vector` parameter is Ignixa-specific until HL7 standardizes semantic search

## References

- **SQL Server 2025 Vector Type**: https://learn.microsoft.com/en-us/sql/t-sql/data-types/vector-data-type
- **VECTOR_DISTANCE Function**: https://learn.microsoft.com/en-us/sql/t-sql/functions/vector-distance-transact-sql
- **DiskANN Overview**: https://github.com/Azure-Samples/azure-sql-diskann
- **Microsoft FHIR Server Vector Branch**: https://github.com/microsoft/fhir-server/compare/main...personal/bkowitz/vector-search-implementation
- **Related ADR**: adr-2509-inmemory-search.md (file-based search patterns)
