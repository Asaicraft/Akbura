using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Engine;

public readonly record struct InjectionInfo(
    Type RequestedService,
    AkburaControl? TargetControl = null,
    IAkburaServiceProvider? NextProvider = null,
    bool? IsOptional = null,
    string? FieldName = null)

{
    public InjectionInfo(Type RequestedService) : this(RequestedService, null, null, null, null)
    {

    }
}