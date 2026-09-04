// This file is adapted from dotnet/roslyn:
// https://github.com/dotnet/roslyn/blob/ba115f9ff1dc391dca9b42b7a41e98e4c2affc97/src/Razor/src/Compiler/Microsoft.CodeAnalysis.Razor.Compiler/src/Language/CodeGeneration/CodeWriter.cs
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Language.CodeGeneration;

internal readonly struct CodeWriterPosition : IEquatable<CodeWriterPosition>
{
    public CodeWriterPosition(int absoluteIndex, int lineIndex, int characterIndex)
    {
        AbsoluteIndex = absoluteIndex;
        LineIndex = lineIndex;
        CharacterIndex = characterIndex;
    }

    public int AbsoluteIndex { get; }

    public int LineIndex { get; }

    public int CharacterIndex { get; }

    public bool Equals(CodeWriterPosition other)
    {
        return AbsoluteIndex == other.AbsoluteIndex &&
            LineIndex == other.LineIndex &&
            CharacterIndex == other.CharacterIndex;
    }

    public override bool Equals(object? obj)
    {
        return obj is CodeWriterPosition other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(AbsoluteIndex, LineIndex, CharacterIndex);
    }

    public override string ToString()
    {
        return $"({AbsoluteIndex}:{LineIndex},{CharacterIndex})";
    }

    public static bool operator ==(CodeWriterPosition left, CodeWriterPosition right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(CodeWriterPosition left, CodeWriterPosition right)
    {
        return !left.Equals(right);
    }
}

internal sealed partial class CodeWriter : IDisposable
{
    // This is the size of each "page", which consists of arrays of text chunks.
    // ReadOnlyMemory<char> is 16 bytes, so a minimum-size page stays well below the LOH.
    private const int MinimumPageSize = 1000;

    private readonly LinkedList<ReadOnlyMemory<char>[]> _pages;
    private int _pageOffset;
    private char? _lastChar;

    private string _newLine;
    private int _indentSize;
    private ReadOnlyMemory<char> _indentString;

    private int _absoluteIndex;
    private int _currentLineIndex;
    private int _currentLineCharacterIndex;

    public CodeWriter()
        : this("\r\n")
    {
    }

    public CodeWriter(string newLine, bool indentWithTabs = false, int tabSize = 4)
    {
        ThrowHelper.ThrowIfLessThan(tabSize, 1);

        SetNewLine(newLine);
        IndentWithTabs = indentWithTabs;
        TabSize = tabSize;

        _indentSize = 0;
        _indentString = ReadOnlyMemory<char>.Empty;
        _pages = new LinkedList<ReadOnlyMemory<char>[]>();
    }

    public void Dispose()
    {
        foreach (var page in _pages)
        {
            ArrayPool<ReadOnlyMemory<char>>.Shared.Return(page, clearArray: true);
        }

        _pages.Clear();
    }

    private void AddTextChunk(ReadOnlyMemory<char> value)
    {
        if (value.Length == 0)
        {
            return;
        }

        ReadOnlyMemory<char>[] lastPage;

        if (_pageOffset == 0)
        {
            lastPage = ArrayPool<ReadOnlyMemory<char>>.Shared.Rent(MinimumPageSize);
            _pages.AddLast(lastPage);
        }
        else
        {
            lastPage = _pages.Last!.Value;
        }

        lastPage[_pageOffset] = value;
        _pageOffset++;

        if (_pageOffset == lastPage.Length)
        {
            _pageOffset = 0;
        }

        _lastChar = value.Span[value.Length - 1];
    }

    public int CurrentIndent
    {
        get => _indentSize;
        set
        {
            ThrowHelper.ThrowIfNegative(value);

            if (_indentSize != value)
            {
                _indentSize = value;
                _indentString = IndentCache.GetIndentString(value, IndentWithTabs, TabSize);
            }
        }
    }

    public int Length => _absoluteIndex;

    public string NewLine
    {
        get => _newLine;
        set => SetNewLine(value);
    }

    [MemberNotNull(nameof(_newLine))]
    private void SetNewLine(string value)
    {
        ThrowHelper.ThrowIfNull(value);

        if (value != "\r\n" && value != "\n")
        {
            throw new ArgumentException(
                $"Invalid newline sequence '{value}'. Supported newline sequences are '\\r\\n' and '\\n'.",
                nameof(value));
        }

        _newLine = value;
    }

    public bool IndentWithTabs { get; }

    public int TabSize { get; }

    public CodeWriterPosition Location => new CodeWriterPosition(
        _absoluteIndex,
        _currentLineIndex,
        _currentLineCharacterIndex);

    public char this[int index]
    {
        get
        {
            Debug.Fail("Do not use this indexer without reimplementing it more efficiently.");

            foreach (var page in _pages)
            {
                foreach (var chars in page)
                {
                    if (index < chars.Length)
                    {
                        return chars.Span[index];
                    }

                    index -= chars.Length;
                }
            }

            throw new IndexOutOfRangeException(nameof(index));
        }
    }

    public char? LastChar => _lastChar;

    public CodeWriter Indent(int size)
    {
        if (size == 0 || LastChar is not '\n')
        {
            return this;
        }

        var indentString = size == _indentSize
            ? _indentString
            : IndentCache.GetIndentString(size, IndentWithTabs, TabSize);

        AddTextChunk(indentString);

        var indentLength = indentString.Length;
        _currentLineCharacterIndex += indentLength;
        _absoluteIndex += indentLength;

        return this;
    }

    internal CodeWriter WriteCurrentIndent()
    {
        if (_indentSize == 0)
        {
            return this;
        }

        AddTextChunk(_indentString);

        var indentLength = _indentString.Length;
        _currentLineCharacterIndex += indentLength;
        _absoluteIndex += indentLength;

        return this;
    }

    public CodeWriter Write(string value)
    {
        ThrowHelper.ThrowIfNull(value);

        return WriteCore(value.AsMemory());
    }

    public CodeWriter Write(ReadOnlyMemory<char> value)
    {
        return WriteCore(value);
    }

    public CodeWriter Write(string value, int startIndex, int count)
    {
        ThrowHelper.ThrowIfNull(value);
        ThrowHelper.ThrowIfNegative(startIndex);
        ThrowHelper.ThrowIfNegative(count);

        if (startIndex > value.Length - count)
        {
            ThrowHelper.ThrowArgumentOutOfRangeException(nameof(startIndex));
        }

        return WriteCore(value.AsMemory(startIndex, count));
    }

    internal CodeWriter Write<T>(T value)
        where T : IWriteableValue
    {
        value.WriteTo(this);

        return this;
    }

    public CodeWriter Write(
        [InterpolatedStringHandlerArgument("")] ref WriteInterpolatedStringHandler handler)
    {
        return this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private CodeWriter WriteCore(ReadOnlyMemory<char> value, bool allowIndent = true)
    {
        if (value.IsEmpty)
        {
            return this;
        }

        if (allowIndent)
        {
            Indent(_indentSize);
        }

        var lastChar = _lastChar;

        AddTextChunk(value);

        var span = value.Span;
        _absoluteIndex += span.Length;

        // A CRLF sequence may be split across two Write calls. The CR was already counted.
        if (lastChar == '\r' && span[0] == '\n')
        {
            span = span.Slice(1);
        }

        int newLineIndex;
        while ((newLineIndex = span.IndexOfAny('\r', '\n')) >= 0)
        {
            _currentLineIndex++;
            _currentLineCharacterIndex = 0;

            newLineIndex++;

            if (newLineIndex < span.Length &&
                span[newLineIndex - 1] == '\r' &&
                span[newLineIndex] == '\n')
            {
                newLineIndex++;
            }

            span = span.Slice(newLineIndex);
        }

        _currentLineCharacterIndex += span.Length;

        return this;
    }

    public CodeWriter WriteLine()
    {
        return WriteCore(_newLine.AsMemory(), allowIndent: false);
    }

    public CodeWriter WriteLine(ReadOnlyMemory<char> value)
    {
        return WriteCore(value).WriteLine();
    }

    public CodeWriter WriteLine(string value)
    {
        ThrowHelper.ThrowIfNull(value);

        return WriteCore(value.AsMemory()).WriteLine();
    }

    public CodeWriter WriteLine(
        [InterpolatedStringHandlerArgument("")] ref WriteInterpolatedStringHandler handler)
    {
        return WriteLine();
    }

    public SourceText GetText()
    {
        using var reader = new Reader(_pages, Length);
        return SourceText.From(reader, Length, Encoding.UTF8);
    }

    // Internal for testing.
    internal static TextReader GetTestTextReader(LinkedList<ReadOnlyMemory<char>[]> pages)
    {
        return new Reader(pages, pages.Count);
    }

    private sealed class Reader : TextReader
    {
        private LinkedListNode<ReadOnlyMemory<char>[]>? _page;
        private int _remainingLength;
        private int _chunkIndex;
        private int _charIndex;

        public Reader(LinkedList<ReadOnlyMemory<char>[]> pages, int length)
        {
            _page = pages.First;
            _remainingLength = length;
        }

        public override int Read()
        {
            if (!TryGetNextCharReadLocation(out var page, out var chunkIndex, out var charIndex))
            {
                return -1;
            }

            _page = page;
            _chunkIndex = chunkIndex;
            _charIndex = charIndex + 1;
            _remainingLength--;

            return page.Value[chunkIndex].Span[charIndex];
        }

        public override int Peek()
        {
            if (!TryGetNextCharReadLocation(out var page, out var chunkIndex, out var charIndex))
            {
                return -1;
            }

            return page.Value[chunkIndex].Span[charIndex];
        }

        private bool TryGetNextCharReadLocation(
            [NotNullWhen(true)] out LinkedListNode<ReadOnlyMemory<char>[]>? page,
            out int chunkIndex,
            out int charIndex)
        {
            page = _page;
            chunkIndex = _chunkIndex;
            charIndex = _charIndex;

            if (page is null)
            {
                return false;
            }

            do
            {
                var chunks = page.Value.AsSpan(chunkIndex);

                foreach (var chunk in chunks)
                {
                    if (charIndex < chunk.Length)
                    {
                        return true;
                    }

                    chunkIndex++;
                    charIndex = 0;
                }

                page = page.Next;
                chunkIndex = 0;
                charIndex = 0;
            }
            while (page is not null);

            chunkIndex = -1;
            charIndex = -1;

            return false;
        }

        public override int Read(char[] buffer, int index, int count)
        {
            ThrowHelper.ThrowIfNull(buffer);
            ThrowHelper.ThrowIfNegative(index);
            ThrowHelper.ThrowIfNegative(count);

            if (buffer.Length - index < count)
            {
                throw new ArgumentException(
                    $"{nameof(count)} is greater than the number of elements from {nameof(index)} to the end of {nameof(buffer)}.");
            }

            if (_page is null)
            {
                return 0;
            }

            var destination = buffer.AsSpan(index, count);
            var charsWritten = 0;

            var page = _page;
            var chunkIndex = _chunkIndex;
            var charIndex = _charIndex;

            Debug.Assert(chunkIndex >= 0);
            Debug.Assert(charIndex >= 0);

            do
            {
                var chunks = page.Value.AsSpan(chunkIndex);
                var isFirst = true;

                foreach (var chunk in chunks)
                {
                    if (destination.IsEmpty)
                    {
                        break;
                    }

                    var source = chunk.Span;

                    if (isFirst)
                    {
                        isFirst = false;

                        if (charIndex > 0)
                        {
                            source = source.Slice(charIndex);
                        }
                    }

                    if (source.Length > destination.Length)
                    {
                        source = source.Slice(0, destination.Length);
                        charIndex += source.Length;
                    }
                    else
                    {
                        chunkIndex++;
                        charIndex = 0;
                    }

                    if (source.IsEmpty)
                    {
                        continue;
                    }

                    source.CopyTo(destination);
                    destination = destination.Slice(source.Length);
                    charsWritten += source.Length;
                }

                if (destination.IsEmpty)
                {
                    break;
                }

                page = page.Next;
                chunkIndex = 0;
                charIndex = 0;
            }
            while (page is not null);

            if (page is not null)
            {
                _page = page;
                _chunkIndex = chunkIndex;
                _charIndex = charIndex;
            }
            else
            {
                _page = null;
                _chunkIndex = -1;
                _charIndex = -1;
            }

            _remainingLength -= charsWritten;

            return charsWritten;
        }

        public override string ReadToEnd()
        {
            if (_page is null)
            {
                return string.Empty;
            }

            // string.Create(SpanAction<...>) is unavailable on netstandard2.0.
            // Fill one exact-sized array, then construct the result string from it.
            var resultChars = new char[_remainingLength];
            var destination = resultChars.AsSpan();

            var page = _page;
            var chunkIndex = _chunkIndex;
            var charIndex = _charIndex;

            Debug.Assert(chunkIndex >= 0);
            Debug.Assert(charIndex >= 0);

            var chunks = page.Value.AsSpan(chunkIndex);

            do
            {
                foreach (var chunk in chunks)
                {
                    var source = chunk.Span;

                    if (charIndex > 0)
                    {
                        source = source.Slice(charIndex);
                        charIndex = 0;
                    }

                    if (source.IsEmpty)
                    {
                        continue;
                    }

                    source.CopyTo(destination);
                    destination = destination.Slice(source.Length);
                }

                page = page.Next;
                chunks = (page?.Value ?? Array.Empty<ReadOnlyMemory<char>>()).AsSpan();
            }
            while (page is not null);

            Debug.Assert(destination.Length == 0, "The destination span was not completely filled.");

            _page = null;
            _chunkIndex = -1;
            _charIndex = -1;
            _remainingLength = 0;

            return new string(resultChars);
        }
    }
}
