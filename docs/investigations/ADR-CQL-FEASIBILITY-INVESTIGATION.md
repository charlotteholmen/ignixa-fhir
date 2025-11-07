# ADR: Clinical Quality Language (CQL) Feasibility Investigation

**Status**: Investigation
**Date**: 2025-11-07
**Decision**: To Be Determined

## Executive Summary

Clinical Quality Language (CQL) is an ANSI Normative Standard (v1.5.3) designed for expressing clinical quality measures, clinical decision support, and computable guidelines. This investigation examines whether implementing CQL on the Ignixa FHIR server is technically feasible, strategically valuable, and architecturally sound **given the requirement for a pure .NET solution**.

**Key Finding**: **Full CQL support is NOT feasible as a pure .NET solution** due to the Java-based CQL-to-ELM translator dependency. The only viable pure .NET option is **read-only ELM execution** (pre-compiled CQL), which addresses ~40% of use cases. For the majority of use cases, **enhanced FHIRPath** or **SQL-on-FHIR** are more practical pure .NET alternatives that deliver 70-80% of the value without the complexity.

## Pure .NET Constraint Analysis

**Requirement**: Implement CQL support using only .NET libraries, no Java dependencies.

**Critical Blocker**: The official CQL-to-ELM translator is Java-based, and **no pure .NET CQL-to-ELM translator exists**.

### CQL Execution Pipeline

```
┌─────────────┐        ┌──────────────┐        ┌─────────────┐
│  CQL Text   │───────▶│ CQL-to-ELM   │───────▶│ ELM JSON/XML│
│  (Author)   │        │  Translator  │        │  (Compiled) │
└─────────────┘        └──────────────┘        └─────────────┘
                              ⚠️                       │
                         JAVA REQUIRED                │
                         NOT PURE .NET                ▼
                                              ┌─────────────┐
                                              │   CQL       │
                                              │  Engine     │
                                              │ (.NET ✅)   │
                                              └─────────────┘
                                                      │
                                                      ▼
                                              ┌─────────────┐
                                              │   Results   │
                                              └─────────────┘
```

### Pure .NET Options

| Approach | Pure .NET? | Feasibility | Use Cases Covered |
|----------|-----------|-------------|-------------------|
| **Full CQL (CQL text → ELM → Execute)** | ❌ **NO** | Not possible without Java | 100% |
| **Read-only ELM (Pre-compiled ELM → Execute)** | ✅ **YES** | Feasible | ~40% (consume measures) |
| **Build .NET CQL-to-ELM translator** | ✅ **YES** | 6-12 months | 100% (long-term) |
| **Enhanced FHIRPath** | ✅ **YES** | 4-6 weeks | ~70% (queries) |
| **SQL-on-FHIR** | ✅ **YES** | 2-4 weeks | ~80% (analytics) |

### Recommendation for Pure .NET

**DO NOT attempt full CQL implementation** with Java interop. Instead:

1. **Short-term** (4-6 weeks): Enhance FHIRPath for query capabilities
2. **Medium-term** (2-4 weeks): Implement SQL-on-FHIR for analytics
3. **Long-term** (if justified): Build pure .NET CQL-to-ELM translator (6-12 months)

---

## Context

### What is CQL?

Clinical Quality Language (CQL) is a high-level, domain-specific language focused on clinical quality and targeted at measure and decision support artifact authors. It is:

- **ANSI Normative Standard** (v1.5.3, generated March 7, 2025)
- **Superset of FHIRPath** - any valid FHIRPath expression is valid CQL
- **Mandated by CMS** - transitioning all quality measures to digital quality measures (dQMs) using CQL
- **Used across domains**: Quality measurement, clinical decision support, cohort definition, public health reporting, computable guidelines

### Current Ignixa Architecture

Ignixa currently has:
- ✅ **Custom FHIRPath implementation** (`Ignixa.FhirPath` - Superpower parser-based)
- ✅ **FHIRPath evaluation engine** (used for validation invariants)
- ✅ **Multi-FHIR version support** (R4, R4B, R5, STU3)
- ✅ **Multi-tenant architecture** with partition isolation
- ✅ **High-performance search** (JSON + SQL hybrid)
- ❌ **No CQL support** (no Library, Measure, PlanDefinition resources)

## CQL vs FHIRPath Comparison

### Relationship

```
FHIRPath ⊂ CQL
├── FHIRPath: Navigation + simple expressions
└── CQL: FHIRPath + clinical logic + temporal reasoning + libraries + functions
```

