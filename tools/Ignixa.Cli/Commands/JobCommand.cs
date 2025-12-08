using System.CommandLine;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Ignixa.Cli.Commands;

/// <summary>
/// Command group for managing import/export jobs.
/// </summary>
internal static class JobCommand
{
    public static Command Create()
    {
        var jobCommand = new Command("job", "Manage import/export jobs");

        // Add subcommands
        jobCommand.AddCommand(CreateImportCommand());
        jobCommand.AddCommand(CreateExportCommand());
        jobCommand.AddCommand(CreateListCommand());

        return jobCommand;
    }

    private static Command CreateImportCommand()
    {
        var importCommand = new Command("import", "Start an import job");

        var inputOption = new Option<string>("--input", "Input file path in blob storage") { IsRequired = true };
        inputOption.AddAlias("-i");

        var tenantOption = new Option<int>("--tenant", "Tenant ID") { IsRequired = true };
        tenantOption.AddAlias("-t");

        var urlOption = new Option<string>("--url", "URL of the Ignixa server (default: http://localhost:5000)");
        urlOption.SetDefaultValue("http://localhost:5000");
        urlOption.AddAlias("-u");

        importCommand.AddOption(inputOption);
        importCommand.AddOption(tenantOption);
        importCommand.AddOption(urlOption);

        importCommand.SetHandler(async (input, tenant, url) =>
        {
            await HandleImportJobAsync(input, tenant, url);
        }, inputOption, tenantOption, urlOption);

        return importCommand;
    }

