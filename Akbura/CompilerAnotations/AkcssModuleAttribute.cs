using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.CompilerAnotations;


[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class AkcssModuleAttribute : Attribute
{
    public AkcssModuleAttribute(string path)
    {
        Path = path;
    }

    public string Path
    {
        get; 
    }
}