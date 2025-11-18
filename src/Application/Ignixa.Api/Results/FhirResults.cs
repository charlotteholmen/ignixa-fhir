// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Api.Http;

/// <summary>
/// Static factory class for creating FHIR HTTP responses.
/// Provides fluent API for building responses with FHIR-specific headers.
/// </summary>
public static class FhirResults
{
    /// <summary>
    /// Creates a 200 OK response with FHIR resource bytes.
    /// </summary>
    /// <param name="resourceBytes">Raw JSON bytes of the FHIR resource</param>
    public static FhirResult Ok(ReadOnlyMemory<byte> resourceBytes)
    {
        return new FhirResult(StatusCodes.Status200OK, resourceBytes);
    }

    /// <summary>
    /// Creates a 201 Created response with FHIR resource bytes.
    /// Typically used after successful POST operations.
    /// </summary>
    /// <param name="location">The URI of the created resource (e.g., "/Patient/123")</param>
    /// <param name="resourceBytes">Raw JSON bytes of the created FHIR resource</param>
    public static FhirResult Created(string location, ReadOnlyMemory<byte> resourceBytes)
    {
        return new FhirResult(StatusCodes.Status201Created, resourceBytes, location);
    }

    /// <summary>
    /// Creates a 201 Created response without a body (for Prefer: return=minimal).
    /// </summary>
    /// <param name="location">The URI of the created resource (e.g., "/Patient/123")</param>
    public static FhirResult Created(string location)
    {
        return new FhirResult(StatusCodes.Status201Created, bytes: null, location);
    }

    /// <summary>
    /// Creates a 304 Not Modified response.
    /// Used when conditional GET headers indicate the resource hasn't changed.
    /// </summary>
    public static FhirResult NotModified()
    {
        return new FhirResult(StatusCodes.Status304NotModified);
    }

    /// <summary>
    /// Creates a 400 Bad Request response with FHIR OperationOutcome.
    /// </summary>
    /// <param name="diagnostics">Human-readable error message</param>
    /// <param name="code">FHIR issue type code (default: "invalid")</param>
    public static IResult BadRequest(string diagnostics, string code = "invalid")
    {
        return OperationOutcome(StatusCodes.Status400BadRequest, "error", code, diagnostics);
    }

    /// <summary>
    /// Creates a 404 Not Found response with FHIR OperationOutcome.
    /// </summary>
    /// <param name="diagnostics">Human-readable error message</param>
    public static IResult NotFound(string diagnostics)
    {
        return OperationOutcome(StatusCodes.Status404NotFound, "error", "not-found", diagnostics);
    }

    /// <summary>
    /// Creates a 409 Conflict response with FHIR OperationOutcome.
    /// </summary>
    /// <param name="diagnostics">Human-readable error message</param>
    public static IResult Conflict(string diagnostics)
    {
        return OperationOutcome(StatusCodes.Status409Conflict, "error", "conflict", diagnostics);
    }

    /// <summary>
    /// Creates a 422 Unprocessable Entity response with FHIR OperationOutcome.
    /// </summary>
    /// <param name="diagnostics">Human-readable error message</param>
    public static IResult UnprocessableEntity(string diagnostics)
    {
        return OperationOutcome(StatusCodes.Status422UnprocessableEntity, "error", "processing", diagnostics);
    }

    /// <summary>
    /// Creates a custom status code response with FHIR OperationOutcome.
    /// </summary>
    /// <param name="statusCode">HTTP status code</param>
    /// <param name="severity">FHIR issue severity (error, warning, information)</param>
    /// <param name="code">FHIR issue type code</param>
    /// <param name="diagnostics">Human-readable error message</param>
    public static IResult OperationOutcome(int statusCode, string severity, string code, string diagnostics)
    {
        var outcome = new
        {
            resourceType = "OperationOutcome",
            issue = new[]
            {
                new
                {
                    severity,
                    code,
                    diagnostics
                }
            }
        };

        return Results.Json(outcome, statusCode: statusCode, contentType: KnownContentTypes.ApplicationFhirJson);
    }
}
