// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Validation.Schema;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.PackageManagement.Tests.Infrastructure;

/// <summary>
/// Tests for <see cref="StructureDefinitionTypeBuilder"/>.
/// </summary>
public class StructureDefinitionTypeBuilderTests
{
    private readonly StructureDefinitionTypeBuilder _builder;

    public StructureDefinitionTypeBuilderTests()
    {
        var logger = Substitute.For<ILogger>();
        _builder = new StructureDefinitionTypeBuilder(logger);
    }

    #region Minimal StructureDefinition

    /// <summary>
    /// Minimal Patient StructureDefinition with a few elements.
    /// </summary>
    private const string MinimalPatientJson = """
        {
          "resourceType": "StructureDefinition",
          "id": "Patient",
          "url": "http://hl7.org/fhir/StructureDefinition/Patient",
          "name": "Patient",
          "kind": "resource",
          "abstract": false,
          "type": "Patient",
          "snapshot": {
            "element": [
              {
                "id": "Patient",
                "path": "Patient",
                "min": 0,
                "max": "*",
                "type": [{ "code": "DomainResource" }]
              },
              {
                "id": "Patient.id",
                "path": "Patient.id",
                "min": 0,
                "max": "1",
                "type": [{ "code": "id" }],
                "isSummary": true
              },
              {
                "id": "Patient.active",
                "path": "Patient.active",
                "min": 0,
                "max": "1",
                "type": [{ "code": "boolean" }],
                "isModifier": true,
                "isSummary": true
              },
              {
                "id": "Patient.name",
                "path": "Patient.name",
                "min": 0,
                "max": "*",
                "type": [{ "code": "HumanName" }],
                "isSummary": true
              },
              {
                "id": "Patient.name.family",
                "path": "Patient.name.family",
                "min": 0,
                "max": "1",
                "type": [{ "code": "string" }],
                "isSummary": true
              },
              {
                "id": "Patient.name.given",
                "path": "Patient.name.given",
                "min": 0,
                "max": "*",
                "type": [{ "code": "string" }],
                "isSummary": true
              },
              {
                "id": "Patient.gender",
                "path": "Patient.gender",
                "min": 0,
                "max": "1",
                "type": [{ "code": "code" }],
                "isSummary": true,
                "binding": {
                  "strength": "required",
                  "valueSet": "http://hl7.org/fhir/ValueSet/administrative-gender",
                  "description": "The gender of a person used for administrative purposes."
                }
              },
              {
                "id": "Patient.birthDate",
                "path": "Patient.birthDate",
                "min": 0,
                "max": "1",
                "type": [{ "code": "date" }],
                "isSummary": true
              },
              {
                "id": "Patient.generalPractitioner",
                "path": "Patient.generalPractitioner",
                "min": 0,
                "max": "*",
                "type": [{
                  "code": "Reference",
                  "targetProfile": [
                    "http://hl7.org/fhir/StructureDefinition/Organization",
                    "http://hl7.org/fhir/StructureDefinition/Practitioner"
                  ]
                }]
              }
            ]
          }
        }
        """;

    [Fact]
    public void Build_MinimalPatient_ReturnsRootWithCorrectInfo()
    {
        // Act
        var result = _builder.Build(MinimalPatientJson);

        // Assert
        result.ShouldNotBeNull();
        result.Info.Name.ShouldBe("Patient");
        result.Info.IsResource.ShouldBeTrue();
        result.Info.IsAbstract.ShouldBeFalse();
        result.Info.IsPrimitive.ShouldBeFalse();
    }

    [Fact]
    public void Build_MinimalPatient_HasCorrectChildCount()
    {
        // Act
        var result = _builder.Build(MinimalPatientJson);

        // Assert - Direct children of Patient (id, active, name, gender, birthDate, generalPractitioner)
        result.ShouldNotBeNull();
        result.Children.Count.ShouldBe(6);
    }

