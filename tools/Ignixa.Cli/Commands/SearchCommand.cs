using System.CommandLine;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Ignixa.Cli.Commands;

/// <summary>
/// Command for searching FHIR resources on the server.
/// </summary>
internal static class SearchCommand
{
    private static readonly HttpClient s_httpClient = new HttpClient();

    public static Command Create()
    {
        var searchCommand = new Command("search", "Search for FHIR resources on the server");

        var resourceTypeArg = new Argument<string>("resourceType", "The FHIR resource type to search (e.g., Patient, Observation)");

        var urlOption = new Option<string>("--url", "URL of the Ignixa server (default: http://localhost:5000)");
        urlOption.SetDefaultValue("http://localhost:5000");
        urlOption.AddAlias("-u");

        var tenantOption = new Option<int?>("--tenant", "Tenant ID (optional in single-tenant scenarios)");
        tenantOption.AddAlias("-t");

        searchCommand.AddArgument(resourceTypeArg);
        searchCommand.AddOption(urlOption);
        searchCommand.AddOption(tenantOption);

        // Allow arbitrary additional options to be passed as search parameters
        searchCommand.SetHandler(async (context) =>
        {
            var resourceType = context.ParseResult.GetValueForArgument(resourceTypeArg);
            var url = context.ParseResult.GetValueForOption(urlOption);
            var tenantId = context.ParseResult.GetValueForOption(tenantOption);

            // Extract all unparsed tokens as search parameters
            var searchParams = new Dictionary<string, string>();
            var tokens = context.ParseResult.Tokens;

            for (int i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i].Value;
                if (token.StartsWith("--") && token.Contains('='))
                {
                    var parts = token[2..].Split('=', 2);
                    if (parts.Length == 2)
                    {
                        searchParams[parts[0]] = parts[1];
                    }
                }
            }

            await HandleSearchCommandAsync(resourceType, url!, tenantId, searchParams);
        });

        return searchCommand;
    }

    private static async Task HandleSearchCommandAsync(
        string resourceType,
        string url,
        int? tenantId,
        Dictionary<string, string> searchParams)
    {
        try
        {
            // Build search URL
            var baseUrl = tenantId.HasValue
                ? $"{url.TrimEnd('/')}/tenant/{tenantId}/{resourceType}"
                : $"{url.TrimEnd('/')}/{resourceType}";

            var queryString = string.Join("&", searchParams.Select(kvp =>
                $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

            var searchUrl = string.IsNullOrEmpty(queryString)
                ? baseUrl
                : $"{baseUrl}?{queryString}";

            Console.WriteLine($"Searching {resourceType} at {searchUrl}...");

            // Send HTTP GET request
            s_httpClient.DefaultRequestHeaders.Accept.Clear();
            s_httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/fhir+json"));

            var response = await s_httpClient.GetAsync(new Uri(searchUrl));

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine("Success!");

                if (!string.IsNullOrWhiteSpace(responseContent))
                {
                    // Pretty print the response
                    using var responseDoc = JsonDocument.Parse(responseContent);
                    var root = responseDoc.RootElement;

                    // Display search results summary
                    if (root.TryGetProperty("resourceType", out var resourceTypeElement) &&
                        resourceTypeElement.GetString() == "Bundle")
                    {
                        if (root.TryGetProperty("total", out var totalElement))
                        {
                            Console.WriteLine($"Total results: {totalElement.GetInt32()}");
                        }

                        if (root.TryGetProperty("entry", out var entryElement) && entryElement.ValueKind == JsonValueKind.Array)
                        {
                            Console.WriteLine($"Returned entries: {entryElement.GetArrayLength()}");
                            Console.WriteLine();

                            // Display each entry
                            var index = 1;
                            foreach (var entry in entryElement.EnumerateArray())
                            {
                                if (entry.TryGetProperty("resource", out var resource))
                                {
                                    Console.WriteLine($"--- Entry {index} ---");
                                    var formattedJson = JsonSerializer.Serialize(resource, new JsonSerializerOptions
                                    {
                                        WriteIndented = true
                                    });
                                    Console.WriteLine(formattedJson);
                                    Console.WriteLine();
                                    index++;
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine("No entries found.");
                        }
                    }
                    else
                    {
                        // Not a bundle, just display the response
                        var formattedJson = JsonSerializer.Serialize(responseDoc, new JsonSerializerOptions
                        {
                            WriteIndented = true
                        });
                        Console.WriteLine(formattedJson);
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
