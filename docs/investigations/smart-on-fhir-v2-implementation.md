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
7. Multi-IDP support (OpenIddict, EntraID, EntraID External ID)

---

## Table of Contents

1. [Background](#background)
2. [Problem Statement](#problem-statement)
3. [Proposed Solution](#proposed-solution)
4. [Multi-IDP Strategy](#multi-idp-strategy)
5. [Architecture](#architecture)
6. [Implementation Phases](#implementation-phases)
7. [Security Considerations](#security-considerations)
8. [Testing Strategy](#testing-strategy)
9. [Configuration](#configuration)
10. [Alternatives Considered](#alternatives-considered)
11. [Risks & Mitigation](#risks--mitigation)
12. [Success Criteria](#success-criteria)
13. [References](#references)

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

## Multi-IDP Strategy

### Three-Tier Identity Provider Architecture

Supporting multiple identity providers enables optimal developer experience, enterprise integration, and SMART ecosystem compatibility.

```
┌─────────────────────────────────────────────────────────────┐
│                   Development Environment                    │
├─────────────────────────────────────────────────────────────┤
│  OpenIddict (self-hosted)                                   │
│  ✅ Full OAuth 2.0 / OIDC server                            │
│  ✅ Custom scopes (SMART + RBAC)                            │
│  ✅ Custom claims (patient, tenant, roles)                  │
│  ✅ Fast iteration, no external dependencies                │
│  ✅ Consistent with production token structure              │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│                 Production - Internal Users                  │
├─────────────────────────────────────────────────────────────┤
│  EntraID (Azure AD)                                         │
│  ✅ Enterprise SSO                                          │
│  ✅ Role-based access (Administrator, Clinician, etc.)      │
│  ✅ Security groups → roles claim                           │
│  ✅ MFA, Conditional Access policies                        │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│             Production - External Apps (SMART)               │
├─────────────────────────────────────────────────────────────┤
│  EntraID External ID (formerly B2C)                         │
│  ✅ Federated IDPs (Auth0, Okta, Google, etc.)              │
│  ✅ SMART scopes (patient.*, user.*, system.*)              │
│  ✅ Patient/encounter context in tokens                     │
│  ✅ Unified token format regardless of upstream IDP         │
│  ✅ Custom policies for scope translation                   │
└─────────────────────────────────────────────────────────────┘
```

### Why This Architecture

#### 1. OpenIddict for Development

**Benefits**:
- ✅ **Zero Cloud Dependency**: Runs completely locally
- ✅ **Fast Iteration**: No need to configure EntraID for every developer
- ✅ **Full Control**: Can issue any scope, claim, or token structure
- ✅ **Consistent**: Mirrors production token format exactly
- ✅ **Cost-Free**: No Azure subscription needed for development

**Setup Example**:

```csharp
// In Program.cs (Development only)
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddOpenIddict()
        .AddCore(options =>
        {
            options.UseEntityFrameworkCore()
                .UseDbContext<AuthDbContext>();
        })
        .AddServer(options =>
        {
            options.SetAuthorizationEndpointUris("/connect/authorize")
                   .SetTokenEndpointUris("/connect/token")
                   .SetUserinfoEndpointUris("/connect/userinfo");

            options.AllowAuthorizationCodeFlow()
                   .AllowClientCredentialsFlow()
                   .AllowRefreshTokenFlow();

            // SMART scopes (EntraID-compatible format)
            options.RegisterScopes(
                "openid", "profile", "fhirUser", "offline_access",
                "patient.Patient.read", "patient.Observation.rs",
                "user.Patient.cruds", "user.all.read",
                "system.Patient.read");

            options.AddDevelopmentEncryptionCertificate()
                   .AddDevelopmentSigningCertificate();

            options.UseAspNetCore()
                   .EnableAuthorizationEndpointPassthrough()
                   .EnableTokenEndpointPassthrough();
        })
        .AddValidation(options =>
        {
            options.UseLocalServer();
            options.UseAspNetCore();
        });
}
```

**Token Generation (Dev)**:

```csharp
// Can issue tokens with any combination of scopes and claims
var identity = new ClaimsIdentity(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

// SMART scopes
identity.AddClaim("scope", "patient.Patient.read");
identity.AddClaim("scope", "patient.Observation.rs");

// SMART context
identity.AddClaim("patient", "patient-123");
identity.AddClaim("fhirUser", "Practitioner/practitioner-456");
identity.AddClaim("tenant", "1");

// OR RBAC roles
// identity.AddClaim("role", "Physician");
// identity.AddClaim("role", "Clinician");
```

#### 2. EntraID for Internal RBAC

**Use Case**: Hospital staff, administrators, system operators

**Configuration**:

```json
{
  "TenantId": 1,
  "Authorization": {
    "Provider": "EntraID",
    "EnableRbac": true,
    "EnableSmartOnFhir": false,
    "TokenIssuer": "https://login.microsoftonline.com/{tenant-id}/v2.0",
    "TokenAudience": "api://ignixa-fhir",
    "EntraIdTenantId": "{your-entra-tenant-id}",
    "EntraIdClientId": "{your-app-registration-id}"
  }
}
```

**EntraID App Registration**:
- **App Roles**: Define custom roles (Administrator, Physician, Nurse)
- **Group Claims**: Map Azure AD security groups → roles
- **Token Claims**: Include `roles` claim in token

#### 3. EntraID External ID for SMART Apps

**Use Case**: Third-party SMART apps, patient-facing apps, research applications

**Why EntraID External ID (B2C)?**

| Feature | Benefit |
|---------|---------|
| **IDP Federation** | Support Auth0, Okta, Google, Apple, etc. |
| **Custom Policies** | Transform scopes (`.` → `/` if needed) |
| **SMART Compliance** | Can issue SMART-compliant scopes |
| **Patient Context** | Include patient, encounter, fhirUser claims |
| **Unified Tokens** | Same token format regardless of upstream IDP |

**Configuration**:

```json
{
  "TenantId": 2,
  "Authorization": {
    "Provider": "EntraIDExternalID",
    "EnableRbac": false,
    "EnableSmartOnFhir": true,
    "TokenIssuer": "https://{tenant-name}.b2clogin.com/{tenant-id}/v2.0",
    "TokenAudience": "api://ignixa-fhir",
    "EntraIdB2CTenantId": "{b2c-tenant-id}",
    "EntraIdB2CPolicy": "B2C_1A_SMART_SIGNIN"
  }
}
```

### Unified Token Validation

Regardless of IDP (OpenIddict, EntraID, EntraID External ID), all tokens validated the same way:

```csharp
public class UnifiedTokenValidator
{
    public async Task<ClaimsPrincipal?> ValidateTokenAsync(
        string token,
        TenantConfiguration tenantConfig,
        CancellationToken cancellationToken)
    {
        var issuer = tenantConfig.Authorization.TokenIssuer;
        var audience = tenantConfig.Authorization.TokenAudience;

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,

            ValidateAudience = true,
            ValidAudience = audience,

            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(5),

            ValidateIssuerSigningKey = true,
            // Fetch signing keys from JWKS endpoint
            IssuerSigningKeyResolver = (token, securityToken, kid, validationParams) =>
            {
                return FetchSigningKeys(issuer, cancellationToken);
            },

            RequireSignedTokens = true,
            RequireExpirationTime = true
        };

        var handler = new JwtSecurityTokenHandler();
        var principal = handler.ValidateToken(token, validationParameters, out _);

        return principal;
    }
}
```

**Key Point**: Same validation code works for all three IDPs. Only difference is issuer/audience configuration.

### Development Workflow with OpenIddict

**Step 1: Seed Test Data**

```csharp
// In AuthDbContext or seed script
public static async Task SeedTestUsersAsync(IServiceProvider services)
{
    var manager = services.GetRequiredService<IOpenIddictApplicationManager>();

    // SMART app (patient-facing)
    await manager.CreateAsync(new OpenIddictApplicationDescriptor
    {
        ClientId = "smart-app-123",
        DisplayName = "Patient Portal App",
        Permissions =
        {
            Permissions.Endpoints.Authorization,
            Permissions.Endpoints.Token,
            Permissions.GrantTypes.AuthorizationCode,
            Permissions.ResponseTypes.Code,
            Permissions.Scopes.Profile,
            "patient.Patient.read",
            "patient.Observation.rs",
            "patient.MedicationRequest.rs"
        },
        RedirectUris = { new Uri("https://localhost:5001/callback") }
    });

    // Internal user (RBAC)
    await manager.CreateAsync(new OpenIddictApplicationDescriptor
    {
        ClientId = "internal-admin",
        DisplayName = "Hospital Admin Portal",
        Permissions =
        {
            Permissions.Endpoints.Token,
            Permissions.GrantTypes.ClientCredentials,
            "role:Administrator"
        }
    });
}
```

**Step 2: Issue Test Tokens**

```bash
# SMART token (patient context)
curl -X POST http://localhost:5000/connect/token \
  -d "grant_type=authorization_code" \
  -d "client_id=smart-app-123" \
  -d "code=..." \
  -d "scope=patient.Patient.read patient.Observation.rs"

# RBAC token (internal user)
curl -X POST http://localhost:5000/connect/token \
  -d "grant_type=client_credentials" \
  -d "client_id=internal-admin" \
  -d "client_secret=..." \
  -d "scope=role:Administrator"
```

**Step 3: Test FHIR Requests**

```bash
# Using SMART token
curl http://localhost:5000/tenant/1/Patient/123 \
  -H "Authorization: Bearer {smart-token}"

# Using RBAC token
curl http://localhost:5000/tenant/1/Patient \
  -H "Authorization: Bearer {rbac-token}"
```

### Multi-IDP Benefits

#### ✅ **Developer Experience**
- No Azure subscription needed for local dev
- Instant token generation (no external API calls)
- Full control over test scenarios
- Consistent across all developers

#### ✅ **Production Flexibility**
- Support any IDP via EntraID External ID federation
- Internal users → EntraID (enterprise SSO)
- External apps → EntraID External ID (SMART compliance)
- Easy to add new IDPs without code changes

#### ✅ **Cost Optimization**
- OpenIddict: Free (self-hosted)
- EntraID: Included with Microsoft 365 (for internal users)
- EntraID External ID: Pay-per-use (MAU model, free tier for dev)

#### ✅ **Security**
- Unified token validation (same security posture)
- EntraID External ID supports MFA, conditional access
- Audit logging across all IDPs
- Token revocation support

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
    /// <summary>Identity provider: OpenIddict, EntraID, EntraIDExternalID</summary>
    public string Provider { get; init; } = "OpenIddict";

    public bool EnableSmartOnFhir { get; init; } = false;
    public bool EnableRbac { get; init; } = true;

    public string? EntraIdTenantId { get; init; }
    public string? EntraIdClientId { get; init; }
    public string? EntraIdB2CTenantId { get; init; }
    public string? EntraIdB2CPolicy { get; init; }

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

**Goal**: Set up project structure and OpenIddict for development

**Tasks**:
- [ ] Create `Ignixa.Application.SmartOnFhir` project
- [ ] Add project references (Domain, Application)
- [ ] Create folder structure
- [ ] Add README.md with SMART overview
- [ ] Update All.sln

**OpenIddict Setup (Development)**:
- [ ] Add `OpenIddict.AspNetCore` NuGet package
- [ ] Add `OpenIddict.EntityFrameworkCore` NuGet package
- [ ] Create `AuthDbContext` for OpenIddict storage
- [ ] Add OpenIddict registration in Program.cs (dev only)
- [ ] Create seed script for test users/apps
- [ ] Document local token generation workflow
- [ ] Add OpenIddict endpoints for authorization/token

**Deliverables**:
- ✅ Project structure created
- ✅ OpenIddict running locally
- ✅ Can issue test tokens (SMART + RBAC)

### Phase 1: Foundation - RBAC (2 weeks)

**Goal**: Basic authentication, role-based authorization, and multi-IDP support

**Tasks**:
- [ ] Add `TenantAuthorizationConfiguration` to domain
- [ ] Add `Provider` enum (OpenIddict, EntraID, EntraIDExternalID)
- [ ] Implement `AuthorizationMiddleware` with JWT validation
- [ ] Configure ASP.NET Core authentication (JwtBearer)
- [ ] Implement `UnifiedTokenValidator`
- [ ] Create `ITokenValidationStrategy` interface
- [ ] Implement `OpenIddictTokenValidator`
- [ ] Implement `EntraIdTokenValidator`
- [ ] Implement `EntraIdExternalIdTokenValidator`
- [ ] Factory pattern for validator selection
- [ ] Implement `RbacEvaluator`
- [ ] Add `IRequireAuthorization` interface
- [ ] Create basic role definitions
- [ ] Update endpoints to require authentication
- [ ] Add unit tests for each validator

**Deliverables**:
- ✅ JWT authentication working with all three IDPs
- ✅ Basic RBAC enforcement
- ✅ Role-based permissions configurable
- ✅ Can switch between IDPs via configuration

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

### Development Configuration (OpenIddict)

```json
{
  "Environment": "Development",
  "Tenants": {
    "Mode": "Isolated",
    "Configurations": [
      {
        "TenantId": 1,
        "DisplayName": "Development Tenant",
        "FhirVersion": "4.0",
        "Authorization": {
          "Provider": "OpenIddict",
          "EnableSmartOnFhir": true,
          "EnableRbac": true,
          "TokenIssuer": "https://localhost:5000",
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

### Production Configuration (Multi-IDP)

```json
{
  "Environment": "Production",
  "Tenants": {
    "Mode": "Isolated",
    "Configurations": [
      {
        "TenantId": 1,
        "DisplayName": "Internal Users (RBAC)",
        "FhirVersion": "4.0",
        "Authorization": {
          "Provider": "EntraID",
          "EnableSmartOnFhir": false,
          "EnableRbac": true,
          "TokenIssuer": "https://login.microsoftonline.com/{tenant-id}/v2.0",
          "TokenAudience": "api://ignixa-fhir",
          "EntraIdTenantId": "12345678-1234-1234-1234-123456789abc",
          "EntraIdClientId": "87654321-4321-4321-4321-cba987654321",
          "RolePermissions": {
            "Administrator": {
              "AllowedInteractions": ["read", "create", "update", "delete", "search"]
            },
            "Physician": {
              "AllowedInteractions": ["read", "create", "update", "search"],
              "DeniedResourceTypes": ["AuditEvent", "Provenance"]
            },
            "Nurse": {
              "AllowedInteractions": ["read", "create", "update", "search"],
              "AllowedResourceTypes": ["Patient", "Observation", "Medication", "Condition"]
            },
            "ReadOnly": {
              "AllowedInteractions": ["read", "search"]
            }
          }
        }
      },
      {
        "TenantId": 2,
        "DisplayName": "External Apps (SMART)",
        "FhirVersion": "4.0",
        "Authorization": {
          "Provider": "EntraIDExternalID",
          "EnableSmartOnFhir": true,
          "EnableRbac": false,
          "TokenIssuer": "https://{tenant-name}.b2clogin.com/{tenant-id}/v2.0",
          "TokenAudience": "api://ignixa-fhir",
          "EntraIdB2CTenantId": "abcdef12-3456-7890-abcd-ef1234567890",
          "EntraIdB2CPolicy": "B2C_1A_SMART_SIGNIN"
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

### SMART on FHIR Specification
- [SMART App Launch v2.2.0](https://hl7.org/fhir/smart-app-launch/)
- [SMART Scopes and Launch Context](https://hl7.org/fhir/smart-app-launch/scopes-and-launch-context.html)
- [SMART Backend Services](https://hl7.org/fhir/smart-app-launch/backend-services.html)

### Microsoft Documentation
- [Microsoft SMART on FHIR](https://learn.microsoft.com/en-us/azure/healthcare-apis/fhir/smart-on-fhir)
- [EntraID OAuth Scopes](https://learn.microsoft.com/en-us/entra/identity-platform/scopes-oidc)
- [EntraID External ID (B2C)](https://learn.microsoft.com/en-us/entra/external-id/)

### OpenIddict
- [OpenIddict Documentation](https://documentation.openiddict.com/)
- [OpenIddict Samples](https://github.com/openiddict/openiddict-samples)
- [OpenIddict with ASP.NET Core](https://documentation.openiddict.com/guides/getting-started/creating-your-own-server-instance.html)

### Internal Documentation
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
