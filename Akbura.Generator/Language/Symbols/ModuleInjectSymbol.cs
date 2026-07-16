using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System;
using System.Threading;

namespace Akbura.Language.Symbols;

internal sealed class ModuleInjectSymbol : Symbol, IInjectSymbol
{
    private readonly AkburaReferencedSource _source;
    private readonly int _sourceStart;
    private readonly int _sourceLength;
    private InjectDeclarationSyntax? _lazySyntax;

    public ModuleInjectSymbol(
        AkburaReferencedSource source,
        AkburaModuleComponentInject injectedService,
        CSharpSymbolDefinition type,
        ISymbol containingSymbol)
        : base(containingSymbol)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        if (injectedService == null)
        {
            throw new ArgumentNullException(nameof(injectedService));
        }

        Name = injectedService.Name;
        Type = type;
        _sourceStart = injectedService.SourceStart;
        _sourceLength = injectedService.SourceLength;
    }

    public override SymbolKind Kind => SymbolKind.InjectedService;

    public override SymbolLanguage Language => SymbolLanguage.Akbura;

    public override string Name { get; }

    public InjectDeclarationSyntax DeclarationSyntax
    {
        get
        {
            var syntax = Volatile.Read(ref _lazySyntax);
            if (syntax != null)
            {
                return syntax;
            }

            syntax = _source.GetSyntax<InjectDeclarationSyntax>(_sourceStart, _sourceLength);
            return Interlocked.CompareExchange(ref _lazySyntax, syntax, null) ?? syntax;
        }
    }

    public CSharpSymbolDefinition Type { get; }

    public bool IsRequired => true;

    public override void Accept(SymbolVisitor visitor)
    {
        visitor.VisitInject(this);
    }

    public override TResult Accept<TResult>(SymbolVisitor<TResult> visitor)
    {
        return visitor.VisitInject(this);
    }

    public override TResult Accept<TParameter, TResult>(
        SymbolVisitor<TParameter, TResult> visitor,
        TParameter parameter)
    {
        return visitor.VisitInject(this, parameter);
    }

    public override string ToDisplayString()
    {
        return Type.IsDefault
            ? $"inject {Name}"
            : $"inject {Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} {Name}";
    }
}
