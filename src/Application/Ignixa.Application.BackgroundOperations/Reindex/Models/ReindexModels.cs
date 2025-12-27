// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Application.BackgroundOperations.Reindex.Models;

/// <summary>
/// Represents a SearchParameter that needs to be reindexed.
/// Contains the metadata needed to index resources for this parameter.
/// </summary>
public record ReindexSearchParam(
    /// <summary>
    /// Canonical URL of the SearchParameter (e.g., "http://hl7.org/fhir/SearchParameter/Patient-birthdate").
    /// </summary>
    string Canonical,

    /// <summary>
    /// Code identifying the search parameter (e.g., "birthdate").
    /// </summary>
    string Code,

    /// <summary>
    /// Internal database ID for the SearchParameter.
    /// </summary>
    int SearchParamId,

    /// <summary>
    /// Transaction ID at which this SearchParameter becomes active.
    /// Resources with transaction ID >= this value are already indexed for this parameter.
    /// Resources with transaction ID < this value need reindexing.
    /// </summary>
    long ActivationTransactionId);

/// <summary>
/// Orchestration input for reindexing SearchParameters.
/// Initiates the entire reindex job for a single resource type.
/// </summary>
public record ReindexOrchestrationInput(
    /// <summary>
    /// Unique job identifier.
    /// </summary>
    string JobId,

    /// <summary>
    /// Tenant that owns the resources to be reindexed.
    /// </summary>
    int TenantId,

    /// <summary>
    /// FHIR resource type to reindex (e.g., "Patient", "Observation").
    /// </summary>
    string ResourceType,

    /// <summary>
    /// List of SearchParameters to index for this resource type.
    /// </summary>
    IReadOnlyList<ReindexSearchParam> SearchParameters,

    /// <summary>
    /// Optional: Number of surrogate ID ranges for parallel processing.
    /// Default is 8. Valid range: 1-16.
    /// More ranges = more parallelism but higher DurableTask overhead.
    /// </summary>
    int NumberOfRangesPerType = 8);

/// <summary>
/// Orchestration output for reindexing.
/// Contains results of the entire reindex job.
/// </summary>
public record ReindexOrchestrationOutput(
    /// <summary>
    /// True if the reindex job completed successfully.
    /// </summary>
    bool Success,

    /// <summary>
    /// Total number of resources that were reindexed.
    /// </summary>
    long TotalResourcesReindexed,

    /// <summary>
    /// Results from each worker activity (null if job failed during initialization).
    /// </summary>
    IReadOnlyList<ReindexWorkerOutput>? WorkerResults,

    /// <summary>
    /// Error message if the job failed (null if successful).
    /// </summary>
    string? ErrorMessage,

    /// <summary>
    /// Phase where the failure occurred (null if successful).
    /// Possible values: "Initialization", "WorkerExecution", "Aggregation", "Orchestration".
    /// </summary>
    string? FailurePhase);

/// <summary>
/// Input for the reindex worker activity.
/// Represents a single partition (resource type + surrogate ID range) to be processed.
/// </summary>
public record ReindexWorkerInput(
    /// <summary>
    /// Unique identifier for the reindex job (used for tracking/logging).
    /// </summary>
    string JobId,

    /// <summary>
    /// Tenant ID that owns the resources.
    /// </summary>
    int TenantId,

    /// <summary>
    /// FHIR resource type (e.g., "Patient", "Observation").
    /// </summary>
    string ResourceType,

    /// <summary>
    /// Start of the surrogate ID range (inclusive).
    /// </summary>
    long StartSurrogateId,

    /// <summary>
    /// End of the surrogate ID range (inclusive).
    /// </summary>
    long EndSurrogateId,

    /// <summary>
    /// List of SearchParameters to index for resources in this range.
    /// </summary>
    IReadOnlyList<ReindexSearchParam> SearchParameters);

/// <summary>
/// Output from the reindex worker activity.
/// Contains results of processing a single partition.
/// </summary>
public record ReindexWorkerOutput(
    /// <summary>
    /// FHIR resource type that was reindexed.
    /// </summary>
    string ResourceType,

    /// <summary>
    /// Start of the surrogate ID range that was processed.
    /// </summary>
    long StartSurrogateId,

    /// <summary>
    /// End of the surrogate ID range that was processed.
    /// </summary>
    long EndSurrogateId,

    /// <summary>
    /// Total number of resources processed in this range.
    /// </summary>
    long ResourcesProcessed,

    /// <summary>
    /// Total number of search index entries created.
    /// </summary>
    long IndexEntriesCreated);

/// <summary>
/// Input for getting reindex ranges activity.
/// Determines how to partition a resource type for parallel reindexing.
/// </summary>
public record GetReindexRangesInput(
    /// <summary>
    /// Tenant ID that owns the resources.
    /// </summary>
    int TenantId,

    /// <summary>
    /// FHIR resource type to partition.
    /// </summary>
    string ResourceType,

    /// <summary>
    /// Number of ranges to create (valid: 1-16).
    /// </summary>
    int NumberOfRanges);

/// <summary>
/// Output from getting reindex ranges activity.
/// Contains the surrogate ID ranges for parallel processing.
/// </summary>
public record GetReindexRangesOutput(
    /// <summary>
    /// FHIR resource type that was partitioned.
    /// </summary>
    string ResourceType,

    /// <summary>
    /// List of surrogate ID ranges (StartId, EndId) for parallel processing.
    /// Empty list if resource type has no resources.
    /// </summary>
    IReadOnlyList<(long StartId, long EndId)> Ranges);

/// <summary>
/// Input for emitting reindex events.
/// Used to publish SearchParameterReindexStarted/Completed/Failed events.
/// </summary>
public record EmitReindexEventsInput(
    /// <summary>
    /// Tenant ID.
    /// </summary>
    int TenantId,

    /// <summary>
    /// FHIR resource type being reindexed.
    /// </summary>
    string ResourceType,

    /// <summary>
    /// List of SearchParameters being reindexed.
    /// </summary>
    IReadOnlyList<ReindexSearchParam> SearchParameters,

    /// <summary>
    /// Unique job identifier.
    /// </summary>
    string JobId,

    /// <summary>
    /// True if emitting start events, false if emitting completion/failure events.
    /// </summary>
    bool IsStart,

    /// <summary>
    /// Total resources indexed (required for completion events).
    /// </summary>
    long? ResourcesIndexed = null,

    /// <summary>
    /// Duration of the reindex job (required for completion events).
    /// </summary>
    TimeSpan? Duration = null,

    /// <summary>
    /// Error message (required for failure events).
    /// </summary>
    string? ErrorMessage = null);
