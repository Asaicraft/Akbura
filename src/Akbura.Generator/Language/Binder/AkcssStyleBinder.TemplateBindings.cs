using Akbura.Language.BoundTree;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Collections.Immutable;
using CandidateReason = Akbura.Language.Symbols.CandidateReason;
using RoslynSymbol = Microsoft.CodeAnalysis.ISymbol;

namespace Akbura.Language.Binder;

internal sealed partial class AkcssStyleBinder
{
    private static readonly ObjectPool<Dictionary<RoslynSymbol, AkburaSyntax>> s_akcssConfigurationPool =
        new(static () => new(SymbolEqualityComparer.Default));

    internal static Dictionary<RoslynSymbol, AkburaSyntax> RentAkcssConfigurationMap() => s_akcssConfigurationPool.Allocate();

    internal static void FreeAkcssConfigurationMap(Dictionary<RoslynSymbol, AkburaSyntax> configured)
    {
        if (configured.Count <= 64)
        {
            configured.Clear();
            s_akcssConfigurationPool.Free(configured);
        }
        else
        {
            s_akcssConfigurationPool.ForgetTrackedObject(configured);
        }
    }

    internal BoundNode BindMisplacedAkcssMarkupConfiguration(AkburaSyntax syntax, AkburaSyntax declaration)
    {
        var symbolInfo = SemanticModel.GetDeclarationSymbolInfo(declaration);
        if (symbolInfo.Symbol is not IAkcssDeclarationOwner owner)
        {
            return new BoundDeclaration(syntax, this, symbolInfo, SemanticModel.GetCachedSemanticDiagnostics(declaration));
        }

        var isInclude = syntax is MarkupIncludeDirectiveSyntax;
        var bound = InvalidAkcssMember(isInclude ? BoundKind.AkcssInclude : BoundKind.AkcssTemplateBinding,
            syntax, CreateAkcssContext(owner), isInclude ? ErrorCodes.AKBURA_SEMANTIC_AkcssMixin : ErrorCodes.AKBURA_SEMANTIC_AkcssTemplateBinding,
            "This configuration must appear directly in a markup element.");
        PublishAkcssContextualTree(bound);
        return bound;
    }

    internal ImmutableArray<BoundNode> BindAkcssElementConfigurations(MarkupElementSyntax element, AkburaSyntax declaration)
    {
        if (SemanticModel.GetDeclarationSymbolInfo(declaration).Symbol is not IAkcssDeclarationOwner owner)
        {
            return [];
        }

        var context = EnterAkcssElement(CreateAkcssContext(owner), element);
        var results = ArrayBuilder<BoundNode>.GetInstance();
        var configured = RentAkcssConfigurationMap();
        var diagnostics = ArrayBuilder<AkburaSemanticDiagnostic>.GetInstance();
        try
        {
            AddAkcssAttributeConfigurations(element, configured);
            foreach (var member in element.Body)
            {
                var bound = member switch
                {
                    MarkupIncludeDirectiveSyntax include => BindAkcssInclude(include, include.Directive, context),
                    MarkupTemplateBindDirectiveSyntax binding => BindAkcssTemplateBinding(binding, binding.Directive, context),
                    _ => null,
                };
                if (bound is null)
                {
                    continue;
                }

                CollectAkcssConfigurations(bound, configured, diagnostics);
                if (diagnostics.Count > 0)
                {
                    bound = bound.WithDiagnostics(bound.Diagnostics.AddRange(diagnostics));
                    diagnostics.Clear();
                }

                PublishAkcssContextualTree(bound);
                results.Add(bound);
            }

            return results.ToImmutable();
        }
        finally
        {
            diagnostics.Free();
            FreeAkcssConfigurationMap(configured);
            results.Free();
        }
    }

    private AkcssBindingContext EnterAkcssElement(AkcssBindingContext enclosing, MarkupElementSyntax element)
    {
        var context = enclosing with
        {
            OwningElement = element,
            IsMixinDefinition = false,
            StructuralParent = default,
            Selector = null,
            Subject = SemanticModel.GetSymbolInfo(element).Symbol is IMarkupComponentSymbol { ComponentType: { } type }
                ? new AkcssReceiver(new CSharpSymbolDefinition(type), AkcssReceiverOrigin.MarkupElement, element)
                : default,
        };
        if (enclosing.Owner is AkcssThemeSymbol theme &&
            AkcssSemanticFacts.FindTemplate(element) is { } template &&
            ReferenceEquals(template.Parent, theme.DeclarationSyntax) &&
            !SemanticModel.BindingSession.MarkupTemplateContent.IsInsideDeferredContent(element))
        {
            context = context with { ActiveTemplate = DescribeAkcssTemplate(theme, template).Template };
        }

        return context;
    }

    internal void CollectOtherConfiguredAkcssProperties(
        AkcssBindingContext context, MarkupElementSyntax element, AkcssTemplateBindSyntax editing,
        Dictionary<RoslynSymbol, AkburaSyntax> configured)
    {
        AddAkcssAttributeConfigurations(element, configured);
        foreach (var member in element.Body)
        {
            var bound = member switch
            {
                MarkupIncludeDirectiveSyntax include => BindAkcssInclude(include, include.Directive, context),
                MarkupTemplateBindDirectiveSyntax binding when !ReferenceEquals(editing, binding.Directive) =>
                    BindAkcssTemplateBinding(binding, binding.Directive, context),
                _ => null,
            };
            if (bound is not null)
            {
                CollectAkcssConfigurations(bound, configured, diagnostics: null);
            }
        }
    }

