using System;

namespace Akbura.CompilerAnotations;

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class UseHookAttribute : Attribute
{
}