    [Fact]
    public void Build_MinimalPatient_NameHasGrandchildren()
    {
        // Act
        var result = _builder.Build(MinimalPatientJson);

        // Assert - Patient.name should have 2 children: family, given
        result.ShouldNotBeNull();
        var nameElement = result.Children.First(c => c.Info.Name == "name");
        nameElement.Children.Count.ShouldBe(2);

        var familyElement = nameElement.Children.First(c => c.Info.Name == "family");
        familyElement.Info.Primitive.ShouldBe(FhirPrimitive.String);
        familyElement.IsCollection.ShouldBeFalse();

        var givenElement = nameElement.Children.First(c => c.Info.Name == "given");
        givenElement.Info.Primitive.ShouldBe(FhirPrimitive.String);
        givenElement.IsCollection.ShouldBeTrue();
    }

    [Fact]
    public void Build_MinimalPatient_GenderHasBinding()
    {
        // Act
        var result = _builder.Build(MinimalPatientJson);

        // Assert
        result.ShouldNotBeNull();
        var genderElement = result.Children.OfType<ITypeExtended>().First(c => c.Info.Name == "gender");
        genderElement.Binding.ShouldNotBeNull();
        genderElement.Binding!.Strength.ShouldBe("required");
        genderElement.Binding.ValueSet.ShouldBe("http://hl7.org/fhir/ValueSet/administrative-gender");
        genderElement.Info.Primitive.ShouldBe(FhirPrimitive.Code);
    }

    [Fact]
    public void Build_MinimalPatient_ActiveIsModifier()
    {
        // Act
        var result = _builder.Build(MinimalPatientJson);

        // Assert
        result.ShouldNotBeNull();
        var activeElement = result.Children.First(c => c.Info.Name == "active");
        activeElement.Info.IsModifier.ShouldBeTrue();
        activeElement.InSummary.ShouldBeTrue();
        activeElement.Info.Primitive.ShouldBe(FhirPrimitive.Boolean);
    }

    [Fact]
    public void Build_MinimalPatient_GeneralPractitionerHasReferenceTargets()
    {
        // Act
        var result = _builder.Build(MinimalPatientJson);

        // Assert
        result.ShouldNotBeNull();
        var gpElement = result.Children.OfType<ITypeExtended>().First(c => c.Info.Name == "generalPractitioner");
        gpElement.IsCollection.ShouldBeTrue();
        gpElement.ReferenceTargets.ShouldContain("Organization");
        gpElement.ReferenceTargets.ShouldContain("Practitioner");
    }

    [Fact]
    public void Build_MinimalPatient_CardinaltiesCorrect()
    {
        // Act
        var result = _builder.Build(MinimalPatientJson);

        // Assert
        result.ShouldNotBeNull();
        var idElement = result.Children.OfType<ITypeExtended>().First(c => c.Info.Name == "id");
        idElement.Min.ShouldBe(0);
        idElement.Max.ShouldBe("1");
        idElement.IsRequired.ShouldBeFalse();
        idElement.IsCollection.ShouldBeFalse();

        var nameElement = result.Children.OfType<ITypeExtended>().First(c => c.Info.Name == "name");
        nameElement.Min.ShouldBe(0);
        nameElement.Max.ShouldBe("*");
        nameElement.IsCollection.ShouldBeTrue();
    }

    #endregion

    #region Choice elements, constraints, fixed/pattern values

