// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using OpenIddict.Abstractions;

namespace Ignixa.Sidecar.OpenIdDict;

/// <summary>
/// Seeds OpenIdDict with default clients and scopes for local development.
/// </summary>
public class OpenIdDictSeeder : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OpenIdDictSeeder> _logger;

    public OpenIdDictSeeder(
        IServiceProvider serviceProvider,
        ILogger<OpenIdDictSeeder> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.Database.EnsureCreatedAsync(cancellationToken);

        var applicationManager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var scopeManager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();

        // Create default scopes
        await CreateScopeIfNotExistsAsync(scopeManager, "fhir.read", "Read FHIR resources", cancellationToken);
        await CreateScopeIfNotExistsAsync(scopeManager, "fhir.write", "Write FHIR resources", cancellationToken);
        await CreateScopeIfNotExistsAsync(scopeManager, "fhir.delete", "Delete FHIR resources", cancellationToken);
        await CreateScopeIfNotExistsAsync(scopeManager, "fhir.*", "Full FHIR access", cancellationToken);

        // Create default client for testing (client credentials flow)
        if (await applicationManager.FindByClientIdAsync("fhir-test-client", cancellationToken) is null)
        {
            await applicationManager.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = "fhir-test-client",
                ClientSecret = "fhir-test-secret",
                DisplayName = "FHIR Test Client",
                Permissions =
                {
                    OpenIddictConstants.Permissions.Endpoints.Token,
                    OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
                    OpenIddictConstants.Permissions.GrantTypes.Password,
                    OpenIddictConstants.Permissions.Scopes.Roles,
                    OpenIddictConstants.Permissions.Prefixes.Scope + "fhir.read",
                    OpenIddictConstants.Permissions.Prefixes.Scope + "fhir.write",
                    OpenIddictConstants.Permissions.Prefixes.Scope + "fhir.delete",
                    OpenIddictConstants.Permissions.Prefixes.Scope + "fhir.*"
                }
            }, cancellationToken);

            _logger.LogInformation("Created default test client 'fhir-test-client'");
        }

        // Create admin client with all permissions
        if (await applicationManager.FindByClientIdAsync("fhir-admin-client", cancellationToken) is null)
        {
            await applicationManager.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = "fhir-admin-client",
                ClientSecret = "fhir-admin-secret",
                DisplayName = "FHIR Admin Client",
                Permissions =
                {
                    OpenIddictConstants.Permissions.Endpoints.Token,
                    OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
                    OpenIddictConstants.Permissions.Scopes.Roles,
                    OpenIddictConstants.Permissions.Prefixes.Scope + "fhir.*"
                }
            }, cancellationToken);

            _logger.LogInformation("Created admin client 'fhir-admin-client'");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task CreateScopeIfNotExistsAsync(
        IOpenIddictScopeManager scopeManager,
        string name,
        string displayName,
        CancellationToken cancellationToken)
    {
        if (await scopeManager.FindByNameAsync(name, cancellationToken) is null)
        {
            await scopeManager.CreateAsync(new OpenIddictScopeDescriptor
            {
                Name = name,
                DisplayName = displayName
            }, cancellationToken);

            _logger.LogInformation("Created scope '{Scope}'", name);
        }
    }
}
