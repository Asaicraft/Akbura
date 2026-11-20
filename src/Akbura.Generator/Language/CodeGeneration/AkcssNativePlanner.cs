using Akbura.Language.Binder;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Immutable;
using System.Globalization;
using System.Threading;
using RoslynSymbol = Microsoft.CodeAnalysis.ISymbol;
using NativeOperation = Akbura.Language.Operations.IOperation;
using OperationKind = Akbura.Language.Operations.OperationKind;

namespace Akbura.Language.CodeGeneration;

internal sealed class AkcssNativePlanner(AkburaSemanticModel model, CancellationToken cancellationToken)
{
    private static readonly ImmutableDictionary<AkcssMixinParameterSymbol, string> s_emptyLocals =
        ImmutableDictionary<AkcssMixinParameterSymbol, string>.Empty;
    private readonly ImmutableArray<AkburaSemanticDiagnostic>.Builder _diagnostics =
        ImmutableArray.CreateBuilder<AkburaSemanticDiagnostic>();
    private readonly INamedTypeSymbol? _styledElement =
        model.Compilation.CSharpCompilation.GetTypeByMetadataName("Avalonia.StyledElement");
    private AkcssNativeNames _names = new([]);

    public static AkcssNativeModulePlan? Create(in AkcssGenerationInput input, CancellationToken cancellationToken) =>
        Create(input.Module, input.SemanticModel, cancellationToken);

    internal static AkcssNativeModulePlan? Create(IAkcssModuleSymbol module, AkburaSemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (module.DeclaringSyntax is not { } syntax ||
            AkcssSemanticFacts.FindContextualModuleMember(syntax) is null)
        {
            return null;
        }

        return new AkcssNativePlanner(semanticModel, cancellationToken).Create(module);
    }

