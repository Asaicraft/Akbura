using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;

namespace Akbura.Language.Symbols;

internal enum MarkupConditionalTemplateRootKind
{
    None,
    DataTemplate,
    DeferredDataTemplate,
    DeferredControlTemplate,
    UnsupportedDeferredTemplate,
}

internal readonly struct MarkupConditionalTemplateRootInfo
{
    public MarkupConditionalTemplateRootInfo(MarkupElementSyntax boundary,
        Microsoft.CodeAnalysis.IPropertySymbol property, ITypeSymbol resultType,
        MarkupContentModel contentModel, MarkupConditionalTemplateRootKind kind,
        bool isImplicitControlRoot, bool isSupported)
    {
        Boundary = boundary;
        Property = property;
        ResultType = resultType;
        ContentModel = contentModel;
        Kind = kind;
        IsImplicitControlRoot = isImplicitControlRoot;
        IsSupported = isSupported;
    }

    public MarkupElementSyntax? Boundary { get; }
    public Microsoft.CodeAnalysis.IPropertySymbol? Property { get; }
    public ITypeSymbol? ResultType { get; }
    public MarkupContentModel ContentModel { get; }
    public MarkupConditionalTemplateRootKind Kind { get; }
    public bool IsImplicitControlRoot { get; }
    public bool IsSupported { get; }
}
