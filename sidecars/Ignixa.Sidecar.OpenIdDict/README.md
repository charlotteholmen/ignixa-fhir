# Ignixa OpenIdDict Sidecar

A local development sidecar with a built-in OAuth2/OpenID Connect token server for testing the sidecar functionality of the Ignixa FHIR Server.

## Overview

This sidecar provides:
1. **OAuth2 Token Server** - Issues JWT tokens for testing authentication/authorization
2. **gRPC Authorization Service** - Validates tokens and makes authorization decisions
3. **Audit and Logging Services** - Captures audit events and logs to console

## Quick Start with Docker Compose

The easiest way to test the sidecar locally is using Docker Compose:

```bash
# From the repository root
docker compose -f docker-compose.sidecar.yml up
```

This will start:
- The Ignixa FHIR Server on port 8080
- The OpenIdDict Sidecar on port 5050 (gRPC) and 8081 (HTTP for tokens)

## Getting a Token

Request a token using client credentials:

```bash
curl -X POST http://localhost:8081/connect/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=client_credentials" \
  -d "client_id=fhir-test-client" \
  -d "client_secret=fhir-test-secret" \
  -d "scope=fhir.read fhir.write"
```

## Pre-configured Clients

| Client ID | Client Secret | Description |
|-----------|---------------|-------------|
| `fhir-test-client` | `fhir-test-secret` | General testing with read/write/delete scopes |
| `fhir-admin-client` | `fhir-admin-secret` | Admin access with all scopes |

## Available Scopes

| Scope | Description |
|-------|-------------|
| `fhir.read` | Read FHIR resources |
| `fhir.write` | Write FHIR resources |
| `fhir.delete` | Delete FHIR resources |
| `fhir.*` | Full FHIR access |

## Configuration

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `OpenIdDictAuthorization__AllowAuthenticatedByDefault` | Allow all authenticated users | `true` |
| `OpenIdDictAuthorization__RequiredScopes__0` | Required scope(s) | (none) |

### appsettings.json

```json
{
  "OpenIdDictAuthorization": {
    "RequiredScopes": [],
    "ActionScopeMapping": {
      "read": ["fhir.read", "fhir.*"],
      "write": ["fhir.write", "fhir.*"],
      "delete": ["fhir.delete", "fhir.*"]
    },
    "AllowAuthenticatedByDefault": true
  }
}
```

## Building

### Using Docker

```bash
# Build from repository root
docker build -f sidecars/Ignixa.Sidecar.OpenIdDict/Dockerfile -t ignixa-sidecar-openiddict:latest .
```

### Using .NET CLI

```bash
cd sidecars/Ignixa.Sidecar.OpenIdDict
dotnet build
dotnet run
```

## Endpoints

| Endpoint | Port | Protocol | Description |
|----------|------|----------|-------------|
| `/connect/token` | 8080 | HTTP/1.1 | OAuth2 token endpoint |
| `/health` | 8080 | HTTP/1.1 | Health check |
| gRPC services | 5050 | HTTP/2 | Authorization, Audit, Logging |

## Testing the Full Flow

1. Start the services:
   ```bash
   docker compose -f docker-compose.sidecar.yml up
   ```

2. Get a token:
   ```bash
   TOKEN=$(curl -s -X POST http://localhost:8081/connect/token \
     -d "grant_type=client_credentials" \
     -d "client_id=fhir-test-client" \
     -d "client_secret=fhir-test-secret" \
     -d "scope=fhir.read fhir.write" | jq -r '.access_token')
   ```

3. Access the FHIR Server:
   ```bash
   curl http://localhost:8080/fhir/Patient \
     -H "Authorization: Bearer $TOKEN"
   ```

## Related Documentation

- [Sidecar Provider Pattern](../../src/Application/Ignixa.Sidecar/)
- [Entra ID Sidecar](../Ignixa.Sidecar.Entra/)
- [Azure Web Apps with Entra ID Guide](../../docs/azure-webapps-entra.md)
