// This file is adapted from dotnet/roslyn:
// https://github.com/dotnet/roslyn/blob/ba115f9ff1dc391dca9b42b7a41e98e4c2affc97/src/Razor/src/Compiler/Microsoft.CodeAnalysis.Razor.Compiler/src/Language/CodeGeneration/IWriteableValue.cs
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// A value that can write itself directly to a <see cref="CodeWriter"/>.
/// </summary>
internal interface IWriteableValue
{
    void WriteTo(CodeWriter writer);
}
