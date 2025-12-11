# Azure Web Apps with Microsoft Entra ID Authentication

This guide explains how to set up the Ignixa FHIR Server on Azure Web Apps with Microsoft Entra ID (formerly Azure AD) authentication using the sidecar provider pattern.

## Overview

The sidecar provider pattern allows the FHIR server to delegate cross-cutting concerns (authorization, audit logging, telemetry) to external services. When deploying to Azure, you can use Microsoft Entra ID for authentication and the **Ignixa Entra Sidecar** for role-based authorization decisions.

### Architecture

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
│  │  - AuthorizationService (Entra ID roles/scopes)    │    │
│  │  - AuditLoggerService                              │    │
│  │  - LoggingService                                  │    │
│  └────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────┘
                            │
                            ▼
                   ┌─────────────────┐
                   │ Microsoft Entra │
                   │   ID (Azure AD) │
                   └─────────────────┘
```

## The Entra Sidecar

The Ignixa Entra Sidecar (`sidecars/Ignixa.Sidecar.Entra`) is a gRPC server that implements the sidecar authorization services. It validates user claims from Entra ID tokens and makes authorization decisions based on:

- **Required Roles**: Users must have specific Entra ID app roles
- **Required Scopes**: Users must have specific delegated permissions
- **Action Role Mapping**: Different roles for read/write/delete operations
- **Resource Type Role Mapping**: Different roles for specific FHIR resource types

### Building the Sidecar

```bash
# Build using Docker
docker build -f sidecars/Ignixa.Sidecar.Entra/Dockerfile -t ignixa-sidecar-entra:latest .

# Or build using .NET CLI
cd sidecars/Ignixa.Sidecar.Entra
dotnet build
```

## Prerequisites

1. **Azure Subscription** with permissions to create:
   - App Service
   - SQL Database
   - Storage Account
   - App Registration in Microsoft Entra ID

2. **Microsoft Entra ID App Registration**:
   - Application (client) ID
   - Configured API permissions
   - Redirect URIs set up

## Deployment Options

### Option 1: Using the ARM Template

Deploy using the provided ARM template with Entra ID configuration:

```bash
# Set your parameters
RESOURCE_GROUP="ignixa-fhir-rg"
APP_NAME="my-fhir-server"
ENTRA_CLIENT_ID="your-app-registration-client-id"
ENTRA_TENANT_ID="your-tenant-id"

# Deploy the template
az deployment group create \
  --resource-group $RESOURCE_GROUP \
  --template-file deploy/azure/azuredeploy-entrasidecar.json \
  --parameters appName=$APP_NAME \
               entraClientId=$ENTRA_CLIENT_ID \
               entraTenantId=$ENTRA_TENANT_ID
```

### Option 2: Manual Configuration

If you already have a FHIR server deployed, you can add Entra ID authentication manually.

## Step-by-Step Setup

### Step 1: Create an App Registration in Microsoft Entra ID

1. Navigate to **Azure Portal** → **Microsoft Entra ID** → **App registrations**
2. Click **New registration**
3. Configure:
   - **Name**: `Ignixa FHIR Server`
   - **Supported account types**: Choose based on your needs
   - **Redirect URI**: `https://<your-app-name>.azurewebsites.net/.auth/login/aad/callback`

4. After creation, note the:
   - **Application (client) ID**
   - **Directory (tenant) ID**

### Step 2: Configure API Permissions

1. In the App Registration, go to **API permissions**
2. Add permissions as needed:
   - `User.Read` (delegated) - for user profile access
   - Custom scopes for FHIR operations

3. Click **Grant admin consent** if required

### Step 3: Configure App Service Authentication

1. Navigate to your App Service in Azure Portal
2. Go to **Authentication** in the left menu
3. Click **Add identity provider**
4. Select **Microsoft**
5. Configure:
   - **App registration type**: Pick an existing app registration
   - **Application (client) ID**: Your app registration client ID
   - **Client secret**: (Optional for public client flows)
   - **Issuer URL**: `https://login.microsoftonline.com/<tenant-id>/v2.0`
   - **Allowed token audiences**: `api://<client-id>` and your client ID

### Step 4: Configure the FHIR Server

Add the following configuration to your App Service application settings:

```json
{
  "Authentication__Entra__ClientId": "<your-client-id>",
  "Authentication__Entra__TenantId": "<your-tenant-id>",
  "Authentication__Entra__Audience": "api://<your-client-id>",
  "Sidecar__ProviderMode": "Local",
  "Sidecar__Endpoint": "http://localhost:5050",
  "Sidecar__FailOpen": "false"
}
```

