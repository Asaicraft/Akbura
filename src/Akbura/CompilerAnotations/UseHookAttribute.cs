using System;

namespace Akbura.CompilerAnotations;

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class UseHookAttribute : Attribute
{
    /// <summary>
    /// Runs a legacy state factory once instead of composing primitives each render.
    /// </summary>
    public bool IsInitializer { get; set; }
}
