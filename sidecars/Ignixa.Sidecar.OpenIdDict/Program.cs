// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Sidecar.OpenIdDict;
using Ignixa.Sidecar.OpenIdDict.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Configure gRPC services
builder.Services.AddGrpc();

// Configure OpenIdDict authorization options
builder.Services.Configure<OpenIdDictAuthorizationOptions>(
    builder.Configuration.GetSection("OpenIdDictAuthorization"));

// Add DbContext for OpenIdDict
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseInMemoryDatabase("OpenIdDict");
    options.UseOpenIddict();
});

// Configure OpenIdDict
builder.Services.AddOpenIddict()
    .AddCore(options =>
    {
        options.UseEntityFrameworkCore()
               .UseDbContext<ApplicationDbContext>();
    })
    .AddServer(options =>
    {
        // Enable the token endpoint
        options.SetTokenEndpointUris("/connect/token");
        
        // Enable the client credentials flow
        options.AllowClientCredentialsFlow();
        
        // Enable the password flow (for testing)
        options.AllowPasswordFlow();
        
        // Register scopes
        options.RegisterScopes("fhir.read", "fhir.write", "fhir.delete", "fhir.*");
        
        // Use development encryption/signing keys
        options.AddDevelopmentEncryptionCertificate()
               .AddDevelopmentSigningCertificate();
        
        // Register the ASP.NET Core host
        options.UseAspNetCore()
               .EnableTokenEndpointPassthrough();
    })
    .AddValidation(options =>
    {
        options.UseLocalServer();
        options.UseAspNetCore();
    });

// Add hosted service to seed the database
builder.Services.AddHostedService<OpenIdDictSeeder>();

var app = builder.Build();

// Map gRPC services
app.MapGrpcService<OpenIdDictAuthorizationService>();
app.MapGrpcService<ConsoleAuditLoggerService>();
app.MapGrpcService<ConsoleLoggingService>();

// Health check endpoint for container orchestration
app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Timestamp = DateTime.UtcNow }));

// Token endpoint (handled by OpenIdDict)
app.MapPost("/connect/token", async (HttpContext context) =>
{
    // OpenIdDict handles this via middleware
    await Task.CompletedTask;
});

app.Logger.LogInformation("Ignixa OpenIdDict Sidecar starting on {Urls}", string.Join(", ", app.Urls));

await app.RunAsync();
