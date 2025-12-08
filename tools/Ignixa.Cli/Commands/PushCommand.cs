using System.CommandLine;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Ignixa.Cli.Commands;

/// <summary>
/// Command for pushing FHIR resources or bundles to the server.
/// </summary>
internal static class PushCommand
{
    public static Command Create()
    {
        var pushCommand = new Command("push", "Push a FHIR resource or bundle to the server");

        var urlOption = new Option<string>("--url", "URL of the Ignixa server") { IsRequired = true };
        urlOption.AddAlias("-u");

        var fileOption = new Option<string>("--file", "Path to the FHIR resource or bundle file") { IsRequired = true };
        fileOption.AddAlias("-f");

        var tenantOption = new Option<int?>("--tenant", "Tenant ID (optional in single-tenant scenarios)");
        tenantOption.AddAlias("-t");

        pushCommand.AddOption(urlOption);
        pushCommand.AddOption(fileOption);
        pushCommand.AddOption(tenantOption);

        pushCommand.SetHandler(async (url, file, tenant) =>
        {
            await HandlePushCommandAsync(url, file, tenant);
        }, urlOption, fileOption, tenantOption);

        return pushCommand;
    }

    private static async Task HandlePushCommandAsync(string url, string filePath, int? tenantId)
    {
        try
        {
            // Read file
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"Error: File not found: {filePath}");
                return;
            }

            var content = await File.ReadAllTextAsync(filePath);

            // Parse JSON to determine resource type
            using var jsonDoc = JsonDocument.Parse(content);
            var root = jsonDoc.RootElement;

            if (!root.TryGetProperty("resourceType", out var resourceTypeElement))
            {
                Console.WriteLine("Error: Invalid FHIR resource - missing resourceType property");
                return;
            }

            var resourceType = resourceTypeElement.GetString();

            // Determine endpoint based on resource type
            string endpoint;
            if (resourceType == "Bundle")
            {
                // For bundles, use the root endpoint
                endpoint = tenantId.HasValue
                    ? $"{url.TrimEnd('/')}/tenant/{tenantId}"
                    : url.TrimEnd('/');
            }
            else
            {
                // For individual resources, use resource-specific endpoint
                endpoint = tenantId.HasValue
                    ? $"{url.TrimEnd('/')}/tenant/{tenantId}/{resourceType}"
                    : $"{url.TrimEnd('/')}/{resourceType}";
            }

            // Send HTTP POST request
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/fhir+json"));

            var httpContent = new StringContent(content, Encoding.UTF8, "application/fhir+json");

            Console.WriteLine($"Pushing {resourceType} to {endpoint}...");

            var response = await httpClient.PostAsync(new Uri(endpoint), httpContent);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine("Success!");
                Console.WriteLine($"Status: {response.StatusCode}");

                if (!string.IsNullOrWhiteSpace(responseContent))
                {
                    // Pretty print the response
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
}