    private AkcssNativeModulePlan Create(IAkcssModuleSymbol module)
    {
        var export = model.SyntaxTree is AkcssSyntaxTree tree ? model.Compilation.AkcssPublicExports.Find(tree) : null;
        if (export != null)
        {
            _diagnostics.AddRange(export.Diagnostics);
        }

        var declarations = ImmutableArray.CreateBuilder<NativeOperation>();
        foreach (var owner in module.Declarations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (owner.DeclarationSyntax is not { } syntax)
            {
                continue;
            }

            if (owner is IAkcssSymbol)
            {
                if (AkcssSemanticFacts.HasContextualBody(syntax))
                {
                    var member = syntax.DescendantNodes().FirstOrDefault(AkcssSemanticFacts.IsContextualMember) ?? syntax;
                    _diagnostics.Add(Error(member, "Nested rules and includes in legacy class or utility applications require native fragment activation."));
                }
                continue;
            }

            var errors = model.GetSemanticDiagnostics(syntax)
                .Where(static diagnostic => diagnostic.Severity == AkburaDiagnosticSeverity.Error).ToImmutableArray();
            if (!errors.IsEmpty)
            {
                _diagnostics.AddRange(errors);
                continue;
            }

            if (model.GetOperation(syntax) is { HasErrors: false } operation)
            {
                declarations.Add(operation);
            }
            else
            {
                _diagnostics.Add(Error(syntax, "This declaration contains errors and cannot generate native AKCSS."));
            }
        }

        var reserved = AkcssNativeExpression.Enumerate(declarations.ToImmutable())
            .SelectMany(static operation => operation.CSharpDefinition.Syntax?.DescendantTokens() ?? Enumerable.Empty<Microsoft.CodeAnalysis.SyntaxToken>())
            .Where(static token => token.RawKind == (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.IdentifierToken)
            .Select(static token => token.ValueText);
        _names = new AkcssNativeNames(reserved);
        var styles = ImmutableArray.CreateBuilder<AkcssNativeStylePlan>();
        var themes = ImmutableArray.CreateBuilder<AkcssNativeThemePlan>();
        foreach (var operation in declarations.OfType<AkcssContextualOperation>())
        {
            if (operation.Kind == OperationKind.AkcssSelectorRule && operation.Data is AkcssSelectorData selector)
            {
                if (BuildStyle(operation, selector, s_emptyLocals) is { } style)
                {
                    styles.Add(style);
                }
            }
            else if (operation.Kind == OperationKind.AkcssTheme &&
                operation.Data is AkcssDeclarationData { Declaration: AkcssThemeSymbol theme })
            {
                if (ValidateTarget(theme.TargetType.Symbol, operation, "theme") is { } target)
                {
                    themes.Add(new AkcssNativeThemePlan(theme, target,
                        BuildMembers(operation.Children, s_emptyLocals, inTemplate: false)));
                }
            }
            // Mixin definitions are compile-time declarations. Includes lower their bound instances.
        }

        return new AkcssNativeModulePlan(styles.ToImmutable(), themes.ToImmutable(), export,
            _diagnostics.Distinct().ToImmutableArray(), _names.ToImmutable());
    }

    private AkcssNativeStylePlan? BuildStyle(AkcssContextualOperation operation, AkcssSelectorData data,
        ImmutableDictionary<AkcssMixinParameterSymbol, string> locals)
    {
        var diagnosticCount = _diagnostics.Count;
        ValidateCompound(data.Selector.Initial);
        foreach (var step in data.Selector.Steps)
        {
            ValidateCompound(step.Target);
        }

        if (diagnosticCount != _diagnostics.Count)
        {
            return null;
        }

        return new AkcssNativeStylePlan(operation.Syntax, data.Selector,
            BuildMembers(operation.Children, locals, inTemplate: false));

        void ValidateCompound(AkcssSelectorCompound compound)
        {
            foreach (var predicate in compound.Predicates)
            {
                if (predicate.Kind == AkcssPredicateKind.ExactType)
                {
                    ValidateTarget(predicate.Type.Symbol, operation, "selector", predicate.Syntax);
                }
            }
        }
    }

    private ImmutableArray<AkcssNativeMemberPlan> BuildMembers(ImmutableArray<NativeOperation> operations,
        ImmutableDictionary<AkcssMixinParameterSymbol, string> locals, bool inTemplate)
    {
        var result = ImmutableArray.CreateBuilder<AkcssNativeMemberPlan>();
        foreach (var operation in operations.OfType<AkcssContextualOperation>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (operation.Data)
            {
                case AkcssSetterData setter:
                    if (BuildSetter(operation, setter, locals, inTemplate) is { } assignment)
                    {
                        result.Add(assignment);
                    }
                    break;
                case AkcssSelectorData selector when !inTemplate:
                    if (BuildStyle(operation, selector, locals) is { } nested)
                    {
                        result.Add(nested);
                    }
                    break;
                case AkcssIncludeData include when include.Mixin != null:
                    result.Add(BuildInclude(operation, include, locals, inTemplate));
                    break;
                case AkcssBaseThemeData baseTheme:
                    if (baseTheme.SourceTheme is { } source)
                    {
                        result.Add(new AkcssNativeBaseThemePlan(operation.Syntax, GetModuleReference(source) + "." + GetThemeAccessor(source)));
                    }
                    else if (TryFormat(operation, baseTheme.ExternalTheme.OperationDefinition, locals, out var reference))
                    {
                        result.Add(new AkcssNativeBaseThemePlan(operation.Syntax, reference));
                    }
                    break;
                case AkcssTemplateData { Template: { } template }:
                    if (BuildTemplate(operation, template) is { } templatePlan)
                    {
                        result.Add(templatePlan);
                    }
                    break;
                case AkcssTemplateBindingData bindings when inTemplate && !bindings.IsDeferred &&
                    bindings.Template != null && bindings.Element != null:
                    foreach (var entry in bindings.Properties)
                    {
                        if (GetPropertyHandle(entry.Source) is { } sourceField &&
                            GetPropertyHandle(entry.Destination) is { } destinationField)
                        {
                            result.Add(new AkcssNativeTemplateBindingPlan(entry.Syntax, sourceField, destinationField));
                        }
                        else
                        {
                            Report(operation, "No native property handle is available for this template binding.", entry.Syntax);
                        }
                    }
                    break;
                case AkcssConditionData:
                    Report(operation, "Reactive @if in a native style, theme, or template include is not supported.");
                    break;
                case AkcssMixinArgumentData:
                    break;
                default:
                    Report(operation, $"Native generation is unavailable for {operation.Syntax.Kind} in this context.");
                    break;
            }
        }

        return result.ToImmutable();
    }

    private AkcssNativeIncludePlan BuildInclude(AkcssContextualOperation site, AkcssIncludeData include,
        ImmutableDictionary<AkcssMixinParameterSymbol, string> callerLocals, bool inTemplate)
    {
        var arguments = ImmutableArray.CreateBuilder<AkcssNativeArgumentPlan>();
        var locals = callerLocals;
        foreach (var child in site.Children.OfType<AkcssContextualOperation>())
        {
            if (child.Data is not AkcssMixinArgumentData { Argument: { } argument } ||
                argument.Parameter.Type.Symbol is not ITypeSymbol type)
            {
                continue;
            }

            if (TryFormat(child, argument.Binding.OperationDefinition, callerLocals, out var value))
            {
                var local = _names.Allocate("__akcssArgument");
                arguments.Add(new AkcssNativeArgumentPlan(argument.Source, argument.Parameter, local, type, value));
                locals = locals.SetItem(argument.Parameter, local);
            }
        }

        if (arguments.Count != include.Arguments.Length)
        {
            return new AkcssNativeIncludePlan(site.Syntax, arguments.ToImmutable(), []);
        }

        return new AkcssNativeIncludePlan(site.Syntax, arguments.ToImmutable(),
            BuildMembers(site.Children, locals, inTemplate));
    }

    private AkcssNativeControlTemplatePlan? BuildTemplate(AkcssContextualOperation operation, AkcssTemplateContext template)
    {
        var configurationStart = _diagnostics.Count;
        var configurations = ImmutableDictionary.CreateBuilder<MarkupElementSyntax, ImmutableArray<AkcssNativeMemberPlan>>();
        foreach (var attribute in AkcssNativeExpression.Enumerate(operation.Children).OfType<IMarkupPropertySetterOperation>())
        {
            if (attribute.ConvertedValue is AkcssAmxInvocationValue resource)
            {
                if (GetPropertyHandle(attribute.Property) is null ||
                    resource.Kind is not (AkcssAmxInvocationKind.DynamicResource or AkcssAmxInvocationKind.StaticResource) ||
                    resource.Key.IsDefault || resource.Key.ConstantValue is { HasValue: true, Value: null })
                {
                    _diagnostics.Add(Error(attribute.Syntax!, "A template resource requires a supported invocation, a non-null key, and an Avalonia property."));
                    continue;
                }
            }

            var value = attribute.ConvertedValue is AkcssAmxInvocationValue invocation
                ? invocation.Key.Operation
                : attribute.ValueOperation.Operation;
            if (value is { SemanticModel: { } semantic } &&
                value.DescendantsAndSelf().OfType<IInvocationOperation>().Any(call => AkcssNativeExpression.IsAmx(call.TargetMethod, semantic.Compilation)))
            {
                _diagnostics.Add(Error(attribute.Syntax!, "An embedded Amx invocation has no supported template lowering."));
            }
        }
        var environment = BindingWriterEnvironment.Create(model.Compilation.CSharpCompilation, withinType: null);
        foreach (var element in AkcssNativeExpression.Enumerate(operation.Children)
            .OfType<AkcssContextualOperation>().Where(static child => child.Kind == OperationKind.MarkupElement))
        {
            if (element.Syntax is MarkupElementSyntax markup)
            {
                if (element.TargetSymbol is IMarkupComponentSymbol { ComponentType: INamedTypeSymbol type } &&
                    (type.IsAbstract || !IsClosed(type) || !environment.IsAccessible(type) ||
                     !type.InstanceConstructors.Any(constructor => environment.IsAccessible(constructor) &&
                         constructor.Parameters.All(static parameter => parameter.IsOptional || parameter.IsParams))))
                {
                    Report(operation, $"Template element '{type.ToDisplayString()}' requires an accessible construction with no explicit arguments.", markup.StartTag?.Name);
                    continue;
                }

                var members = element.Children.OfType<AkcssContextualOperation>()
                    .Where(static child => child.Data is AkcssIncludeData or AkcssTemplateBindingData)
                    .Cast<NativeOperation>().ToImmutableArray();
                if (!members.IsEmpty)
                {
                    configurations.Add(markup, BuildMembers(members, s_emptyLocals, inTemplate: true));
                }
            }
        }

        return configurationStart == _diagnostics.Count
            ? new AkcssNativeControlTemplatePlan(operation.Syntax, template, configurations.ToImmutable())
            : null;
    }

    private AkcssNativeSetterPlan? BuildSetter(AkcssContextualOperation operation, AkcssSetterData setter,
        ImmutableDictionary<AkcssMixinParameterSymbol, string> locals, bool inTemplate)
    {
        var property = GetPropertyHandle(setter.Property);
        if (property is null && inTemplate &&
            setter.Property?.ClrPropertyDefinition.Symbol is Microsoft.CodeAnalysis.IPropertySymbol
                { SetMethod: { DeclaredAccessibility: Accessibility.Public } } clr)
        {
            property = clr;
        }

        if (property is null || setter.Property?.Type.Symbol is not ITypeSymbol type)
        {
            Report(operation, "A native style setter requires a resolved, writable Avalonia property.");
            return null;
        }

        if (setter.ConvertedValue is AkcssAmxInvocationValue resource)
        {
            if (resource.Kind == AkcssAmxInvocationKind.StaticResource)
            {
                Report(operation, "Static resources cannot be resolved in a native setter without an ambient resource context.");
                return null;
            }

            if (resource.Kind != AkcssAmxInvocationKind.DynamicResource)
            {
                Report(operation, "This Amx operation has no supported native lowering.");
                return null;
            }

            if (property is not IFieldSymbol ||
                resource.Key.ConstantValue is { HasValue: true, Value: null })
            {
                Report(operation, "A native dynamic resource requires a non-null key and an Avalonia setter destination.");
                return null;
            }

            return TryFormat(operation, resource.Key, locals, out var key)
                ? new AkcssNativeSetterPlan(operation.Syntax, property, type, setter.PropertyKind, key, IsDynamicResource: true)
                : null;
        }

        string value;
        switch (setter.ConvertedValue)
        {
            case AkcssColorValue color:
                value = AkcssExpressionGenerator.CreateColorExpression(color);
                break;
            case AkcssThicknessValue thickness:
                value = AkcssExpressionGenerator.CreateThicknessExpression(thickness);
                break;
            case CSharpSymbolDefinition member when AkcssExpressionGenerator.GetStaticMemberReference(member.Symbol) is { } reference:
                value = reference;
                break;
            default:
                if (!TryFormat(operation, setter.Value, locals, out value))
                {
                    return null;
                }
                break;
        }

        if (setter.RequiresBrushConversion)
        {
            value = AkcssExpressionGenerator.WrapSolidColorBrush(value);
        }
        return new AkcssNativeSetterPlan(operation.Syntax, property, type, setter.PropertyKind, value, IsDynamicResource: false);
    }

    private bool TryFormat(AkcssContextualOperation operation, CSharpOperationDefinition value,
        ImmutableDictionary<AkcssMixinParameterSymbol, string> locals, out string expression)
    {
        if (AkcssNativeExpression.TryFormat(operation, value, locals, out expression, out var error))
        {
            return true;
        }
        Report(operation, error);
        return false;
    }

    private INamedTypeSymbol? ValidateTarget(RoslynSymbol? type, AkcssContextualOperation operation,
        string role, AkburaSyntax? syntax = null)
    {
        if (type is INamedTypeSymbol { TypeKind: TypeKind.Class } named && IsClosed(named) &&
            _styledElement != null && AkburaSemanticModel.IsAssignableTo(named, _styledElement))
        {
            return named;
        }

        Report(operation, $"Native AKCSS {role} target '{type?.ToDisplayString()}' must be a closed Avalonia StyledElement type.", syntax);
        return null;
    }

    private static bool IsClosed(ITypeSymbol type) => type switch
    {
        ITypeParameterSymbol => false,
        INamedTypeSymbol name => !name.IsUnboundGenericType && name.TypeArguments.All(IsClosed) &&
            (name.ContainingType is null || IsClosed(name.ContainingType)),
        IArrayTypeSymbol array => IsClosed(array.ElementType),
        _ => type.TypeKind != TypeKind.Error,
    };

    private void Report(AkcssContextualOperation operation, string message, AkburaSyntax? syntax = null)
    {
        syntax ??= operation.Syntax;
        var site = operation.Context?.Inclusion?.OutermostSite;
        if (site is null && operation.Syntax.Parent is AkcssMixinArgumentListSyntax arguments)
        {
            site = arguments.Parent;
        }
        _diagnostics.Add(Error(site ?? syntax, message, site != null && !ReferenceEquals(site, syntax) ? [syntax] : []));
    }

    internal static AkburaSemanticDiagnostic Error(AkburaSyntax syntax, string message,
        ImmutableArray<AkburaSyntax> related = default) =>
        new(syntax, ErrorCodes.AKBURA_GENERATION_AkcssUnsupported, [message], relatedSyntax: related);

    private static RoslynSymbol? GetPropertyHandle(Symbols.IPropertySymbol? property) =>
        property?.AttachedPropertyDefinition.Symbol ?? property?.AvaloniaPropertyDefinition.Symbol;

    internal static string GetThemeAccessor(AkcssThemeSymbol theme) =>
        "__AkcssTheme" + theme.DeclarationSyntax.Span.Start.ToString(CultureInfo.InvariantCulture);

    internal static string GetModuleReference(AkcssThemeSymbol theme)
    {
        var model = theme.DefinitionModel;
        var compilation = model.Compilation;
        if (model.SyntaxTree is AkcssSyntaxTree tree)
        {
            var path = AkburaGenerationCatalogBuilder.GetSourcePath(tree, compilation.ProjectDirectory);
            return AkcssGeneratedModuleNames.GetFullyQualifiedTypeName(compilation.RootNamespace, path);
        }

        var componentTree = (ComponentSyntaxTree)model.SyntaxTree;
        var componentPath = AkburaGenerationCatalogBuilder.GetSourcePath(componentTree, compilation.ProjectDirectory);
        var component = (IAkburaComponentSymbol)model.GetDeclaredSymbol(componentTree.GetRoot())!;
        for (var index = 0; index < component.AkcssModules.Length; index++)
        {
            if (ReferenceEquals(component.AkcssModules[index].DeclaringSyntax, theme.ModuleSyntax))
            {
                var identity = AkburaGenerationCatalogBuilder.GetInlineAkcssModuleIdentity(componentPath, index);
                return AkcssGeneratedModuleNames.GetFullyQualifiedTypeName(compilation.RootNamespace, identity);
            }
        }

        throw new InvalidOperationException("An inline theme must belong to its component's AKCSS modules.");
    }
}
