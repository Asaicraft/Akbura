using Akbura.Language;
using Akbura.Language.Binder;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Akbura.Pools;
using System.Collections.Immutable;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Akbura.Workspaces;

internal sealed class AkburaCSharpProjection
{
    public AkburaCSharpProjection(
        CompilationUnitSyntax root,
        AkburaCSharpProjectionMapping activeMapping,
        ImmutableArray<AkburaCSharpProjectionMapping> mappings,
        int projectedPosition,
        ImmutableArray<string> stateNames,
        AkburaCSharpImportContext importContext,
        ImmutableArray<AkburaProjectedSymbolOrigin> syntheticSymbols)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));
        ActiveMapping = activeMapping;
        Mappings = mappings.IsDefault
            ? ImmutableArray<AkburaCSharpProjectionMapping>.Empty
            : mappings;
        ProjectedPosition = projectedPosition;
        StateNames = stateNames.IsDefault
            ? ImmutableArray<string>.Empty
            : stateNames;
        ImportContext = importContext ??
            throw new ArgumentNullException(nameof(importContext));
        SyntheticSymbols = syntheticSymbols.IsDefault
            ? ImmutableArray<AkburaProjectedSymbolOrigin>.Empty
            : syntheticSymbols;

        if (Mappings.IsDefaultOrEmpty ||
            Mappings[0].Kind !=
                AkburaCSharpProjectionMappingKind.ActiveFragment ||
            !Mappings[0].Equals(activeMapping))
        {
            throw new ArgumentException(
                "The active C# mapping must be the first mapping.",
                nameof(mappings));
        }
    }

    public CompilationUnitSyntax Root { get; }

    public AkburaCSharpProjectionMapping ActiveMapping { get; }

    public ImmutableArray<AkburaCSharpProjectionMapping> Mappings { get; }

    public TextSpan HostSpan => ActiveMapping.HostSpan;

    public TextSpan ProjectedSpan => ActiveMapping.ProjectedSpan;

    public int ProjectedPosition { get; }

    public ImmutableArray<string> StateNames { get; }

    public AkburaCSharpImportContext ImportContext { get; }

    public ImmutableArray<AkburaProjectedSymbolOrigin> SyntheticSymbols { get; }

    public AkburaCSharpProjection WithProjectedPosition(
        int projectedPosition)
    {
        if (projectedPosition < ActiveMapping.ProjectedSpan.Start ||
            projectedPosition > ActiveMapping.ProjectedSpan.End)
        {
            throw new ArgumentOutOfRangeException(
                nameof(projectedPosition));
        }

        if (projectedPosition == ProjectedPosition)
        {
            return this;
        }

        return new AkburaCSharpProjection(
            Root,
            ActiveMapping,
            Mappings,
            projectedPosition,
            StateNames,
            ImportContext,
            SyntheticSymbols);
    }

    public bool TryGetSyntheticOrigin(
        SyntaxNode declarationSyntax,
        out AkburaProjectedSymbolOrigin origin)
    {
        if (declarationSyntax == null)
        {
            throw new ArgumentNullException(nameof(declarationSyntax));
        }

        for (var current = declarationSyntax;
             current != null;
             current = current.Parent)
        {
            foreach (var annotation in current.GetAnnotations(
                         CSharpProbeBinder.ProjectedSymbolAnnotationKind))
            {
                if (!CSharpProbeSymbolOrigin.TryParse(
                        annotation.Data,
                        out var probeOrigin))
                {
                    continue;
                }

                foreach (var candidate in SyntheticSymbols)
                {
                    if (string.Equals(
                            candidate.AnnotationId,
                            probeOrigin.AnnotationId,
                            StringComparison.Ordinal))
                    {
                        origin = candidate;
                        return true;
                    }
                }
            }
        }

        origin = null!;
        return false;
    }

    public bool IsStateName(string name)
    {
        return !string.IsNullOrEmpty(name) &&
            StateNames.Contains(name, StringComparer.Ordinal);
    }

    public bool TryMapToHost(
        TextSpan projectedSpan,
        out TextSpan hostSpan)
    {
        foreach (var mapping in Mappings)
        {
            if (!Contains(mapping.ProjectedSpan, projectedSpan))
            {
                continue;
            }

            hostSpan = Translate(
                projectedSpan,
                mapping.ProjectedSpan,
                mapping.HostSpan);
            return true;
        }

        hostSpan = default;
        return false;
    }


    public bool TryMapToProjected(
        TextSpan hostSpan,
        out TextSpan projectedSpan)
    {
        foreach (var mapping in Mappings)
        {
            if (!Contains(mapping.HostSpan, hostSpan))
            {
                continue;
            }

            projectedSpan = Translate(
                hostSpan,
                mapping.HostSpan,
                mapping.ProjectedSpan);
            return true;
        }

        projectedSpan = default;
        return false;
    }

    public bool TryMapPositionToHost(
        int projectedPosition,
        out int hostPosition)
    {
        foreach (var mapping in Mappings)
        {
            if (projectedPosition < mapping.ProjectedSpan.Start ||
                projectedPosition > mapping.ProjectedSpan.End)
            {
                continue;
            }

            hostPosition = mapping.HostSpan.Start +
                projectedPosition -
                mapping.ProjectedSpan.Start;
            return true;
        }

        hostPosition = default;
        return false;
    }

    public bool TryMapPositionToProjected(
        int hostPosition,
        out int projectedPosition)
    {
        foreach (var mapping in Mappings)
        {
            if (hostPosition < mapping.HostSpan.Start ||
                hostPosition > mapping.HostSpan.End)
            {
                continue;
            }

            projectedPosition = mapping.ProjectedSpan.Start +
                hostPosition -
                mapping.HostSpan.Start;
            return true;
        }

        projectedPosition = default;
        return false;
    }

    private static bool Contains(TextSpan container, TextSpan value)
    {
        return value.Start >= container.Start &&
            value.End <= container.End;
    }

    private static TextSpan Translate(
        TextSpan value,
        TextSpan source,
        TextSpan target)
    {
        return new TextSpan(
            target.Start + value.Start - source.Start,
            value.Length);
    }
}

