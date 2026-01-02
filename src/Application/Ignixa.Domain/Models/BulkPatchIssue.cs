// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Models;

/// <summary>
/// Represents an issue encountered during bulk patch operation.
/// </summary>
/// <param name="ResourceType">FHIR resource type.</param>
/// <param name="ResourceId">FHIR resource ID.</param>
/// <param name="ErrorMessage">Error message describing the failure.</param>
public record BulkPatchIssue(
    string ResourceType,
    string ResourceId,
    string ErrorMessage);
