using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using AkburaPropertySymbol = Akbura.Language.Symbols.IPropertySymbol;

namespace Akbura.Workspaces.QuickInfo;

internal sealed class AkburaQuickInfoService : IAkburaQuickInfoService
{
    private readonly AkcssReferenceResolver _referenceResolver;
    private readonly AkcssSymbolDisplayService _display = new();

    public AkburaQuickInfoService(AkcssReferenceResolver referenceResolver)
    {
        _referenceResolver = referenceResolver ??
            throw new ArgumentNullException(nameof(referenceResolver));
    }

    public AkburaQuickInfo? GetQuickInfo(
        AkburaDocumentContext context,
        int position,
        CancellationToken cancellationToken = default)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var semanticModel = context.Project.Compilation.GetSemanticModel(
            context.Document.SyntaxTree);
        var root = context.Document.SyntaxTree.GetRootSyntax();
        var lookup = position == root.FullSpan.End && position > 0
            ? position - 1
            : position;
        var declaration = root
            .FindToken(lookup)
            .Parent?
            .AncestorsAndSelf()
            .OfType<CommandDeclarationSyntax>()
            .FirstOrDefault();
        if (declaration != null &&
            declaration.Name.Span.Contains(position) &&
            semanticModel.GetSymbolInfo(declaration).Symbol is ICommandSymbol command)
        {
            return new AkburaQuickInfo(
                declaration.Name.Span,
                AkburaQuickInfoKind.Symbol,
                command.ToDisplayString(),
                ["Akbura command"]);
        }

