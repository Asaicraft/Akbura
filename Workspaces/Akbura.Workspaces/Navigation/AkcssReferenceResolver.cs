using Akbura.Language;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using System.Collections.Immutable;

namespace Akbura.Workspaces.Navigation;

internal sealed class AkcssReferenceResolver
{
    public bool TryResolve(
        AkburaDocumentContext context,
        int position,
        out AkcssResolvedReference reference,
        CancellationToken cancellationToken = default)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var document = context.Document;
        if ((uint)position >= (uint)document.Text.Length)
        {
            reference = default;
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var root = document.SyntaxTree.GetRootSyntax();
        var token = root.FindTokenInternal(position);
        var semanticModel = context.Project.Compilation.GetSemanticModel(
            document.SyntaxTree);

        for (var node = token.Parent; node != null; node = node.Parent)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (node)
            {
                case AkcssUsingDirectiveSyntax usingDirective
                    when TryResolveModuleImport(
                        semanticModel,
                        usingDirective,
                        position,
                        cancellationToken,
                        out reference):
                    return true;

                case AkcssApplyDirectiveSyntax apply
                    when TryResolveApplyItem(
                        semanticModel,
                        document,
                        apply,
                        position,
                        cancellationToken,
                        out reference):
                    return true;

                case AkcssAssignmentSyntax assignment
                    when TryResolveProperty(
                        semanticModel,
                        assignment,
                        position,
                        out reference):
                    return true;

                case AkcssUtilityParameterSyntax parameter
                    when TryResolveUtilityParameter(
                        semanticModel,
                        parameter,
                        position,
                        out reference):
                    return true;

                case AkcssUtilityDeclarationSyntax utility
                    when TryResolveUtilityDeclaration(
                        semanticModel,
                        utility,
                        position,
                        out reference):
                    return true;

                case AkcssStyleRuleSyntax style
                    when TryResolveStyleDeclaration(
                        semanticModel,
                        style,
                        position,
                        out reference):
                    return true;
            }
        }