### Scope Comparison

| Capability | FHIRPath | CQL |
|------------|----------|-----|
| **Path navigation** | ✅ Core feature | ✅ Inherited |
| **Boolean logic** | ✅ Basic | ✅ Extended |
| **Data extraction** | ✅ Primary use | ✅ Yes |
| **Temporal reasoning** | ❌ Limited | ✅ Rich (intervals, duration) |
| **Queries across resources** | ❌ No | ✅ Yes (`from`, `where`) |
| **Aggregations** | ❌ Limited | ✅ Full (`sum`, `avg`, `count`) |
| **Libraries/reusability** | ❌ No | ✅ Yes (FHIR Library resource) |
| **Type system** | ✅ FHIR types | ✅ FHIR + CQL types |
| **Function definitions** | ❌ No | ✅ Yes |
| **Value set operations** | ❌ No | ✅ Yes (`in`, `expand`) |
| **Clinical calculations** | ❌ Limited | ✅ Yes (BMI, age, risk scores) |

### Implementation Complexity

| Aspect | FHIRPath | CQL |
|--------|----------|-----|
| **Specification size** | ~30 pages | ~200+ pages |
| **Parser complexity** | Low | High |
| **Runtime complexity** | Low | High |
| **Authoring expertise** | Moderate | High |
| **Tooling maturity** | High | Moderate |
| **Implementation cost** | Low | **Very High** |
| **Maintenance cost** | Low | **High** |

## Use Cases Analysis

### Primary CQL Use Cases

1. **Digital Quality Measures (dQMs)**
   - CMS-mandated transition for all reporting programs
   - Examples: HEDIS measures (NCQA), CMS quality measures
   - **Requires**: Measure resource, Library resource, ValueSet expansion, Terminology server

2. **Clinical Decision Support (CDS)**
   - Real-time alerts and recommendations
   - PlanDefinition + ActivityDefinition resources
   - **Requires**: CQL engine, CDS Hooks integration, external data retrieval

3. **Public Health Reporting**
   - CDC NHSN reporting via FHIR APIs
   - Automated case detection and reporting
   - **Requires**: Background processing, bulk data access, external system integration

4. **Cohort Definition**
   - Research and analytics
   - Patient population identification
   - **Alternative**: Can be achieved with advanced FHIR search + FHIRPath

5. **Computable Guidelines**
   - CPGs (Clinical Practice Guidelines)
   - Treatment pathways and protocols
   - **Requires**: Complex orchestration, workflow integration

### Ignixa Current Roadmap Alignment

From `ADR-2500-master-roadmap.md` analysis:

| Use Case | Ignixa Priority | Complexity | Dependencies |
|----------|----------------|------------|--------------|
| dQMs | ❓ Unknown | Very High | Terminology, ValueSets, Measure |
| CDS | ❓ Unknown | Very High | Real-time, integrations |
| Cohort queries | 🟢 Achievable | Medium | Search improvements |
| Public health | ❓ Unknown | High | Bulk operations, subscriptions |

**Finding**: No explicit CQL requirements identified in current roadmap phases.

## Technical Implementation Analysis

### Architecture Option 1: Full CQL Support (Industry Standard)

**Components Required**:

```
CQL Text
    ↓
[CQL-to-ELM Translator] (Java-based ⚠️)
    ↓
ELM JSON/XML
    ↓
[CQL Engine] (.NET: Hl7.Cql.Elm)
    ↓
[Data Provider] (FHIR Server integration)
    ↓
[Terminology Service] (ValueSet expansion)
    ↓
Results
```

**NuGet Packages**:
- `Hl7.Cql.Elm` (v2.2.0) - ELM execution engine
- `Hl7.Cql.Fhir` - FHIR data model bindings
- `Hl7.Cql.Model` - CQL type system
- Dependencies: Microsoft.CodeAnalysis.CSharp, System.Text.Json

**New FHIR Resources**:
- `Library` - CQL/ELM storage, versioning, dependencies
- `Measure` - Quality measure definitions
- `MeasureReport` - Evaluation results
- `PlanDefinition` - Decision support logic
- `ActivityDefinition` - Actions to perform
- `RequestGroup` - Grouped actions

**New Operations**:
- `Library/$evaluate` - Execute CQL library
- `Measure/$evaluate-measure` - Calculate measure
- `PlanDefinition/$apply` - Execute CDS logic
- `ValueSet/$expand` - Required for CQL value set operations

