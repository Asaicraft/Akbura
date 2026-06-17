using System;

namespace Akbura.CompilerAnotations;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class UserHookAttribute : Attribute
{
}
