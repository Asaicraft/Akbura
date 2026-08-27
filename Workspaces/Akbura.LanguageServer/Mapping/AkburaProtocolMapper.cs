using Akbura.Language.Syntax;

namespace Akbura.LanguageServer.Mapping;

internal static class AkburaProtocolMapper
{
    public static Uri ParseUri(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            throw new AkburaProtocolException(
                LspErrorCodes.InvalidParams,
                $"'{value}' is not a valid absolute document URI.");
        }

        return NormalizeWindowsFileUri(uri);
    }

    private static Uri NormalizeWindowsFileUri(Uri uri)
    {
        if (Path.DirectorySeparatorChar != '\\' ||
            !uri.IsFile ||
            !string.IsNullOrEmpty(uri.Host))
        {
            return uri;
        }

        var localPath = uri.LocalPath;
        if (localPath.Length < 4 ||
            localPath[0] is not ('/' or '\\') ||
            !char.IsAsciiLetter(localPath[1]) ||
            localPath[2] != ':' ||
            localPath[3] is not ('/' or '\\'))
        {
            return uri;
        }

        var windowsPath = localPath[1..]
            .Replace('/', Path.DirectorySeparatorChar);
        return new Uri(Path.GetFullPath(windowsPath));
    }

    public static Diagnostic[] ToDiagnostics(
        SourceText text,
        ImmutableArray<AkburaDiagnosticSpan> diagnostics,
        IAkburaPositionConverter positions)
    {
        var result = new Diagnostic[diagnostics.Length];
        for (var index = 0; index < diagnostics.Length; index++)
        {
            var diagnostic = diagnostics[index];
            result[index] = new Diagnostic
            {
                Range = positions.ToRange(text, diagnostic.Span),
                Severity = diagnostic.Severity switch
                {
                    AkburaDiagnosticSeverity.Error => 1,
                    AkburaDiagnosticSeverity.Warning => 2,
                    AkburaDiagnosticSeverity.Info => 3,
                    _ => 4,
                },
                Code = diagnostic.Code,
                Source = "akbura",
                Message = diagnostic.Message,
            };
        }

        return result;
    }

    public static TextEdit[] ToTextEdits(
        SourceText text,
        ImmutableArray<TextChange> changes,
        IAkburaPositionConverter positions)
    {
        var result = new TextEdit[changes.Length];
        for (var index = 0; index < changes.Length; index++)
        {
            result[index] = new TextEdit
            {
                Range = positions.ToRange(text, changes[index].Span),
                NewText = changes[index].NewText ?? string.Empty,
            };
        }

        return result;
    }

    public static int ToCompletionItemKind(AkburaCompletionKind kind)
    {
        return kind switch
        {
            AkburaCompletionKind.Component => 7,
            AkburaCompletionKind.ClosingTag => 7,
            AkburaCompletionKind.PropertyElement => 10,
            AkburaCompletionKind.Parameter => 6,
            AkburaCompletionKind.Property => 10,
            AkburaCompletionKind.Event => 23,
            AkburaCompletionKind.Command => 2,
            AkburaCompletionKind.MarkupExtension => 7,
            AkburaCompletionKind.AkcssStyle => 3,
            AkburaCompletionKind.AkcssModule => 9,
            AkburaCompletionKind.AkcssValue => 12,
            AkburaCompletionKind.AkcssColor => 16,
            AkburaCompletionKind.TailwindUtility => 3,
            AkburaCompletionKind.Keyword => 14,
            AkburaCompletionKind.Hook => 15,
            _ => 1,
        };
    }
    public static int ToCompletionItemKind(
        AkburaProjectedCompletionKind kind)
    {
        return kind switch
        {
            AkburaProjectedCompletionKind.Method => 2,
            AkburaProjectedCompletionKind.Constructor => 4,
            AkburaProjectedCompletionKind.Field => 5,
            AkburaProjectedCompletionKind.Variable => 6,
            AkburaProjectedCompletionKind.Class => 7,
            AkburaProjectedCompletionKind.Interface => 8,
            AkburaProjectedCompletionKind.Module => 9,
            AkburaProjectedCompletionKind.Property => 10,
            AkburaProjectedCompletionKind.Enum => 13,
            AkburaProjectedCompletionKind.Keyword => 14,
            AkburaProjectedCompletionKind.EnumMember => 20,
            AkburaProjectedCompletionKind.Constant => 21,
            AkburaProjectedCompletionKind.Struct => 22,
            AkburaProjectedCompletionKind.Event => 23,
            AkburaProjectedCompletionKind.Operator => 24,
            AkburaProjectedCompletionKind.TypeParameter => 25,
            _ => 1,
        };
    }
    public static string EscapeSnippet(string text)
    {
        return text
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("$", "\\$", StringComparison.Ordinal)
            .Replace("}", "\\}", StringComparison.Ordinal);
    }
}