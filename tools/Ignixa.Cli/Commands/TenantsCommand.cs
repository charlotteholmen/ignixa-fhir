using System.CommandLine;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Ignixa.Cli.Commands;

/// <summary>
/// Command for listing active tenants on the server.
/// </summary>
internal static class TenantsCommand
{
    private static readonly HttpClient s_httpClient = new HttpClient();

    public static Command Create()
    {
        var tenantsCommand = new Command("tenants", "List all active tenants on the server");

        var urlOption = new Option<string>("--url", "URL of the Ignixa server (default: http://localhost:5000)");
        urlOption.SetDefaultValue("http://localhost:5000");
        urlOption.AddAlias("-u");

        tenantsCommand.AddOption(urlOption);

        tenantsCommand.SetHandler(async (url) =>
        {
            await HandleTenantsCommandAsync(url);
        }, urlOption);

        return tenantsCommand;
    }

    private static async Task HandleTenantsCommandAsync(string url)
    {
        try
        {
            var endpoint = $"{url.TrimEnd('/')}/$tenants";

            Console.WriteLine($"Fetching tenants from {endpoint}...");

            // Send HTTP GET request
            s_httpClient.DefaultRequestHeaders.Accept.Clear();
            s_httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await s_httpClient.GetAsync(new Uri(endpoint));

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine("Success!");
                Console.WriteLine();

                if (!string.IsNullOrWhiteSpace(responseContent))
                {
                    // Parse and display tenant information
                    using var responseDoc = JsonDocument.Parse(responseContent);
                    var root = responseDoc.RootElement;

                    if (root.TryGetProperty("mode", out var modeElement))
                    {
                        Console.WriteLine($"Server Mode: {modeElement.GetString()}");
                    }

                    if (root.TryGetProperty("totalCount", out var totalElement))
                    {
                        Console.WriteLine($"Total Tenants: {totalElement.GetInt32()}");
                    }

                    Console.WriteLine();

                    if (root.TryGetProperty("tenants", out var tenantsElement) && tenantsElement.ValueKind == JsonValueKind.Array)
                    {
                        Console.WriteLine("Available Tenants:");
                        Console.WriteLine("------------------");

                        foreach (var tenant in tenantsElement.EnumerateArray())
                        {
                            if (tenant.TryGetProperty("id", out var idElement))
                            {
                                Console.Write($"  ID: {idElement.GetInt32()}");
                            }

                            if (tenant.TryGetProperty("name", out var nameElement))
                            {
                                Console.Write($" | Name: {nameElement.GetString()}");
                            }

                            if (tenant.TryGetProperty("fhirVersion", out var versionElement))
                            {
                                Console.Write($" | FHIR: {versionElement.GetString()}");
                            }

                            if (tenant.TryGetProperty("validationTier", out var validationElement))
                            {
                                Console.Write($" | Validation: {validationElement.GetString()}");
                            }

                            Console.WriteLine();

                            if (tenant.TryGetProperty("description", out var descElement))
                            {
                                Console.WriteLine($"    {descElement.GetString()}");
                            }

                            Console.WriteLine();
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
