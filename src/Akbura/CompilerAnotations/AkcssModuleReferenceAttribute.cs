using System;
using System.ComponentModel;

namespace Akbura.CompilerAnotations;

/// <summary>
/// Points to a generated AKCSS module whose public metadata is available to
/// referencing compilations.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
[Browsable(false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class AkcssModuleReferenceAttribute : Attribute
{
    public AkcssModuleReferenceAttribute(Type moduleType)
    {
        ModuleType = moduleType ?? throw new ArgumentNullException(nameof(moduleType));
    }

    public Type ModuleType { get; }
}
