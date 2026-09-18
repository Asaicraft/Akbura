using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Akbura.TestUtilities.Documentation;

/// <summary>
/// Reads the documentation embedded by DocumentationExamples.props. No network,
/// repository-relative working directory, or separately maintained snippet copy.
/// Supports the ATX headings and fenced blocks used by the registered pages.
/// </summary>
internal static class MarkdownDocumentation
{
    private const string ResourcePrefix = "Akbura.Documentation.";
    private static readonly Regex Fence = new(
        @"^ {0,3}(?<fence>`{3,}|~{3,})(?<info>.*)$",
        RegexOptions.CultureInvariant);
    private static readonly Regex Heading = new(
        @"^ {0,3}#{1,6}[ \t]+(?<heading>.*?)(?:[ \t]+#+)?[ \t]*$",
        RegexOptions.CultureInvariant);

    public static IReadOnlyList<MarkdownCodeBlock> Read(string relativePath)
    {
        var resourceName = ResourcePrefix + relativePath.Replace('\\', '/');
        var assembly = typeof(MarkdownDocumentation).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Documentation resource '{resourceName}' is missing in '{assembly.GetName().Name}'. " +
                "Check the DocumentationExamples.props import; this is a test failure, not a skip.");
        using var reader = new StreamReader(stream);
        return Extract(reader.ReadToEnd(), relativePath);
    }

    public static MarkdownCodeBlock Get(string path, string section, int ordinal, string language = "akbura")
    {
        var matches = Read(path).Where(block => block.Section == section &&
            block.Language == language && block.Ordinal == ordinal).ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException(
                $"Expected one {language} block at {path} / '{section}' #{ordinal + 1}; " +
                $"found {matches.Length}. Update the explicit catalog when documentation is reorganized.");
        }

        return matches[0];
    }

    public static IReadOnlyList<MarkdownCodeBlock> Extract(string markdown, string path)
    {
        var blocks = new List<MarkdownCodeBlock>();
        var ordinals = new Dictionary<(string Section, string Language), int>();
        var section = string.Empty;
        var fenceCharacter = '\0';
        var fenceLength = 0;
        var language = string.Empty;
        var codeStart = 0;
        var codeLine = 0;
        var position = 0;
        var lineNumber = 1;

        while (position < markdown.Length)
        {
            var lineStart = position;
            while (position < markdown.Length && markdown[position] is not ('\r' or '\n'))
            {
                position++;
            }

            var line = markdown.Substring(lineStart, position - lineStart);
            if (position < markdown.Length && markdown[position] == '\r') position++;
            if (position < markdown.Length && markdown[position] == '\n') position++;
            var match = Fence.Match(line);

            if (fenceLength > 0)
            {
                if (match.Success && match.Groups["fence"].Value[0] == fenceCharacter &&
                    match.Groups["fence"].Value.Length >= fenceLength &&
                    string.IsNullOrWhiteSpace(match.Groups["info"].Value))
                {
                    var key = (section, language);
                    ordinals.TryGetValue(key, out var ordinal);
                    blocks.Add(new MarkdownCodeBlock(path, section, ordinal, language, codeLine,
                        markdown.Substring(codeStart, lineStart - codeStart)));
                    ordinals[key] = ordinal + 1;
                    fenceLength = 0;
                }
            }
            else if (match.Success)
            {
                var fence = match.Groups["fence"].Value;
                var info = match.Groups["info"].Value.Trim();
                // An opening backtick fence cannot contain another backtick in its info string.
                if (fence[0] != '`' || !info.Contains('`'))
                {
                    fenceCharacter = fence[0];
                    fenceLength = fence.Length;
                    language = info.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault() ?? string.Empty;
                    codeStart = position;
                    codeLine = lineNumber + 1;
                }
            }
            else
            {
                var heading = Heading.Match(line);
                if (heading.Success) section = heading.Groups["heading"].Value.Trim();
            }

            lineNumber++;
        }

        if (fenceLength > 0)
        {
            throw new InvalidOperationException($"Unclosed code fence in {path}:{codeLine - 1}.");
        }

        return blocks;
    }
}

internal sealed record MarkdownCodeBlock(
    string Path,
    string Section,
    int Ordinal,
    string Language,
    int StartLine,
    string Code)
{
    public string Location => $"{Path}:{StartLine} / {Section} / {Language} #{Ordinal + 1}";
}
