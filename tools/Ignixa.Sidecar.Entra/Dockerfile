# Multi-stage Dockerfile for Ignixa Entra Sidecar
# Build arguments for versioning (passed from CI/CD)
ARG VERSION=0.0.0-dev
ARG BUILD_DATE
ARG VCS_REF

# Stage 1: Build
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:9.0-azurelinux3.0 AS build
ARG VERSION

WORKDIR /src

# Copy root-level configuration files for centralized package management
COPY Directory.Build.props ./
COPY Directory.Packages.props ./
COPY .editorconfig ./

# Copy proto files (needed for gRPC code generation)
COPY src/Application/Ignixa.Sidecar/Protos/ src/Application/Ignixa.Sidecar/Protos/

# Copy sidecar project files for layer caching
COPY tools/Ignixa.Sidecar.Entra/Ignixa.Sidecar.Entra.csproj tools/Ignixa.Sidecar.Entra/

# Restore dependencies
WORKDIR /src/tools/Ignixa.Sidecar.Entra
RUN dotnet restore Ignixa.Sidecar.Entra.csproj /p:DisableGitVersion=true

# Copy remaining source files
WORKDIR /src
COPY tools/Ignixa.Sidecar.Entra/ tools/Ignixa.Sidecar.Entra/

# Build and publish
WORKDIR /src/tools/Ignixa.Sidecar.Entra
RUN dotnet publish Ignixa.Sidecar.Entra.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false \
    /p:DisableGitVersion=true

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0-azurelinux3.0 AS runtime
ARG VERSION
ARG BUILD_DATE
ARG VCS_REF

# OCI Labels
LABEL org.opencontainers.image.title="Ignixa Entra Sidecar" \
      org.opencontainers.image.description="Sidecar container for Entra ID authorization with Ignixa FHIR Server" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.created="${BUILD_DATE}" \
      org.opencontainers.image.revision="${VCS_REF}" \
      org.opencontainers.image.vendor="Ignixa Contributors" \
      org.opencontainers.image.source="https://github.com/brendankowitz/ignixa-fhir" \
      org.opencontainers.image.licenses="MIT"

# Install dependencies and clean up
RUN tdnf clean all && tdnf repolist --refresh && tdnf update -y && tdnf clean all

WORKDIR /app

# Copy published output from build stage
COPY --from=build /app/publish .

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

# Set environment variables
ENV ASPNETCORE_URLS="http://+:5050;http://+:8080" \
    ASPNETCORE_ENVIRONMENT=Production

USER nonroot
EXPOSE 5050 8080

# Entry point
ENTRYPOINT ["dotnet", "Ignixa.Sidecar.Entra.dll"]
