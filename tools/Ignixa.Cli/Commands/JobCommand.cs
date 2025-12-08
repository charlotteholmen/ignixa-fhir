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
    private static readonly HttpClient s_httpClient = new HttpClient();

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

            s_httpClient.DefaultRequestHeaders.Accept.Clear();
            s_httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/fhir+json"));

            var httpContent = new StringContent(json, Encoding.UTF8, "application/fhir+json");

            Console.WriteLine($"Starting import job for tenant {tenantId}...");
            Console.WriteLine($"Input: {input}");

            var response = await s_httpClient.PostAsync(new Uri(endpoint), httpContent);

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

            s_httpClient.DefaultRequestHeaders.Accept.Clear();
            s_httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/fhir+json"));

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

            var response = await s_httpClient.PostAsync(new Uri(endpoint), null);

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

        var tenantOption = new Option<int?>("--tenant", "Tenant ID (required - use 'ignixa tenants' to see available tenants)");
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
            // Tenant ID is required for jobs listing
            if (!tenantId.HasValue)
            {
                Console.WriteLine("Error: --tenant parameter is required for listing jobs.");
                Console.WriteLine("Use 'ignixa tenants' to see available tenants.");
                return;
            }

            // Build the endpoint URL
            var endpoint = $"{url.TrimEnd('/')}/tenant/{tenantId}/$jobs-list";

            // Add query parameters if specified
            var queryParams = new List<string>();
            if (!string.IsNullOrEmpty(jobType))
            {
                queryParams.Add($"jobType={Uri.EscapeDataString(jobType)}");
            }
            if (!string.IsNullOrEmpty(status))
            {
                queryParams.Add($"status={Uri.EscapeDataString(status)}");
            }

            if (queryParams.Count > 0)
            {
                endpoint += "?" + string.Join("&", queryParams);
            }

            Console.WriteLine($"Fetching jobs from {endpoint}...");

            // Send HTTP GET request
            s_httpClient.DefaultRequestHeaders.Accept.Clear();
            s_httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            var response = await s_httpClient.GetAsync(new Uri(endpoint));

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine("Success!");
                Console.WriteLine();

                if (!string.IsNullOrWhiteSpace(responseContent))
                {
                    // Parse and display job information
                    using var responseDoc = System.Text.Json.JsonDocument.Parse(responseContent);
                    var jobs = responseDoc.RootElement;

                    if (jobs.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        var jobCount = jobs.GetArrayLength();
                        Console.WriteLine($"Total Jobs: {jobCount}");
                        Console.WriteLine();

                        if (jobCount > 0)
                        {
                            Console.WriteLine("Jobs:");
                            Console.WriteLine("-----");

                            foreach (var job in jobs.EnumerateArray())
                            {
                                if (job.TryGetProperty("jobId", out var jobIdElement))
                                {
                                    Console.WriteLine($"  Job ID: {jobIdElement.GetString()}");
                                }

                                if (job.TryGetProperty("jobType", out var jobTypeElement))
                                {
                                    Console.Write($"  Type: {jobTypeElement.GetString()}");
                                }

                                if (job.TryGetProperty("status", out var statusElement))
                                {
                                    Console.WriteLine($" | Status: {statusElement.GetString()}");
                                }

                                if (job.TryGetProperty("progressDescription", out var progressElement))
                                {
                                    Console.WriteLine($"  Progress: {progressElement.GetString()}");
                                }

                                if (job.TryGetProperty("createDate", out var createDateElement))
                                {
                                    Console.WriteLine($"  Created: {createDateElement.GetString()}");
                                }

                                if (job.TryGetProperty("errorMessage", out var errorElement) && !string.IsNullOrEmpty(errorElement.GetString()))
                                {
                                    Console.WriteLine($"  Error: {errorElement.GetString()}");
                                }

                                Console.WriteLine();
                            }
                        }
                        else
                        {
                            Console.WriteLine("No jobs found.");
                        }
                    }
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
}
