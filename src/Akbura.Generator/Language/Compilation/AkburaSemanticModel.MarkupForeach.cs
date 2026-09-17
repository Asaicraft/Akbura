using Akbura.Language.Symbols;
using Akbura.Language.Binder;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;

namespace Akbura.Language;

internal abstract partial class AkburaSemanticModel
{
    internal static bool IsMarkupForeachKeyDirective(MarkupAttributeSyntax attribute) =>
        IsMarkupDirective(attribute, "id");

    internal CSharpProbeProjection CreateCSharpCompletionProjection(MarkupForeachHeaderSyntax syntax,
        int relativePosition) => new CSharpProbeBuilder(BindingSession.GetCSharpProbeBinder(syntax, BinderUsage.Markup))
        .CreateMarkupForeachHeaderProjection(syntax, relativePosition);

    internal CSharpProbeProjection CreateCSharpCompletionProjection(MarkupCodeStatementSyntax syntax,
        int relativePosition) => new CSharpProbeBuilder(BindingSession.GetCSharpProbeBinder(syntax, BinderUsage.Markup))
        .CreateMarkupLoopStatementProjection(syntax, relativePosition);

    internal void AddMarkupForeachDestinationDiagnostics(MarkupForeachStatementSyntax syntax,
        MarkupContentModel model, INamedTypeSymbol? ownerType,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnostics)
    {
        var property = model.ContentProperty.Symbol as Microsoft.CodeAnalysis.IPropertySymbol;
        var type = property?.Type ?? model.ContentParameter?.Type.Symbol as ITypeSymbol ?? ownerType;
        if (model.Kind == MarkupContentKind.Collection && type is not IArrayTypeSymbol && type != null &&
            IsReversibleMarkupConditionalList(type, model.AllowedChildType.Symbol as ITypeSymbol) &&
            !(type is INamedTypeSymbol { Name: "ReadOnlyCollection", ContainingNamespace: { } ns } &&
              ns.ToDisplayString() == "System.Collections.ObjectModel"))
        {
            return;
        }
        diagnostics.Add(new(syntax, ErrorCodes.AKBURA_SEMANTIC_UnsupportedForeachContentDestination, []));
    }
}
