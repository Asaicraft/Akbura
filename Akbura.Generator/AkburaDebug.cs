using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Akbura;

/// <summary>
/// Nullable support for Akbura project.
/// </summary>
internal static class AkburaDebug
{
    /// <inheritdoc cref="Debug.Assert(bool)"/>
    [Conditional("DEBUG")]
    public static void Assert([DoesNotReturnIf(false)] bool condition) => Debug.Assert(condition);

    /// <inheritdoc cref="Debug.Assert(bool, string)"/>
    [Conditional("DEBUG")]
    public static void Assert([DoesNotReturnIf(false)] bool condition, string message)
        => Debug.Assert(condition, message);

    /// <inheritdoc cref="Debug.Assert(bool, string)"/>
    [Conditional("DEBUG")]
    public static void Assert([DoesNotReturnIf(false)] bool condition, [InterpolatedStringHandlerArgument(nameof(condition))] ref AssertInterpolatedStringHandler message)
    {
        if (!condition)
        {
            Debug.Fail(message.ToStringAndClear());
        }
    }

    [Conditional("DEBUG")]
    public static void AssertNotNull<T>([NotNull] T value)
    {
        Assert(value is object, "Unexpected null reference");
    }


    [InterpolatedStringHandler]
    public readonly struct AssertInterpolatedStringHandler
    {
        private readonly StringBuilder? _builder;

        public AssertInterpolatedStringHandler(int literalLength, int formattedCount, bool condition, out bool shouldAppend)
        {
            shouldAppend = !condition;
            if (shouldAppend)
            {
                _builder = new StringBuilder(literalLength + formattedCount);
            }
        }

        internal string ToStringAndClear() => _builder!.ToString();

        public void AppendLiteral(string value) => _builder!.Append(value);

        public void AppendFormatted<T>(T value) => _builder!.Append(value);

        public void AppendFormatted(ReadOnlySpan<char> value) => _builder!.Append(value.ToString());
    }
}