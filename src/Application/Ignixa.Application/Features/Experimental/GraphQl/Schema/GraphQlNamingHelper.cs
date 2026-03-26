// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using Ignixa.Abstractions;

namespace Ignixa.Application.Features.Experimental.GraphQl.Schema;

internal static class GraphQlNamingHelper
{
    [SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "Schema name convention requires lowercase FHIR version identifier")]
    internal static string GetSchemaName(FhirVersion version)
        => $"fhir-{version.ToString().ToLowerInvariant()}";
}