**Estimated Effort**: 12-16 weeks (3-4 months)

**Challenges**:
1. ⚠️ **Java Dependency**: CQL-to-ELM translator requires Java runtime
   - **Mitigation**: Pre-compile CQL to ELM offline, store ELM in Library resources
   - **Trade-off**: Cannot support dynamic CQL authoring in server

2. ⚠️ **Terminology Server**: CQL heavily relies on ValueSet operations
   - **Current Status**: Ignixa has no terminology server
   - **Required**: Implement ValueSet storage + $expand operation
   - **Complexity**: High (CodeSystem, ConceptMap, ValueSet resources)

3. ⚠️ **Data Provider Integration**: CQL engine needs FHIR data access
   - **Challenge**: Multi-tenant partition awareness
   - **Challenge**: Performance (N+1 query problem in CQL expressions)
   - **Challenge**: Search parameter support parity

4. ⚠️ **ELM Runtime Compilation**: Generates C# code dynamically
   - **Dependency**: Microsoft.CodeAnalysis.CSharp (Roslyn)
   - **Performance**: JIT compilation overhead
   - **Memory**: Increased memory footprint

### Architecture Option 2: FHIRPath Enhancement (Pragmatic)

**Enhance existing `Ignixa.FhirPath` for common use cases**:

```
Enhanced FHIRPath
    ├── Temporal functions (age, duration)
    ├── Aggregation functions (sum, avg, count)
    ├── Advanced filtering
    ├── Cross-resource queries (limited)
    └── Value set checking (in-memory)
```

**Benefits**:
- ✅ No Java dependency
- ✅ No new FHIR resources
- ✅ Builds on existing parser
- ✅ Lighter weight
- ✅ Full control over implementation

**Limitations**:
- ❌ Not CQL-compliant
- ❌ Cannot execute CMS/NCQA measures directly
- ❌ No Library resource reusability
- ❌ Cannot integrate with CQL ecosystem

**Estimated Effort**: 4-6 weeks

### Architecture Option 3: Hybrid Approach

**Phase 1**: FHIRPath enhancements (4-6 weeks)
**Phase 2**: CQL read-only support (8-10 weeks)
- Accept pre-compiled ELM (Library resources)
- Implement CQL engine integration
- No CQL-to-ELM translation (offline only)

**Phase 3**: Full CQL authoring (future)
- Add CQL-to-ELM translation
- Online authoring tools

## Vendor Implementations Review

### HAPI FHIR (CQF Ruler)

**Architecture**:
- Java-based FHIR server
- CQF Ruler plugin adds CQL support
- Uses `cql-engine` library (Java)
- Integrated terminology server

**Effort**: Pre-built, ~2 weeks integration

**Alignment**: ❌ Java-based, not compatible with Ignixa

### Smile CDR

**Architecture**:
- Commercial Java-based FHIR server
- Embedded CQL engine
- Full terminology server
- CQL module configuration

**Licensing**: Commercial
**Alignment**: ❌ Proprietary, different tech stack

### Firely .NET SDK

**Architecture**:
- NuGet: `Hl7.Cql.Elm` (NCQA + Firely)
- Executes ELM (not CQL text)
- Requires CQL-to-ELM translator (Java-based)
- Pre-compilation recommended for production

**Testing**: Validated against HEDIS + CMS measures
**Downloads**: 24.1K total
**Alignment**: ✅ .NET, ⚠️ Requires Java for translation

### Google CQL Engine

**Architecture**:
- Experimental
- For analyzing FHIR data at scale
- Not production-ready

**Alignment**: ❌ Experimental status

## Drawbacks and Limitations

### 1. Implementation Complexity

From research: *"The degree of difficulty for implementing CQL is higher than [simpler approaches], especially if targeting comprehensive coverage of CQL features. This means that CQL implementations are more costly to build and maintain, and there are only a small number that have achieved good coverage of the specification."*

### 2. Authoring Expertise

From research: *"CQL authoring also requires a higher level of expertise due to the scope and complexity of the language."*

**Impact**:
- Clinical informaticists need CQL training
- Authoring tools required (VS Code extension, CQL Builder)
- Testing/validation frameworks needed

### 3. Java Dependency for Translation

**CQL Text → ELM Translation** requires Java-based translator.