        if (AkburaMarkupSemanticFacts.GetPropertyReference(semanticModel, position)
            is { } propertyReference)
        {
            if (propertyReference.OwnerSpan.Contains(position))
            {
                return new AkburaQuickInfo(propertyReference.OwnerSpan, AkburaQuickInfoKind.Type,
                    GetTypeSignature(propertyReference.LookupOwner), []);
            }

            if (propertyReference.PropertySpan.Contains(position))
            {
                return new AkburaQuickInfo(propertyReference.PropertySpan,
                    AkburaQuickInfoKind.Property, propertyReference.Field.ToDisplayString(),
                    ["Avalonia property reference", "Value type: " + propertyReference.ValueType
                        .ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)]);
            }
        }

        if (AkburaMarkupSemanticFacts.GetSelectorTypeReference(semanticModel, position)
            is { Type: { } selectorType } selectorReference)
        {
            return new AkburaQuickInfo(selectorReference.Span, AkburaQuickInfoKind.Type,
                GetTypeSignature(selectorType), ["Selector target type"]);
        }

        if (AkburaMarkupSemanticFacts.TryGetAssignment(semanticModel, position,
                out var assignmentProperty, out var contract, out var nameSpan))
        {
            using var details = ImmutableArrayBuilder<string>.Rent();
            if (contract.ContextualValueType != null &&
                !SymbolEqualityComparer.Default.Equals(contract.DeclaredType, contract.ContextualValueType))
            {
                details.Add("Contextual value type: " + contract.ContextualValueType
                    .ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
            }

            if (contract.TargetPropertyReference is { } target)
            {
                details.Add("Target property: " + target.Field.ToDisplayString());
            }

            if (contract.AssignBinding)
            {
                details.Add("Binding objects are assigned directly.");
            }

            return new AkburaQuickInfo(nameSpan, AkburaQuickInfoKind.Property,
                _display.FormatProperty(assignmentProperty), details.ToImmutable());
        }

        foreach (var expression in context.Document.SyntaxTree.GetRootSyntax().DescendantNodes())
        {
            if (!expression.Span.Contains(position))
            {
                continue;
            }

            var references = expression switch
            {
                InlineExpressionSyntax inline => semanticModel.GetCSharpSymbolReferences(inline),
                CSharpExpressionSyntax condition when condition.Parent is
                    MarkupIfStatementSyntax or MarkupElseIfClauseSyntax or MarkupCodeIfStatementSyntax or MarkupForeachKeyClauseSyntax =>
                    semanticModel.GetCSharpSymbolReferences(condition),
                MarkupForeachHeaderSyntax header => semanticModel.GetCSharpSymbolReferences(header),
                MarkupCodeStatementSyntax code => semanticModel.GetCSharpSymbolReferences(code),
                _ => default,
            };
            if (references.IsDefaultOrEmpty)
            {
                continue;
            }

            foreach (var symbolReference in references)
            {
                if (!symbolReference.SourceSpan.Contains(position))
                {
                    continue;
                }

                var signature = symbolReference.AkburaSymbol?.ToDisplayString() ??
                    GetCSharpSignature(symbolReference.CSharpDefinition.Symbol);
                if (signature != null)
                {
                    var details = symbolReference.CSharpDefinition.Symbol is { } projectedSymbol &&
                        Akbura.Workspaces.References.AkburaSymbolKeyFactory.TryGetProjectedLocalOrigin(projectedSymbol,
                            out var origin) && origin.Kind == Akbura.Language.Symbols.SymbolKind.MarkupLoopIndex
                        ? ImmutableArray.Create("Read-only zero-based index in the source sequence, before filtering or jumps.")
                        : ImmutableArray<string>.Empty;
                    return new AkburaQuickInfo(symbolReference.SourceSpan,
                        AkburaQuickInfoKind.Symbol, signature, details);
                }
            }
        }

        if (!_referenceResolver.TryResolve(
                context,
                position,
                out var reference,
                cancellationToken))
        {
            return null;
        }

        return CreateQuickInfo(reference, cancellationToken);
    }

    private AkburaQuickInfo? CreateQuickInfo(
        AkcssResolvedReference reference,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (reference.Kind == AkcssReferenceKind.PropertyOwnerType &&
            reference.CSharpDefinition.Symbol is ITypeSymbol type)
        {
            return new AkburaQuickInfo(
                reference.SourceSpan,
                AkburaQuickInfoKind.Type,
                GetTypeSignature(type),
                ImmutableArray<string>.Empty);
        }

        return reference.Symbol switch
        {
            AkburaPropertySymbol property => CreateProperty(reference, property),
            ITailwindUtilityParameterSymbol parameter =>
                CreateParameter(reference, parameter),
            ITailwindUtilitySymbol utility =>
                CreateUtility(reference, utility, cancellationToken),
            IAkcssModuleSymbol module =>
                CreateModule(reference, module, cancellationToken),
            IAkcssSymbol style => CreateStyle(reference, style),
            _ => null,
        };
    }

    private AkburaQuickInfo CreateProperty(
        AkcssResolvedReference reference,
        AkburaPropertySymbol property)
    {
        return new AkburaQuickInfo(
            reference.SourceSpan,
            AkburaQuickInfoKind.Property,
            _display.FormatProperty(property),
            [AkcssSymbolDisplayService.GetPropertyKind(property)]);
    }

    private AkburaQuickInfo CreateStyle(
        AkcssResolvedReference reference,
        IAkcssSymbol style)
    {
        using var details = ImmutableArrayBuilder<string>.Rent();
        var target = _display.FormatTarget(style);
        details.Add(string.IsNullOrEmpty(target)
            ? "Target: default AKCSS target"
            : "Target: " + target);
        if (style.IsIntercepted && style.InterceptType.Symbol is ITypeSymbol intercept)
        {
            details.Add("Intercept: " + _display.FormatType(intercept));
        }
        AddDeclaredIn(style, details);

        return new AkburaQuickInfo(
            reference.SourceSpan,
            AkburaQuickInfoKind.Style,
            _display.FormatStyle(style),
            details.ToImmutable());
    }

    private AkburaQuickInfo CreateUtility(
        AkcssResolvedReference reference,
        ITailwindUtilitySymbol utility,
        CancellationToken cancellationToken)
    {
        using var details = ImmutableArrayBuilder<string>.Rent();
        var target = _display.FormatTarget(utility);
        details.Add(string.IsNullOrEmpty(target)
            ? "Target: default AKCSS target"
            : "Target: " + target);
        AddDeclaredIn(utility, details);

        return new AkburaQuickInfo(
            reference.SourceSpan,
            AkburaQuickInfoKind.Utility,
            _display.FormatUtility(utility, cancellationToken),
            details.ToImmutable());
    }

    private AkburaQuickInfo CreateParameter(
        AkcssResolvedReference reference,
        ITailwindUtilityParameterSymbol parameter)
    {
        using var details = ImmutableArrayBuilder<string>.Rent();
        details.Add("AKCSS utility parameter");
        if (parameter.ContainingSymbol is ITailwindUtilitySymbol utility)
        {
            var target = _display.FormatTarget(utility);
            details.Add("Containing utility: " +
                (string.IsNullOrEmpty(target)
                    ? utility.Name
                    : target + "." + utility.Name));
        }

        return new AkburaQuickInfo(
            reference.SourceSpan,
            AkburaQuickInfoKind.Parameter,
            _display.FormatParameter(parameter),
            details.ToImmutable());
    }

    private AkburaQuickInfo CreateModule(
        AkcssResolvedReference reference,
        IAkcssModuleSymbol module,
        CancellationToken cancellationToken)
    {
        var styles = 0;
        var utilities = 0;
        foreach (var symbol in module.AkcssSymbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (symbol is ITailwindUtilitySymbol)
            {
                utilities++;
            }
            else
            {
                styles++;
            }
        }

        using var details = ImmutableArrayBuilder<string>.Rent();
        details.Add($"Styles: {styles} · Utilities: {utilities}");
        if (module.Path is { Length: > 0 } path)
        {
            details.Add("Source: " + path);
        }
        if (module is IMetadataAkcssModuleSymbol metadata)
        {
            details.Add("Assembly: " +
                metadata.RuntimeModuleType.ContainingAssembly.Name);
        }

        return new AkburaQuickInfo(
            reference.SourceSpan,
            AkburaQuickInfoKind.Module,
            _display.FormatModule(module),
            details.ToImmutable());
    }

    private static string GetTypeSignature(ITypeSymbol type)
    {
        var prefix = type.TypeKind switch
        {
            TypeKind.Class => "class ",
            TypeKind.Struct => "struct ",
            TypeKind.Interface => "interface ",
            TypeKind.Enum => "enum ",
            TypeKind.Delegate => "delegate ",
            _ => string.Empty,
        };
        return prefix + type.ToDisplayString(
            SymbolDisplayFormat.MinimallyQualifiedFormat);
    }

    private static string? GetCSharpSignature(Microsoft.CodeAnalysis.ISymbol? symbol)
    {
        return symbol is ILocalSymbol local
            ? local.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) + " " + local.Name
            : symbol?.ToDisplayString();
    }

    private static void AddDeclaredIn(
        IAkcssSymbol symbol,
        ImmutableArrayBuilder<string> details)
    {
        if (symbol.ContainingSymbol is IAkcssModuleSymbol
            {
                Path: { Length: > 0 } path,
            })
        {
            details.Add("Declared in: " + path);
        }
    }
}