internal static class AkburaCSharpProjectionFactory
{
    private const string UsingMappingAnnotationKind =
        "AkburaCSharpUsingMapping";

    public static bool TryCreate(
        AkburaSyntacticDocument syntacticDocument,
        AkburaDocumentContext semanticContext,
        AkburaCSharpCompletionContext completionContext,
        out AkburaCSharpProjection projection,
        CancellationToken cancellationToken = default)
    {
        return TryCreate(
            syntacticDocument,
            semanticContext,
            new AkburaEmbeddedCSharpContext(
                completionContext.Kind,
                completionContext.OwnerKind,
                completionContext.OwnerSpan,
                completionContext.HostSpan,
                completionContext.HostPosition),
            out projection,
            cancellationToken);
    }

    public static bool TryCreate(
        AkburaSyntacticDocument syntacticDocument,
        AkburaDocumentContext semanticContext,
        AkburaEmbeddedCSharpContext embeddedContext,
        out AkburaCSharpProjection projection,
        CancellationToken cancellationToken = default)
    {
        return TryCreate(
            syntacticDocument,
            semanticContext,
            embeddedContext,
            out projection,
            out _,
            cancellationToken);
    }

    public static bool TryCreate(
        AkburaSyntacticDocument syntacticDocument,
        AkburaDocumentContext semanticContext,
        AkburaEmbeddedCSharpContext embeddedContext,
        out AkburaCSharpProjection projection,
        out string? failureReason,
        CancellationToken cancellationToken = default)
    {
        failureReason = null;

        if (syntacticDocument == null)
        {
            throw new ArgumentNullException(nameof(syntacticDocument));
        }

        if (semanticContext == null)
        {
            throw new ArgumentNullException(nameof(semanticContext));
        }

        if (!TryGetCurrentContext(
                syntacticDocument,
                semanticContext,
                out semanticContext))
        {
            projection = null!;
            failureReason =
                "current-semantic-context-unavailable";
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var root = semanticContext.Document.SyntaxTree
            .GetRootSyntax();
        var semanticModel = semanticContext.Project.Compilation
            .GetSemanticModel(
                semanticContext.Document.SyntaxTree);

        CSharpProbeProjection probe;
        try
        {
            probe = embeddedContext.Kind switch
            {
                AkburaCSharpCompletionContextKind.Expression =>
                    CreateExpressionProjection(
                        semanticModel,
                        root,
                        embeddedContext),

                AkburaCSharpCompletionContextKind.Statement =>
                    CreateStatementProjection(
                        semanticModel,
                        root,
                        embeddedContext),

                AkburaCSharpCompletionContextKind.Type =>
                    CreateTypeProjection(
                        semanticModel,
                        root,
                        syntacticDocument,
                        embeddedContext),

                AkburaCSharpCompletionContextKind
                    .UsingDirectiveName =>
                    CreateUsingProjection(
                        semanticModel,
                        root,
                        embeddedContext),

                AkburaCSharpCompletionContextKind
                    .CommandParameterList =>
                    CreateCommandParameterProjection(
                        semanticModel,
                        root,
                        embeddedContext),

                _ => throw new InvalidOperationException(
                    "The C# completion context is not supported."),
            };
        }
        catch (Exception exception)
            when (exception is
                InvalidOperationException or
                ArgumentException or
                InvalidCastException)
        {
            projection = null!;
            failureReason =
                exception.GetType().Name +
                ": " +
                exception.Message;
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (probe.ProjectedSpan.Length !=
            embeddedContext.HostSpan.Length)
        {
            projection = null!;
            failureReason =
                "span-length-mismatch: " +
                $"host={embeddedContext.HostSpan.Length}, " +
                $"projected={probe.ProjectedSpan.Length}";
            return false;
        }

        var projectedRoot = AddUsingMappings(
            syntacticDocument,
            embeddedContext,
            probe,
            out var projectedActiveSpan,
            out var usingMappings);
        var activeMapping = new AkburaCSharpProjectionMapping(
            AkburaCSharpProjectionMappingKind.ActiveFragment,
            embeddedContext.HostSpan,
            projectedActiveSpan);
        using var mappings =
            ImmutableArrayBuilder<AkburaCSharpProjectionMapping>.Rent();
        mappings.Add(activeMapping);
        mappings.AddRange(usingMappings);
        using var syntheticSymbols =
            ImmutableArrayBuilder<AkburaProjectedSymbolOrigin>.Rent();
        foreach (var symbolOrigin in probe.SymbolOrigins)
        {
            syntheticSymbols.Add(new AkburaProjectedSymbolOrigin(
                symbolOrigin.AnnotationId,
                symbolOrigin.Kind,
                symbolOrigin.Name,
                symbolOrigin.DeclarationSpan));
        }

        projection = new AkburaCSharpProjection(
            projectedRoot,
            activeMapping,
            mappings.ToImmutable(),
            projectedActiveSpan.Start +
                embeddedContext.RelativePosition,
            probe.StateNames,
            AkburaUsingEditService.CreateImportContext(
                syntacticDocument,
                embeddedContext.HostPosition),
            syntheticSymbols.ToImmutable());
        return true;
    }

    private static CSharpProbeProjection CreateExpressionProjection(
        AkburaSemanticModel semanticModel,
        AkburaSyntax root,
        AkburaEmbeddedCSharpContext context)
    {
        var syntax = FindSyntax<CSharpExpressionSyntax>(root, context);
        if (syntax == null ||
            !EmbeddedCSharpSyntaxFacts.TryGetExpression(
                syntax,
                out _,
                out var hostSpan) ||
            hostSpan != context.HostSpan)
        {
            throw new InvalidOperationException(
                "The C# expression no longer matches the current document.");
        }

        return semanticModel.CreateCSharpCompletionProjection(
            syntax,
            context.RelativePosition);
    }

    private static CSharpProbeProjection CreateStatementProjection(
        AkburaSemanticModel semanticModel,
        AkburaSyntax root,
        AkburaEmbeddedCSharpContext context)
    {
        var syntax = FindSyntax<CSharpStatementSyntax>(root, context);
        if (syntax == null ||
            !EmbeddedCSharpSyntaxFacts.TryGetStatement(
                syntax,
                out _,
                out var hostSpan) ||
            hostSpan != context.HostSpan)
        {
            throw new InvalidOperationException(
                "The C# statement no longer matches the current document.");
        }

        return semanticModel.CreateCSharpCompletionProjection(
            syntax,
            context.RelativePosition);
    }

    private static CSharpProbeProjection CreateTypeProjection(
        AkburaSemanticModel semanticModel,
        AkburaSyntax root,
        AkburaSyntacticDocument syntacticDocument,
        AkburaEmbeddedCSharpContext context)
    {
        var syntax = FindSyntax<CSharpTypeSyntax>(root, context);
        if (syntax != null &&
            syntax.Tokens.FullSpan == context.HostSpan)
        {
            return semanticModel.CreateCSharpCompletionProjection(
                syntax,
                context.RelativePosition);
        }

        var declaration = FindSyntax<AkburaSyntax>(
            root,
            context);
        if (declaration is not (
                StateDeclarationSyntax or
                ParamDeclarationSyntax or
                InjectDeclarationSyntax))
        {
            throw new InvalidOperationException(
                "The C# type no longer matches the current document.");
        }

        var type = CSharpSyntaxFactory.ParseTypeName(
            syntacticDocument.Text.ToString(context.HostSpan));
        return semanticModel.CreateCSharpCompletionProjection(
            declaration,
            type,
            context.RelativePosition);
    }

    private static CSharpProbeProjection CreateUsingProjection(
        AkburaSemanticModel semanticModel,
        AkburaSyntax root,
        AkburaEmbeddedCSharpContext context)
    {
        var akcssUsing = FindSyntax<AkcssUsingDirectiveSyntax>(
            root,
            context);
        if (akcssUsing != null)
        {
            if (akcssUsing.Name.Tokens.FullSpan != context.HostSpan)
            {
                throw new InvalidOperationException(
                    "The AKCSS using directive no longer matches the current document.");
            }

            return semanticModel.CreateCSharpCompletionProjection(
                akcssUsing,
                context.RelativePosition);
        }

        var syntax = FindSyntax<Akbura.Language.Syntax.UsingDirectiveSyntax>(
            root,
            context);
        if (syntax == null || syntax.Name.Tokens.FullSpan != context.HostSpan)
        {
            throw new InvalidOperationException(
                "The C# using directive no longer matches the current document.");
        }

        return semanticModel.CreateCSharpCompletionProjection(
            syntax,
            context.RelativePosition);
    }

    private static CSharpProbeProjection
        CreateCommandParameterProjection(
            AkburaSemanticModel semanticModel,
            AkburaSyntax root,
            AkburaEmbeddedCSharpContext context)
    {
        var syntax = FindSyntax<CSharpParameterListSyntax>(root, context);
        if (syntax == null ||
            syntax.Parameters.FullSpan != context.HostSpan)
        {
            throw new InvalidOperationException(
                "The command parameter list no longer matches the current document.");
        }

        return semanticModel.CreateCSharpCompletionProjection(
            syntax,
            context.RelativePosition);
    }

    private static TSyntax? FindSyntax<TSyntax>(
        AkburaSyntax root,
        AkburaEmbeddedCSharpContext context)
        where TSyntax : AkburaSyntax
    {
        return root.DescendantNodes()
            .OfType<TSyntax>()
            .FirstOrDefault(candidate =>
                candidate.Kind == context.OwnerKind &&
                candidate.FullSpan == context.OwnerSpan);
    }

    private static bool TryGetCurrentContext(
        AkburaSyntacticDocument syntacticDocument,
        AkburaDocumentContext semanticContext,
        out AkburaDocumentContext currentContext)
    {
        var semanticDocument = semanticContext.Document;
        if (semanticDocument.Text.ContentEquals(
                syntacticDocument.Text))
        {
            currentContext = semanticContext;
            return true;
        }

        if (!string.Equals(
                semanticDocument.FilePath,
                syntacticDocument.FilePath,
                StringComparison.OrdinalIgnoreCase) ||
            semanticDocument.SyntaxTree.Kind !=
                syntacticDocument.SyntaxTree.Kind)
        {
            currentContext = null!;
            return false;
        }

        var currentDocument = new AkburaDocumentSnapshot(
            semanticDocument.Id,
            semanticDocument.ProjectId,
            semanticDocument.Uri,
            semanticDocument.FilePath,
            VersionStamp.Create(),
            syntacticDocument.Text,
            syntacticDocument.SyntaxTree,
            semanticDocument.IsOpen);

        try
        {
            var currentProject = semanticContext.Project
                .ReplaceDocument(currentDocument);
            currentContext = new AkburaDocumentContext(
                currentProject,
                currentDocument);
            return true;
        }
        catch (ArgumentException)
        {
            currentContext = null!;
            return false;
        }
        catch (InvalidOperationException)
        {
            currentContext = null!;
            return false;
        }
    }

    private static CompilationUnitSyntax AddUsingMappings(
        AkburaSyntacticDocument document,
        AkburaEmbeddedCSharpContext context,
        CSharpProbeProjection probe,
        out TextSpan projectedActiveSpan,
        out ImmutableArray<AkburaCSharpProjectionMapping> mappings)
    {
        var root = probe.Root;
        var replacements = new Dictionary<
            Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax,
            Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax>();
        var mappingSources = new List<UsingMappingSource>();
        var claimedUsings = new HashSet<
            Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax>();
        var hostRoot = document.SyntaxTree.GetRootSyntax();
        var hasAkcssRegion = AkcssLanguageRegion.TryCreate(
            document.SyntaxTree,
            document.Text,
            context.HostPosition,
            out var akcssRegion);

        foreach (var hostUsing in hostRoot.DescendantNodes()
                     .OfType<Akbura.Language.Syntax.UsingDirectiveSyntax>())
        {
            if (hostUsing.FullSpan == context.OwnerSpan ||
                AkburaUsingEditService.IsAkcssUsingDirective(hostUsing))
            {
                continue;
            }

            Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax
                hostCSharpUsing;
            try
            {
                hostCSharpUsing = hostUsing.ToCSharp();
            }
            catch (Exception exception)
                when (exception is InvalidOperationException ||
                      exception is ArgumentException ||
                      exception is InvalidCastException)
            {
                continue;
            }

            var key = CSharpUsingKey.Create(hostCSharpUsing);
            var projectedUsing = root.Usings.FirstOrDefault(candidate =>
                !claimedUsings.Contains(candidate) &&
                CSharpUsingKey.Create(candidate).Equals(key));
            if (projectedUsing == null)
            {
                continue;
            }

            var hostSpan = hostUsing.Span;
            var parsedUsing = CSharpSyntaxFactory
                .ParseCompilationUnit(document.Text.ToString(hostSpan))
                .Usings
                .SingleOrDefault();
            if (parsedUsing == null ||
                parsedUsing.Span.Length != hostSpan.Length)
            {
                continue;
            }

            var annotation = new SyntaxAnnotation(
                UsingMappingAnnotationKind,
                Guid.NewGuid().ToString("N"));
            claimedUsings.Add(projectedUsing);
            replacements.Add(
                projectedUsing,
                parsedUsing.WithAdditionalAnnotations(annotation));
            mappingSources.Add(new UsingMappingSource(
                annotation,
                hostSpan));
        }

        foreach (var hostUsing in hostRoot.DescendantNodes()
                     .OfType<AkcssUsingDirectiveSyntax>())
        {
            if (!hasAkcssRegion ||
                hostUsing.FullSpan == context.OwnerSpan ||
                hostUsing.IsAkcssModuleImport ||
                akcssRegion.Kind == AkcssLanguageRegionKind.InlineBlock &&
                (hostUsing.Span.Start < akcssRegion.MembersSpan.Start ||
                 hostUsing.Span.End > akcssRegion.MembersSpan.End))
            {
                continue;
            }

            Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax
                hostCSharpUsing;
            try
            {
                hostCSharpUsing = hostUsing.ToCSharp();
            }
            catch (Exception exception)
                when (exception is InvalidOperationException ||
                      exception is ArgumentException ||
                      exception is InvalidCastException)
            {
                continue;
            }

            var key = CSharpUsingKey.Create(hostCSharpUsing);
            var projectedUsing = root.Usings.FirstOrDefault(candidate =>
                !claimedUsings.Contains(candidate) &&
                CSharpUsingKey.Create(candidate).Equals(key));
            if (projectedUsing?.Name == null)
            {
                continue;
            }

            var hostSpan = hostUsing.Name.Tokens.FullSpan;
            var annotation = new SyntaxAnnotation(
                UsingMappingAnnotationKind,
                Guid.NewGuid().ToString("N"));
            claimedUsings.Add(projectedUsing);
            replacements.Add(
                projectedUsing,
                projectedUsing.WithName(
                    projectedUsing.Name.WithAdditionalAnnotations(
                        annotation)));
            mappingSources.Add(new UsingMappingSource(
                annotation,
                hostSpan));
        }

        if (replacements.Count != 0)
        {
            root = root.ReplaceNodes(
                replacements.Keys,
                (original, _) => replacements[original]);
        }

        var activeNode = root
            .GetAnnotatedNodes(probe.ActiveAnnotation)
            .Single();
        projectedActiveSpan = activeNode.FullSpan;

        using var builder =
            ImmutableArrayBuilder<AkburaCSharpProjectionMapping>.Rent();
        foreach (var source in mappingSources)
        {
            var projectedNode = root
                .GetAnnotatedNodes(source.Annotation)
                .Single();
            if (projectedNode.Span.Length != source.HostSpan.Length)
            {
                continue;
            }

            builder.Add(new AkburaCSharpProjectionMapping(
                AkburaCSharpProjectionMappingKind.UsingDirective,
                source.HostSpan,
                projectedNode.Span));
        }

        mappings = builder.ToImmutable();
        return root;
    }

    private readonly struct UsingMappingSource
    {
        public UsingMappingSource(
            SyntaxAnnotation annotation,
            TextSpan hostSpan)
        {
            Annotation = annotation;
            HostSpan = hostSpan;
        }

        public SyntaxAnnotation Annotation { get; }

        public TextSpan HostSpan { get; }
    }
}