**Options**:
- a) Run Java process from .NET (interop complexity)
- b) Pre-compile CQL to ELM offline (limits flexibility)
- c) Build .NET CQL-to-ELM translator (6+ months effort)

### 4. Terminology Server Requirement

CQL heavily uses value sets:
```cql
[Condition: "Diabetes Value Set"]
[Observation: "HbA1c Value Set"] O
  where O.value > 7 '%'
```

**Required**:
- ValueSet resource storage
- CodeSystem resource storage
- ValueSet `$expand` operation
- ConceptMap for translations
- SNOMED CT, LOINC, RxNorm datasets (GB+ storage)

**Ignixa Status**: ❌ No terminology server
**Estimated Effort**: 8-12 weeks standalone

### 5. Performance Concerns

**Multi-tenant impact**:
- CQL queries may cross partitions (not supported)
- N+1 query problem in CQL evaluation
- ValueSet expansion overhead
- Dynamic compilation overhead

**From Ignixa architecture**:
- Partition isolation is critical
- High RPS requirements (10K+ RPS export)
- Query performance optimized for specific patterns

**Risk**: CQL evaluation may not meet Ignixa performance targets.

### 6. Maintenance Burden

**Ongoing**:
- CQL spec updates (2.0 in trial-use ballot)
- Firely SDK updates
- Terminology dataset updates (quarterly)
- Measure library updates (annual)
- Test suite maintenance (NCQA, CMS measures)

**Estimated**: 15-20% of developer time ongoing

## Alternative Solutions

### Option A: Partner with Terminology Service Providers

**Approach**: Don't build CQL engine, integrate with external services.

**Providers**:
- Ontoserver (CSIRO)
- Snowstorm (SNOMED)
- HAPI FHIR Terminology Service
- TX.fhir.org (HL7)

**Benefits**:
- ✅ Offload complexity
- ✅ Expert maintenance
- ✅ Up-to-date terminology

**Drawbacks**:
- ❌ External dependency
- ❌ Latency (network calls)
- ❌ Cost (per-query pricing)
- ❌ Multi-tenant isolation concerns

### Option B: SQL-on-FHIR for Analytics

**Approach**: Use SQL-on-FHIR v2 for cohort queries and analytics instead of CQL.

**Benefits**:
- ✅ SQL is universal
- ✅ Leverages existing SQL Provider architecture
- ✅ ViewDefinition resources (lighter than CQL)
- ✅ Better performance for analytics

**Drawbacks**:
- ❌ Not suitable for real-time CDS
- ❌ Cannot execute CMS/NCQA measures
- ❌ Different paradigm than CQL

**Ignixa Alignment**: 🟢 Strong - already have `Ignixa.DataLayer.Sql`

### Option C: GraphQL/FHIRPath for Complex Queries

**Approach**: Enhance FHIRPath or add GraphQL for complex queries.

**Benefits**:
- ✅ More flexible than basic search
- ✅ Client-defined queries
- ✅ No clinical logic on server
- ✅ Lighter implementation

**Drawbacks**:
- ❌ Not CQL-compliant
- ❌ Logic lives in client
- ❌ No measure authoring

### Option D: Await Pure .NET CQL Implementation

**Approach**: Wait for community to build native .NET CQL-to-ELM translator.

**Status**: No active projects identified

**Timeline**: Unknown (could be years)

**Risk**: ⚠️ May never materialize

## Cost-Benefit Analysis

### Full CQL Implementation

**Costs**:
- Development: 12-16 weeks (1-2 developers)
- Terminology server: 8-12 weeks
- Testing/validation: 4-6 weeks
- Documentation: 2-3 weeks
- **Total**: 26-37 weeks (6-9 months)
- Ongoing maintenance: 15-20% developer time

**Benefits**:
- ✅ Industry standard compliance
- ✅ CMS measure compatibility
- ✅ Clinical decision support capable
- ✅ Market differentiator
- ✅ Academic/research appeal

**ROI**: Depends on customer demand for dQMs/CDS

### FHIRPath Enhancement

**Costs**:
- Development: 4-6 weeks
- Testing: 1-2 weeks
- **Total**: 5-8 weeks (1-2 months)
- Ongoing: 5% developer time

**Benefits**:
- ✅ Improved query capabilities
- ✅ No new dependencies
- ✅ Lower complexity

**Limitations**:
- ❌ Not industry standard
- ❌ Limited clinical reasoning

## Recommendations

