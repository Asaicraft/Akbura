using Akbura.Language;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Language.CodeGeneration;

internal sealed class ComponentGenerationSourceMap
{
    private readonly ComponentSyntaxTree _syntaxTree;

    public ComponentGenerationSourceMap(ComponentSyntaxTree syntaxTree)
    {
        _syntaxTree = syntaxTree ?? throw new System.ArgumentNullException(nameof(syntaxTree));
    }

    public bool TryGetLineDirective(
        AkburaSyntax syntax,
        out LinePositionSpan lineSpan,
        out string path)
    {
        return TryGetLineDirective(syntax, syntax.Span, out lineSpan, out path);
    }

    public bool TryGetLineDirective(
        AkburaSyntax syntax,
        TextSpan relativeSpan,
        out LinePositionSpan lineSpan,
        out string path)
    {
        path = _syntaxTree.FilePath;
        if (!ReferenceEquals(syntax.Root, _syntaxTree.GetRootSyntax()) ||
            (uint)relativeSpan.Start > (uint)syntax.FullWidth ||
            (uint)relativeSpan.End > (uint)syntax.FullWidth ||
            relativeSpan.Length == 0)
        {
            lineSpan = default;
            path = string.Empty;
            return false;
        }

        var span = new TextSpan(syntax.Position + relativeSpan.Start, relativeSpan.Length);
        return TryGetLineDirective(path, span, out lineSpan);
    }

    private bool TryGetLineDirective(
        string path,
        TextSpan span,
        out LinePositionSpan lineSpan)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            path.IndexOf('"') >= 0 ||
            path.IndexOf('\r') >= 0 ||
            path.IndexOf('\n') >= 0 ||
            span.Length == 0 ||
            (uint)span.Start > (uint)_syntaxTree.Text.Length ||
            (uint)span.End > (uint)_syntaxTree.Text.Length)
        {
            lineSpan = default;
            return false;
        }

        lineSpan = _syntaxTree.Text.Lines.GetLinePositionSpan(span);
        if (!IsValid(lineSpan.Start) ||
            !IsValid(lineSpan.End) ||
            lineSpan.End.Line == lineSpan.Start.Line &&
            lineSpan.End.Character <= lineSpan.Start.Character)
        {
            lineSpan = default;
            return false;
        }

        return true;
    }

    private static bool IsValid(LinePosition position)
    {
        return (uint)position.Line < 0x20000000 &&
               position.Line != 0xfeefee &&
               (uint)position.Character < 0x10000;
    }
}
