using System;
using System.ComponentModel;

namespace Akbura.CompilerAnotations;


[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
[Browsable(false)]
[EditorBrowsable(EditorBrowsableState.Never)]
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

    public string MetadataName { get; init; } = string.Empty;

    public int FormatVersion { get; init; }
}
