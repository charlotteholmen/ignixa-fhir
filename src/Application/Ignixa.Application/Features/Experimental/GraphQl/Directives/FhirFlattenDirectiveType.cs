// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using HotChocolate.Types;

namespace Ignixa.Application.Features.Experimental.GraphQl.Directives;

public sealed class FhirFlattenDirectiveType : DirectiveType
{
    protected override void Configure(IDirectiveTypeDescriptor descriptor)
    {
        descriptor.Name("flatten");
        descriptor.Description("Hoist children up to parent level. Children become lists.");
        descriptor.Location(DirectiveLocation.Field);
    }
}
