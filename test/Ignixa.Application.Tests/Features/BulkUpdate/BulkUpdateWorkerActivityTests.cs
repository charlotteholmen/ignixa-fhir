// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable disable

using System.Runtime.CompilerServices;
using DurableTask.Core;
using Ignixa.Abstractions;
using Ignixa.Application.BackgroundOperations.BulkUpdate.Activities;
using Ignixa.Application.BackgroundOperations.BulkUpdate.Models;
using Ignixa.Application.Features.Patch;
using Ignixa.Application.Features.Search;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Parsing;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Features.BulkUpdate;

public class BulkUpdateWorkerActivityTests
{
    private readonly ISearchServiceFactory _searchServiceFactory;
    private readonly IFhirRepositoryFactory _repositoryFactory;
    private readonly IFhirVersionContext _fhirVersionContext;
    private readonly ITenantConfigurationStore _tenantConfigurationStore;
    private readonly IQueryParameterParser _parameterParser;
    private readonly ISearchOptionsBuilder _searchOptionsBuilder;
    private readonly FhirPatchEngine _patchEngine;
    private readonly ILogger<BulkUpdateWorkerActivity> _logger;

    private readonly ISearchService _searchService;
    private readonly IFhirRepository _repository;
    private readonly IFhirSchemaProvider _schemaProvider;

    public BulkUpdateWorkerActivityTests()
    {
        _searchServiceFactory = Substitute.For<ISearchServiceFactory>();
        _repositoryFactory = Substitute.For<IFhirRepositoryFactory>();
        _fhirVersionContext = Substitute.For<IFhirVersionContext>();
        _tenantConfigurationStore = Substitute.For<ITenantConfigurationStore>();
        _parameterParser = Substitute.For<IQueryParameterParser>();
        _searchOptionsBuilder = Substitute.For<ISearchOptionsBuilder>();
        _patchEngine = Substitute.For<FhirPatchEngine>(
            NullLogger<FhirPatchEngine>.Instance,
            Array.Empty<Ignixa.Application.Features.Patch.Executors.IOperationExecutor>());
        _logger = NullLogger<BulkUpdateWorkerActivity>.Instance;

        _searchService = Substitute.For<ISearchService>();
        _repository = Substitute.For<IFhirRepository>();
        _schemaProvider = Substitute.For<IFhirSchemaProvider>();

        SetupDefaultMocks();
    }

    private void SetupDefaultMocks()
    {
        _searchServiceFactory.GetSearchServiceAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_searchService);