    /// <summary>
    /// Observation with choice element value[x], constraints, and fixed values.
    /// </summary>
    private const string ObservationWithChoiceJson = """
        {
          "resourceType": "StructureDefinition",
          "id": "Observation",
          "name": "Observation",
          "kind": "resource",
          "abstract": false,
          "type": "Observation",
          "snapshot": {
            "element": [
              {
                "id": "Observation",
                "path": "Observation",
                "min": 0,
                "max": "*"
              },
              {
                "id": "Observation.status",
                "path": "Observation.status",
                "min": 1,
                "max": "1",
                "type": [{ "code": "code" }],
                "isSummary": true,
                "binding": {
                  "strength": "required",
                  "valueSet": "http://hl7.org/fhir/ValueSet/observation-status"
                },
                "constraint": [
                  {
                    "key": "obs-1",
                    "severity": "error",
                    "human": "Status must be a valid code",
                    "expression": "status.exists()"
                  }
                ]
              },
              {
                "id": "Observation.value[x]",
                "path": "Observation.value[x]",
                "min": 0,
                "max": "1",
                "type": [
                  { "code": "Quantity" },
                  { "code": "CodeableConcept" },
                  { "code": "string" },
                  { "code": "boolean" },
                  { "code": "integer" },
                  { "code": "Range" },
                  { "code": "Ratio" },
                  { "code": "SampledData" },
                  { "code": "time" },
                  { "code": "dateTime" },
                  { "code": "Period" }
                ],
                "isSummary": true
              }
            ]
          }
        }
        """;

    [Fact]
    public void Build_Observation_ValueXIsChoiceElement()
    {
        // Act
        var result = _builder.Build(ObservationWithChoiceJson);

        // Assert
        result.ShouldNotBeNull();
        var valueElement = result.Children.OfType<ITypeExtended>().First(c => c.Info.Name == "value[x]");
        valueElement.Info.IsChoiceElement.ShouldBeTrue();
        valueElement.Types.Count.ShouldBe(11);
        valueElement.Types.Select(t => t.Code).ShouldContain("Quantity");
        valueElement.Types.Select(t => t.Code).ShouldContain("string");
        valueElement.Types.Select(t => t.Code).ShouldContain("dateTime");
    }

    [Fact]
    public void Build_Observation_StatusHasConstraint()
    {
        // Act
        var result = _builder.Build(ObservationWithChoiceJson);

        // Assert
        result.ShouldNotBeNull();
        var statusElement = result.Children.OfType<ITypeExtended>().First(c => c.Info.Name == "status");
        statusElement.Constraints.Count.ShouldBe(1);
        statusElement.Constraints[0].Key.ShouldBe("obs-1");
        statusElement.Constraints[0].Severity.ShouldBe("error");
        statusElement.Constraints[0].Expression.ShouldBe("status.exists()");
        statusElement.IsRequired.ShouldBeTrue();
        statusElement.Min.ShouldBe(1);
    }

    #endregion

    #region Profile with fixed/pattern values, slicing

    /// <summary>
    /// US Core Patient-like profile with extensions, slicing, and mustSupport.
    /// </summary>
    private const string UsCoreLikePatientJson = """
        {
          "resourceType": "StructureDefinition",
          "id": "us-core-patient",
          "url": "http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient",
          "name": "USCorePatientProfile",
          "kind": "resource",
          "abstract": false,
          "type": "Patient",
          "snapshot": {
            "element": [
              {
                "id": "Patient",
                "path": "Patient",
                "min": 0,
                "max": "*"
              },
              {
                "id": "Patient.identifier",
                "path": "Patient.identifier",
                "min": 1,
                "max": "*",
                "type": [{ "code": "Identifier" }],
                "isSummary": true,
                "slicing": {
                  "discriminator": [
                    { "type": "pattern", "path": "type" }
                  ],
                  "rules": "open",
                  "ordered": false
                }
              },
              {
                "id": "Patient.identifier.system",
                "path": "Patient.identifier.system",
                "min": 1,
                "max": "1",
                "type": [{ "code": "uri" }],
                "fixedUri": "http://hl7.org/fhir/sid/us-ssn"
              },
              {
                "id": "Patient.identifier.value",
                "path": "Patient.identifier.value",
                "min": 1,
                "max": "1",
                "type": [{ "code": "string" }]
              },
              {
                "id": "Patient.name",
                "path": "Patient.name",
                "min": 1,
                "max": "*",
                "type": [{ "code": "HumanName" }]
              },
              {
                "id": "Patient.name.family",
                "path": "Patient.name.family",
                "min": 1,
                "max": "1",
                "type": [{ "code": "string" }]
              },
              {
                "id": "Patient.gender",
                "path": "Patient.gender",
                "min": 1,
                "max": "1",
                "type": [{ "code": "code" }],
                "binding": {
                  "strength": "required",
                  "valueSet": "http://hl7.org/fhir/ValueSet/administrative-gender"
                }
              },
              {
                "id": "Patient.communication",
                "path": "Patient.communication",
                "min": 0,
                "max": "*"
              },
              {
                "id": "Patient.communication.language",
                "path": "Patient.communication.language",
                "min": 1,
                "max": "1",
                "type": [{ "code": "CodeableConcept" }],
                "binding": {
                  "strength": "extensible",
                  "valueSet": "http://hl7.org/fhir/us/core/ValueSet/simple-language"
                }
              },
              {
                "id": "Patient.link",
                "path": "Patient.link",
                "min": 0,
                "max": "*",
                "isModifier": true,
                "contentReference": "#Patient.link"
              }
            ]
          }
        }
        """;