### Primary Recommendation: **DO NOT Implement Full CQL Support**

**Rationale**:
1. **❌ BLOCKER: Java dependency** - **Pure .NET requirement makes full CQL implementation impossible** without building a CQL-to-ELM translator from scratch (6-12 months)
2. **No clear customer demand** - Roadmap doesn't indicate dQM/CDS requirements
3. **High implementation cost** - Even read-only ELM: 8-10 weeks + terminology server (8-12 weeks)
4. **Terminology server prerequisite** - Major undertaking on its own (ValueSet, CodeSystem, $expand)
5. **Performance risks** - Multi-tenant, high-RPS architecture may not align with CQL query patterns
6. **Better pure .NET alternatives exist** - FHIRPath enhancements, SQL-on-FHIR deliver 70-80% of value in 25% of time

### Secondary Recommendation: **Invest in FHIRPath Enhancements**

**Immediate (Phase 23-24)**:
- Extend `Ignixa.FhirPath` with temporal functions (`age()`, `duration()`)
- Add aggregation functions (`sum()`, `avg()`, `count()`, `min()`, `max()`)
- Improve cross-resource navigation (limited)
- **Effort**: 4-6 weeks
- **ROI**: High - benefits validation, search, and custom queries

**Benefits**:
- Incremental improvement
- No architectural disruption
- Addresses 70% of common use cases
- Foundation for future CQL if needed

### Tertiary Recommendation: **Monitor CQL Adoption Signals**

**Watch for**:
1. **Customer requests** - Explicit dQM/CDS requirements
2. **Regulatory mandates** - CMS requiring CQL in specific contexts
3. **.NET ecosystem** - Pure .NET CQL-to-ELM translator emergence
4. **Terminology partnerships** - Cost-effective external terminology services
5. **CQL 2.0** - Breaking changes that might simplify implementation

### Conditional Recommendation: **CQL Lite (Read-Only ELM)**

**If customer demand emerges**:

**Phase 1** (8-10 weeks):
- Implement Library resource (ELM storage)
- Integrate Hl7.Cql.Elm engine
- Basic data provider (single partition)
- **Limitation**: Pre-compiled ELM only (offline CQL translation)

**Phase 2** (later):
- Add Measure resource + $evaluate-measure
- Multi-tenant data provider
- Performance optimization

**Phase 3** (future):
- CQL-to-ELM translation (if pure .NET available)
- Full CDS support

## Building a Pure .NET CQL-to-ELM Translator

If full CQL support becomes a **strategic imperative** (e.g., major contract requires it), the only pure .NET path is to **build a CQL-to-ELM translator from scratch**.

### Effort Estimate

| Component | Effort | Complexity |
|-----------|--------|------------|
| **Lexer/Parser** (CQL grammar) | 6-8 weeks | High |
| **AST (Abstract Syntax Tree)** | 2-3 weeks | Medium |
| **Semantic analyzer** (type checking) | 6-8 weeks | Very High |
| **ELM generator** (AST → ELM) | 6-8 weeks | Very High |
| **CQL standard library** | 4-6 weeks | High |
| **Testing framework** | 4-6 weeks | Medium |
| **CQL 1.5.3 spec compliance** | 4-6 weeks | High |
| **Documentation + examples** | 2-3 weeks | Medium |
| **TOTAL** | **34-48 weeks** | **8-12 months** |

### Technical Approach

**Recommended stack**:
- **Parser**: Superpower (already used in Ignixa.FhirPath) or ANTLR4
- **Code generation**: Roslyn (already dependency of Hl7.Cql.Elm)
- **Testing**: CQL specification test suite (1000+ test cases)

**Architecture**:
```
Ignixa.Cql.Translator/
├── Lexer/                  # Tokenization
├── Parser/                 # Grammar → AST
├── Semantic/               # Type checking, name resolution
├── CodeGen/                # AST → ELM
├── StandardLibrary/        # Built-in functions
└── Tests/                  # CQL spec test suite
```

**Reference implementations to study**:
- Java CQL-to-ELM translator: https://github.com/cqframework/clinical_quality_language
- TypeScript CQL parser: https://github.com/cqframework/cql-execution

### Risk Analysis

| Risk | Impact | Mitigation |
|------|--------|------------|
| **Spec complexity** | High | Start with CQL subset, expand iteratively |
| **Semantic analysis** | Very High | Hire/consult clinical informaticist |
| **Compliance testing** | High | Use official CQL test suite |
| **Ongoing maintenance** | High | Allocate 20-30% developer time |
| **CQL 2.0 breaking changes** | Medium | Design for extensibility |

