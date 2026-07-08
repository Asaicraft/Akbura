using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Akbura.Language;

internal abstract class AkburaSyntaxTree
{
    protected AkburaSyntaxTree(SourceText text, string filePath)
    {
        Text = text;
        FilePath = filePath;
    }

    public SourceText Text { get; }

    public string FilePath { get; }

    public abstract SyntaxTreeKind Kind { get; }

    public virtual string ComponentName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(FilePath))
            {
                return string.Empty;
            }

            return Path.GetFileNameWithoutExtension(FilePath);
        }
    }

    public virtual AkburaDocumentSyntax GetRoot()
    {
        throw new System.InvalidOperationException("Only component syntax trees expose an Akbura document root.");
    }

    public abstract AkburaSyntax GetRootSyntax();

    public static ComponentSyntaxTree ParseText(string text, CancellationToken cancellationToken = default)
    {
        return ComponentSyntaxTree.ParseText(text, cancellationToken);
    }

    public static ComponentSyntaxTree ParseText(
        string text,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        return ComponentSyntaxTree.ParseText(text, filePath, cancellationToken);
    }

    public static ComponentSyntaxTree ParseText(SourceText text, CancellationToken cancellationToken = default)
    {
        return ComponentSyntaxTree.ParseText(text, cancellationToken);
    }

    public static ComponentSyntaxTree ParseText(
        SourceText text,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        return ComponentSyntaxTree.ParseText(text, filePath, cancellationToken);
    }
}
