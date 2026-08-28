using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Language;

internal sealed class MarkupWhitespaceNormalizer
{
    private readonly MarkupWhitespaceMode _mode;
    private readonly StringBuilder _literalBuilder = new();
    private readonly StringBuilder _interpolatedBuilder = new();

    private bool _hasOutput;
    private bool _hasPendingWhitespace;
    private bool _hasText;

    public MarkupWhitespaceNormalizer(
        MarkupWhitespaceMode mode)
    {
        _mode = mode;
    }

    public bool HasText => _hasText;

    public string LiteralText =>
        _literalBuilder.ToString();

    public string InterpolatedText =>
        _interpolatedBuilder.ToString();

    public void AppendText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (_mode == MarkupWhitespaceMode.Preserve)
        {
            AppendPreservedText(text);
            return;
        }

        AppendNormalizedText(text);
    }

    public void AppendExpression(string expressionText)
    {
        if (_mode == MarkupWhitespaceMode.Default)
        {
            FlushPendingWhitespace();
        }

        _interpolatedBuilder
            .Append('{')
            .Append(expressionText)
            .Append('}');

        _hasOutput = true;
    }

    private void AppendPreservedText(string text)
    {
        _literalBuilder.Append(text);
        AppendEscapedInterpolatedText(
            _interpolatedBuilder,
            text);

        _hasOutput = true;
        _hasText = true;
    }

    private void AppendNormalizedText(string text)
    {
        foreach (var character in text)
        {
            if (IsMarkupWhitespace(character))
            {
                _hasPendingWhitespace = true;
                continue;
            }

            FlushPendingWhitespace();
            AppendTextCharacter(character);

            _hasOutput = true;
            _hasText = true;
        }
    }

    private void FlushPendingWhitespace()
    {
        if (!_hasPendingWhitespace)
        {
            return;
        }

        if (_hasOutput)
        {
            AppendTextCharacter(' ');
        }

        _hasPendingWhitespace = false;
    }

    private void AppendTextCharacter(char character)
    {
        _literalBuilder.Append(character);

        switch (character)
        {
            case '{':
                _interpolatedBuilder.Append("{{");
                break;

            case '}':
                _interpolatedBuilder.Append("}}");
                break;

            case '"':
                _interpolatedBuilder.Append("\"\"");
                break;

            default:
                _interpolatedBuilder.Append(character);
                break;
        }
    }

    private static void AppendEscapedInterpolatedText(
        StringBuilder builder,
        string text)
    {
        foreach (var character in text)
        {
            switch (character)
            {
                case '{':
                    builder.Append("{{");
                    break;

                case '}':
                    builder.Append("}}");
                    break;

                case '"':
                    builder.Append("\"\"");
                    break;

                default:
                    builder.Append(character);
                    break;
            }
        }
    }

    private static bool IsMarkupWhitespace(char character)
    {
        return character is ' ' or '\t' or '\r' or '\n';
    }
}