    [Fact]
    public void Build_USCorePatient_RootIsResource()
    {
        // Act
        var result = _builder.Build(UsCoreLikePatientJson);

        // Assert
        result.ShouldNotBeNull();
        result.Info.Name.ShouldBe("Patient");
        result.Info.IsResource.ShouldBeTrue();
    }

    [Fact]
    public void Build_USCorePatient_RequiredElementsAreMarked()
    {
        // Act
        var result = _builder.Build(UsCoreLikePatientJson);

        // Assert
        result.ShouldNotBeNull();
        var identifierElement = result.Children.OfType<ITypeExtended>().First(c => c.Info.Name == "identifier");
        identifierElement.IsRequired.ShouldBeTrue();
        identifierElement.Min.ShouldBe(1);
        identifierElement.IsCollection.ShouldBeTrue();

        var genderElement = result.Children.OfType<ITypeExtended>().First(c => c.Info.Name == "gender");
        genderElement.IsRequired.ShouldBeTrue();
        genderElement.Min.ShouldBe(1);
        genderElement.Max.ShouldBe("1");
    }

    [Fact]
    public void Build_USCorePatient_FixedValueExtracted()
    {
        // Act
        var result = _builder.Build(UsCoreLikePatientJson);

        // Assert - Patient.identifier.system has fixedUri
        result.ShouldNotBeNull();
        var identifierElement = result.Children.First(c => c.Info.Name == "identifier");
        var systemElement = identifierElement.Children.OfType<ITypeExtended>().First(c => c.Info.Name == "system");
        systemElement.FixedValue.ShouldBe("http://hl7.org/fhir/sid/us-ssn");
    }

    [Fact]
    public void Build_USCorePatient_BackboneElementHasChildren()
    {
        // Act
        var result = _builder.Build(UsCoreLikePatientJson);

        // Assert - Patient.communication should have language child
        result.ShouldNotBeNull();
        var commElement = result.Children.First(c => c.Info.Name == "communication");
        commElement.Children.Count.ShouldBe(1);
        commElement.Children[0].Info.Name.ShouldBe("language");
    }

    [Fact]
    public void Build_USCorePatient_ContentReferenceExtracted()
    {
        // Act
        var result = _builder.Build(UsCoreLikePatientJson);

        // Assert - Patient.link has contentReference
        result.ShouldNotBeNull();
        var linkElement = result.Children.OfType<ITypeExtended>().First(c => c.Info.Name == "link");
        linkElement.ContentReference.ShouldBe("#Patient.link");
        linkElement.Info.IsModifier.ShouldBeTrue();
    }

    #endregion

    #region Differential fallback

