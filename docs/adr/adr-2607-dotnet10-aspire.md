# ADR 2607: .NET 10 Support and ASP.NET Aspire Adoption

## Status

Proposed

## Context

.NET 10 (LTS) ships November 2025. The project currently targets `net9.0` exclusively — a single TFM across all layers with no multi-targeting infrastructure. The Core SDK packages are consumed externally via NuGet and must support users on both .NET 9 and .NET 10. The Application layer (server runtime) has no external consumption constraint and can move to .NET 10 directly.

Separately, ASP.NET Aspire provides orchestration, service discovery, and observability as a first-class .NET hosting model. Adopting Aspire replaces manual Docker Compose / Bicep-managed service wiring for local development and simplifies cloud deployment via the Aspire deployment tooling (azd, Azure Container Apps).

**Current state:**
- All projects: `net9.0` single TFM
- Docker: `mcr.microsoft.com/dotnet/sdk:9.0-azurelinux3.0` (multi-platform via `$BUILDPLATFORM`)
- Deployment: Azure Bicep templates (App Service, SQL Server, Blob Storage)
- No Aspire usage anywhere in the solution
- CI: single SDK version in build matrix (`9.0.x`)

## Decision

### 1. Multi-Targeting Strategy (Core Layer)

Core SDK packages (`src/Core/**`) adopt `net9.0;net10.0` multi-targeting to support consumers on either runtime:

```xml
<!-- src/Core/Directory.Build.props (new file, layered over root) -->
<Project>
  <PropertyGroup>
    <TargetFrameworks>net9.0;net10.0</TargetFrameworks>
  </PropertyGroup>
</Project>
```