        reference = default;
        return false;
    }

    public ImmutableArray<AkcssResolvedReference> GetApplyReferences(
        AkburaDocumentContext context,
        AkcssApplyDirectiveSyntax apply,
        CancellationToken cancellationToken = default)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var semanticModel = context.Project.Compilation.GetSemanticModel(
            context.Document.SyntaxTree);
        var references = semanticModel.GetAkcssApplyItemReferences(
            apply,
            cancellationToken);
        using var result =
            ImmutableArrayBuilder<AkcssResolvedReference>.Rent(
                references.Length);
        foreach (var reference in references)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Add(new AkcssResolvedReference(
                AkcssReferenceKind.ApplyItem,
                reference.SourceSpan,
                reference.Symbol,
                reference.Symbol?.CSharpDefinition ?? default));
        }

        return result.ToImmutable();
    }

    public ImmutableArray<AkcssResolvedReference> GetPropertyReferences(
        AkburaDocumentContext context,
        AkcssAssignmentSyntax assignment,
        CancellationToken cancellationToken = default)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (assignment == null)
        {
            throw new ArgumentNullException(nameof(assignment));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var semanticModel = context.Project.Compilation.GetSemanticModel(
            context.Document.SyntaxTree);
        return CreatePropertyReferences(semanticModel, assignment);
    }

    private static bool TryResolveModuleImport(
        AkburaSemanticModel semanticModel,
        AkcssUsingDirectiveSyntax usingDirective,
        int position,
        CancellationToken cancellationToken,
        out AkcssResolvedReference reference)
    {
        var sourceSpan = usingDirective.Name.Tokens.Span;
        if (!sourceSpan.Contains(position) ||
            !semanticModel.TryResolveAkcssModuleImport(
                usingDirective,
                out var module,
                cancellationToken))
        {
            reference = default;
            return false;
        }

        reference = new AkcssResolvedReference(
            AkcssReferenceKind.ModuleImport,
            sourceSpan,
            module,
            module.CSharpDefinition);
        return true;
    }

    private static bool TryResolveApplyItem(
        AkburaSemanticModel semanticModel,
        AkburaDocumentSnapshot document,
        AkcssApplyDirectiveSyntax apply,
        int position,
        CancellationToken cancellationToken,
        out AkcssResolvedReference reference)
    {
        if (!AkcssApplyItemFacts.TryGetReferenceItem(
                document.Text,
                apply,
                position,
                out var item))
        {
            reference = default;
            return false;
        }

        foreach (var candidate in semanticModel.GetAkcssApplyItemReferences(
                     apply,
                     cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidate.SourceSpan != item.Span || candidate.Symbol == null)
            {
                continue;
            }

            reference = new AkcssResolvedReference(
                AkcssReferenceKind.ApplyItem,
                candidate.SourceSpan,
                candidate.Symbol,
                candidate.Symbol.CSharpDefinition);
            return true;
        }

        reference = default;
        return false;
    }

    private static bool TryResolveProperty(
        AkburaSemanticModel semanticModel,
        AkcssAssignmentSyntax assignment,
        int position,
        out AkcssResolvedReference reference)
    {
        foreach (var candidate in CreatePropertyReferences(
                     semanticModel,
                     assignment))
        {
            if (candidate.SourceSpan.Contains(position))
            {
                reference = candidate;
                return true;
            }
        }

        reference = default;
        return false;
    }

    private static ImmutableArray<AkcssResolvedReference>
        CreatePropertyReferences(
            AkburaSemanticModel semanticModel,
            AkcssAssignmentSyntax assignment)
    {
        if (semanticModel.GetOperation(assignment) is not
                IAkcssPropertySetterOperation { Property: { } property } ||
            !AkcssPropertyReferenceFacts.TryGetSpans(
                assignment.PropertyName,
                out var spans))
        {
            return ImmutableArray<AkcssResolvedReference>.Empty;
        }

        using var references =
            ImmutableArrayBuilder<AkcssResolvedReference>.Rent(2);
        if (spans.OwnerSpan is { } ownerSpan &&
            AkcssPropertyReferenceFacts.GetPropertyOwnerType(property) is
                { } ownerType)
        {
            references.Add(new AkcssResolvedReference(
                AkcssReferenceKind.PropertyOwnerType,
                ownerSpan,
                symbol: null,
                new CSharpSymbolDefinition(ownerType)));
        }

        references.Add(new AkcssResolvedReference(
            AkcssReferenceKind.Property,
            spans.PropertySpan,
            property,
            property.WriteDefinition.IsDefault
                ? property.ReadDefinition
                : property.WriteDefinition));
        return references.ToImmutable();
    }

    private static bool TryResolveUtilityParameter(
        AkburaSemanticModel semanticModel,
        AkcssUtilityParameterSyntax parameter,
        int position,
        out AkcssResolvedReference reference)
    {
        var sourceSpan = parameter.ParamName.Identifier.Span;
        if (!sourceSpan.Contains(position) ||
            parameter.Parent is not AkcssUtilitySelectorSyntax selector ||
            selector.Parent is not AkcssUtilityDeclarationSyntax utility ||
            semanticModel.GetDeclaredSymbol(utility) is not
                ITailwindUtilitySymbol utilitySymbol)
        {
            reference = default;
            return false;
        }

        var index = utility.Selector.Parameters.IndexOf(parameter);
        if ((uint)index >= (uint)utilitySymbol.Parameters.Length)
        {
            reference = default;
            return false;
        }

        var symbol = utilitySymbol.Parameters[index];
        reference = new AkcssResolvedReference(
            AkcssReferenceKind.UtilityParameter,
            sourceSpan,
            symbol,
            symbol.CSharpDefinition);
        return true;
    }

    private static bool TryResolveUtilityDeclaration(
        AkburaSemanticModel semanticModel,
        AkcssUtilityDeclarationSyntax utility,
        int position,
        out AkcssResolvedReference reference)
    {
        var sourceSpan = utility.Selector.Name.Identifier.Span;
        if (!sourceSpan.Contains(position) ||
            semanticModel.GetDeclaredSymbol(utility) is not
                ITailwindUtilitySymbol symbol)
        {
            reference = default;
            return false;
        }

        reference = new AkcssResolvedReference(
            AkcssReferenceKind.UtilityDeclaration,
            sourceSpan,
            symbol,
            symbol.CSharpDefinition);
        return true;
    }

    private static bool TryResolveStyleDeclaration(
        AkburaSemanticModel semanticModel,
        AkcssStyleRuleSyntax style,
        int position,
        out AkcssResolvedReference reference)
    {
        if (style.Selector.Name is not { } name ||
            !name.Identifier.Span.Contains(position) ||
            semanticModel.GetDeclaredSymbol(style) is not IAkcssSymbol symbol)
        {
            reference = default;
            return false;
        }

        reference = new AkcssResolvedReference(
            AkcssReferenceKind.StyleDeclaration,
            name.Identifier.Span,
            symbol,
            symbol.CSharpDefinition);
        return true;
    }
}
