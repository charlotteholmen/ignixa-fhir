// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Sidecar.Entra.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure gRPC services
builder.Services.AddGrpc();

// Configure Entra ID settings
builder.Services.Configure<EntraAuthorizationOptions>(
    builder.Configuration.GetSection("EntraAuthorization"));

var app = builder.Build();

// Map gRPC services
app.MapGrpcService<EntraAuthorizationService>();
app.MapGrpcService<ConsoleAuditLoggerService>();
app.MapGrpcService<ConsoleLoggingService>();

// Health check endpoint for container orchestration
app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Timestamp = DateTime.UtcNow }));

app.Logger.LogInformation("Ignixa Entra Sidecar starting on {Urls}", string.Join(", ", app.Urls));

await app.RunAsync();
