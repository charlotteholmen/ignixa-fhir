// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Domain.Abstractions;
using Ignixa.Sidecar.Audit;
using Ignixa.Sidecar.Authorization;
using Ignixa.Sidecar.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ignixa.Sidecar;

/// <summary>
/// Extension methods for registering sidecar services.
/// </summary>
public static class SidecarServiceCollectionExtensions
{
    /// <summary>
    /// Adds sidecar provider services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSidecarProviders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Bind configuration
        services.Configure<SidecarOptions>(configuration.GetSection(SidecarOptions.SectionName));

        // Get options to determine provider mode
        var sidecarSection = configuration.GetSection(SidecarOptions.SectionName);
        var providerMode = sidecarSection.GetValue<ProviderMode>("ProviderMode");

        // Log the active mode
        services.AddSingleton<IStartupFilter>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<SidecarOptions>>();
            var options = sp.GetRequiredService<IOptions<SidecarOptions>>().Value;
            logger.LogInformation(
                "Sidecar provider mode: {ProviderMode}, Endpoint: {Endpoint}",
                options.ProviderMode,
                options.Endpoint);
            return new NoOpStartupFilter();
        });

        switch (providerMode)
        {
            case ProviderMode.Sidecar:
                RegisterSidecarProviders(services);
                break;

            case ProviderMode.Hybrid:
                RegisterHybridProviders(services, sidecarSection);
                break;

            case ProviderMode.Local:
            default:
                // Local providers are registered in the Application layer
                // Nothing to do here
                break;
        }

        return services;
    }

    private static void RegisterSidecarProviders(IServiceCollection services)
    {
        // Override with sidecar implementations
        services.AddSingleton<IFhirAuthorizationService, SidecarFhirAuthorizationService>();
        services.AddSingleton<IAuditLogger, SidecarAuditLogger>();
    }

    private static void RegisterHybridProviders(IServiceCollection services, IConfigurationSection sidecarSection)
    {
        var hybridSection = sidecarSection.GetSection("Hybrid");

        // Authorization
        var authMode = hybridSection.GetValue<ProviderMode>("Authorization");
        if (authMode == ProviderMode.Sidecar)
        {
            services.AddSingleton<IFhirAuthorizationService, SidecarFhirAuthorizationService>();
        }

        // Audit logging
        var auditMode = hybridSection.GetValue<ProviderMode>("AuditLogging");
        if (auditMode == ProviderMode.Sidecar)
        {
            services.AddSingleton<IAuditLogger, SidecarAuditLogger>();
        }
    }

    /// <summary>
    /// No-op startup filter used to trigger logging during startup.
    /// </summary>
    private class NoOpStartupFilter : IStartupFilter
    {
        public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(
            Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next) => next;
    }
}
