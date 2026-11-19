using Akbura.Language.BoundTree;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using System.Linq;
using CandidateReason = Akbura.Language.Symbols.CandidateReason;

namespace Akbura.Language.Binder;

internal sealed partial class AkcssStyleBinder
{
    private BoundAkcssContextual BindAkcssMixin(
        AkcssMixinSymbol mixin, AkcssBindingContext context, AkburaSymbolInfo symbolInfo)
    {
        var children = ImmutableArray.CreateBuilder<BoundNode>();
        var optionalSeen = false;
        foreach (var parameter in mixin.Parameters)
        {
            using var diagnostics = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
            diagnostics.AddRange(SemanticModel.GetCachedSemanticDiagnostics(parameter.DeclarationSyntax));
            AkcssMixinArgument? defaultValue = null;
            if (parameter.DeclarationSyntax.Default is { } parameterDefault)
            {
                optionalSeen = true;
                defaultValue = BindAkcssDefaultArgument(parameter, parameterDefault.Expression, diagnostics);
            }
            else if (optionalSeen)
            {
                diagnostics.Add(new AkburaSemanticDiagnostic(parameter.DeclarationSyntax, ErrorCodes.AKBURA_SEMANTIC_AkcssMixin,
                    ["A required mixin parameter cannot follow an optional parameter."]));
            }

            children.Add(new BoundAkcssContextual(BoundKind.AkcssMixinParameter, parameter.DeclarationSyntax,
                this, context, new AkcssMixinParameterData(parameter, defaultValue), AkburaSymbolInfo.Success(parameter),
                diagnostics.ToImmutable()));
        }

        children.AddRange(BindContextualMembers(mixin.DeclarationSyntax.Members, context));
        return new BoundAkcssContextual(BoundKind.AkcssMixin, mixin.DeclarationSyntax, this, context,
            new AkcssDeclarationData(mixin), symbolInfo, SemanticModel.GetCachedSemanticDiagnostics(mixin.DeclarationSyntax),
            children.ToImmutable());
    }

    private BoundAkcssContextual BindAkcssInclude(
        AkburaSyntax site, AkcssIncludeSyntax directive, AkcssBindingContext caller)
    {
        var name = directive.Name.Identifier.ValueText;
        var matches = caller.DefinitionModel.LookupAkcssSourceDeclarations<AkcssMixinSymbol>(directive, name);
        if (matches.Length != 1)
        {
            var diagnostic = CreateContextDiagnostic(directive.Name, caller, ErrorCodes.AKBURA_SEMANTIC_AkcssMixin,
                matches.IsEmpty ? $"Mixin '{name}' could not be found." : $"The mixin name '{name}' is ambiguous.");
            return new BoundAkcssContextual(BoundKind.AkcssInclude, site, this, caller, new AkcssIncludeData(null, []),
                AkburaSymbolInfo.None(matches.IsEmpty ? CandidateReason.NotFound : CandidateReason.Ambiguous), [diagnostic]);
        }

        var mixin = matches[0];
        using var diagnostics = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
        var children = ImmutableArray.CreateBuilder<BoundNode>();
        var arguments = ImmutableArray.CreateBuilder<AkcssMixinArgument>();
        var inclusion = new AkcssInclusion(site, caller.Inclusion);
        var definitionContext = caller with
        {
            Owner = mixin,
            DefinitionModel = mixin.DefinitionModel,
            DefinitionReceiver = mixin.TargetType,
            IncludeChain = caller.IncludeChain.Add(mixin),
            Inclusion = inclusion,
            Arguments = [],
        };
        var definitionBinder = (AkcssStyleBinder)mixin.DefinitionModel.GetBinder(mixin.DeclarationSyntax, BinderUsage.Akcss);
        if (caller.IncludeChain.Contains(mixin))
        {
            var chain = string.Join(" -> ", definitionContext.IncludeChain.Select(static item => item.Name));
            diagnostics.Add(CreateContextDiagnostic(directive.Name, caller, ErrorCodes.AKBURA_SEMANTIC_AkcssMixin,
                $"Mixin include cycle: {chain}.", mixin.DeclarationSyntax));
            return MakeInclude(mixin, caller, site, [], diagnostics.ToImmutable(), []);
        }

        if (mixin.TargetType.Symbol is not ITypeSymbol requiredTarget ||
            (!caller.IsMixinDefinition || !caller.Subject.Type.IsDefault) &&
            (caller.Subject.Type.Symbol is not ITypeSymbol receiver || !AkburaSemanticModel.IsAssignableTo(receiver, requiredTarget)))
        {
            var actual = caller.Subject.Type.Symbol?.Name ?? "an unknown type";
            var expected = mixin.TargetType.Symbol?.Name ?? mixin.DeclarationSyntax.TargetType.ToFullString().Trim();
            diagnostics.Add(CreateContextDiagnostic(directive.Name, caller, ErrorCodes.AKBURA_SEMANTIC_AkcssMixin,
                $"Mixin '{name}' requires a receiver assignable to '{expected}'. This include selects {actual}.", mixin.DeclarationSyntax));
        }

        var supplied = directive.Arguments.Arguments;
        if (supplied.Count > mixin.Parameters.Length ||
            mixin.Parameters.Skip(supplied.Count).Any(static parameter => !parameter.IsOptional))
        {
            diagnostics.Add(CreateContextDiagnostic(directive.Arguments, caller, ErrorCodes.AKBURA_SEMANTIC_AkcssMixin,
                $"Mixin '{name}' cannot be called with {supplied.Count} argument(s).", mixin.DeclarationSyntax.Parameters));
        }

        foreach (var parameter in mixin.Parameters)
        {
            if (parameter.Ordinal < supplied.Count)
            {
                var argumentSyntax = supplied[parameter.Ordinal];
                using var argumentDiagnostics = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
                var argument = BindAkcssSuppliedArgument(parameter, argumentSyntax, caller, argumentDiagnostics);
                arguments.Add(argument);
                children.Add(new BoundAkcssContextual(BoundKind.AkcssMixinArgument, argumentSyntax, this, caller,
                    new AkcssMixinArgumentData(argument), AkburaSymbolInfo.Success(parameter), argumentDiagnostics.ToImmutable()));
            }
            else if (parameter.DeclarationSyntax.Default is { } parameterDefault)
            {
                using var defaultDiagnostics = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
                var argument = definitionBinder.BindAkcssDefaultArgument(parameter, parameterDefault.Expression, defaultDiagnostics);
                arguments.Add(argument);
                children.Add(new BoundAkcssContextual(BoundKind.AkcssMixinArgument, parameterDefault.Expression, definitionBinder,
                    definitionContext, new AkcssMixinArgumentData(argument), AkburaSymbolInfo.Success(parameter), defaultDiagnostics.ToImmutable()));
            }
        }

        var boundArguments = arguments.ToImmutable();
        if (diagnostics.Count == 0 && !children.Any(static child => child.HasErrors))
        {
            children.AddRange(definitionBinder.BindContextualMembers(mixin.DeclarationSyntax.Members,
                definitionContext with { Arguments = boundArguments }));
        }

        return MakeInclude(mixin, caller, site, boundArguments, diagnostics.ToImmutable(), children.ToImmutable());
    }

