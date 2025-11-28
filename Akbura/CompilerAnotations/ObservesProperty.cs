using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akbura.CompilerAnotations;
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class ObservesPropertyAttribute : Attribute
{
    public required string PropertyName
    {
        get;
        init;
    }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ObservesPropertyAttribute(string propertyName)
    {
        PropertyName = propertyName;
    }

    public ObservesPropertyAttribute()
    {
    }
}