### Alternative: Community Contribution

**Option**: Partner with Firely, NCQA, or HL7 community to fund/build open-source pure .NET CQL translator.

**Benefits**:
- ✅ Shared cost with community
- ✅ Broader adoption and testing
- ✅ Official HL7 backing potential

**Challenges**:
- ⚠️ Coordination overhead
- ⚠️ Slower decision-making
- ⚠️ Need to align with community priorities

### ROI Analysis

**Costs**:
- Development: 8-12 months (2 senior developers)
- Maintenance: 20-30% developer time ongoing
- Testing: 1000+ test cases validation

**Benefits**:
- ✅ Pure .NET CQL ecosystem (first-mover advantage)
- ✅ Open-source contribution (industry recognition)
- ✅ Full control over implementation
- ✅ Enable full CQL support on Ignixa

**Break-even**: Likely 3-5 years unless multiple customers demand CQL

### Recommendation

**Only pursue if**:
- [ ] 5+ customers explicitly requesting CQL authoring (not just measure execution)
- [ ] $500K+ contract value dependent on CQL
- [ ] Strategic partnership with NCQA/Firely to share development
- [ ] 2+ senior .NET developers available for 12+ months
- [ ] Executive commitment to 2-3 year investment

**Otherwise**: Use read-only ELM execution or FHIRPath enhancements.

---

## Decision Criteria

**Implement CQL if**:
- [ ] 3+ customers explicitly request dQM/CDS capabilities
- [ ] CMS mandate requires CQL for specific use case
- [ ] Pure .NET CQL-to-ELM translator becomes available
- [ ] Partnership with terminology service provider established
- [ ] Dedicated 2+ developers available for 6+ months

**Enhance FHIRPath if**:
- [x] Need better query capabilities now
- [x] Want to avoid Java dependency
- [x] Have 1 developer for 4-6 weeks
- [x] Prioritize performance and simplicity

## Conclusion

**Clinical Quality Language is the industry standard for digital quality measures and clinical decision support**, and CMS is mandating its use for quality reporting programs. However, **full CQL support is NOT feasible as a pure .NET solution** without building a CQL-to-ELM translator from scratch (8-12 months).

### Critical Finding: Pure .NET Blocker

**The official CQL-to-ELM translator is Java-based, and no pure .NET implementation exists.** This is a **hard blocker** for full CQL authoring support.

### Pure .NET Options Summary

| Solution | Effort | CQL Compliance | Use Cases |
|----------|--------|----------------|-----------|
| **Full CQL** | ❌ Not possible | 100% | All (blocked by Java) |
| **Build translator** | 8-12 months | 100% | All (huge investment) |
| **Read-only ELM** | 8-10 weeks | 40% | Consume measures only |
| **FHIRPath++** | 4-6 weeks | 0% | 70% of queries |
| **SQL-on-FHIR** | 2-4 weeks | 0% | 80% of analytics |

### Recommended Path Forward (Pure .NET)

**PRIMARY**: **Enhance FHIRPath** (4-6 weeks)
- ✅ Pure .NET
- ✅ Builds on existing infrastructure
- ✅ Addresses 70% of query use cases
- ✅ Foundation for future enhancements

**SECONDARY**: **Implement SQL-on-FHIR** (2-4 weeks)
- ✅ Pure .NET
- ✅ Leverages existing Ignixa.DataLayer.Sql
- ✅ Addresses 80% of analytics use cases
- ✅ Standards-compliant (FHIR specification)

**DEFER**: **Full CQL support**
- ❌ Requires Java OR 8-12 month .NET translator development
- ⚠️ No clear customer demand
- ⚠️ High ongoing maintenance (20-30% developer time)

**CONDITIONAL**: **Build pure .NET CQL-to-ELM translator**
- Only if 5+ customers demand CQL authoring
- Only if $500K+ contract value depends on it
- Consider community partnership (Firely/NCQA/HL7)

### If Customer Demands CQL

**Option A**: Read-only ELM execution (8-10 weeks + terminology server)
- Customers author CQL externally (Java tooling)
- Upload compiled ELM to Library resources
- Ignixa executes ELM only

**Option B**: Partner with terminology service provider
- Offload CQL execution to external service
- Integrate via API

