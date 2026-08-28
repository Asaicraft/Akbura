// This file is adapted from dotnet/roslyn:
// https://github.com/dotnet/roslyn/blob/ba115f9ff1dc391dca9b42b7a41e98e4c2affc97/src/Razor/src/Compiler/Microsoft.CodeAnalysis.Razor.Compiler/src/Language/CodeGeneration/CodeWriterExtensions.cs
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Akbura.Language.CodeGeneration;

internal static class CodeWriterExtensions
{
    private static readonly ReadOnlyMemory<char> s_true = "true".AsMemory();
    private static readonly ReadOnlyMemory<char> s_false = "false".AsMemory();
    private static readonly ReadOnlyMemory<char> s_zeroes = "0000000000".AsMemory();

    private static readonly ImmutableArray<ReadOnlyMemory<char>> s_integerTable =
        InitializeIntegerTable();

    private static readonly char[] s_cStyleStringLiteralEscapeCharacters =
    {
        '\r',
        '\t',
        '"',
        '\'',
        '\\',
        '\0',
        '\n',
        '\u2028',
        '\u2029',
    };

    private static ImmutableArray<ReadOnlyMemory<char>> InitializeIntegerTable()
    {
        var array = new ReadOnlyMemory<char>[1000];

        for (var i = 100; i < 1000; i++)
        {
            array[i] = i.ToString(CultureInfo.InvariantCulture).AsMemory();
        }

        for (var i = 10; i < 100; i++)
        {
            array[i] = array[i + 100].Slice(1, 2);
        }

        for (var i = 1; i < 10; i++)
        {
            array[i] = array[i + 10].Slice(1, 1);
        }

        array[0] = s_zeroes.Slice(0, 1);

        return array.ToImmutableArrayUnsafe();
    }

    public static bool IsAtBeginningOfLine(this CodeWriter writer)
    {
        return writer.LastChar is '\n';
    }

    public static void EnsureNewLine(this CodeWriter writer)
    {
        if (!writer.IsAtBeginningOfLine())
        {
            writer.WriteLine();
        }
    }

    public static CodeWriter WriteVariableDeclaration(
        this CodeWriter writer,
        string type,
        string name,
        string value)
    {
        writer.Write(type).Write(" ").Write(name);

        if (!string.IsNullOrEmpty(value))
        {
            writer.Write(" = ").Write(value);
        }
        else
        {
            writer.Write(" = null");
        }

        writer.WriteLine(";");

        return writer;
    }

    public static CodeWriter WriteBooleanLiteral(this CodeWriter writer, bool value)
    {
        return writer.Write(value ? s_true : s_false);
    }

    public static CodeWriter WriteIntegerLiteral(this CodeWriter writer, int value)
    {
        if (value == 0)
        {
            return writer.Write(s_integerTable[0]);
        }

        var isNegative = value < 0;
        if (isNegative)
        {
            writer.Write("-");
        }

        if (value is > -1000 and < 1000)
        {
            var index = isNegative ? -value : value;
            return writer.Write(s_integerTable[index]);
        }

        var remaining = isNegative ? -(long)value : value;
        long divisor = 1;

        while (remaining >= divisor * 1000)
        {
            divisor *= 1000;
        }

        var first = true;
        while (divisor > 0)
        {
            var group = (int)(remaining / divisor);
            remaining %= divisor;
            divisor /= 1000;

            Debug.Assert(group >= 0 && group < 1000);

            if (group == 0)
            {
                Debug.Assert(!first);
                writer.Write(s_zeroes.Slice(0, 3));
                continue;
            }

            if (first)
            {
                writer.Write(s_integerTable[group]);
                first = false;
                continue;
            }

            var leadingZeroCount = group switch
            {
                < 10 => 2,
                < 100 => 1,
                _ => 0,
            };

            if (leadingZeroCount > 0)
            {
                writer.Write(s_zeroes.Slice(0, leadingZeroCount));
            }

            writer.Write(s_integerTable[group]);
        }

        return writer;
    }

    public static CodeWriter WriteStartAssignment(
        this CodeWriter writer,
        [InterpolatedStringHandlerArgument(nameof(writer))]
        ref CodeWriter.WriteInterpolatedStringHandler left)
    {
        return writer.Write(ref left).Write(" = ");
    }

    public static CodeWriter WriteStartAssignment(this CodeWriter writer, string left)
    {
        return writer.Write(left).Write(" = ");
    }

    public static CodeWriter WriteParameterSeparator(this CodeWriter writer)
    {
        return writer.Write(", ");
    }

