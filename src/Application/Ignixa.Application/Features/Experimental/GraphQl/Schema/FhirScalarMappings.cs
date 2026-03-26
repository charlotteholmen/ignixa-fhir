// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Frozen;
using HotChocolate.Types;

namespace Ignixa.Application.Features.Experimental.GraphQl.Schema;

public static class FhirScalarMappings
{
    private static readonly FrozenDictionary<string, Type> _mappings =
        new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            ["boolean"] = typeof(BooleanType),
            ["integer"] = typeof(IntType),
            ["positiveInt"] = typeof(IntType),
            ["unsignedInt"] = typeof(IntType),
            ["decimal"] = typeof(FloatType),
            ["id"] = typeof(StringType),
            ["string"] = typeof(StringType),
            ["markdown"] = typeof(StringType),
            ["uri"] = typeof(StringType),
            ["url"] = typeof(StringType),
            ["canonical"] = typeof(StringType),
            ["oid"] = typeof(StringType),
            ["uuid"] = typeof(StringType),
            ["base64Binary"] = typeof(StringType),
            ["code"] = typeof(StringType),
            ["xhtml"] = typeof(StringType),
            ["date"] = typeof(StringType),
            ["dateTime"] = typeof(StringType),
            ["instant"] = typeof(StringType),
            ["time"] = typeof(StringType),
        }.ToFrozenDictionary();

    public static Type GetScalarType(string fhirPrimitiveName)
    {
        ArgumentNullException.ThrowIfNull(fhirPrimitiveName);

        return _mappings.TryGetValue(fhirPrimitiveName, out var type) ? type : typeof(StringType);
    }
}
