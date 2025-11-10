# SMART on FHIR v2 Implementation Investigation

**Date**: 2025-11-09  
**Status**: Investigation  
**Authors**: Claude AI  
**Related**: ADR-2523 (Multi-Tenancy), ADR-2524 (FHIR History)

---

## Executive Summary

This investigation provides a comprehensive technical analysis of implementing **SMART on FHIR v2.2** natively within the Ignixa FHIR server. The key challenge is supporting SMART's standard scope format (`patient/Patient.read`) while maintaining compatibility with Azure EntraID, which rejects scopes containing forward slash characters.

**Proposed Solution**: Implement bidirectional scope translation (`.` ↔ `/`) with a new `Ignixa.Application.SmartOnFhir` assembly, establishing a dual authorization system supporting both traditional RBAC and SMART scope-based access control.

**Estimated Effort**: 10 weeks (5 phases)

**Key Deliverables**:
1. New `Ignixa.Application.SmartOnFhir` project assembly
2. Bidirectional EntraID scope translation (`.` ↔ `/`)
3. Dual authorization system (RBAC + SMART scopes)
4. Three-layer access control (endpoint, resource-type, instance)
5. Patient compartment enforcement
6. SMART discovery endpoints

---

## Table of Contents

1. [Background](#background)
2. [Problem Statement](#problem-statement)
3. [Proposed Solution](#proposed-solution)
4. [Architecture](#architecture)
5. [Implementation Phases](#implementation-phases)
6. [Security Considerations](#security-considerations)
7. [Testing Strategy](#testing-strategy)
8. [Configuration](#configuration)
9. [Alternatives Considered](#alternatives-considered)
10. [Risks & Mitigation](#risks--mitigation)
11. [Success Criteria](#success-criteria)
12. [References](#references)

---

## Background

### SMART on FHIR Overview

SMART (Substitutable Medical Applications, Reusable Technologies) on FHIR is an OAuth 2.0-based authorization framework that enables third-party applications to securely access FHIR resources with granular permissions.

**Key Features**:
- **Granular Scopes**: Resource-type and permission-level control (e.g., `patient/Observation.read`)
- **Launch Context**: Patient, encounter, and practitioner context passed via tokens
- **Multiple Flows**: EHR launch, standalone launch, backend services (client credentials)
- **User/Patient/System Contexts**: Different authorization contexts for different use cases

### SMART v2.2 Scope Syntax

```
{context}/{resourceType}.{permissions}[?param=value&...]

Examples:
- patient/Observation.rs                    # Read/search patient observations
- user/Appointment.cruds                    # Full access to user appointments
- system/Patient.r                          # Read all patients (backend)
- patient/Observation.rs?category=laboratory # Only lab observations
```

**Components**:
- **Context**: `patient/` (single patient), `user/` (user's data), `system/` (all data)
- **ResourceType**: FHIR resource name or `*` (wildcard)
- **Permissions**: `c` (create), `r` (read), `u` (update), `d` (delete), `s` (search)
- **Constraints**: Optional FHIR search parameter filters

---

## Problem Statement

### Challenge 1: EntraID Scope Incompatibility

**Issue**: Azure EntraID (Azure AD) v2 endpoints **reject OAuth scopes containing forward slash (`/`) characters**, which are fundamental to SMART on FHIR's standard scope format.

**Impact**:
- ❌ Standard scope `patient/Patient.read` is rejected by EntraID
- ❌ Cannot use SMART-compliant apps without transformation
- ❌ Microsoft's legacy solution requires external proxy (retiring Sept 2026)

**Microsoft's Official Workaround**:
```
Replace "/" with "."
Replace "*" with "all"
```

### Challenge 2: Dual Authorization Requirements

**Issue**: Need to support two distinct authorization models simultaneously:

1. **General RBAC** - Traditional roles for internal users (Administrator, Clinician, ReadOnly)
2. **SMART Scopes** - Dynamic, granular permissions for third-party apps

**Requirements**:
- Both models must coexist within same tenant
- Different users may use different authorization strategies
- Multi-level access control (endpoint, resource-type, instance)
- Authorization strategy determined by JWT claims

### Challenge 3: Multi-Tenancy Integration

**Issue**: Authorization must integrate seamlessly with existing multi-tenant architecture (ADR-2523).

**Requirements**:
- Per-tenant authorization configuration
- Tenant-scoped tokens prevent cross-tenant access
- Support both FHIR versions and storage backends per tenant
- Respect existing `TenantResolutionMiddleware` and partition strategy

---

## Proposed Solution

### Solution 1: Bidirectional Scope Translation

**Approach**: Translate scopes at the API boundary while using SMART-standard internally.

| SMART Standard | EntraID Compatible | Notes |
|---------------|-------------------|-------|
| `patient/Patient.read` | `patient.Patient.read` | Replace `/` with `.` |
| `user/*.cruds` | `user.all.cruds` | Replace `*` with `all` |
| `system/Observation.rs` | `system.Observation.rs` | System context |
| `patient/Observation.rs?category=lab` | `patient.Observation.rs?category=lab` | Constraints preserved |

**Benefits**:
- ✅ No external proxy required
- ✅ SMART-compliant internally
- ✅ EntraID-compatible externally
- ✅ Reversible, lossless transformation

### Solution 2: New Project Assembly - `Ignixa.Application.SmartOnFhir`

Following the existing `BackgroundOperations` pattern, create a dedicated assembly for SMART functionality.

**Structure**:
```
src/Ignixa.Application.SmartOnFhir/
  ├── Scopes/
  │   ├── SmartScopeParser.cs           # Parse and translate scopes
  │   ├── SmartScopeEvaluator.cs        # Evaluate scope permissions
  │   ├── SmartScopeNormalizer.cs       # EntraID ↔ SMART translation
  │   └── SmartScopeCache.cs            # Cache parsed scopes
  ├── Context/
  │   ├── LaunchContextExtractor.cs     # Extract patient, encounter
  │   ├── SmartContextValidator.cs      # Validate context
  │   └── PatientCompartmentEnforcer.cs # Compartment enforcement
  ├── Authorization/
  │   ├── SmartAuthorizationHandler.cs
  │   ├── SmartAuthorizationEvaluator.cs
  │   └── SmartClaimsTransformer.cs     # JWT claims → SmartContext
  ├── Behaviors/
  │   └── SmartAuthorizationBehavior.cs # Medino pipeline behavior
  ├── Services/
  │   ├── SmartConfigurationService.cs  # .well-known/smart-configuration
  │   ├── SmartCapabilityStatementBuilder.cs
  │   └── SmartTokenIntrospectionService.cs
  ├── Flows/
  │   ├── EhrLaunchHandler.cs           # Future: native OAuth flows
  │   ├── StandaloneLaunchHandler.cs
  │   └── BackendServicesHandler.cs
  └── Extensions/
      └── SmartOnFhirServiceExtensions.cs
```

**Benefits**:
- ✅ Clear feature boundary
- ✅ Optional dependency (can be disabled per tenant)
- ✅ Independent testing and documentation
- ✅ Follows existing codebase pattern

### Solution 3: Three-Layer Authorization

**Architecture**:

```
Layer 1: Endpoint-Level (Middleware)
  → Fast rejection before handler execution
  → JWT validation and claims extraction
  → Basic endpoint access checks
  ↓
Layer 2: Resource-Type Level (Medino Behavior)
  → Validate access to specific resource types
  → Evaluate SMART scopes OR RBAC permissions
  → Runs before handler execution
  ↓
Layer 3: Instance-Level (Handler Logic)
  → Fine-grained authorization on resource content
  → Patient compartment enforcement for patient/ scopes
  → Search parameter constraint validation
```

---

## Architecture

### High-Level Flow

```
┌─────────────────────────────────────────────────────────────┐
│                         HTTP Request                         │
│                Authorization: Bearer <JWT>                   │
└─────────────────────────────────────────────────────────────┘
                           │
                           ↓
┌─────────────────────────────────────────────────────────────┐
│               ASP.NET Core Middleware Pipeline               │
├─────────────────────────────────────────────────────────────┤
│  1. TenantResolutionMiddleware (existing)                   │
│     → Extract TenantId, load TenantConfiguration            │
│  2. UseAuthentication() [ASP.NET Core]                      │
│     → Validate JWT signature, verify issuer/audience        │
│  3. AuthorizationMiddleware [NEW]                           │
│     → Determine auth strategy (RBAC vs SMART)               │
│     → Parse SMART scopes (translate EntraID → Standard)     │
│     → Store scopes in HttpContext.Items                     │
└─────────────────────────────────────────────────────────────┘
                           │
                           ↓
┌─────────────────────────────────────────────────────────────┐
│                    Minimal API Endpoint                      │
│  MapGet("/{resourceType}/{id}", handler)                    │
└─────────────────────────────────────────────────────────────┘
                           │
                           ↓
┌─────────────────────────────────────────────────────────────┐
│                     Medino Pipeline                          │
├─────────────────────────────────────────────────────────────┤
│  1. CapabilityEnforcementBehavior (existing)                │
│  2. SmartAuthorizationBehavior [NEW]                        │
│     → Evaluate SMART scopes OR RBAC permissions             │
│  3. ValidationBehavior (existing)                           │
└─────────────────────────────────────────────────────────────┘
                           │
                           ↓
┌─────────────────────────────────────────────────────────────┐
│                    Request Handler                           │
│  GetResourceHandler.HandleAsync()                           │
│    → Fetch resource                                         │
│    → Instance-level checks (patient compartment)            │
│    → Return resource                                        │
└─────────────────────────────────────────────────────────────┘
```

### Domain Models

#### Core Domain Models (Ignixa.Domain)

```csharp
public record SmartScope
{
    public SmartContext Context { get; init; }
    public string ResourceType { get; init; } = "*";
    public SmartPermissions Permissions { get; init; }
    public Dictionary<string, string>? Constraints { get; init; }
    public string OriginalScope { get; init; } = string.Empty;
}

public enum SmartContext
{
    Patient,  // patient/ prefix
    User,     // user/ prefix
    System    // system/ prefix
}

[Flags]
public enum SmartPermissions
{
    None = 0,
    Create = 1,
    Read = 2,
    Update = 4,
    Delete = 8,
    Search = 16
}

public enum AuthorizationStrategy
{
    RoleBased,     // Traditional RBAC
    SmartScopes,   // SMART on FHIR
    Anonymous      // No auth required
}
```

#### Tenant Configuration Extension

```csharp
public record TenantAuthorizationConfiguration
{
    public bool EnableSmartOnFhir { get; init; } = false;
    public bool EnableRbac { get; init; } = true;
    public string? EntraIdTenantId { get; init; }
    public string? EntraIdClientId { get; init; }
    public string? TokenIssuer { get; init; }
    public string? TokenAudience { get; init; }
    public Dictionary<string, RolePermissions> RolePermissions { get; init; } = new();
}
```

### Key Components

#### 1. SmartScopeParser

Translates EntraID scopes to SMART standard:

```csharp
public class SmartScopeParser
{
    public static SmartScope Parse(string entraIdScope)
    {
        // "patient.Observation.rs" → "patient/Observation.rs"
        var standardScope = TranslateFromEntraId(entraIdScope);
        
        // Parse components: context, resource type, permissions, constraints
        // Return structured SmartScope object
    }
    
    private static string TranslateFromEntraId(string entraIdScope)
    {
        return entraIdScope
            .ReplaceFirst(".", "/")     // patient.X → patient/X
            .Replace(".all.", "/*.")    // .all. → /*.
            .Replace(".all", "/*");     // .all → /*
    }
}
```

#### 2. SmartScopeEvaluator

Evaluates if user scopes satisfy required scope:

```csharp
public class SmartScopeEvaluator
{
    public bool Evaluate(
        IEnumerable<SmartScope> userScopes,
        SmartScope requiredScope,
        FhirRequestContext requestContext)
    {
        foreach (var userScope in userScopes)
        {
            // 1. Context must match
            // 2. Resource type must match (or wildcard)
            // 3. Permissions must be sufficient
            // 4. Constraints must be satisfied
            
            if (AllChecksPass())
                return true;
        }
        return false;
    }
}
```

#### 3. AuthorizationMiddleware

HTTP pipeline integration:

```csharp
public class AuthorizationMiddleware
{
    public async Task InvokeAsync(HttpContext context)
    {
        // 1. Skip public endpoints
        // 2. Extract and validate JWT
        // 3. Determine authorization strategy
        // 4. Parse SMART scopes if applicable
        // 5. Store in HttpContext.Items
    }
}
```

#### 4. SmartAuthorizationBehavior

Medino pipeline behavior for resource-type authorization:

```csharp
public class SmartAuthorizationBehavior<TRequest, TResponse> 
    : IPipelineBehavior<TRequest, TResponse>
{
    public async Task<TResponse> HandleAsync(...)
    {
        if (request is not IRequireAuthorization authRequest)
            return await next();
        
        var strategy = httpContext.Items["AuthorizationStrategy"];
        
        if (strategy == AuthorizationStrategy.SmartScopes)
        {
            var userScopes = httpContext.Items["SmartScopes"];
            var requiredScope = authRequest.GetRequiredSmartScope();
            
            if (!_evaluator.Evaluate(userScopes, requiredScope, context))
                throw new FhirAuthorizationException(...);
        }
        else if (strategy == AuthorizationStrategy.RoleBased)
        {
            // RBAC evaluation
        }
        
        return await next();
    }
}
```

---

## RBAC System Design

### Role Hierarchy

```
Administrator
  ├─ TenantAdministrator (tenant-scoped)
  ├─ Clinician
  │   ├─ Physician
  │   └─ Nurse
  ├─ ReadOnly
  └─ SystemService (backend)
```

### Permission Model

| Role | Create | Read | Update | Delete | Search |
|------|--------|------|--------|--------|--------|
| Administrator | ✅ | ✅ | ✅ | ✅ | ✅ |
| Physician | ✅ | ✅ | ✅ | ❌ | ✅ |
| Nurse | ⚠️ | ✅ | ⚠️ | ❌ | ✅ |
| ReadOnly | ❌ | ✅ | ❌ | ❌ | ✅ |

⚠️ = Limited to specific resource types

---

## Implementation Phases

### Phase 0: Project Setup (3 days)

- [ ] Create `Ignixa.Application.SmartOnFhir` project
- [ ] Add project references
- [ ] Create folder structure
- [ ] Add README.md
- [ ] Update All.sln

### Phase 1: Foundation - RBAC (2 weeks)

**Goal**: Basic authentication and role-based authorization

**Tasks**:
- [ ] Add `TenantAuthorizationConfiguration` to domain
- [ ] Implement `AuthorizationMiddleware` with JWT validation
- [ ] Configure ASP.NET Core authentication (JwtBearer)
- [ ] Implement `RbacEvaluator`
- [ ] Add `IRequireAuthorization` interface
- [ ] Create basic role definitions
- [ ] Update endpoints to require authentication
- [ ] Add unit tests

**Deliverables**:
- ✅ JWT authentication working
- ✅ Basic RBAC enforcement
- ✅ Role-based permissions configurable

### Phase 2: SMART Scopes (3 weeks)

**Goal**: SMART scope parsing and evaluation

**Tasks**:
- [ ] Implement `SmartScope` domain model
- [ ] Create `SmartScopeParser` with EntraID translation
- [ ] Implement `SmartScopeEvaluator`
- [ ] Add `SmartAuthorizationBehavior`
- [ ] Update commands/queries for `IRequireAuthorization`
- [ ] Add scope claim extraction
- [ ] Implement `.well-known/smart-configuration`
- [ ] Add SMART metadata to CapabilityStatement
- [ ] Integration tests

**Deliverables**:
- ✅ SMART scope parsing working
- ✅ Scope-based authorization functional
- ✅ SMART discovery endpoints

### Phase 3: Patient Compartment (2 weeks)

**Goal**: Instance-level authorization

**Tasks**:
- [ ] Implement patient compartment extraction
- [ ] Add instance-level checks to handlers
- [ ] Create `FhirRequestContext`
- [ ] Validate patient/ scope context
- [ ] Add audit logging
- [ ] Compartment tests

**Deliverables**:
- ✅ Patient compartment enforcement
- ✅ Patient ID validation
- ✅ Comprehensive tests

### Phase 4: Advanced Features (2 weeks)

**Goal**: Constraints and optimization

**Tasks**:
- [ ] Search parameter constraint parsing
- [ ] Constraint evaluation
- [ ] User/ context evaluation
- [ ] Wildcard resource support
- [ ] Scope narrowing
- [ ] Audit trails
- [ ] Performance optimization

**Deliverables**:
- ✅ Search constraints working
- ✅ User/ context supported
- ✅ Performance benchmarks met

### Phase 5: Production Hardening (1 week)

**Goal**: Security and documentation

**Tasks**:
- [ ] Security audit
- [ ] Performance testing
- [ ] Integration guide
- [ ] Sample applications
- [ ] Error message improvements
- [ ] Monitoring setup
- [ ] Rate limiting

**Deliverables**:
- ✅ Security audit complete
- ✅ Documentation published
- ✅ Production-ready

**Total Timeline**: 10 weeks

---

## Security Considerations

### Token Validation

**Critical Requirements**:
- ✅ Validate JWT signature (JWKS)
- ✅ Verify issuer matches EntraID tenant
- ✅ Verify audience matches FHIR server
- ✅ Verify expiration (exp)
- ✅ Verify not-before (nbf)
- ✅ Reject unexpected algorithms
- ✅ Cache JWKS keys (1 hour TTL)

### Attack Vectors & Mitigations

| Attack | Mitigation |
|--------|-----------|
| Scope Injection | JWT signature validation |
| Patient Context Mismatch | Strict patient ID matching |
| Privilege Escalation | Strategy isolation |
| Cross-Tenant Access | Token tenant validation |
| Token Replay | Validate exp, nbf, iat |
| Compartment Bypass | Instance-level checks |

### Multi-Tenancy Isolation

```csharp
private void ValidateTenantIsolation(FhirRequestContext ctx)
{
    var tokenTenantId = ctx.Claims.FindFirst("tenant")?.Value;
    var routeTenantId = ctx.TenantId.ToString();

    if (tokenTenantId != null && tokenTenantId != routeTenantId)
    {
        throw new FhirAuthorizationException(
            $"Token for tenant {tokenTenantId} cannot access tenant {routeTenantId}");
    }
}
```

---

## Testing Strategy

### Unit Tests

**SmartScopeParser**:
- EntraID to SMART translation
- Wildcard handling
- Constraint parsing
- Malformed scope handling

**SmartScopeEvaluator**:
- Context matching (patient, user, system)
- Permission checks
- Resource type matching
- Patient compartment validation

**Example**:
```csharp
[Fact]
public void Parse_EntraIdScope_TranslatesToSmartStandard()
{
    var scope = SmartScopeParser.Parse("patient.Observation.rs");
    
    Assert.Equal(SmartContext.Patient, scope.Context);
    Assert.Equal("Observation", scope.ResourceType);
    Assert.Equal(SmartPermissions.Read | SmartPermissions.Search, 
                 scope.Permissions);
}
```

### Integration Tests

**E2E Authorization**:
- Valid scope → 200 OK
- Insufficient scope → 403 Forbidden
- Invalid token → 401 Unauthorized
- Patient mismatch → 403 Forbidden
- Cross-tenant → 403 Forbidden

**Example**:
```csharp
[Fact]
public async Task GetResource_WithValidSmartScope_Returns200()
{
    var token = CreateJwt(scopes: "patient.Patient.read", patientId: "123");
    _client.DefaultRequestHeaders.Authorization = 
        new AuthenticationHeaderValue("Bearer", token);

    var response = await _client.GetAsync("/tenant/1/Patient/123");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
}
```

### Security Tests

**Penetration Testing**:
- [ ] Token tampering
- [ ] Expired token access
- [ ] Missing signature validation
- [ ] Patient compartment bypass
- [ ] Cross-tenant access
- [ ] Privilege escalation
- [ ] Scope injection
- [ ] Rate limit bypass

---

## Configuration

### appsettings.json Example

```json
{
  "Tenants": {
    "Mode": "Isolated",
    "Configurations": [
      {
        "TenantId": 1,
        "DisplayName": "Mayo Clinic",
        "FhirVersion": "4.0",
        "Authorization": {
          "EnableSmartOnFhir": true,
          "EnableRbac": true,
          "EntraIdTenantId": "12345678-1234-1234-1234-123456789abc",
          "EntraIdClientId": "87654321-4321-4321-4321-cba987654321",
          "TokenIssuer": "https://login.microsoftonline.com/.../v2.0",
          "TokenAudience": "api://ignixa-fhir",
          "RolePermissions": {
            "Administrator": {
              "AllowedInteractions": ["read", "create", "update", "delete", "search"]
            },
            "Physician": {
              "AllowedInteractions": ["read", "create", "update", "search"],
              "DeniedResourceTypes": ["AuditEvent"]
            }
          }
        }
      }
    ]
  }
}
```

---

## SMART Discovery

### .well-known/smart-configuration

```json
{
  "issuer": "https://login.microsoftonline.com/{tenant}/v2.0",
  "jwks_uri": "https://login.microsoftonline.com/{tenant}/.well-known/jwks",
  "authorization_endpoint": "https://login.microsoftonline.com/{tenant}/oauth2/v2.0/authorize",
  "token_endpoint": "https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token",
  "scopes_supported": [
    "openid", "fhirUser", "offline_access",
    "patient.Patient.read", "patient.all.read",
    "user.Patient.cruds", "user.all.cruds",
    "system.Patient.read"
  ],
  "capabilities": [
    "launch-ehr",
    "launch-standalone",
    "client-public",
    "context-ehr-patient",
    "permission-patient",
    "permission-user"
  ]
}
```

---

## Alternatives Considered

### Alternative 1: External SMART Proxy

**Approach**: Use Microsoft's proxy or build custom proxy

**Pros**:
- No FHIR server changes

**Cons**:
- ❌ External dependency
- ❌ Microsoft proxy retiring Sept 2026
- ❌ Additional latency
- ❌ Complex deployment

**Decision**: ✗ Rejected - Native implementation preferred

### Alternative 2: Custom Scope Format

**Approach**: Design custom format for EntraID

**Pros**:
- Optimized for EntraID

**Cons**:
- ❌ Not SMART-compliant
- ❌ Existing apps won't work
- ❌ Loses ecosystem compatibility

**Decision**: ✗ Rejected - SMART compliance critical

---

## Risks & Mitigation

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| EntraID translation edge cases | High | Medium | Comprehensive tests, SMART test suite |
| Patient compartment errors | High | Medium | Extensive testing, security review |
| Performance degradation | Medium | Low | Token caching, benchmarks |
| SMART v3 breaking changes | Medium | Low | Extensible design, version abstraction |
| Multi-tenant confusion | High | Low | Strict validation, audit logging |

---

## Success Criteria

### Functional

- ✅ SMART scopes supported internally
- ✅ EntraID scopes accepted in tokens
- ✅ RBAC and SMART both working
- ✅ Patient compartment accurate
- ✅ `.well-known/smart-configuration` functional
- ✅ CapabilityStatement includes SMART metadata

### Non-Functional

- ✅ Authorization latency < 50ms (p95)
- ✅ JWT validation cached
- ✅ Zero cross-tenant leakage
- ✅ All auth events audited
- ✅ Security audit passed
- ✅ Test coverage > 90%

---

## References

- [SMART App Launch v2.2.0](https://hl7.org/fhir/smart-app-launch/)
- [SMART Scopes](https://hl7.org/fhir/smart-app-launch/scopes-and-launch-context.html)
- [Microsoft SMART on FHIR](https://learn.microsoft.com/en-us/azure/healthcare-apis/fhir/smart-on-fhir)
- [EntraID OAuth Scopes](https://learn.microsoft.com/en-us/entra/identity-platform/scopes-oidc)
- ADR-2523: Multi-Tenancy Architecture
- ADR-2506: Capability Enforcement

---

## Next Steps

1. **Stakeholder Review** (1 week)
   - Review with team
   - Gather feedback
   - Adjust design

2. **ADR Creation** (3 days)
   - Formalize decisions
   - Document alternatives
   - Create ADR-XXXX

3. **Phase 0 Implementation** (3 days)
   - Create project structure
   - Basic framework
   - Test scaffolding

4. **Proof of Concept** (1 week)
   - Build minimal SMART app
   - Test EntraID integration
   - Validate scope translation

5. **Phase 1 Kickoff** (2 weeks)
   - Begin RBAC implementation
   - JWT authentication
   - Basic enforcement

---

**End of Investigation**
