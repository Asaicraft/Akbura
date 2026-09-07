using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using CSharp = Microsoft.CodeAnalysis.CSharp;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Exact, compilation-independent inputs to document generation. This deliberately
/// over-invalidates: source positions and embedded C# trivia are part of the shape.
/// </summary>
internal sealed class DocumentSyntaxVersion
{
    private readonly AkburaSyntaxTree _syntaxTree;

    private DocumentSyntaxVersion(
        AkburaSyntaxTree syntaxTree,
        int meaningfulLength,
        bool canReuse,
        string generationShape,
        ImmutableArray<string> surfaceShape,
        ImmutableArray<DocumentDependencyName> dependencyNames,
        bool requiresConservativeDependencies,
        bool hasGlobalUsings)
    {
        _syntaxTree = syntaxTree;
        Kind = syntaxTree.Kind;
        FilePath = syntaxTree.FilePath;
        LogicalName = syntaxTree is AkcssSyntaxTree akcssTree ? akcssTree.LogicalName : string.Empty;
        MeaningfulLength = meaningfulLength;
        CanReuse = canReuse;
        GenerationShape = generationShape;
        SurfaceShape = surfaceShape;
        DependencyNames = dependencyNames;
        RequiresConservativeDependencies = requiresConservativeDependencies;
        HasGlobalUsings = hasGlobalUsings;
    }

    public AkburaSyntaxTree SyntaxTree => _syntaxTree;
    public SyntaxTreeKind Kind { get; }
    public string FilePath { get; }
    public string LogicalName { get; }
    public int MeaningfulLength { get; }
    public bool CanReuse { get; }
    public string GenerationShape { get; }

    // Until a narrower semantic shape is proven, body and mapping versions both
    // retain the exact meaningful prefix. Neither comparison ignores trivia.
    public string BodyShape => GenerationShape;
    public string SourceMapShape => GenerationShape;

    public ImmutableArray<string> SurfaceShape { get; }
    public ImmutableArray<DocumentDependencyName> DependencyNames { get; }
    public bool RequiresConservativeDependencies { get; }
    public bool HasGlobalUsings { get; }

