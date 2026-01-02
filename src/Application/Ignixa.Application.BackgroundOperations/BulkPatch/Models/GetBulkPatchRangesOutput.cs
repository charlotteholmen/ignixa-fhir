// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.BulkPatch.Models;

/// <summary>
/// Represents a range of resources to process in a bulk patch operation.
/// Resources are partitioned by type and surrogate ID for parallel processing.
/// </summary>
/// <param name="ResourceType">FHIR resource type for this range.</param>
/// <param name="StartSurrogateId">Starting surrogate ID (inclusive).</param>
/// <param name="EndSurrogateId">Ending surrogate ID (inclusive).</param>
/// <param name="EstimatedCount">Estimated number of resources in this range.</param>
public record BulkPatchRange(
    string ResourceType,
    long StartSurrogateId,
    long EndSurrogateId,
    int EstimatedCount);

/// <summary>
/// Output from the GetBulkPatchRanges activity.
/// Contains the partitioned ranges for parallel worker processing.
/// </summary>
/// <param name="Ranges">List of resource ranges to process.</param>
/// <param name="TotalEstimatedResources">Total estimated resources across all ranges.</param>
public record GetBulkPatchRangesOutput(
    IReadOnlyList<BulkPatchRange> Ranges,
    int TotalEstimatedResources);
