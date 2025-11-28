using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akbura.CompilerAnotations;
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class StyleNameAttribute : Attribute
{
    public required string Name 
    { 
        get; 
        init; 
    }

    [SetsRequiredMembers]
    public StyleNameAttribute(string name)
    {
        Name = name;
    }

    public StyleNameAttribute()
    {

    }
}