- Core packages ship with both TFMs in the nupkg — NuGet resolves the best match for the consumer
- `#if NET10_0_OR_GREATER` conditionals used sparingly for .NET 10–only APIs (e.g., new BCL methods, performance improvements)
- `LangVersion` remains `latest` (C# 14 features available when targeting net10.0)

### 2. Application Layer: .NET 10 Only

Application, DataLayer, and Api projects (`src/Application/**`, `src/DataLayer/**`) upgrade directly to `net10.0`:

```xml
<!-- These projects change from net9.0 → net10.0 -->
<TargetFramework>net10.0</TargetFramework>
```

Rationale: these are not consumed as NuGet packages by external users. The server runtime deploys as a container image — consumers don't link against it. Single TFM keeps the build matrix simple and avoids carrying two runtime configurations in production.

### 3. ASP.NET Aspire Adoption

Introduce an Aspire AppHost project for orchestration:

- **New project:** `src/Application/Ignixa.AppHost/Ignixa.AppHost.csproj` (Aspire 9.x initially, upgrade to Aspire 10 when stable)
- **New project:** `src/Application/Ignixa.ServiceDefaults/Ignixa.ServiceDefaults.csproj` (shared telemetry, health checks, resilience defaults)
- `Ignixa.Web` references `Ignixa.ServiceDefaults` for OpenTelemetry, health endpoints, and service discovery
- AppHost orchestrates: SQL Server (container or connection string), Blob Storage (Azurite or Azure), the FHIR server itself
- **F5 experience preserved:** `dotnet run --project src/Application/Ignixa.AppHost` starts everything — no external Docker Compose or manual infrastructure

**Aspire does NOT replace:**
- Production Bicep/ARM deployments (Aspire manifest complements these)
- The standalone Dockerfile (still needed for non-Aspire container deployments)

### 4. Docker & Container Updates

| Component | Before | After |
|-----------|--------|-------|
| Build SDK | `sdk:9.0-azurelinux3.0` | `sdk:10.0-azurelinux3.0` |
| Runtime | `aspnet:9.0-azurelinux3.0` | `aspnet:10.0-azurelinux3.0` |
| Platform | `$BUILDPLATFORM` (x64 + ARM64) | Unchanged |
| Base OS | Azure Linux 3.0 | Unchanged (or Azure Linux 4.0 if available) |

The Dockerfile remains a single-stage publish targeting `net10.0`. No multi-targeting in the container image.

### 5. CI/CD Pipeline Changes

- **Build matrix:** `dotnet-version: [ '9.0.x', '10.0.x' ]` — the 9.0 SDK builds Core multi-target tests, the 10.0 SDK builds and publishes the full solution
- **`global.json`:** Add with `rollForward: latestFeature` pinned to `10.0.x` to prevent accidental SDK mismatches
- **NuGet pack:** Core packages produce multi-TFM nupkgs; Application/DataLayer packages remain single-TFM

### 6. Azure Deployment (Bicep) Update Path

Bicep templates in `deploy/azure/` require minimal changes:

- `modules/app-service.bicep`: Update `linuxFxVersion` from `DOTNETCORE|9.0` to `DOTNETCORE|10.0`
- Container image tags: update from `9.0` to `10.0` runtime references
- **Optional (future):** Add Aspire manifest-based deployment via `azd` as an alternative to raw Bicep. The Aspire manifest (`aspire-manifest.json`) can generate equivalent infrastructure — evaluate once Aspire deployment tooling matures for production use

### 7. .NET 9 Deprecation Path

- .NET 9 reaches end-of-support May 2026 (STS)
- Core packages retain `net9.0` TFM for one release cycle after .NET 10 GA to allow consumers time to migrate
- After the grace period: drop `net9.0` from Core, remove multi-targeting, simplify to `net10.0` only
- CI drops the `9.0.x` SDK from the build matrix at the same time

## Consequences

**Positive:**
- Core SDK consumers can adopt .NET 10 incrementally without forced upgrades
- Application layer benefits from .NET 10 performance improvements (AOT, GC, networking) immediately
- Aspire provides a unified F5 experience — SQL, storage, and the server start together
- Aspire telemetry (OpenTelemetry, structured logging, distributed tracing) replaces ad-hoc configuration
- ARM64 container support maintained throughout

**Negative:**
- Multi-targeting in Core increases build/test matrix complexity (doubled test runs for Core projects)
- Aspire AppHost adds 2 new projects to the solution
- Aspire has its own release cadence — version management overhead
- Teams unfamiliar with Aspire face a learning curve for orchestration model
- `Directory.Packages.props` needs conditional version ranges for ASP.NET Core packages (9.0.x vs 10.0.x TFM)

**Trade-offs:**

| Concern | Mitigation |
|---------|------------|
| Multi-TFM build time | CI parallelizes TFM builds; Core projects are small |
| Aspire lock-in | ServiceDefaults is opt-in; server still runs standalone without AppHost |
| .NET 9 EoL consumers | Grace period + clear deprecation notice in package release notes |
| Bicep vs Aspire deployment | Both coexist — Bicep for production, Aspire for dev/staging |

## Implementation Checklist

**Phase 1: .NET 10 Upgrade (Application Layer)**
- [ ] Add `global.json` with .NET 10 SDK pin
- [ ] Update `Directory.Build.props`: `RuntimePackageVersion` → `10.0`, `AspNetPackageVersion` → `10.0.0`
- [ ] Update `Directory.Packages.props`: bump `Microsoft.Extensions.*`, `Microsoft.EntityFrameworkCore.*`, `Microsoft.AspNetCore.*` to 10.0.x
- [ ] Change Application/DataLayer/Api csproj files from `net9.0` → `net10.0`
- [ ] Update Dockerfile SDK/runtime images to `10.0-azurelinux3.0`
- [ ] Update CI matrix to include `10.0.x`
- [ ] Verify build and tests pass on .NET 10

**Phase 2: Core Multi-Targeting**
- [ ] Add `src/Core/Directory.Build.props` with `<TargetFrameworks>net9.0;net10.0</TargetFrameworks>`
- [ ] Resolve any conditional compilation needs (`#if NET10_0_OR_GREATER`)
- [ ] Verify NuGet pack produces multi-TFM packages
- [ ] Test Core packages from a `net9.0` consumer project

**Phase 3: Aspire Integration**
- [ ] Add `Ignixa.AppHost` project with Aspire SDK references
- [ ] Add `Ignixa.ServiceDefaults` project (OpenTelemetry, health checks, resilience)
- [ ] Wire `Ignixa.Web` to use ServiceDefaults
- [ ] Configure AppHost to orchestrate SQL Server + Azurite + FHIR server
- [ ] Verify F5 experience: `dotnet run --project Ignixa.AppHost` starts full stack

**Phase 4: Deployment Updates**
- [ ] Update `deploy/azure/modules/app-service.bicep` runtime version
- [ ] Update container image references in deployment scripts
- [ ] Document Aspire manifest as alternative deployment path
- [ ] Update README with new development setup instructions

**Phase 5: .NET 9 Deprecation (future)**
- [ ] Announce deprecation timeline in release notes
- [ ] Remove `net9.0` from Core TFMs
- [ ] Remove `9.0.x` from CI matrix
- [ ] Simplify `Directory.Packages.props` (remove conditional version groups)

## References

- **.NET 10 Release**: https://dotnet.microsoft.com/en-us/platform/dotnet-10
- **ASP.NET Aspire**: https://learn.microsoft.com/en-us/dotnet/aspire/
- **Multi-targeting**: https://learn.microsoft.com/en-us/nuget/create-packages/multiple-target-frameworks-project-file
- **Azure Linux containers**: https://learn.microsoft.com/en-us/azure/azure-linux/
- **.NET Support Policy**: https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core