    private const string DifferentialOnlyJson = """
        {
          "resourceType": "StructureDefinition",
          "id": "my-profile",
          "name": "MyProfile",
          "kind": "resource",
          "abstract": false,
          "type": "Patient",
          "differential": {
            "element": [
              {
                "id": "Patient",
                "path": "Patient"
              },
              {
                "id": "Patient.name",
                "path": "Patient.name",
                "min": 1,
                "max": "*",
                "type": [{ "code": "HumanName" }]
              }
            ]
          }
        }
        """;

    [Fact]
    public void Build_DifferentialOnly_FallsBackSuccessfully()
    {
        // Act
        var result = _builder.Build(DifferentialOnlyJson);

        // Assert
        result.ShouldNotBeNull();
        result.Info.Name.ShouldBe("Patient");
        result.Children.Count.ShouldBe(1);
        result.Children[0].Info.Name.ShouldBe("name");
    }

    #endregion

    #region Error handling

    [Fact]
    public void Build_InvalidJson_ReturnsNull()
    {
        var result = _builder.Build("not valid json");
        result.ShouldBeNull();
    }

    [Fact]
    public void Build_NonStructureDefinition_ReturnsNull()
    {
        var result = _builder.Build("""{"resourceType": "Patient", "id": "123"}""");
        result.ShouldBeNull();
    }

    [Fact]
    public void Build_NoElements_ReturnsNull()
    {
        var json = """{"resourceType": "StructureDefinition", "name": "Empty"}""";
        var result = _builder.Build(json);
        result.ShouldBeNull();
    }

    #endregion

    #region Abstract type detection

    private const string AbstractResourceJson = """
        {
          "resourceType": "StructureDefinition",
          "id": "Resource",
          "name": "Resource",
          "kind": "resource",
          "abstract": true,
          "type": "Resource",
          "snapshot": {
            "element": [
              {
                "id": "Resource",
                "path": "Resource",
                "min": 0,
                "max": "*"
              },
              {
                "id": "Resource.id",
                "path": "Resource.id",
                "min": 0,
                "max": "1",
                "type": [{ "code": "id" }],
                "isSummary": true
              }
            ]
          }
        }
        """;

    [Fact]
    public void Build_AbstractResource_IsAbstractTrue()
    {
        // Act
        var result = _builder.Build(AbstractResourceJson);

        // Assert
        result.ShouldNotBeNull();
        result.Info.IsAbstract.ShouldBeTrue();
        result.Info.IsResource.ShouldBeTrue();
    }

    #endregion

    #region Primitive type

    private const string PrimitiveTypeJson = """
        {
          "resourceType": "StructureDefinition",
          "id": "string",
          "name": "string",
          "kind": "primitive-type",
          "abstract": false,
          "type": "string",
          "snapshot": {
            "element": [
              {
                "id": "string",
                "path": "string",
                "min": 0,
                "max": "*"
              },
              {
                "id": "string.value",
                "path": "string.value",
                "min": 0,
                "max": "1"
              }
            ]
          }
        }
        """;

    [Fact]
    public void Build_PrimitiveType_IsPrimitiveTrue()
    {
        // Act
        var result = _builder.Build(PrimitiveTypeJson);

        // Assert
        result.ShouldNotBeNull();
        result.Info.IsPrimitive.ShouldBeTrue();
        result.Info.Primitive.ShouldBe(FhirPrimitive.String);
        result.Info.IsResource.ShouldBeFalse();
    }

    #endregion

    #region Element ordering

    [Fact]
    public void Build_MinimalPatient_ElementsHaveCorrectOrder()
    {
        // Act
        var result = _builder.Build(MinimalPatientJson);

        // Assert - Children should maintain order from snapshot
        result.ShouldNotBeNull();
        var children = result.Children.OfType<ITypeExtended>().ToList();
        for (int i = 1; i < children.Count; i++)
        {
            children[i].Order.ShouldBeGreaterThan(children[i - 1].Order);
        }
    }

    #endregion
}