    public static CodeWriter WriteStartNewObject(this CodeWriter writer, string typeName)
    {
        return writer.Write("new ").Write(typeName).Write("(");
    }

    public static CodeWriter WriteStringLiteral(
        this CodeWriter writer,
        string literal,
        bool utf8 = false)
    {
        ThrowHelper.ThrowIfNull(literal);

        return writer.WriteStringLiteral(literal.AsMemory(), utf8);
    }

    public static CodeWriter WriteStringLiteral(
        this CodeWriter writer,
        ReadOnlyMemory<char> literal,
        bool utf8 = false)
    {
        if (literal.Length >= 256 &&
            literal.Length <= 1500 &&
            literal.Span.IndexOf('\0') == -1)
        {
            WriteVerbatimStringLiteral(writer, literal, utf8);
        }
        else
        {
            WriteCStyleStringLiteral(writer, literal, utf8);
        }

        return writer;
    }

    public static CodeWriter WriteUsing(this CodeWriter writer, string name)
    {
        return writer.WriteUsing(name, endLine: true);
    }

    public static CodeWriter WriteUsing(this CodeWriter writer, string name, bool endLine)
    {
        writer.Write("using ").Write(name);

        if (endLine)
        {
            writer.WriteLine(";");
        }

        return writer;
    }

    public static CodeWriter WriteStartMethodInvocation(
        this CodeWriter writer,
        string methodName)
    {
        return writer.Write(methodName).Write("(");
    }

    public static CodeWriter WriteStartMethodInvocation(
        this CodeWriter writer,
        [InterpolatedStringHandlerArgument(nameof(writer))]
        ref CodeWriter.WriteInterpolatedStringHandler methodName)
    {
        return writer.Write(ref methodName).Write("(");
    }

    public static CodeWriter WriteEndMethodInvocation(this CodeWriter writer)
    {
        return writer.WriteEndMethodInvocation(endLine: true);
    }

    public static CodeWriter WriteEndMethodInvocation(this CodeWriter writer, bool endLine)
    {
        writer.Write(")");

        if (endLine)
        {
            writer.WriteLine(";");
        }

        return writer;
    }

    public static CodeWriter WriteInstanceMethodInvocation(
        this CodeWriter writer,
        string instanceName,
        string methodName,
        params ImmutableArray<string> arguments)
    {
        return writer.WriteInstanceMethodInvocation(
            instanceName,
            methodName,
            endLine: true,
            arguments);
    }

    public static CodeWriter WriteInstanceMethodInvocation(
        this CodeWriter writer,
        string instanceName,
        string methodName,
        bool endLine,
        params ImmutableArray<string> arguments)
    {
        return writer.WriteMethodInvocation(
            $"{instanceName}.{methodName}",
            endLine,
            arguments);
    }

    public static CodeWriter WriteStartInstanceMethodInvocation(
        this CodeWriter writer,
        string instanceName,
        string methodName)
    {
        return writer.WriteStartMethodInvocation($"{instanceName}.{methodName}");
    }

    public static CodeWriter WriteFieldDeclaration(
        this CodeWriter writer,
        ImmutableArray<string> modifiers,
        string type,
        string name,
        string? expression = null)
    {
        expression = string.IsNullOrEmpty(expression) ? "null" : expression;

        return writer
            .WriteModifierList(modifiers)
            .WriteLine($"{type} {name} = {expression};");
    }

    private static CodeWriter WriteModifierList(
        this CodeWriter writer,
        ImmutableArray<string> modifiers)
    {
        if (!modifiers.IsDefaultOrEmpty)
        {
            foreach (var modifier in modifiers)
            {
                writer.Write($"{modifier} ");
            }
        }

        return writer;
    }

    public static CodeWriter WriteField(
        this CodeWriter writer,
        ImmutableArray<string> suppressWarnings,
        ImmutableArray<string> modifiers,
        string type,
        string name)
    {
        if (!suppressWarnings.IsDefaultOrEmpty)
        {
            foreach (var suppressWarning in suppressWarnings)
            {
                writer.WriteLine($"#pragma warning disable {suppressWarning}");
            }
        }

        writer.WriteModifierList(modifiers);
        writer.WriteLine($"{type} {name};");

        if (!suppressWarnings.IsDefaultOrEmpty)
        {
            for (var i = suppressWarnings.Length - 1; i >= 0; i--)
            {
                writer.WriteLine($"#pragma warning restore {suppressWarnings[i]}");
            }
        }

        return writer;
    }

    public static CodeWriter WriteMethodInvocation(
        this CodeWriter writer,
        string methodName,
        params ImmutableArray<string> arguments)
    {
        return writer.WriteMethodInvocation(methodName, endLine: true, arguments);
    }

