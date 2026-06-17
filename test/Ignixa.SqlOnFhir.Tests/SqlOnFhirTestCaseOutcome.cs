/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Result of evaluating a single SQL on FHIR conformance test case.
 */

namespace Ignixa.SqlOnFhir.Tests;

/// <summary>
/// Outcome of a single SQL on FHIR conformance test case: whether it passed and,
/// if not, a human-readable reason.
/// </summary>
public record SqlOnFhirTestCaseOutcome(bool Passed, string? Reason);
