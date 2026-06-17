/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Test runner for official SQL on FHIR v2 specification test suite.
 * Loads test cases from official JSON files, evaluates each via SqlOnFhirTestCaseEvaluator,
 * records outcomes into the report collector, and asserts to preserve the build gate.
 */

namespace Ignixa.SqlOnFhir.Tests;

/// <summary>
/// Test runner for official SQL on FHIR v2 test suite.
/// Dynamically loads JSON test files, evaluates each case, records the outcome for the
/// emitted report, then asserts so any conformance regression still fails the build.
/// </summary>
[Collection("SqlOnFhirReport")]
public class OfficialSqlOnFhirTestRunner(SqlOnFhirReportCollector collector)
{
    private readonly SqlOnFhirReportCollector _collector = collector;

    private static readonly string TestFilesDirectory = Path.Combine(
        Path.GetDirectoryName(typeof(OfficialSqlOnFhirTestRunner).Assembly.Location) ?? "",
        "sql-on-fhir-tests", "tests");

    /// <summary>
    /// Gets all official SQL on FHIR test files from the specification repository.
    /// </summary>
    public static IEnumerable<object[]> GetOfficialTestCases()
    {
        var testCases = new List<object[]>();

        if (!Directory.Exists(TestFilesDirectory))
        {
            return testCases;
        }

        var testFiles = Directory.GetFiles(TestFilesDirectory, "*.json").OrderBy(f => f);

        foreach (var testFile in testFiles)
        {
            SqlOnFhirTestFile? testData = null;
            Exception? parseError = null;

            try
            {
                testData = TestFileParser.ParseTestFile(testFile);
            }
            catch (Exception ex)
            {
                parseError = ex;
            }

            var fileName = Path.GetFileNameWithoutExtension(testFile);

            if (parseError != null)
            {
                testCases.Add([$"{fileName}_ERROR", null!, new ErrorTestCase(parseError)]);
                continue;
            }

            if (testData != null)
            {
                foreach (var testCase in testData.Tests)
                {
                    testCases.Add([fileName, testData, testCase]);
                }
            }
        }

        return testCases;
    }

    /// <summary>
    /// Runs a single official SQL on FHIR test case.
    /// </summary>
    [Theory]
    [MemberData(nameof(GetOfficialTestCases))]
    public void GivenViewDefinition_WhenEvaluated_ThenMatchesExpectedOutput(string fileName, SqlOnFhirTestFile? testFile, object testCase)
    {
        if (testCase is ErrorTestCase errorCase)
        {
            Assert.Fail($"Failed to parse test file {fileName}: {errorCase.Exception.Message}");
        }

        var sqlTestCase = testCase as SqlOnFhirTestCase;
        Assert.NotNull(sqlTestCase);
        Assert.NotNull(testFile);

        var reportKey = $"{fileName}.json";

        if (sqlTestCase.ViewNode is null)
        {
            _collector.Record(reportKey, sqlTestCase.Title, new SqlOnFhirTestCaseOutcome(false, "skipped"));
            return;
        }

        var outcome = SqlOnFhirTestCaseEvaluator.Run(testFile, sqlTestCase);
        _collector.Record(reportKey, sqlTestCase.Title, outcome);
        Assert.True(outcome.Passed, outcome.Reason);
    }

    /// <summary>
    /// Placeholder for test cases that failed to parse.
    /// </summary>
    private sealed class ErrorTestCase(Exception exception)
    {
        public Exception Exception { get; } = exception;
    }
}
