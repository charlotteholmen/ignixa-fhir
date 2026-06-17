/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * xunit collection fixture that accumulates SQL on FHIR conformance test outcomes
 * and writes them as a sql-on-fhir.js-format test_report.json when the collection completes.
 */

using System.Text.Json;

namespace Ignixa.SqlOnFhir.Tests;

/// <summary>
/// Collection fixture that accumulates per-case conformance outcomes during a test run and
/// writes a sql-on-fhir.js-format <c>test_report.json</c> on dispose (after the last case).
/// </summary>
public sealed class SqlOnFhirReportCollector : IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly Lock _gate = new();
    private readonly Dictionary<string, SqlOnFhirFileReport> _reports = [];

    /// <summary>
    /// Records the outcome of a single test case under its source file (keyed by file name
    /// including the <c>.json</c> extension), appending entries in arrival order.
    /// </summary>
    public void Record(string fileName, string testName, SqlOnFhirTestCaseOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(testName);
        ArgumentNullException.ThrowIfNull(outcome);

        var entry = new SqlOnFhirTestEntry(testName, new SqlOnFhirTestResult(outcome.Passed, outcome.Reason));

        lock (_gate)
        {
            if (!_reports.TryGetValue(fileName, out var report))
            {
                report = new SqlOnFhirFileReport();
                _reports[fileName] = report;
            }

            report.Tests.Add(entry);
        }
    }

    /// <summary>
    /// Resolves the output path: <c>SOF_TEST_REPORT_PATH</c> if set, otherwise
    /// <c>test_report.json</c> next to the test assembly.
    /// </summary>
    public static string ResolveOutputPath()
    {
        var overridePath = Environment.GetEnvironmentVariable("SOF_TEST_REPORT_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return overridePath;
        }

        var assemblyDir = Path.GetDirectoryName(typeof(SqlOnFhirReportCollector).Assembly.Location) ?? ".";
        return Path.Combine(assemblyDir, "test_report.json");
    }

    public void Dispose()
    {
        var outputPath = ResolveOutputPath();

        Dictionary<string, SqlOnFhirFileReport> snapshot;
        lock (_gate)
        {
            snapshot = new Dictionary<string, SqlOnFhirFileReport>(_reports);
        }

        try
        {
            var json = JsonSerializer.Serialize(snapshot, SerializerOptions);
            File.WriteAllText(outputPath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.Error.WriteLine($"Failed to write SQL-on-FHIR test report to '{outputPath}': {ex.Message}");
        }
    }
}