    public static CodeWriter WriteMethodInvocation(
        this CodeWriter writer,
        [InterpolatedStringHandlerArgument(nameof(writer))]
        ref CodeWriter.WriteInterpolatedStringHandler methodName,
        params ImmutableArray<string> arguments)
    {
        return writer.WriteMethodInvocation(ref methodName, endLine: true, arguments);
    }

    public static CodeWriter WriteMethodInvocation(
        this CodeWriter writer,
        string methodName,
        bool endLine,
        params ImmutableArray<string> arguments)
    {
        return writer
            .WriteStartMethodInvocation(methodName)
            .WriteCommaSeparatedList(arguments)
            .WriteEndMethodInvocation(endLine);
    }

    public static CodeWriter WriteMethodInvocation(
        this CodeWriter writer,
        [InterpolatedStringHandlerArgument(nameof(writer))]
        ref CodeWriter.WriteInterpolatedStringHandler methodName,
        bool endLine,
        params ImmutableArray<string> arguments)
    {
        return writer
            .WriteStartMethodInvocation(ref methodName)
            .WriteCommaSeparatedList(arguments)
            .WriteEndMethodInvocation(endLine);
    }

    public static CodeWriter WriteIdentifierEscapeIfNeeded(
        this CodeWriter writer,
        string identifier)
    {
        if (identifier.IdentifierRequiresEscaping())
        {
            writer.Write("@");
        }

        return writer;
    }

    public static bool IdentifierRequiresEscaping(this string identifier)
    {
        return Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetKeywordKind(identifier) !=
                Microsoft.CodeAnalysis.CSharp.SyntaxKind.None ||
            Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetContextualKeywordKind(identifier) !=
                Microsoft.CodeAnalysis.CSharp.SyntaxKind.None;
    }

    public static CSharpCodeWritingScope BuildScope(this CodeWriter writer)
    {
        return new CSharpCodeWritingScope(writer);
    }

    public static CSharpCodeWritingScope BuildLambda(this CodeWriter writer)
    {
        return writer.WriteLambdaHeader().BuildScope();
    }

    public static CSharpCodeWritingScope BuildLambda(
        this CodeWriter writer,
        string parameterName)
    {
        return writer.WriteLambdaHeader(parameterName).BuildScope();
    }

    public static CSharpCodeWritingScope BuildLambda<T>(
        this CodeWriter writer,
        T parameterName)
        where T : IWriteableValue
    {
        return writer.WriteLambdaHeader(parameterName).BuildScope();
    }

    public static CSharpCodeWritingScope BuildAsyncLambda(this CodeWriter writer)
    {
        return writer.WriteAsyncLambdaHeader().BuildScope();
    }

    public static CSharpCodeWritingScope BuildAsyncLambda(
        this CodeWriter writer,
        string parameterName)
    {
        return writer.WriteAsyncLambdaHeader(parameterName).BuildScope();
    }

    public static CSharpCodeWritingScope BuildAsyncLambda<T>(
        this CodeWriter writer,
        T parameterName)
        where T : IWriteableValue
    {
        return writer.WriteAsyncLambdaHeader(parameterName).BuildScope();
    }

    public static CodeWriter WriteLambdaHeader(this CodeWriter writer)
    {
        return writer.Write("() => ");
    }

    public static CodeWriter WriteLambdaHeader(
        this CodeWriter writer,
        string parameterName)
    {
        return writer.Write($"({parameterName}) => ");
    }

    public static CodeWriter WriteLambdaHeader<T>(
        this CodeWriter writer,
        T parameterName)
        where T : IWriteableValue
    {
        writer.Write("(");
        parameterName.WriteTo(writer);
        writer.Write(") => ");

        return writer;
    }

    public static CodeWriter WriteAsyncLambdaHeader(this CodeWriter writer)
    {
        return writer.Write("async() => ");
    }

    public static CodeWriter WriteAsyncLambdaHeader(
        this CodeWriter writer,
        string parameterName)
    {
        return writer.Write($"async({parameterName}) => ");
    }

    public static CodeWriter WriteAsyncLambdaHeader<T>(
        this CodeWriter writer,
        T parameterName)
        where T : IWriteableValue
    {
        writer.Write("async(");
        parameterName.WriteTo(writer);
        writer.Write(") => ");

        return writer;
    }

    public static CodeWriter WriteCommaSeparatedList(
        this CodeWriter writer,
        ImmutableArray<string> items)
    {
        return writer.WriteSeparatedList(", ", items);
    }

