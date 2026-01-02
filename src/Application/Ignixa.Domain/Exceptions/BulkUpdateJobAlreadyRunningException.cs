// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Exceptions;

public class BulkUpdateJobAlreadyRunningException : InvalidOperationException
{
    public string ExistingJobId { get; }
    public int TenantId { get; }

    public BulkUpdateJobAlreadyRunningException(int tenantId, string existingJobId)
        : base($"A bulk update job is already running for tenant {tenantId}. Only one bulk update job can run at a time. Job ID: {existingJobId}")
    {
        TenantId = tenantId;
        ExistingJobId = existingJobId;
    }
}
