// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Microsoft.Extensions.Logging;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Conformance.Events.Events;

namespace Ignixa.Application.BackgroundOperations.Reindex.Activities;

/// <summary>
/// DurableTask activity that emits SearchParameterReindex events to the event store.
/// Emits Started, Completed, or Failed events depending on the reindex phase.
/// Events are observability records - failures here do not fail the reindex operation.
/// </summary>
public class EmitReindexEventsActivity(
    ISourceEventStore eventStore,
    ILogger<EmitReindexEventsActivity> logger) : AsyncTaskActivity<EmitReindexEventsInput, bool>
{
    protected override async Task<bool> ExecuteAsync(
        TaskContext context,
        EmitReindexEventsInput input)
    {
        try
        {
            var events = new List<NewSourceEvent>();

            foreach (var sp in input.SearchParameters)
            {
                NewSourceEvent evt;

                if (input.IsStart)
                {
                    evt = new NewSourceEvent(
                        StreamId: $"searchparam:{sp.Canonical}",
                        EventType: nameof(SearchParameterReindexStarted),
                        Data: new SearchParameterReindexStarted(
                            Canonical: sp.Canonical,
                            Code: sp.Code,
                            ResourceType: input.ResourceType,
                            JobId: input.JobId,
                            AffectedResourceTypes: [input.ResourceType]));

                    logger.LogInformation(
                        "Emitting SearchParameterReindexStarted: SP={Canonical}, ResourceType={ResourceType}, JobId={JobId}",
                        sp.Canonical,
                        input.ResourceType,
                        input.JobId);
                }
                else if (!string.IsNullOrEmpty(input.ErrorMessage))
                {
                    evt = new NewSourceEvent(
                        StreamId: $"searchparam:{sp.Canonical}",
                        EventType: nameof(SearchParameterReindexFailed),
                        Data: new SearchParameterReindexFailed(
                            Canonical: sp.Canonical,
                            Code: sp.Code,
                            ResourceType: input.ResourceType,
                            JobId: input.JobId,
                            ErrorMessage: input.ErrorMessage));

                    logger.LogWarning(
                        "Emitting SearchParameterReindexFailed: SP={Canonical}, ResourceType={ResourceType}, Error={Error}",
                        sp.Canonical,
                        input.ResourceType,
                        input.ErrorMessage);
                }
                else
                {
                    evt = new NewSourceEvent(
                        StreamId: $"searchparam:{sp.Canonical}",
                        EventType: nameof(SearchParameterReindexCompleted),
                        Data: new SearchParameterReindexCompleted(
                            Canonical: sp.Canonical,
                            Code: sp.Code,
                            ResourceType: input.ResourceType,
                            JobId: input.JobId,
                            ResourcesIndexed: input.ResourcesIndexed ?? 0,
                            Duration: input.Duration ?? TimeSpan.Zero));

                    logger.LogInformation(
                        "Emitting SearchParameterReindexCompleted: SP={Canonical}, ResourceType={ResourceType}, Resources={Count}, Duration={Duration}",
                        sp.Canonical,
                        input.ResourceType,
                        input.ResourcesIndexed,
                        input.Duration);
                }

                events.Add(evt);
            }

            if (events.Count > 0)
            {
                await eventStore.AppendAsync(events, CancellationToken.None);

                logger.LogInformation(
                    "Appended {Count} reindex events to event store for JobId={JobId}",
                    events.Count,
                    input.JobId);
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to emit reindex events for JobId={JobId}, ResourceType={ResourceType}",
                input.JobId,
                input.ResourceType);

            return false;
        }
    }
}