    public static CodeWriter WriteCommaSeparatedList<T>(
        this CodeWriter writer,
        ImmutableArray<T> items,
        Action<CodeWriter, T> elementWriter)
    {
        return writer.WriteSeparatedList(", ", items, elementWriter);
    }

    public static CodeWriter WriteSeparatedList(
        this CodeWriter writer,
        string separator,
        ImmutableArray<string> items)
    {
        var first = true;

        foreach (var item in items)
        {
            if (!first)
            {
                writer.Write(separator);
            }
            else
            {
                first = false;
            }

            writer.Write(item);
        }

        return writer;
    }

    public static CodeWriter WriteSeparatedList<T>(
        this CodeWriter writer,
        string separator,
        ImmutableArray<T> items,
        Action<CodeWriter, T> elementWriter)
    {
        var first = true;

        foreach (var item in items)
        {
            if (!first)
            {
                writer.Write(separator);
            }
            else
            {
                first = false;
            }

            elementWriter(writer, item);
        }

        return writer;
    }

    private static void WriteVerbatimStringLiteral(
        CodeWriter writer,
        ReadOnlyMemory<char> literal,
        bool utf8)
    {
        writer.Write("@\"");

        var oldIndent = writer.CurrentIndent;
        writer.CurrentIndent = 0;

        int index;
        while ((index = literal.Span.IndexOf('"')) >= 0)
        {
            writer.Write(literal.Slice(0, index));
            writer.Write("\"\"");

            literal = literal.Slice(index + 1);
        }

        Debug.Assert(index == -1);

        writer.Write(literal).Write("\"");

        if (utf8)
        {
            writer.Write("u8");
        }

        writer.CurrentIndent = oldIndent;
    }

    private static void WriteCStyleStringLiteral(
        CodeWriter writer,
        ReadOnlyMemory<char> literal,
        bool utf8)
    {
        writer.Write("\"");

        int index;
        while ((index = literal.Span.IndexOfAny(s_cStyleStringLiteralEscapeCharacters)) >= 0)
        {
            writer.Write(literal.Slice(0, index));

            switch (literal.Span[index])
            {
                case '\r':
                    writer.Write("\\r");
                    break;
                case '\t':
                    writer.Write("\\t");
                    break;
                case '"':
                    writer.Write("\\\"");
                    break;
                case '\'':
                    writer.Write("\\\'");
                    break;
                case '\\':
                    writer.Write("\\\\");
                    break;
                case '\0':
                    writer.Write("\\0");
                    break;
                case '\n':
                    writer.Write("\\n");
                    break;
                case '\u2028':
                    writer.Write("\\u2028");
                    break;
                case '\u2029':
                    writer.Write("\\u2029");
                    break;
                default:
                    Debug.Fail("Unknown escape character.");
                    break;
            }

            literal = literal.Slice(index + 1);
        }

        Debug.Assert(index == -1);

        writer.Write(literal).Write("\"");

        if (utf8)
        {
            writer.Write("u8");
        }
    }

    public struct CSharpCodeWritingScope : IDisposable
    {
        private readonly CodeWriter? _writer;
        private readonly bool _autoSpace;
        private readonly bool _writeBraces;
        private readonly int _tabSize;
        private int _startIndent;

        public CSharpCodeWritingScope(
            CodeWriter writer,
            bool autoSpace = true,
            bool writeBraces = true)
        {
            _writer = writer;
            _autoSpace = autoSpace;
            _writeBraces = writeBraces;
            _tabSize = writer.TabSize;
            _startIndent = -1;

            WriteStartScope();
        }

        public void Dispose()
        {
            if (_writer is null)
            {
                return;
            }

            WriteEndScope();
        }

        private void WriteStartScope()
        {
            TryAutoSpace(" ");

            if (_writeBraces)
            {
                _writer!.WriteLine("{");
            }
            else
            {
                _writer!.WriteLine();
            }

            _writer.CurrentIndent += _tabSize;
            _startIndent = _writer.CurrentIndent;
        }

        private void WriteEndScope()
        {
            TryAutoSpace(_writer!.NewLine);

            if (_writer.CurrentIndent == _startIndent)
            {
                _writer.CurrentIndent -= _tabSize;
            }

            if (_writeBraces)
            {
                _writer.WriteLine("}");
            }
            else
            {
                _writer.WriteLine();
            }
        }

        private void TryAutoSpace(string spaceCharacter)
        {
            if (_autoSpace &&
                _writer!.LastChar is char character &&
                !char.IsWhiteSpace(character))
            {
                _writer.Write(spaceCharacter);
            }
        }
    }
}
