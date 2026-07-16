using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System;
using System.Threading;

namespace Akbura.Language.Symbols;

internal sealed class ModuleParamSymbol : Symbol, IParamSymbol
{
    private readonly AkburaReferencedSource _source;
    private readonly int _sourceStart;
    private readonly int _sourceLength;
    private readonly bool _hasDefaultValue;
    private ParamDeclarationSyntax? _lazySyntax;

    public ModuleParamSymbol(
        AkburaReferencedSource source,
        AkburaModuleComponentParameter parameter,
        CSharpSymbolDefinition type,
        ISymbol containingSymbol)
        : base(containingSymbol)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        if (parameter == null)
        {
            throw new ArgumentNullException(nameof(parameter));
        }

        Name = parameter.Name;
        Type = type;
        DefaultValueType = parameter.HasDefaultValue ? type : default;
        BindingKind = parameter.BindingKind;
        _hasDefaultValue = parameter.HasDefaultValue;
        _sourceStart = parameter.SourceStart;
        _sourceLength = parameter.SourceLength;
    }

    public override SymbolKind Kind => SymbolKind.Parameter;

    public override SymbolLanguage Language => SymbolLanguage.Akbura;

    public override string Name { get; }

    public ParamDeclarationSyntax DeclarationSyntax
    {
        get
        {
            var syntax = Volatile.Read(ref _lazySyntax);
            if (syntax != null)
            {
                return syntax;
            }

            syntax = _source.GetSyntax<ParamDeclarationSyntax>(_sourceStart, _sourceLength);
            return Interlocked.CompareExchange(ref _lazySyntax, syntax, null) ?? syntax;
        }
    }

    public ParamBindingKind BindingKind { get; }

    public CSharpSymbolDefinition Type { get; }

    public CSharpSymbolDefinition DefaultValueType { get; }

    public bool HasExplicitType => DeclarationSyntax.Type != null;

    public bool HasDefaultValue => _hasDefaultValue;

    public CSharpExpressionSyntax? DefaultValueSyntax =>
        _hasDefaultValue ? DeclarationSyntax.DefaultValue : null;

    public bool ReceivesValueFromParent =>
        BindingKind is ParamBindingKind.Default or ParamBindingKind.Bind;

    public bool SendsValueToParent =>
        BindingKind is ParamBindingKind.Bind or ParamBindingKind.Out;

    public bool IsTwoWayBinding => BindingKind == ParamBindingKind.Bind;

    public override void Accept(SymbolVisitor visitor)
    {
        visitor.VisitParameter(this);
    }

    public override TResult Accept<TResult>(SymbolVisitor<TResult> visitor)
    {
        return visitor.VisitParameter(this);
    }

    public override TResult Accept<TParameter, TResult>(
        SymbolVisitor<TParameter, TResult> visitor,
        TParameter parameter)
    {
        return visitor.VisitParameter(this, parameter);
    }

    public override string ToDisplayString()
    {
        var bindingText = BindingKind switch
        {
            ParamBindingKind.Bind => " bind",
            ParamBindingKind.Out => " out",
            _ => string.Empty,
        };

        return Type.IsDefault
            ? $"param{bindingText} {Name}"
            : $"param{bindingText} {Type.Name} {Name}";
    }
}
