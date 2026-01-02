// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Domain.Exceptions;

public class JobNotFoundException : InvalidOperationException
{
    public string JobId { get; }
    public string JobType { get; }
    public int TenantId { get; }

    public JobNotFoundException(string jobType, string jobId, int tenantId)
        : base($"{jobType} job '{jobId}' not found for tenant {tenantId}")
    {
        JobType = jobType;
        JobId = jobId;
        TenantId = tenantId;
    }
}