    public static DocumentSyntaxVersion Create(
        AkburaSyntaxTree syntaxTree,
        CancellationToken cancellationToken = default)
    {
        if (syntaxTree == null)
        {
            throw new ArgumentNullException(nameof(syntaxTree));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var root = syntaxTree.GetRootSyntax();
        var builder = new ShapeBuilder(cancellationToken);
        var canReuse = root.FullWidth == syntaxTree.Text.Length &&
            !root.ContainsDiagnostics && !root.ContainsSkippedText && !root.ContainsAnnotations;
        var meaningfulLength = 0;

        ScanTokens(root, ref meaningfulLength, ref canReuse, builder);
        builder.Collect(root);
        canReuse &= !builder.ContainsRecoverySyntax;

        // Only trivia after the last non-EOF token is eligible. In particular,
        // whitespace in AkTextLiteral or CSharpRawToken.Text is never trimmed.
        for (var i = meaningfulLength; i < syntaxTree.Text.Length; i++)
        {
            if (!char.IsWhiteSpace(syntaxTree.Text[i]))
            {
                meaningfulLength = syntaxTree.Text.Length;
                break;
            }
        }

        if (!canReuse || meaningfulLength > syntaxTree.Text.Length)
        {
            meaningfulLength = syntaxTree.Text.Length;
        }

        var shape = syntaxTree.Text.ToString(new TextSpan(0, meaningfulLength));
        builder.AddPotentialIdentifiers(shape);

        return new DocumentSyntaxVersion(
            syntaxTree,
            meaningfulLength,
            canReuse,
            shape,
            builder.Surface.ToImmutable(),
            builder.Dependencies.ToImmutable(),
            builder.RequiresConservativeDependencies || !canReuse,
            builder.HasGlobalUsings || GlobalUsings.IsComponentFile(syntaxTree) || GlobalUsings.IsAkcssFile(syntaxTree));
    }

    public bool HasSameGenerationShape(DocumentSyntaxVersion? other)
    {
        return other != null && CanReuse && other.CanReuse && HasSameIdentity(other) &&
            (ReferenceEquals(_syntaxTree, other._syntaxTree) ||
             string.Equals(GenerationShape, other.GenerationShape, StringComparison.Ordinal));
    }

    public bool HasSameSurface(DocumentSyntaxVersion? other)
    {
        if (other == null || !CanReuse || !other.CanReuse || !HasSameIdentity(other) ||
            SurfaceShape.Length != other.SurfaceShape.Length)
        {
            return false;
        }

        for (var i = 0; i < SurfaceShape.Length; i++)
        {
            if (!string.Equals(SurfaceShape[i], other.SurfaceShape[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private bool HasSameIdentity(DocumentSyntaxVersion other)
    {
        return Kind == other.Kind &&
            string.Equals(FilePath, other.FilePath, StringComparison.Ordinal) &&
            string.Equals(LogicalName, other.LogicalName, StringComparison.Ordinal);
    }

    private static void ScanTokens(
        AkburaSyntax root,
        ref int meaningfulLength,
        ref bool canReuse,
        ShapeBuilder builder)
    {
        foreach (var syntax in root.DescendantNodesAndTokens())
        {
            builder.CancellationToken.ThrowIfCancellationRequested();

            if (!syntax.IsToken)
            {
                continue;
            }

            var token = syntax.AsToken();

            // A valid zero-width EOF can be marked as missing.
            // Only missing meaningful tokens indicate parser recovery.
            if (token.IsMissing && token.Kind != SyntaxKind.EndOfFileToken)
            {
                canReuse = false;
            }

            if (token.Kind != SyntaxKind.EndOfFileToken)
            {
                meaningfulLength = Math.Max(meaningfulLength, token.Span.End);
            }

            if (token.Kind != SyntaxKind.CSharpRawToken)
            {
                continue;
            }

            var allowVoidReturnType = token.Parent is CSharpTypeSyntax { Parent: CommandDeclarationSyntax command } type &&
                ReferenceEquals(command.ReturnType, type);

            if (HasBlockingRawDiagnostics(token.GetRawCSharpSyntax(), allowVoidReturnType))
            {
                canReuse = false;
            }

            builder.AddCSharpIdentifiers(token.Text);
        }
    }

    private static bool HasBlockingRawDiagnostics(Microsoft.CodeAnalysis.SyntaxNode? syntax, bool allowVoidReturnType)
    {
        if (syntax is not { ContainsDiagnostics: true })
        {
            return false;
        }

        // ParseTypeName treats void as invalid outside a return-type context.
        // A command return type is precisely such a context; no other raw syntax
        // diagnostics (including unfinished strings) are exempted from recovery.
        if (!allowVoidReturnType || syntax is not CSharp.Syntax.PredefinedTypeSyntax type ||
            type.Keyword.RawKind != (int)CSharp.SyntaxKind.VoidKeyword)
        {
            return true;
        }

        foreach (var diagnostic in syntax.GetDiagnostics())
        {
            if (diagnostic.Id != "CS1547")
            {
                return true;
            }
        }

        return false;
    }

    private sealed class ShapeBuilder(CancellationToken cancellationToken)
    {
        private readonly HashSet<DocumentDependencyName> _dependencySet = [];

        public CancellationToken CancellationToken { get; } = cancellationToken;
        public ImmutableArray<string>.Builder Surface { get; } = ImmutableArray.CreateBuilder<string>();
        public ImmutableArray<DocumentDependencyName>.Builder Dependencies { get; } =
            ImmutableArray.CreateBuilder<DocumentDependencyName>();
        public bool RequiresConservativeDependencies { get; private set; }
        public bool HasGlobalUsings { get; private set; }
        public bool ContainsRecoverySyntax { get; private set; }

        public void Collect(AkburaSyntax root)
        {
            foreach (var syntax in root.DescendantNodesAndSelf())
            {
                CancellationToken.ThrowIfCancellationRequested();

                switch (syntax)
                {
                    case MarkupElementSyntax element when element.StartTag == null ||
                        element.StartTag.CloseToken.Kind != SyntaxKind.SlashGreaterToken && element.EndTag == null:
                        ContainsRecoverySyntax = true;
                        RequiresConservativeDependencies = true;
                        break;

                    case UsingDirectiveSyntax directive:
                        AddSurface(directive);
                        AddUsing(directive);
                        break;

                    case AkcssUsingDirectiveSyntax directive:
                        AddSurface(directive);
                        AddDependency(
                            directive.IsAkcssModuleImport ? DocumentDependencyKind.AkcssModule : DocumentDependencyKind.Namespace,
                            directive.Name.ToString().Trim());
                        break;

                    case NamespaceDeclarationSyntax:
                    case ParamDeclarationSyntax:
                    case StateDeclarationSyntax:
                    case CommandDeclarationSyntax:
                    case InjectDeclarationSyntax:
                        AddSurface(syntax);
                        break;

                    case CSharpStatementSyntax statement when statement.Parent is AkburaDocumentSyntax:
                        // User functions can become class members. Conservatively include
                        // their complete syntax until their exported signatures are classified.
                        AddSurface(statement);
                        break;

                    case AkcssStyleRuleSyntax style:
                        AddSurface(style.Selector);
                        break;

                    case AkcssUtilityDeclarationSyntax utility:
                        AddSurface(utility.Selector);
                        break;

                    case AkcssInterceptDirectiveSyntax intercept:
                        AddSurface(intercept);
                        break;

                    case MarkupStartTagSyntax tag:
                        AddDependency(DocumentDependencyKind.Component, tag.Name.ToString().Trim());
                        break;

                    case AkcssApplyDirectiveSyntax apply:
                        AddDependency(DocumentDependencyKind.AkcssApply, apply.Items.ToFullString().Trim());
                        break;
                }

                if (syntax is AkTopLevelMemberSyntax &&
                    syntax is not (UsingDirectiveSyntax or NamespaceDeclarationSyntax or
                        ParamDeclarationSyntax or StateDeclarationSyntax or CommandDeclarationSyntax or
                        InjectDeclarationSyntax or CSharpStatementSyntax or MarkupRootSyntax or InlineAkcssBlockSyntax))
                {
                    RequiresConservativeDependencies = true;
                    AddSurface(syntax);
                }

                if (syntax is AkcssTopLevelMemberSyntax &&
                    syntax is not (AkcssUsingDirectiveSyntax or AkcssStyleRuleSyntax or AkcssUtilitiesSectionSyntax))
                {
                    RequiresConservativeDependencies = true;
                    AddSurface(syntax);
                }

                if (syntax is AkcssBodyMemberSyntax &&
                    syntax is not (AkcssAssignmentSyntax or AkcssApplyDirectiveSyntax or
                        AkcssInterceptDirectiveSyntax or AkcssIfDirectiveSyntax or AkcssPseudoBlockSyntax) ||
                    syntax is MarkupContentSyntax &&
                    syntax is not (MarkupElementContentSyntax or MarkupInlineExpressionSyntax or MarkupTextLiteralSyntax) ||
                    syntax is MarkupAttributeSyntax &&
                    syntax is not (MarkupPlainAttributeSyntax or MarkupAttachedPropertyAttributeSyntax or
                        MarkupPrefixedAttributeSyntax or TailwindAttributeSyntax))
                {
                    RequiresConservativeDependencies = true;
                    AddSurface(syntax);
                }
            }
        }

        public void AddCSharpIdentifiers(string text)
        {
            // ValueText also covers escaped identifiers (for example F\u006fo).
            foreach (var token in CSharp.SyntaxFactory.ParseTokens(text))
            {
                CancellationToken.ThrowIfCancellationRequested();

                if (token.RawKind == (int)CSharp.SyntaxKind.IdentifierToken)
                {
                    AddDependency(DocumentDependencyKind.Identifier, token.ValueText);
                }
            }
        }

        public void AddPotentialIdentifiers(string text)
        {
            // A deliberate superset: references also occur in binding paths and
            // markup literals, not only in tags and embedded C# syntax nodes.
            for (var i = 0; i < text.Length; i++)
            {
                CancellationToken.ThrowIfCancellationRequested();

                if (text[i] != '_' && !char.IsLetter(text[i]))
                {
                    continue;
                }

                var start = i;
                while (i + 1 < text.Length &&
                    (text[i + 1] == '_' || char.IsLetterOrDigit(text[i + 1])))
                {
                    i++;
                }

                AddDependency(DocumentDependencyKind.Identifier, text.Substring(start, i - start + 1));
            }
        }

        private void AddUsing(UsingDirectiveSyntax directive)
        {
            var name = directive.Name.ToString().Trim();
            HasGlobalUsings |= directive.GlobalKeyword.RawKind != 0;

            if (name.EndsWith(".akcss", StringComparison.Ordinal))
            {
                AddDependency(DocumentDependencyKind.AkcssModule, name);
            }
            else if (directive.Alias is { } alias)
            {
                AddDependency(DocumentDependencyKind.Alias, name, alias.Name.Identifier.ValueText);
            }
            else
            {
                AddDependency(
                    directive.StaticKeyword.RawKind == 0 ? DocumentDependencyKind.Namespace : DocumentDependencyKind.StaticUsing,
                    name);
            }
        }

        private void AddSurface(AkburaSyntax syntax)
        {
            // Kind and payload are separate array entries: no delimiter or hash collision
            // can make different declaration sequences compare equal.
            Surface.Add(syntax.Kind.ToString());
            Surface.Add(syntax.ToString());
        }

        private void AddDependency(DocumentDependencyKind kind, string name, string alias = "")
        {
            if (name.Length == 0)
            {
                RequiresConservativeDependencies = true;
                return;
            }

            var dependency = new DocumentDependencyName(kind, name, alias);
            if (_dependencySet.Add(dependency))
            {
                Dependencies.Add(dependency);
            }
        }
    }
}
