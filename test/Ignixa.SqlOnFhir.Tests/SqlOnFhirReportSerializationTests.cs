/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests verifying the emitted report JSON matches the sql-on-fhir.js report format.
 */

using System.Text.Json;

namespace Ignixa.SqlOnFhir.Tests;

/// <summary>
/// Verifies the serialized report has the sql-on-fhir.js shape:
/// <c>tests</c>/<c>name</c>/<c>result</c>/<c>passed</c>, with <c>reason</c> omitted when null.
/// </summary>
public class SqlOnFhirReportSerializationTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    [Fact]
    public void GivenPassingAndFailingEntries_WhenSerialized_ThenMatchesReportFormat()
    {
        // Arrange
        var report = new Dictionary<string, SqlOnFhirFileReport>
        {
            ["logic.json"] = new()
            {
                Tests =
                [
                    new SqlOnFhirTestEntry("filtering with 'and'", new SqlOnFhirTestResult(true, null)),
                    new SqlOnFhirTestEntry("filtering with 'or'", new SqlOnFhirTestResult(false, "skipped"))
                ]
            }
        };

        // Act
        var json = JsonSerializer.Serialize(report, SerializerOptions);

        // Assert
        Assert.Contains("\"logic.json\"", json, StringComparison.Ordinal);
        Assert.Contains("\"tests\"", json, StringComparison.Ordinal);
        Assert.Contains("\"name\"", json, StringComparison.Ordinal);
        Assert.Contains("\"result\"", json, StringComparison.Ordinal);
        Assert.Contains("\"passed\"", json, StringComparison.Ordinal);
        Assert.Contains("\"reason\": \"skipped\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenNullReason_WhenSerialized_ThenReasonOmitted()
    {
        // Arrange
        var result = new SqlOnFhirTestResult(true, null);

        // Act
        var json = JsonSerializer.Serialize(result, SerializerOptions);

        // Assert
        Assert.Contains("\"passed\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("reason", json, StringComparison.Ordinal);
    }

    [Fact]
    public void GivenPresentReason_WhenSerialized_ThenReasonIncluded()
    {
        // Arrange
        var result = new SqlOnFhirTestResult(false, "row mismatch");

        // Act
        var json = JsonSerializer.Serialize(result, SerializerOptions);

        // Assert
        Assert.Contains("\"reason\": \"row mismatch\"", json, StringComparison.Ordinal);
    }
}