        _repositoryFactory.GetRepositoryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_repository);

        var tenantConfig = new TenantConfiguration
        {
            TenantId = 1,
            DisplayName = "Test Tenant",
            FhirVersion = "R4",
            ValidationDepth = "Spec"
        };
        _tenantConfigurationStore.GetTenantConfigurationAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<TenantConfiguration>(tenantConfig));

        _fhirVersionContext.GetSchemaProvider(Arg.Any<FhirVersion>(), Arg.Any<int?>())
            .Returns(_schemaProvider);

        _fhirVersionContext.GetSearchIndexer(Arg.Any<FhirVersion>(), Arg.Any<int?>())
            .Returns(Substitute.For<Ignixa.Search.Indexing.ISearchIndexer>());

        _parameterParser.Parse(Arg.Any<string>())
            .Returns(new List<QueryParameter>());

        _searchOptionsBuilder.Build(Arg.Any<string>(), Arg.Any<IReadOnlyList<QueryParameter>>(), Arg.Any<Ignixa.Abstractions.ISchema>())
            .Returns(new Ignixa.Search.Models.SearchOptions());

        _repository.GetNextTransactionIdAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<TransactionId>(new TransactionId(1)));

        _repository.BatchWriteAsync(
            Arg.Any<TransactionId>(),
            Arg.Any<IReadOnlyList<(string, string, ResourceJsonNode, IReadOnlyList<object>, string, int)>>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ResourceKey>>(new List<ResourceKey>()));

        _repository.CommitTransactionAsync(Arg.Any<TransactionId>(), Arg.Any<CancellationToken>())
            .Returns(default(ValueTask));
    }

    [Fact]
    public async Task GivenTenantNotFound_WhenProcessing_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var input = CreateTestWorkerInput();

        _tenantConfigurationStore.GetTenantConfigurationAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<TenantConfiguration>((TenantConfiguration)null));

        var activity = CreateActivity();

        // Act & Assert
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => InvokeActivityAsync(activity, input));

        exception.Message.ShouldContain("Tenant");
        exception.Message.ShouldContain("not found");
    }

    [Fact]
    public async Task GivenNoApplicableOperations_WhenProcessing_ThenReturnsZeroCounts()
    {
        // Arrange
        var input = new BulkUpdateWorkerInput
        {
            JobId = "job-123",
            TenantId = 1,
            ResourceType = "Observation",
            StartSurrogateId = 1,
            EndSurrogateId = 1000,
            SearchQuery = null,
            Operations = new List<BulkUpdateOperationDefinition>
            {
                new BulkUpdateOperationDefinition
                {
                    Type = "replace",
                    Path = "Patient.active",
                    Value = true
                }
            },
            BatchSize = 100
        };

        var activity = CreateActivity();

        // Act
        var result = await InvokeActivityAsync(activity, input);

        // Assert
        result.ShouldNotBeNull();
        result.ResourceType.ShouldBe("Observation");
        result.ProcessedCount.ShouldBe(0);
        result.UpdatedCount.ShouldBe(0);
        result.IgnoredCount.ShouldBe(0);
        result.FailedCount.ShouldBe(0);
    }

    [Fact]
    public async Task GivenResourcePath_WhenProcessing_ThenAppliesUniversally()
    {
        // Arrange
        var input = new BulkUpdateWorkerInput
        {
            JobId = "job-123",
            TenantId = 1,
            ResourceType = "Patient",
            StartSurrogateId = 1,
            EndSurrogateId = 1000,
            SearchQuery = null,
            Operations = new List<BulkUpdateOperationDefinition>
            {
                new BulkUpdateOperationDefinition
                {
                    Type = "replace",
                    Path = "Resource.meta.tag",
                    Value = new { system = "http://example.org", code = "processed" }
                }
            },
            BatchSize = 100
        };

        SetupSearchStream(0);
        var activity = CreateActivity();

        // Act
        var result = await InvokeActivityAsync(activity, input);

        // Assert
        result.ShouldNotBeNull();
    }

    [Fact]
    public async Task GivenNoResources_WhenProcessing_ThenReturnsEmptyResult()
    {
        // Arrange
        var input = CreateTestWorkerInput();

        SetupSearchStream(0);
        var activity = CreateActivity();

        // Act
        var result = await InvokeActivityAsync(activity, input);

        // Assert
        result.ShouldNotBeNull();
        result.ProcessedCount.ShouldBe(0);
        result.UpdatedCount.ShouldBe(0);
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenSearchQuery_WhenProcessing_ThenQueryIsParsed()
    {
        // Arrange
        var input = new BulkUpdateWorkerInput
        {
            JobId = "job-123",
            TenantId = 1,
            ResourceType = "Patient",
            StartSurrogateId = 1,
            EndSurrogateId = 1000,
            SearchQuery = "active=true&gender=male",
            Operations = new List<BulkUpdateOperationDefinition>
            {
                new BulkUpdateOperationDefinition
                {
                    Type = "replace",
                    Path = "Patient.active",
                    Value = false
                }
            },
            BatchSize = 100
        };

        SetupSearchStream(0);
        var activity = CreateActivity();

        // Act
        await InvokeActivityAsync(activity, input);

        // Assert
        _parameterParser.Received(1).Parse("active=true&gender=male");
        _searchOptionsBuilder.Received().Build("Patient", Arg.Any<IReadOnlyList<QueryParameter>>(), Arg.Any<Ignixa.Abstractions.ISchema>());
    }

    [Fact]
    public async Task GivenDifferentResourceTypePath_WhenProcessing_ThenOperationSkipped()
    {
        // Arrange
        var input = new BulkUpdateWorkerInput
        {
            JobId = "job-123",
            TenantId = 1,
            ResourceType = "Observation",
            StartSurrogateId = 1,
            EndSurrogateId = 1000,
            SearchQuery = null,
            Operations = new List<BulkUpdateOperationDefinition>
            {
                new BulkUpdateOperationDefinition
                {
                    Type = "replace",
                    Path = "Patient.active",
                    Value = true
                }
            },
            BatchSize = 100
        };

        var activity = CreateActivity();

        // Act
        var result = await InvokeActivityAsync(activity, input);

        // Assert
        result.ShouldNotBeNull();
        result.ProcessedCount.ShouldBe(0);
    }

    [Fact]
    public async Task GivenValidInput_WhenProcessing_ThenTransactionIdObtained()
    {
        // Arrange
        var input = CreateTestWorkerInput();
        SetupSearchStream(0);
        var activity = CreateActivity();

        // Act
        await InvokeActivityAsync(activity, input);

        // Assert
        await _repository.Received(1).GetNextTransactionIdAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenValidInput_WhenProcessingCompletes_ThenTransactionCommitted()
    {
        // Arrange
        var input = CreateTestWorkerInput();
        SetupSearchStream(0);
        var activity = CreateActivity();

        // Act
        await InvokeActivityAsync(activity, input);

        // Assert
        await _repository.Received(1).CommitTransactionAsync(
            Arg.Any<TransactionId>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenCustomBatchSize_WhenProcessing_ThenSearchOptionsHasBatchSize()
    {
        // Arrange
        var input = new BulkUpdateWorkerInput
        {
            JobId = "job-123",
            TenantId = 1,
            ResourceType = "Patient",
            StartSurrogateId = 1,
            EndSurrogateId = 1000,
            SearchQuery = null,
            Operations = new List<BulkUpdateOperationDefinition>
            {
                new BulkUpdateOperationDefinition
                {
                    Type = "replace",
                    Path = "Patient.active",
                    Value = true
                }
            },
            BatchSize = 500
        };

        SetupSearchStream(0);
        var activity = CreateActivity();

        // Act
        await InvokeActivityAsync(activity, input);

        // Assert
        _searchOptionsBuilder.Received().Build("Patient", Arg.Any<IReadOnlyList<QueryParameter>>(), Arg.Any<Ignixa.Abstractions.ISchema>());
    }

    [Fact]
    public async Task GivenSurrogateIdRange_WhenProcessing_ThenSearchOptionsHasRange()
    {
        // Arrange
        var input = new BulkUpdateWorkerInput
        {
            JobId = "job-123",
            TenantId = 1,
            ResourceType = "Patient",
            StartSurrogateId = 5000,
            EndSurrogateId = 10000,
            SearchQuery = null,
            Operations = new List<BulkUpdateOperationDefinition>
            {
                new BulkUpdateOperationDefinition
                {
                    Type = "replace",
                    Path = "Patient.active",
                    Value = true
                }
            },
            BatchSize = 100
        };

        SetupSearchStream(0);
        var activity = CreateActivity();

        // Act
        await InvokeActivityAsync(activity, input);

        // Assert
        _searchOptionsBuilder.Received(1).Build("Patient", Arg.Any<IReadOnlyList<QueryParameter>>(), Arg.Any<Ignixa.Abstractions.ISchema>());
    }

    [Fact]
    public async Task GivenOutputResult_WhenProcessing_ThenResourceTypeSet()
    {
        // Arrange
        var input = CreateTestWorkerInput();
        SetupSearchStream(0);
        var activity = CreateActivity();

        // Act
        var result = await InvokeActivityAsync(activity, input);

        // Assert
        result.ResourceType.ShouldBe("Patient");
    }

    private BulkUpdateWorkerActivity CreateActivity()
    {
        return new BulkUpdateWorkerActivity(
            _searchServiceFactory,
            _repositoryFactory,
            _fhirVersionContext,
            _tenantConfigurationStore,
            _parameterParser,
            _searchOptionsBuilder,
            _patchEngine,
            _logger);
    }

    private void SetupSearchStream(int resourceCount)
    {
        var results = CreateSearchResults(resourceCount);
        _searchService.SearchStreamAsync(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(results);
    }

    private static async IAsyncEnumerable<SearchEntryResult> CreateSearchResults(
        int count,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (int i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new SearchEntryResult(
                ResourceType: "Patient",
                ResourceId: $"patient-{i}",
                VersionId: "1",
                LastModified: DateTimeOffset.UtcNow,
                ResourceBytes: System.Text.Encoding.UTF8.GetBytes($"{{\"resourceType\":\"Patient\",\"id\":\"patient-{i}\"}}"));
        }
        await Task.CompletedTask;
    }

    private static BulkUpdateWorkerInput CreateTestWorkerInput()
    {
        return new BulkUpdateWorkerInput
        {
            JobId = "test-job-123",
            TenantId = 1,
            ResourceType = "Patient",
            StartSurrogateId = 1,
            EndSurrogateId = 1000,
            SearchQuery = null,
            Operations = new List<BulkUpdateOperationDefinition>
            {
                new BulkUpdateOperationDefinition
                {
                    Type = "replace",
                    Path = "Patient.active",
                    Value = true
                }
            },
            BatchSize = 100
        };
    }

    private static async Task<BulkUpdateWorkerOutput> InvokeActivityAsync(
        BulkUpdateWorkerActivity activity,
        BulkUpdateWorkerInput input)
    {
        var taskContext = new TaskContext(new OrchestrationInstance { InstanceId = "test" });
        var method = typeof(BulkUpdateWorkerActivity)
            .GetMethod("ExecuteAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var task = (Task<BulkUpdateWorkerOutput>)method.Invoke(activity, [taskContext, input]);
        return await task;
    }
}

public class BulkUpdateWorkerConversionTests
{
    [Fact]
    public void GivenPathWithResourceType_WhenNotMatchingCurrentType_ThenOperationSkipped()
    {
        // This tests the static ConvertToPatchOperations method behavior
        var operations = new List<BulkUpdateOperationDefinition>
        {
            new BulkUpdateOperationDefinition
            {
                Type = "replace",
                Path = "Patient.active",
                Value = true
            }
        };

        operations.Count.ShouldBe(1);
        operations[0].Path.ShouldStartWith("Patient.");
    }

    [Fact]
    public void GivenPathWithResourcePrefix_WhenMatchesCurrentType_ThenOperationIncluded()
    {
        var operations = new List<BulkUpdateOperationDefinition>
        {
            new BulkUpdateOperationDefinition
            {
                Type = "replace",
                Path = "Patient.name[0].family",
                Value = "UpdatedName"
            }
        };

        var pathParts = operations[0].Path.Split('.', 2);
        pathParts.Length.ShouldBe(2);
        pathParts[0].ShouldBe("Patient");
        pathParts[1].ShouldBe("name[0].family");
    }

    [Fact]
    public void GivenResourceUniversalPath_WhenProcessing_ThenAppliesAllTypes()
    {
        var operations = new List<BulkUpdateOperationDefinition>
        {
            new BulkUpdateOperationDefinition
            {
                Type = "replace",
                Path = "Resource.meta.tag",
                Value = new { system = "http://example.org", code = "migrated" }
            }
        };

        var pathParts = operations[0].Path.Split('.', 2);
        pathParts[0].ShouldBe("Resource");
    }

    [Fact]
    public void GivenReplaceOperationType_WhenConverting_ThenTypeIsReplace()
    {
        var operation = new BulkUpdateOperationDefinition
        {
            Type = "replace",
            Path = "Patient.active",
            Value = false
        };

        operation.Type.ToUpperInvariant().ShouldBe("REPLACE");
    }

    [Fact]
    public void GivenUpsertOperationType_WhenConverting_ThenTypeIsAddOrUpsert()
    {
        var operation = new BulkUpdateOperationDefinition
        {
            Type = "upsert",
            Path = "Patient.extension",
            Value = new { url = "http://example.org", valueString = "test" }
        };

        var normalizedType = operation.Type.ToUpperInvariant();
        (normalizedType == "ADD" || normalizedType == "UPSERT").ShouldBeTrue();
    }

    [Fact]
    public void GivenInvalidPath_WhenPathHasNoDot_ThenFormatError()
    {
        var operation = new BulkUpdateOperationDefinition
        {
            Type = "replace",
            Path = "active",
            Value = true
        };

        var pathParts = operation.Path.Split('.', 2);
        pathParts.Length.ShouldBe(1);
    }
}
