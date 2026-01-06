/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for LazyResolvedElement - lazy resolution architecture for resolve() function.
 */

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Evaluation.Functions;
using Shouldly;

namespace Ignixa.FhirPath.Tests.Evaluation;

public class LazyResolvedElementTests
{
    [Fact]
    public void GivenRelativeReference_WhenGettingInstanceType_ThenParsesResourceTypeWithoutResolver()
    {
        var element = new LazyResolvedElement("Patient/123");

        element.InstanceType.ShouldBe("Patient");
    }

    [Fact]
    public void GivenAbsoluteReference_WhenGettingInstanceType_ThenParsesResourceTypeWithoutResolver()
    {
        var element = new LazyResolvedElement("http://example.org/fhir/Patient/123");

        element.InstanceType.ShouldBe("Patient");
    }

    [Fact]
    public void GivenRelativeReference_WhenAccessingIdChild_ThenReturnsIdWithoutResolver()
    {
        var element = new LazyResolvedElement("Patient/123");

        var children = element.Children("id");

        children.ShouldNotBeEmpty();
        children.Count.ShouldBe(1);
        children[0].Value.ShouldBe("123");
        children[0].InstanceType.ShouldBe("id");
    }

    [Fact]
    public void GivenAbsoluteReference_WhenAccessingIdChild_ThenReturnsIdWithoutResolver()
    {
        var element = new LazyResolvedElement("http://example.org/fhir/Patient/456");

        var children = element.Children("id");

        children.ShouldNotBeEmpty();
        children.Count.ShouldBe(1);
        children[0].Value.ShouldBe("456");
    }

    [Fact]
    public void GivenRelativeReference_WhenAccessingResourceTypeChild_ThenReturnsResourceTypeWithoutResolver()
    {
        var element = new LazyResolvedElement("Observation/789");

        var children = element.Children("resourceType");

        children.ShouldNotBeEmpty();
        children.Count.ShouldBe(1);
        children[0].Value.ShouldBe("Observation");
        children[0].InstanceType.ShouldBe("code");
    }

    [Fact]
    public void GivenReferenceWithHistoryVersion_WhenAccessingId_ThenReturnsIdWithoutVersion()
    {
        var element = new LazyResolvedElement("Patient/123/_history/2");

        var children = element.Children("id");

        children.ShouldNotBeEmpty();
        children[0].Value.ShouldBe("123");
    }

    [Fact]
    public void GivenNoResolver_WhenAccessingOtherProperty_ThenReturnsEmpty()
    {
        var element = new LazyResolvedElement("Patient/123");

        var children = element.Children("name");

        children.ShouldBeEmpty();
    }

    [Fact]
    public void GivenResolver_WhenAccessingOtherProperty_ThenInvokesResolver()
    {
        var resolverInvoked = false;
        IElement? mockPatient = new MockElement("Patient", "123");

        var element = new LazyResolvedElement("Patient/123", reference =>
        {
            resolverInvoked = true;
            reference.ShouldBe("Patient/123");
            return mockPatient;
        });

        var children = element.Children("name");

        resolverInvoked.ShouldBeTrue();
    }

    [Fact]
    public void GivenResolver_WhenAccessingMultipleProperties_ThenInvokesResolverOnlyOnce()
    {
        var resolverCallCount = 0;
        IElement mockPatient = new MockElement("Patient", "123");

        var element = new LazyResolvedElement("Patient/123", reference =>
        {
            resolverCallCount++;
            return mockPatient;
        });

        var name1 = element.Children("name");
        var name2 = element.Children("name");
        var birthDate = element.Children("birthDate");

        resolverCallCount.ShouldBe(1);
    }

    [Fact]
    public void GivenResolver_WhenAccessingIdBeforeOtherProperty_ThenDoesNotInvokeResolverForId()
    {
        var resolverInvoked = false;
        IElement mockPatient = new MockElement("Patient", "123");

        var element = new LazyResolvedElement("Patient/123", reference =>
        {
            resolverInvoked = true;
            return mockPatient;
        });

        var id = element.Children("id");
        id.ShouldNotBeEmpty();
        id[0].Value.ShouldBe("123");
        resolverInvoked.ShouldBeFalse();

        var name = element.Children("name");
        resolverInvoked.ShouldBeTrue();
    }

    [Fact]
    public void GivenInvalidReference_WhenAccessingId_ThenReturnsEmpty()
    {
        var element = new LazyResolvedElement("not-a-valid-reference");

        var children = element.Children("id");

        children.ShouldBeEmpty();
    }

    [Fact]
    public void GivenInvalidReference_WhenGettingInstanceType_ThenReturnsResourceFallback()
    {
        var element = new LazyResolvedElement("not-a-valid-reference");

        element.InstanceType.ShouldBe("Resource");
    }

    [Fact]
    public void GivenResolver_WhenAccessingAllChildren_ThenInvokesResolver()
    {
        var resolverInvoked = false;
        IElement mockPatient = new MockElement("Patient", "123");

        var element = new LazyResolvedElement("Patient/123", reference =>
        {
            resolverInvoked = true;
            return mockPatient;
        });

        var children = element.Children(null);

        resolverInvoked.ShouldBeTrue();
    }

    [Fact]
    public void GivenNoResolver_WhenAccessingAllChildren_ThenReturnsEmpty()
    {
        var element = new LazyResolvedElement("Patient/123");

        var children = element.Children(null);

        children.ShouldBeEmpty();
    }

    [Fact]
    public void GivenResolverReturnsNull_WhenAccessingProperty_ThenReturnsEmpty()
    {
        var element = new LazyResolvedElement("Patient/123", reference => null);

        var children = element.Children("name");

        children.ShouldBeEmpty();
    }

    [Fact]
    public void GivenLocationProperty_WhenAccessed_ThenReturnsOriginalReference()
    {
        var element = new LazyResolvedElement("Patient/123");

        element.Location.ShouldBe("Patient/123");
    }

    [Fact]
    public void GivenValueProperty_WhenAccessed_ThenReturnsNull()
    {
        var element = new LazyResolvedElement("Patient/123");

        element.Value.ShouldBeNull();
    }

    [Fact]
    public void GivenTypeProperty_WhenAccessed_ThenReturnsNull()
    {
        var element = new LazyResolvedElement("Patient/123");

        element.Type.ShouldBeNull();
    }

    [Fact]
    public void GivenNameProperty_WhenAccessed_ThenReturnsEmptyString()
    {
        var element = new LazyResolvedElement("Patient/123");

        element.Name.ShouldBe(string.Empty);
    }

    [Fact]
    public void GivenMetaMethod_WhenCalled_ThenReturnsNull()
    {
        var element = new LazyResolvedElement("Patient/123");

        element.Meta<object>().ShouldBeNull();
    }

    private class MockElement : IElement
    {
        public MockElement(string instanceType, string id)
        {
            InstanceType = instanceType;
            Name = string.Empty;
            Location = $"{instanceType}/{id}";
        }

        public string Name { get; }
        public object? Value => null;
        public string InstanceType { get; }
        public string Location { get; }
        public IType? Type => null;
        public IReadOnlyList<IElement> Children(string? name = null) => [];
        public T? Meta<T>() where T : class => null;
    }
}