    private static async Task HandleImportJobAsync(string input, int tenantId, string url)
    {
        try
        {
            var endpoint = $"{url.TrimEnd('/')}/tenant/{tenantId}/$import";

            // Build Parameters resource for import
            var parameters = new
            {
                resourceType = "Parameters",
                parameter = new object[]
                {
                    new
                    {
                        name = "inputFormat",
                        valueString = "application/fhir+ndjson"
                    },
                    new
                    {
                        name = "input",
                        part = new object[]
                        {
                            new
                            {
                                name = "type",
                                valueString = "Resource"
                            },
                            new
                            {
                                name = "url",
                                valueUrl = input
                            }
                        }
                    }
                }
            };

            var json = JsonSerializer.Serialize(parameters, new JsonSerializerOptions
            {
                WriteIndented = false
            });

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/fhir+json"));

            var httpContent = new StringContent(json, Encoding.UTF8, "application/fhir+json");

            Console.WriteLine($"Starting import job for tenant {tenantId}...");
            Console.WriteLine($"Input: {input}");

            var response = await httpClient.PostAsync(new Uri(endpoint), httpContent);

            if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.Accepted)
            {
                Console.WriteLine($"Success! Status: {response.StatusCode}");

                if (response.Headers.TryGetValues("Content-Location", out var locations))
                {
                    var location = locations.FirstOrDefault();
                    Console.WriteLine($"Job status URL: {location}");
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                if (!string.IsNullOrWhiteSpace(responseContent))
                {
                    using var responseDoc = JsonDocument.Parse(responseContent);
                    var formattedJson = JsonSerializer.Serialize(responseDoc, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                    Console.WriteLine("Response:");
                    Console.WriteLine(formattedJson);
                }
            }
            else
            {
                Console.WriteLine($"Error: {response.StatusCode}");
                var errorContent = await response.Content.ReadAsStringAsync();
                if (!string.IsNullOrWhiteSpace(errorContent))
                {
                    Console.WriteLine(errorContent);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    private static Command CreateExportCommand()
    {
        var exportCommand = new Command("export", "Start an export job");

        var typeOption = new Option<string?>("--type", "Resource types to export (comma-separated)");
        var typeFilterOption = new Option<string?>("--typefilter", "Type filters for export");
        var viewDefinitionOption = new Option<string?>("--viewdefinition", "SQL on FHIR view definition for export");
        var tenantOption = new Option<int>("--tenant", "Tenant ID") { IsRequired = true };
        tenantOption.AddAlias("-t");

        var urlOption = new Option<string>("--url", "URL of the Ignixa server (default: http://localhost:5000)");
        urlOption.SetDefaultValue("http://localhost:5000");
        urlOption.AddAlias("-u");

        exportCommand.AddOption(typeOption);
        exportCommand.AddOption(typeFilterOption);
        exportCommand.AddOption(viewDefinitionOption);
        exportCommand.AddOption(tenantOption);
        exportCommand.AddOption(urlOption);

        exportCommand.SetHandler(async (type, typeFilter, viewDefinition, tenant, url) =>
        {
            await HandleExportJobAsync(type, typeFilter, viewDefinition, tenant, url);
        }, typeOption, typeFilterOption, viewDefinitionOption, tenantOption, urlOption);

        return exportCommand;
    }

    private static async Task HandleExportJobAsync(
        string? type,
        string? typeFilter,
        string? viewDefinition,
        int tenantId,
        string url)
    {
        try
        {
            var endpoint = $"{url.TrimEnd('/')}/tenant/{tenantId}/$export";

            // Build query string
            var queryParams = new List<string>();

            if (!string.IsNullOrEmpty(type))
            {
                queryParams.Add($"_type={Uri.EscapeDataString(type)}");
            }

            if (!string.IsNullOrEmpty(typeFilter))
            {
                queryParams.Add($"_typeFilter={Uri.EscapeDataString(typeFilter)}");
            }

            if (!string.IsNullOrEmpty(viewDefinition))
            {
                queryParams.Add($"_viewDefinition={Uri.EscapeDataString(viewDefinition)}");
            }

            if (queryParams.Count > 0)
            {
                endpoint += "?" + string.Join("&", queryParams);
            }

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/fhir+json"));

            Console.WriteLine($"Starting export job for tenant {tenantId}...");
            if (!string.IsNullOrEmpty(type))
            {
                Console.WriteLine($"Resource types: {type}");
            }
            if (!string.IsNullOrEmpty(typeFilter))
            {
                Console.WriteLine($"Type filter: {typeFilter}");
            }
            if (!string.IsNullOrEmpty(viewDefinition))
            {
                Console.WriteLine($"View definition: {viewDefinition}");
            }

            var response = await httpClient.PostAsync(new Uri(endpoint), null);

            if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.Accepted)
            {
                Console.WriteLine($"Success! Status: {response.StatusCode}");

                if (response.Headers.TryGetValues("Content-Location", out var locations))
                {
                    var location = locations.FirstOrDefault();
                    Console.WriteLine($"Job status URL: {location}");
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                if (!string.IsNullOrWhiteSpace(responseContent))
                {
                    using var responseDoc = JsonDocument.Parse(responseContent);
                    var formattedJson = JsonSerializer.Serialize(responseDoc, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                    Console.WriteLine("Response:");
                    Console.WriteLine(formattedJson);
                }
            }
            else
            {
                Console.WriteLine($"Error: {response.StatusCode}");
                var errorContent = await response.Content.ReadAsStringAsync();
                if (!string.IsNullOrWhiteSpace(errorContent))
                {
                    Console.WriteLine(errorContent);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    private static Command CreateListCommand()
    {
        var listCommand = new Command("list", "List all import/export jobs");

        var tenantOption = new Option<int?>("--tenant", "Tenant ID (optional in single-tenant scenarios)");
        tenantOption.AddAlias("-t");

        var urlOption = new Option<string>("--url", "URL of the Ignixa server (default: http://localhost:5000)");
        urlOption.SetDefaultValue("http://localhost:5000");
        urlOption.AddAlias("-u");

        var jobTypeOption = new Option<string?>("--type", "Filter by job type (Import or Export)");
        var statusOption = new Option<string?>("--status", "Filter by status (Queued, Running, Completed, Failed, Cancelled)");

        listCommand.AddOption(tenantOption);
        listCommand.AddOption(urlOption);
        listCommand.AddOption(jobTypeOption);
        listCommand.AddOption(statusOption);

        listCommand.SetHandler(async (tenant, url, jobType, status) =>
        {
            await HandleListJobsAsync(tenant, url, jobType, status);
        }, tenantOption, urlOption, jobTypeOption, statusOption);

        return listCommand;
    }

    private static async Task HandleListJobsAsync(int? tenantId, string url, string? jobType, string? status)
    {
        try
        {
            // For now, use a simple approach to list jobs
            // In a real implementation, this would call a dedicated jobs listing endpoint
            Console.WriteLine("Listing jobs...");

            if (tenantId.HasValue)
            {
                Console.WriteLine($"Tenant: {tenantId}");
            }
            if (!string.IsNullOrEmpty(jobType))
            {
                Console.WriteLine($"Job Type: {jobType}");
            }
            if (!string.IsNullOrEmpty(status))
            {
                Console.WriteLine($"Status: {status}");
            }

            // Note: The actual implementation would require an API endpoint that lists jobs
            // For now, provide a message about this limitation
            Console.WriteLine();
            Console.WriteLine("Note: Job listing requires a dedicated API endpoint.");
            Console.WriteLine("To check the status of a specific job, use the Content-Location URL");
            Console.WriteLine("returned when starting an import or export job.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
}
