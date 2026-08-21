// This file is adapted from dotnet/dotnet:
// https://github.com/dotnet/dotnet/blob/01abb3ec5c4cbffec5b33e02156bd3d2a8913b04/src/runtime/src/libraries/System.Private.CoreLib/src/System/CodeDom/Compiler/IndentedTextWriter.cs
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// The MIT License (MIT)
//
// Copyright (c) .NET Foundation and Contributors
//
// All rights reserved.
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Akbura.Language.CodeGeneration;

internal class IndentedTextWriter : TextWriter
{
    private readonly TextWriter _writer;
    private readonly string _tabString;
    private int _indentLevel;
    private bool _tabsPending;

    public const string DefaultTabString = "    ";

    public IndentedTextWriter(TextWriter writer)
        : this(writer, DefaultTabString)
    {
    }

    public IndentedTextWriter(TextWriter writer, string tabString)
        : base(CultureInfo.InvariantCulture)
    {
        if (writer is null)
        {
            throw new ArgumentNullException(nameof(writer));
        }

        _writer = writer;
        _tabString = tabString;
        _tabsPending = true;
    }

    public override Encoding Encoding => _writer.Encoding;

    [AllowNull]
    public override string NewLine
    {
        get => _writer.NewLine;
        set => _writer.NewLine = value;
    }

    public int Indent
    {
        get => _indentLevel;
        set => _indentLevel = Math.Max(value, 0);
    }

    public TextWriter InnerWriter => _writer;

    public override void Close() => _writer.Close();

    public override void Flush() => _writer.Flush();

    public override Task FlushAsync() => _writer.FlushAsync();

    protected virtual void OutputTabs()
    {
        if (!_tabsPending)
        {
            return;
        }

        for (var index = 0; index < _indentLevel; index++)
        {
            _writer.Write(_tabString);
        }

        _tabsPending = false;
    }

    protected virtual async Task OutputTabsAsync()
    {
        if (!_tabsPending)
        {
            return;
        }

        for (var index = 0; index < _indentLevel; index++)
        {
            await _writer
                .WriteAsync(_tabString)
                .ConfigureAwait(false);
        }

        _tabsPending = false;
    }

    public override void Write(string? value)
    {
        OutputTabs();
        _writer.Write(value);
    }

    public override void Write(bool value)
    {
        OutputTabs();
        _writer.Write(value);
    }

    public override void Write(char value)
    {
        OutputTabs();
        _writer.Write(value);
    }

    public override void Write(char[]? buffer)
    {
        OutputTabs();
        _writer.Write(buffer);
    }

    public override void Write(char[] buffer, int index, int count)
    {
        OutputTabs();
        _writer.Write(buffer, index, count);
    }

    public override void Write(double value)
    {
        OutputTabs();
        _writer.Write(value);
    }

    public override void Write(float value)
    {
        OutputTabs();
        _writer.Write(value);
    }

    public override void Write(int value)
    {
        OutputTabs();
        _writer.Write(value);
    }

    public override void Write(long value)
    {
        OutputTabs();
        _writer.Write(value);
    }

    public override void Write(object? value)
    {
        OutputTabs();
        _writer.Write(value);
    }

    public override void Write(
        [StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format,
        object? arg0)
    {
        OutputTabs();
        _writer.Write(format, arg0);
    }

    public override void Write(
        [StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format,
        object? arg0,
        object? arg1)
    {
        OutputTabs();
        _writer.Write(format, arg0, arg1);
    }

    public override void Write(
        [StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format,
        params object?[] arguments)
    {
        OutputTabs();
        _writer.Write(format, arguments);
    }

    public override async Task WriteAsync(char value)
    {
        await OutputTabsAsync().ConfigureAwait(false);
        await _writer.WriteAsync(value).ConfigureAwait(false);
    }

    public override async Task WriteAsync(
        char[] buffer,
        int index,
        int count)
    {
        await OutputTabsAsync().ConfigureAwait(false);
        await _writer
            .WriteAsync(buffer, index, count)
            .ConfigureAwait(false);
    }

    public override async Task WriteAsync(string? value)
    {
        await OutputTabsAsync().ConfigureAwait(false);
        await _writer.WriteAsync(value).ConfigureAwait(false);
    }

    public void WriteLineNoTabs(string? value) =>
        _writer.WriteLine(value);

    public Task WriteLineNoTabsAsync(string? value) =>
        _writer.WriteLineAsync(value);

    public override void WriteLine(string? value)
    {
        OutputTabs();
        _writer.WriteLine(value);
        _tabsPending = true;
    }

    public override void WriteLine()
    {
        OutputTabs();
        _writer.WriteLine();
        _tabsPending = true;
    }

    public override void WriteLine(bool value)
    {
        OutputTabs();
        _writer.WriteLine(value);
        _tabsPending = true;
    }

    public override void WriteLine(char value)
    {
        OutputTabs();
        _writer.WriteLine(value);
        _tabsPending = true;
    }

    public override void WriteLine(char[]? buffer)
    {
        OutputTabs();
        _writer.WriteLine(buffer);
        _tabsPending = true;
    }

    public override void WriteLine(
        char[] buffer,
        int index,
        int count)
    {
        OutputTabs();
        _writer.WriteLine(buffer, index, count);
        _tabsPending = true;
    }

    public override void WriteLine(double value)
    {
        OutputTabs();
        _writer.WriteLine(value);
        _tabsPending = true;
    }

    public override void WriteLine(float value)
    {
        OutputTabs();
        _writer.WriteLine(value);
        _tabsPending = true;
    }

    public override void WriteLine(int value)
    {
        OutputTabs();
        _writer.WriteLine(value);
        _tabsPending = true;
    }

    public override void WriteLine(long value)
    {
        OutputTabs();
        _writer.WriteLine(value);
        _tabsPending = true;
    }

    public override void WriteLine(object? value)
    {
        OutputTabs();
        _writer.WriteLine(value);
        _tabsPending = true;
    }

    public override void WriteLine(
        [StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format,
        object? arg0)
    {
        OutputTabs();
        _writer.WriteLine(format, arg0);
        _tabsPending = true;
    }

    public override void WriteLine(
        [StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format,
        object? arg0,
        object? arg1)
    {
        OutputTabs();
        _writer.WriteLine(format, arg0, arg1);
        _tabsPending = true;
    }

    public override void WriteLine(
        [StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format,
        params object?[] arguments)
    {
        OutputTabs();
        _writer.WriteLine(format, arguments);
        _tabsPending = true;
    }

    public override void WriteLine(uint value)
    {
        OutputTabs();
        _writer.WriteLine(value);
        _tabsPending = true;
    }

    public override async Task WriteLineAsync()
    {
        await OutputTabsAsync().ConfigureAwait(false);
        await _writer.WriteLineAsync().ConfigureAwait(false);
        _tabsPending = true;
    }

    public override async Task WriteLineAsync(char value)
    {
        await OutputTabsAsync().ConfigureAwait(false);
        await _writer.WriteLineAsync(value).ConfigureAwait(false);
        _tabsPending = true;
    }

    public override async Task WriteLineAsync(
        char[] buffer,
        int index,
        int count)
    {
        await OutputTabsAsync().ConfigureAwait(false);
        await _writer
            .WriteLineAsync(buffer, index, count)
            .ConfigureAwait(false);
        _tabsPending = true;
    }

    public override async Task WriteLineAsync(string? value)
    {
        await OutputTabsAsync().ConfigureAwait(false);
        await _writer.WriteLineAsync(value).ConfigureAwait(false);
        _tabsPending = true;
    }
}
