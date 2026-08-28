// This file is adapted from dotnet/roslyn:
// https://github.com/dotnet/roslyn/blob/ba115f9ff1dc391dca9b42b7a41e98e4c2affc97/src/Razor/src/Compiler/Microsoft.CodeAnalysis.Razor.Compiler/src/Language/CodeGeneration/CodeWriter.IndentCache.cs
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace Akbura.Language.CodeGeneration;

internal sealed partial class CodeWriter
{
    internal static class IndentCache
    {
        internal const int MaxTabCount = 64;
        internal const int MaxSpaceCount = 128;

        private static readonly ReadOnlyMemory<char> s_tabs =
            new string('\t', MaxTabCount).AsMemory();
        private static readonly ReadOnlyMemory<char> s_spaces =
            new string(' ', MaxSpaceCount).AsMemory();

        public static ReadOnlyMemory<char> GetIndentString(
            int size,
            bool useTabs,
            int tabSize)
        {
            ThrowHelper.ThrowIfNegative(size);
            ThrowHelper.ThrowIfLessThan(tabSize, 1);

            if (!useTabs)
            {
                return SliceOrCreate(size, s_spaces);
            }

            var tabCount = size / tabSize;
            var spaceCount = size % tabSize;

            if (spaceCount == 0)
            {
                return SliceOrCreate(tabCount, s_tabs);
            }

            // string.Create(SpanAction<...>) is unavailable on netstandard2.0.
            var characters = new char[tabCount + spaceCount];

            for (var i = 0; i < tabCount; i++)
            {
                characters[i] = '\t';
            }

            for (var i = tabCount; i < characters.Length; i++)
            {
                characters[i] = ' ';
            }

            return new string(characters).AsMemory();
        }

        private static ReadOnlyMemory<char> SliceOrCreate(
            int length,
            ReadOnlyMemory<char> characters)
        {
            return length <= characters.Length
                ? characters.Slice(0, length)
                : new string(characters.Span[0], length).AsMemory();
        }
    }
}
