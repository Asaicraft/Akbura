using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using BinderType = Akbura.Language.Binder.Binder;
using System.Collections.Immutable;
using System.Text;

namespace Akbura.Language.BoundTree;

internal sealed class BoundMarkupWhitespaceDirective : BoundNode
{
    public BoundMarkupWhitespaceDirective(
        MarkupAttachedPropertyAttributeSyntax syntax,
        BinderType binder,
        AkburaSymbolInfo symbolInfo,
        IMarkupComponentSymbol? containingComponent,
        string rawValue,
        MarkupWhitespaceMode? declaredMode,
        MarkupWhitespaceMode effectiveMode,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default,
        bool hasErrors = false)
        : base(
            BoundKind.MarkupWhitespaceDirective,
            syntax,
            binder,
            symbolInfo,
            diagnostics,
            children: default,
            hasErrors)
    {
        ContainingComponent = containingComponent;
        RawValue = rawValue ?? string.Empty;
        DeclaredMode = declaredMode;
        EffectiveMode = effectiveMode;
    }

    public new MarkupAttachedPropertyAttributeSyntax Syntax =>
        (MarkupAttachedPropertyAttributeSyntax)base.Syntax;

    public IMarkupComponentSymbol? ContainingComponent { get; }

    public string RawValue { get; }

    public MarkupWhitespaceMode? DeclaredMode { get; }

    public MarkupWhitespaceMode EffectiveMode { get; }

    public BoundMarkupWhitespaceDirective Update(
        AkburaSymbolInfo symbolInfo,
        IMarkupComponentSymbol? containingComponent,
        string rawValue,
        MarkupWhitespaceMode? declaredMode,
        MarkupWhitespaceMode effectiveMode)
    {
        rawValue ??= string.Empty;

        if (ContainingComponent == containingComponent &&
            SymbolInfo.Equals(symbolInfo) &&
            string.Equals(RawValue, rawValue, StringComparison.Ordinal) &&
            DeclaredMode == declaredMode &&
            EffectiveMode == effectiveMode)
        {
            return this;
        }

        return new BoundMarkupWhitespaceDirective(
            Syntax,
            Binder,
            symbolInfo,
            containingComponent,
            rawValue,
            declaredMode,
            effectiveMode,
            Diagnostics,
            HasErrors);
    }

    public override void Accept(BoundTreeVisitor visitor)
    {
        visitor.VisitMarkupWhitespaceDirective(this);
    }

    public override TResult? Accept<TResult>(
        BoundTreeVisitor<TResult> visitor)
        where TResult : default
    {
        return visitor.VisitMarkupWhitespaceDirective(this);
    }

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default
    {
        return visitor.VisitMarkupWhitespaceDirective(
            this,
            parameter);
    }
}