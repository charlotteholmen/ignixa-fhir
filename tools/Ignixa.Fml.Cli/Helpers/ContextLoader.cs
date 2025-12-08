using Ignixa.FhirMappingLanguage.TypeSystem;
using Ignixa.Serialization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.Abstractions;
using FmlTypeInfo = Ignixa.FhirMappingLanguage.TypeSystem.TypeInfo;

namespace Ignixa.Fml.Cli.Helpers;

/// <summary>
/// Loads FHIR definitions from a context directory and creates a type validator.
/// </summary>
internal static class ContextLoader
{
    /// <summary>
    /// Loads StructureDefinitions and ValueSets from a context directory.
    /// </summary>
    /// <param name="contextPath">Path to directory containing FHIR definition files</param>
    /// <returns>Type validator with loaded definitions, or null if loading fails</returns>
    public static ITypeValidator? LoadContext(string? contextPath)
    {
        if (string.IsNullOrEmpty(contextPath) || !Directory.Exists(contextPath))
        {
            return null;
        }

        try
        {
            var typeValidator = new ContextAwareTypeValidator();
            
            // Find all JSON files in the context directory
            var jsonFiles = Directory.GetFiles(contextPath, "*.json", SearchOption.AllDirectories);
            
            int structureDefCount = 0;
            int valueSetCount = 0;
            int conceptMapCount = 0;
            
            foreach (var file in jsonFiles)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var jsonDoc = JsonDocument.Parse(json);
                    
                    if (jsonDoc.RootElement.TryGetProperty("resourceType", out var resourceType))
                    {
                        var resourceTypeValue = resourceType.GetString();
                        
                        switch (resourceTypeValue)
                        {
                            case "StructureDefinition":
                                typeValidator.AddStructureDefinition(jsonDoc.RootElement);
                                structureDefCount++;
                                break;
                            
                            case "ValueSet":
                                typeValidator.AddValueSet(jsonDoc.RootElement);
                                valueSetCount++;
                                break;
                            
                            case "ConceptMap":
                                typeValidator.AddConceptMap(jsonDoc.RootElement);
                                conceptMapCount++;
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠ Warning: Could not load {Path.GetFileName(file)}: {ex.Message}");
                }
            }
            
            Console.WriteLine($"✓ Loaded {structureDefCount} StructureDefinitions, {valueSetCount} ValueSets, {conceptMapCount} ConceptMaps");
            
            return typeValidator;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error loading context: {ex.Message}");
            return null;
        }
    }
}

/// <summary>
/// Type validator that can be extended with custom StructureDefinitions.
/// </summary>
internal class ContextAwareTypeValidator : ITypeValidator
{
    private readonly Dictionary<string, JsonElement> _structureDefinitions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, JsonElement> _valueSets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, JsonElement> _conceptMaps = new(StringComparer.OrdinalIgnoreCase);
    
