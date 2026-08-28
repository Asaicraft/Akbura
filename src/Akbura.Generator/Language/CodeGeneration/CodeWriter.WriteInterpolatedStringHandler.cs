// This file is adapted from dotnet/roslyn:
// https://github.com/dotnet/roslyn/blob/ba115f9ff1dc391dca9b42b7a41e98e4c2affc97/src/Razor/src/Compiler/Microsoft.CodeAnalysis.Razor.Compiler/src/Language/CodeGeneration/CodeWriter.WriteInterpolatedStringHandler.cs
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Akbura.Language.CodeGeneration;

internal sealed partial class CodeWriter
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    [InterpolatedStringHandler]
    public readonly ref struct WriteInterpolatedStringHandler
    {
        private readonly CodeWriter _writer;

        public WriteInterpolatedStringHandler(
            int literalLength,
            int formattedCount,
            CodeWriter writer)
        {
            _writer = writer;
        }

        public void AppendLiteral(string value)
        {
            _writer.Write(value);
        }

        public void AppendFormatted(ReadOnlyMemory<char> value)
        {
            _writer.Write(value);
        }

        public void AppendFormatted(string? value)
        {
            if (value is not null)
            {
                _writer.Write(value);
            }
        }

        public void AppendFormatted<T>(T value)
        {
            if (value is null)
            {
                return;
            }

            switch (value)
            {
                case ReadOnlyMemory<char> memory:
                    _writer.Write(memory);
                    break;

                case string text:
                    _writer.Write(text);
                    break;

                case IWriteableValue writeableValue:
                    writeableValue.WriteTo(_writer);
                    break;

                default:
                    _writer.Write(value.ToString() ?? string.Empty);
                    break;
            }
        }
    }
}