## Sidecar Provider Configuration

### Provider Modes

| Mode | Description | Use Case |
|------|-------------|----------|
| `Local` | Built-in implementations, no external dependencies | Development, testing |
| `Sidecar` | All requests delegated to sidecar endpoint | Production with custom auth |
| `Hybrid` | Mix of local and sidecar per service | Gradual migration |

### Local Mode (Default)

In Local mode, the server uses built-in implementations:
- **Authorization**: Permits all authenticated requests
- **Audit Logging**: Writes to console/Application Insights

```json
{
  "Sidecar__ProviderMode": "Local"
}
```

### Sidecar Mode

In Sidecar mode, all cross-cutting concerns are delegated to an external gRPC service:

```json
{
  "Sidecar__ProviderMode": "Sidecar",
  "Sidecar__Endpoint": "http://localhost:5050",
  "Sidecar__TimeoutMs": "5000",
  "Sidecar__RetryCount": "3",
  "Sidecar__FailOpen": "false"
}
```

### Hybrid Mode

In Hybrid mode, you can configure each service independently:

```json
{
  "Sidecar__ProviderMode": "Hybrid",
  "Sidecar__Hybrid__Authorization": "Sidecar",
  "Sidecar__Hybrid__AuditLogging": "Sidecar",
  "Sidecar__Hybrid__Logging": "Local"
}
```

## Implementing a Custom Sidecar

To implement a custom authorization sidecar, create a gRPC service that implements the following proto definitions:

### Authorization Service

```protobuf
service AuthorizationService {
  rpc Authorize (AuthorizationRequest) returns (AuthorizationResult);
}

message AuthorizationRequest {
  string user_id = 1;
  map<string, string> claims = 2;
  string resource = 3;
  string action = 4;
  string policy_name = 5;
  int32 tenant_id = 6;
  map<string, string> metadata = 7;
}

message AuthorizationResult {
  bool is_authorized = 1;
  string reason = 2;
  map<string, string> context = 3;
}
```

### Audit Logger Service

```protobuf
service AuditLoggerService {
  rpc LogAuditEvent (AuditEvent) returns (AuditResponse);
  rpc LogAuditEventBatch (AuditEventBatch) returns (AuditResponse);
}
```

## Testing the Configuration

### 1. Get an Access Token

```bash
# Using Azure CLI
az account get-access-token \
  --resource api://<your-client-id> \
  --query accessToken -o tsv
```

### 2. Make a FHIR Request

```bash
TOKEN=$(az account get-access-token --resource api://<your-client-id> --query accessToken -o tsv)

curl -X GET "https://<your-app-name>.azurewebsites.net/tenant/1/Patient" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Accept: application/fhir+json"
```

### 3. Verify in Application Insights

Check Application Insights for:
- Authentication events
- Audit log entries
- Request traces

## Security Best Practices

1. **Always use HTTPS** - Enforced by default on Azure App Service
2. **Use Managed Identity** for Azure resource access (SQL, Storage)
3. **Set `FailOpen` to `false`** in production to deny requests when sidecar is unavailable
4. **Enable audit logging** to track all access attempts
5. **Configure allowed audiences** to prevent token reuse attacks
6. **Use short token lifetimes** and implement token refresh

## Troubleshooting

### Common Issues

#### 401 Unauthorized

- Verify the token audience matches your configuration
- Check the issuer URL includes `/v2.0` for v2 tokens
- Ensure the App Registration has the correct redirect URIs

#### 403 Forbidden

- Check tenant-level authorization configuration
- Verify the user has appropriate claims
- Check sidecar authorization service logs

#### Sidecar Connection Errors

- Verify the sidecar endpoint is accessible
- Check gRPC port configuration (default: 5050)
- Review circuit breaker state in logs

### Diagnostic Logging

Enable detailed logging by setting:

```json
{
  "Logging__LogLevel__Ignixa.Sidecar": "Debug",
  "Logging__LogLevel__Microsoft.AspNetCore.Authentication": "Debug"
}
```

## Related Documentation

- [Sidecar Provider Pattern Requirements](../investigations/sidecar-provider-requirements.md)
- [ARM Template Reference](../deploy/azure/azuredeploy-entrasidecar.json)
- [Multi-Tenancy Guide](./package-management-tenant-guide.md)
- [Microsoft Entra ID Documentation](https://learn.microsoft.com/en-us/entra/identity-platform/)