    // FHIR primitive types
    private static readonly HashSet<string> PrimitiveTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "base64Binary", "boolean", "canonical", "code", "date", "dateTime",
        "decimal", "id", "instant", "integer", "integer64", "markdown",
        "oid", "positiveInt", "string", "time", "unsignedInt", "uri", "url", "uuid"
    };

    // Common FHIR complex types
    private static readonly HashSet<string> ComplexTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Address", "Age", "Annotation", "Attachment", "CodeableConcept", "Coding",
        "ContactPoint", "Count", "Distance", "Duration", "HumanName", "Identifier",
        "Money", "Period", "Quantity", "Range", "Ratio", "Reference", "SampledData",
        "Signature", "Timing", "ContactDetail", "Contributor", "DataRequirement",
        "Expression", "ParameterDefinition", "RelatedArtifact", "TriggerDefinition",
        "UsageContext", "Dosage", "Meta"
    };

    // Common FHIR resource types
    private static readonly HashSet<string> ResourceTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Patient", "Observation", "Practitioner", "Organization", "Encounter",
        "Condition", "Procedure", "MedicationRequest", "DiagnosticReport",
        "AllergyIntolerance", "CarePlan", "CareTeam", "Claim", "Coverage",
        "Device", "DocumentReference", "Goal", "Immunization", "Location",
        "Medication", "Specimen", "Bundle", "OperationOutcome", "Parameters",
        "StructureDefinition", "StructureMap", "ValueSet", "CodeSystem", "ConceptMap"
    };
    
    public void AddStructureDefinition(JsonElement structureDef)
    {
        if (structureDef.TryGetProperty("name", out var name))
        {
            var typeName = name.GetString();
            if (!string.IsNullOrEmpty(typeName))
            {
                _structureDefinitions[typeName] = structureDef;
            }
        }
        
        // Also index by id
        if (structureDef.TryGetProperty("id", out var id))
        {
            var idValue = id.GetString();
            if (!string.IsNullOrEmpty(idValue))
            {
                _structureDefinitions[idValue] = structureDef;
            }
        }
    }
    
    public void AddValueSet(JsonElement valueSet)
    {
        if (valueSet.TryGetProperty("name", out var name))
        {
            var vsName = name.GetString();
            if (!string.IsNullOrEmpty(vsName))
            {
                _valueSets[vsName] = valueSet;
            }
        }
        
        if (valueSet.TryGetProperty("id", out var id))
        {
            var idValue = id.GetString();
            if (!string.IsNullOrEmpty(idValue))
            {
                _valueSets[idValue] = valueSet;
            }
        }
    }
    
    public void AddConceptMap(JsonElement conceptMap)
    {
        if (conceptMap.TryGetProperty("name", out var name))
        {
            var cmName = name.GetString();
            if (!string.IsNullOrEmpty(cmName))
            {
                _conceptMaps[cmName] = conceptMap;
            }
        }
        
        if (conceptMap.TryGetProperty("id", out var id))
        {
            var idValue = id.GetString();
            if (!string.IsNullOrEmpty(idValue))
            {
                _conceptMaps[idValue] = conceptMap;
            }
        }
    }
    
    public IEnumerable<TypeValidationError> ValidateMap(MapExpression map)
    {
        // Basic validation - could be enhanced with custom structure definitions
        return Enumerable.Empty<TypeValidationError>();
    }
    
    public bool IsTypeCompatible(string sourceType, string targetType)
    {
        // Exact match
        if (string.Equals(sourceType, targetType, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        
        // Check if either type is a custom type - be permissive for custom types
        if (_structureDefinitions.ContainsKey(sourceType) || _structureDefinitions.ContainsKey(targetType))
        {
            return true;
        }
        
        // Standard type compatibility
        return true; // Be permissive for now
    }
    
    public FmlTypeInfo? ResolveType(string typeName)
    {
        // First check if it's a custom type
        if (_structureDefinitions.TryGetValue(typeName, out var structureDef))
        {
            // Found a custom structure definition
            // Determine the base type
            string? baseType = null;
            if (structureDef.TryGetProperty("type", out var typeElem))
            {
                baseType = typeElem.GetString();
            }
            
            // Determine category based on kind
            var category = TypeCategory.Complex;
            if (structureDef.TryGetProperty("kind", out var kind))
            {
                var kindValue = kind.GetString();
                if (kindValue == "resource")
                {
                    category = TypeCategory.Resource;
                }
                else if (kindValue == "primitive-type")
                {
                    category = TypeCategory.Primitive;
                }
            }
            
            return new FmlTypeInfo(typeName, category, baseType);
        }
        
        // Fall back to built-in type checking
        if (PrimitiveTypes.Contains(typeName))
        {
            return new FmlTypeInfo(typeName, TypeCategory.Primitive);
        }
        else if (ResourceTypes.Contains(typeName))
        {
            return new FmlTypeInfo(typeName, TypeCategory.Resource, "Resource");
        }
        else if (ComplexTypes.Contains(typeName))
        {
            return new FmlTypeInfo(typeName, TypeCategory.Complex, "Element");
        }
        
        // Unknown type
        return new FmlTypeInfo(typeName, TypeCategory.Unknown);
    }
    
    public TypeValidationError? ValidateElement(IElement element, string expectedType)
    {
        if (element == null)
        {
            return new TypeValidationError($"Element is null, expected type '{expectedType}'");
        }

        var actualType = element.InstanceType;
        if (string.IsNullOrEmpty(actualType))
        {
            return new TypeValidationError($"Element has no type information, expected type '{expectedType}'");
        }

        if (!IsTypeCompatible(actualType, expectedType))
        {
            return new TypeValidationError(
                $"Type mismatch: element has type '{actualType}' but expected '{expectedType}'");
        }

        return null;
    }
    
    public int StructureDefinitionCount => _structureDefinitions.Count;
    public int ValueSetCount => _valueSets.Count;
    public int ConceptMapCount => _conceptMaps.Count;
}