    private void AddAkcssAttributeConfigurations(MarkupElementSyntax element, Dictionary<RoslynSymbol, AkburaSyntax> configured)
    {
        if (element.StartTag is not { } tag)
        {
            return;
        }

        foreach (var attribute in tag.Attributes)
        {
            if (SemanticModel.GetSymbolInfo(attribute).Symbol is Symbols.IPropertySymbol property &&
                GetAkcssConfigurationIdentity(property) is { } identity && !configured.ContainsKey(identity))
            {
                configured.Add(identity, attribute);
            }
        }
    }

    private BoundAkcssContextual BindAkcssTemplateBinding(
        AkburaSyntax site, AkcssTemplateBindSyntax directive, AkcssBindingContext context)
    {
        var properties = ImmutableArray.CreateBuilder<AkcssTemplateBindingProperty>(directive.PropertyNames.Count);
        var diagnostics = ArrayBuilder<AkburaSemanticDiagnostic>.GetInstance();
        try
        {
            var isDeferred = context.IsMixinDefinition && context.ActiveTemplate is null;
            var receiver = context.PropertyReceiver.Symbol as INamedTypeSymbol;
            var parent = context.ActiveTemplate?.TemplatedParentType.Symbol as INamedTypeSymbol;
            var canBindDestination = receiver is not null && (context.OwningElement is not null || isDeferred);
            if (!isDeferred && (!canBindDestination || parent is null))
            {
                diagnostics.Add(CreateContextDiagnostic(site, context, ErrorCodes.AKBURA_SEMANTIC_AkcssTemplateBinding,
                    "Template binding requires an owning markup element and a typed control-template parent."));
            }

            foreach (var nameSyntax in directive.PropertyNames)
            {
                var name = nameSyntax.ToFullString().Trim();
                Symbols.IPropertySymbol? destination = null;
                Symbols.IPropertySymbol? source = null;
                if (canBindDestination && !SemanticModel.TryGetAkcssPropertyReference(name, receiver!, context.Owner, out destination))
                {
                    diagnostics.Add(CreateContextDiagnostic(nameSyntax, context, ErrorCodes.AKBURA_SEMANTIC_AkcssTemplateBinding,
                        $"'{receiver!.Name}' does not provide destination Avalonia property '{name}'."));
                }

                if (parent is not null && !SemanticModel.TryGetAkcssPropertyReference(name, parent, context.Owner, out source))
                {
                    diagnostics.Add(CreateContextDiagnostic(nameSyntax, context, ErrorCodes.AKBURA_SEMANTIC_AkcssTemplateBinding,
                        $"Templated parent '{parent.Name}' does not provide source Avalonia property '{name}'."));
                }

                properties.Add(new AkcssTemplateBindingProperty(nameSyntax, source, destination));
            }

            return new BoundAkcssContextual(BoundKind.AkcssTemplateBinding, site, this, context,
                new AkcssTemplateBindingData(context.ActiveTemplate, context.OwningElement, properties.MoveToImmutable(), isDeferred),
                AkburaSymbolInfo.None(CandidateReason.None), diagnostics.ToImmutable(), hasErrors: directive.ContainsDiagnostics);
        }
        finally
        {
            diagnostics.Free();
        }
    }

    private static void CollectAkcssConfigurations(
        BoundNode node, Dictionary<RoslynSymbol, AkburaSyntax> configured,
        ArrayBuilder<AkburaSemanticDiagnostic>? diagnostics)
    {
        if (node is BoundAkcssContextual bound)
        {
            switch (bound.Data)
            {
                case AkcssSetterData { Property: { } property }:
                    Add(property, bound.Syntax, bound.Context);
                    break;
                case AkcssTemplateBindingData bindings:
                    foreach (var binding in bindings.Properties)
                    {
                        if (binding.Destination is { } destination)
                        {
                            Add(destination, binding.Syntax, bound.Context);
                        }
                    }

                    break;
            }
        }

        foreach (var child in node.Children)
        {
            CollectAkcssConfigurations(child, configured, diagnostics);
        }

        void Add(Symbols.IPropertySymbol property, AkburaSyntax syntax, AkcssBindingContext context)
        {
            if (GetAkcssConfigurationIdentity(property) is not { } identity)
            {
                return;
            }

            if (configured.TryGetValue(identity, out var previous))
            {
                diagnostics?.Add(CreateContextDiagnostic(syntax, context, ErrorCodes.AKBURA_SEMANTIC_AkcssTemplateBinding,
                    $"Property '{property.Name}' is configured more than once on this element.", previous));
            }
            else
            {
                configured.Add(identity, context.Inclusion?.OutermostSite ?? syntax);
            }
        }
    }

    private static RoslynSymbol? GetAkcssConfigurationIdentity(Symbols.IPropertySymbol property) =>
        property.AvaloniaPropertyDefinition.Symbol ?? property.AttachedPropertyDefinition.Symbol ?? property.ClrPropertyDefinition.Symbol;
}