    private BoundAkcssContextual MakeInclude(
        AkcssMixinSymbol mixin, AkcssBindingContext caller, AkburaSyntax site,
        ImmutableArray<AkcssMixinArgument> arguments,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics, ImmutableArray<BoundNode> children) =>
        new(BoundKind.AkcssInclude, site, this, caller, new AkcssIncludeData(mixin, arguments),
            AkburaSymbolInfo.Success(mixin), diagnostics, children, hasErrors: site.ContainsDiagnostics);

    private AkcssMixinArgument BindAkcssSuppliedArgument(
        AkcssMixinParameterSymbol parameter, CSharpExpressionSyntax argument, AkcssBindingContext context,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnostics)
    {
        var binding = argument.GetRawCSharpExpression() is { } expression
            ? SemanticModel.BindAkcssExpression(expression, context, parameter.Type.Symbol as ITypeSymbol)
            : CSharpBindingResult.Empty;
        if (!IsAkcssArgumentConvertible(binding, parameter.Type.Symbol as ITypeSymbol))
        {
            diagnostics.Add(CreateContextDiagnostic(argument, context, ErrorCodes.AKBURA_SEMANTIC_AkcssMixin,
                $"Argument for '{parameter.Name}' must be implicitly convertible to '{parameter.Type.Symbol?.ToDisplayString()}'.",
                parameter.DeclarationSyntax));
        }

        return new AkcssMixinArgument(parameter, argument, binding, IsDefault: false);
    }

    private AkcssMixinArgument BindAkcssDefaultArgument(
        AkcssMixinParameterSymbol parameter, CSharpExpressionSyntax argument,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnostics)
    {
        var type = parameter.Type.Symbol as ITypeSymbol;
        var binding = argument.GetRawCSharpExpression() is { } expression && type is not null
            ? SemanticModel.BindAkcssOptionalDefault(parameter, expression, type)
            : CSharpBindingResult.Empty;
        if (!IsAkcssArgumentConvertible(binding, type))
        {
            diagnostics.Add(new AkburaSemanticDiagnostic(argument, ErrorCodes.AKBURA_SEMANTIC_AkcssMixin,
                [$"The default for '{parameter.Name}' must be a valid C# optional-parameter value of type '{type?.ToDisplayString()}'."]));
        }

        return new AkcssMixinArgument(parameter, argument, binding, IsDefault: true);
    }

    private static bool IsAkcssArgumentConvertible(CSharpBindingResult binding, ITypeSymbol? expectedType) =>
        expectedType is not null && !binding.Diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) &&
        (binding.Conversion.TargetType is not null
            ? binding.Conversion.IsImplicit
            : binding.TypeSymbol is not null && AkburaSemanticModel.IsAssignableTo(binding.TypeSymbol, expectedType));
}
