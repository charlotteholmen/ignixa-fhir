# Ignixa Entra Sidecar

A gRPC sidecar container for integrating Microsoft Entra ID (Azure AD) authorization with the Ignixa FHIR Server.

## Overview

This sidecar implements the gRPC authorization and audit logging services defined in `src/Application/Ignixa.Sidecar/Protos/`. It validates user claims from Entra ID tokens and makes authorization decisions based on roles and scopes.

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Container Group / Pod                    │
│  ┌────────────────────────────────────────────────────┐    │
│  │              Ignixa FHIR Server                     │    │
│  │                 (Port 8080)                         │    │
│  │                                                     │    │
│  │  Sidecar:Endpoint = http://localhost:5050          │    │
│  └──────────────────────┬─────────────────────────────┘    │
│                         │ gRPC                              │
│                         ▼                                   │
│  ┌────────────────────────────────────────────────────┐    │
│  │           Ignixa Entra Sidecar                      │    │
│  │                 (Port 5050)                         │    │
│  │                                                     │    │
│  │  - AuthorizationService                             │    │
│  │  - AuditLoggerService                              │    │
│  │  - LoggingService                                  │    │
│  └────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────┘
```

## Configuration

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `EntraAuthorization__TenantId` | Entra ID tenant ID | (required) |
| `EntraAuthorization__ClientId` | Entra ID client (application) ID | (required) |
| `EntraAuthorization__RequiredRoles__0` | Required role(s) for access | (none) |
| `EntraAuthorization__RequiredScopes__0` | Required scope(s) for access | (none) |
| `EntraAuthorization__AllowAuthenticatedByDefault` | Allow all authenticated users | `true` |

### appsettings.json

```json
{
  "EntraAuthorization": {
    "TenantId": "your-tenant-id",
    "ClientId": "your-client-id",
    "RequiredRoles": ["FHIR.User", "FHIR.Admin"],
    "RequiredScopes": ["user_impersonation"],
    "ActionRoleMapping": {
      "read": ["FHIR.Reader", "FHIR.User", "FHIR.Admin"],
      "write": ["FHIR.User", "FHIR.Admin"],
      "delete": ["FHIR.Admin"]
    },
    "ResourceTypeRoleMapping": {
      "Patient": ["FHIR.User", "FHIR.Admin"],
      "Observation": ["FHIR.Reader", "FHIR.User", "FHIR.Admin"]
    },
    "AllowAuthenticatedByDefault": false
  }
}
```

## Building

### Using Docker

```bash
# Build from repository root
docker build -f tools/Ignixa.Sidecar.Entra/Dockerfile -t ignixa-sidecar-entra:latest .
```

### Using .NET CLI

```bash
cd tools/Ignixa.Sidecar.Entra
dotnet build
dotnet run
```

## Deployment

### Azure Web App with Sidecar

1. Deploy the main FHIR server with `Sidecar:ProviderMode=Sidecar`
2. Deploy this sidecar as a companion container
3. Configure the sidecar endpoint: `Sidecar:Endpoint=http://localhost:5050`

### Docker Compose Example

```yaml
version: '3.8'
services:
  fhir-server:
    image: ghcr.io/brendankowitz/ignixa-fhir:latest
    ports:
      - "8080:8080"
    environment:
      - Sidecar__ProviderMode=Sidecar
      - Sidecar__Endpoint=http://entra-sidecar:5050
    depends_on:
      - entra-sidecar

  entra-sidecar:
    build:
      context: .
      dockerfile: tools/Ignixa.Sidecar.Entra/Dockerfile
    environment:
      - EntraAuthorization__TenantId=your-tenant-id
      - EntraAuthorization__ClientId=your-client-id
      - EntraAuthorization__AllowAuthenticatedByDefault=true
```

### Kubernetes Example

```yaml
apiVersion: v1
kind: Pod
metadata:
  name: ignixa-fhir
spec:
  containers:
  - name: fhir-server
    image: ghcr.io/brendankowitz/ignixa-fhir:latest
    ports:
    - containerPort: 8080
    env:
    - name: Sidecar__ProviderMode
      value: "Sidecar"
    - name: Sidecar__Endpoint
      value: "http://localhost:5050"
  
  - name: entra-sidecar
    image: ghcr.io/brendankowitz/ignixa-sidecar-entra:latest
    ports:
    - containerPort: 5050
    env:
    - name: EntraAuthorization__TenantId
      valueFrom:
        secretKeyRef:
          name: entra-config
          key: tenant-id
    - name: EntraAuthorization__ClientId
      valueFrom:
        secretKeyRef:
          name: entra-config
          key: client-id
```

## Authorization Logic

The sidecar evaluates authorization in the following order:

1. **Required Roles**: If `RequiredRoles` is configured, user must have at least one of the specified roles
2. **Required Scopes**: If `RequiredScopes` is configured, user must have at least one of the specified scopes
3. **Action Role Mapping**: If the action (read/write/delete) has role requirements, user must have a matching role
4. **Resource Type Role Mapping**: If the resource type has role requirements, user must have a matching role
5. **Default Behavior**: If `AllowAuthenticatedByDefault` is true, any authenticated user is authorized

## Claims Extraction

The sidecar extracts authorization information from these Entra ID claims:

| Claim | Description |
|-------|-------------|
| `roles` | Application roles assigned to the user |
| `role` | Single role claim (alternative) |
| `wids` | Directory role IDs |
| `scp` | Delegated permission scopes |

## Health Check

The sidecar exposes a health check endpoint:

```bash
curl http://localhost:8080/health
# Response: {"Status":"Healthy","Timestamp":"2024-01-15T12:00:00Z"}
```

## Extending

To extend this sidecar for other identity providers or additional authorization logic:

1. Create a new service inheriting from `AuthorizationService.AuthorizationServiceBase`
2. Implement the `Authorize` method with your custom logic
3. Register your service in `Program.cs`

## Related Documentation

- [Azure Web Apps with Entra ID Guide](../../docs/azure-webapps-entra.md)
- [Sidecar Provider Pattern](../../src/Application/Ignixa.Sidecar/)
- [ARM Template for Entra Deployment](../../deploy/azure/azuredeploy-entrasidecar.json)
