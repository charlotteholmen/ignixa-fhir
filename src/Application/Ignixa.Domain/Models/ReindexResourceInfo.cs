// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Models;

/// <summary>
/// Resource information for reindexing operations.
/// Contains the resource data plus metadata needed to determine which search parameters need reindexing.
/// </summary>
public record ReindexResourceInfo(
    /// <summary>
    /// Resource surrogate ID (unique identifier for the resource row).
    /// </summary>
    long SurrogateId,

    /// <summary>
    /// Transaction ID when this resource was last written.
    /// Used to determine which SearchParameters need reindexing:
    /// - Resources with TransactionId less than or equal to SP.ActivationTransactionId need reindexing for that SP
    /// - Resources with TransactionId greater than SP.ActivationTransactionId are already indexed for that SP
    /// </summary>
    long TransactionId,

    /// <summary>
    /// FHIR resource type (e.g., "Patient", "Observation").
    /// </summary>
    string ResourceType,

    /// <summary>
    /// Resource logical ID (e.g., "123", "abc-def").
    /// </summary>
    string ResourceId,

    /// <summary>
    /// Version ID of the resource.
    /// </summary>
    string VersionId,

    /// <summary>
    /// Raw JSON bytes of the resource.
    /// Used to parse and extract search index values.
    /// </summary>
    ReadOnlyMemory<byte> ResourceBytes);