**Option C**: Strategic partnership to build .NET translator
- 8-12 month project
- Open-source contribution
- Shared maintenance costs

---

## References

- CQL Specification v1.5.3: https://cql.hl7.org/
- FHIR Clinical Reasoning Module: https://hl7.org/fhir/clinicalreasoning-module.html
- Firely CQL SDK: https://github.com/FirelyTeam/firely-cql-sdk
- Hl7.Cql.Elm NuGet: https://www.nuget.org/packages/Hl7.Cql.Elm/
- Quality Measure IG: https://build.fhir.org/ig/HL7/cqf-measures/
- CQF Ruler: https://github.com/cqframework/cqf-ruler

## Appendices

### Appendix A: CQL Example

```cql
library DiabetesQualityMeasure version '1.0.0'

using FHIR version '4.0.1'

include FHIRHelpers version '4.0.1'

codesystem "LOINC": 'http://loinc.org'
valueset "Diabetes": 'http://example.org/fhir/ValueSet/diabetes'
valueset "HbA1c": 'http://example.org/fhir/ValueSet/hba1c'

context Patient

define "Has Diabetes":
  exists([Condition: "Diabetes"])

define "Recent HbA1c":
  [Observation: "HbA1c"] O
    where O.effective.toDateTime() during
      Interval[Today() - 1 year, Today()]

define "Poor Glycemic Control":
  "Has Diabetes"
    and exists(
      "Recent HbA1c" H
        where H.value as Quantity > 9 '%'
    )
```

**Translation**: Requires Java-based translator to convert to ELM.

**Execution**: .NET Hl7.Cql.Elm engine evaluates ELM.

**Dependencies**:
- ValueSet expansion (Diabetes, HbA1c)
- FHIR data access (Condition, Observation)
- Terminology server

### Appendix B: Equivalent FHIRPath (Enhanced)

```
// Has Diabetes
%resource.where(
  resourceType = 'Condition'
  and code.memberOf('http://example.org/fhir/ValueSet/diabetes')
).exists()

// Recent HbA1c > 9%
%resource.where(
  resourceType = 'Observation'
  and code.memberOf('http://example.org/fhir/ValueSet/hba1c')
  and effective.toDateTime() > now() - 1 year
  and value.ofType(Quantity).value > 9
  and value.ofType(Quantity).unit = '%'
).exists()
```

**Note**: FHIRPath cannot cross resources or define reusable functions like CQL.

### Appendix C: Market Demand Analysis

**CQL Adoption Indicators**:
- ✅ CMS mandating dQMs for quality programs (MIPS, Hospital IQR)
- ✅ NCQA HEDIS measures transitioning to CQL
- ✅ CDC NHSN using CQL for reporting
- ⚠️ Adoption concentrated in US healthcare

**FHIR Server Vendor Support**:
- ✅ HAPI FHIR - Full support
- ✅ Smile CDR - Full support
- ✅ Microsoft FHIR Server - Limited (no CQL engine)
- ❌ Firely Server - No built-in CQL
- ❌ AWS HealthLake - No CQL
- ❌ Google Healthcare API - No CQL

**Finding**: CQL support is not universal, even among major vendors.

### Appendix D: Implementation Checklist (If Proceeding)

**Prerequisites**:
- [ ] Terminology server (ValueSet, CodeSystem, $expand)
- [ ] CQL-to-ELM strategy (offline pre-compilation vs Java interop)
- [ ] Performance testing framework for multi-tenant CQL
- [ ] Customer validation of use cases

**Phase 1 - Foundation** (4 weeks):
- [ ] Add NuGet: Hl7.Cql.Elm, Hl7.Cql.Fhir
- [ ] Implement Library resource CRUD
- [ ] Implement FHIR data provider (single partition)
- [ ] Basic CQL engine integration

**Phase 2 - Measures** (4 weeks):
- [ ] Implement Measure resource
- [ ] Implement MeasureReport resource
- [ ] Implement $evaluate-measure operation
- [ ] Multi-tenant data provider

**Phase 3 - Validation** (4 weeks):
- [ ] Test with NCQA HEDIS measures
- [ ] Test with CMS measures
- [ ] Performance benchmarking
- [ ] Documentation

**Phase 4 - Production** (2 weeks):
- [ ] Monitoring and logging
- [ ] Error handling
- [ ] Rate limiting
- [ ] Deployment

**Total**: 14 weeks minimum (assumes terminology server exists)
