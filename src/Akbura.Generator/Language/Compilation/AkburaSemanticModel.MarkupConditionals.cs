using Akbura.Language.Binder;
using Akbura.Language.BoundTree;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using AkburaSymbol = Akbura.Language.Symbols.ISymbol;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akbura.Language;

internal abstract partial class AkburaSemanticModel
{
    internal static bool HasMarkupConditionalContent(Akbura.Language.Syntax.SyntaxList<MarkupContentSyntax> content)
    {
        foreach (var child in content)
        {
            if (child is MarkupIfStatementSyntax)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool HasMarkupElementOrConditionalContent(Akbura.Language.Syntax.SyntaxList<MarkupContentSyntax> content)
    {
        foreach (var child in content)
        {
            if (child is MarkupElementContentSyntax or MarkupIfStatementSyntax or MarkupForeachStatementSyntax)
            {
                return true;
            }
        }

        return false;
    }

    private static void AddMarkupConditionalHookDiagnostics(AkburaSyntax syntax,
        CSharpOperationDefinition operation, ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnostics)
    {
        if (!CSharpProbeBuilder.HasMarkupConditionalScope(syntax) &&
            !(syntax is CSharpExpressionSyntax condition && CSharpProbeBuilder.IsMarkupCondition(condition)))
        {
            return;
        }

        foreach (var invocation in EnumerateConditionalOperations(operation.Operation)
                .OfType<Microsoft.CodeAnalysis.Operations.IInvocationOperation>())
        {
            if (invocation.TargetMethod.GetAttributes().Any(static attribute =>
                    attribute.AttributeClass?.ToDisplayString() == "Akbura.CompilerAnotations.UseHookAttribute"))
            {
                diagnostics.Add(new AkburaSemanticDiagnostic(syntax,
                    ErrorCodes.AKBURA_SEMANTIC_MarkupConditionalHookNotSupported, []));
                break;
            }
        }
    }

    private static IEnumerable<Microsoft.CodeAnalysis.IOperation> EnumerateConditionalOperations(
        Microsoft.CodeAnalysis.IOperation? operation)
    {
        if (operation == null)
        {
            yield break;
        }

        yield return operation;
        foreach (var child in operation.ChildOperations)
        {
            foreach (var descendant in EnumerateConditionalOperations(child))
            {
                yield return descendant;
            }
        }
    }

    private static void AddMarkupConditionalDictionaryDiagnostics(MarkupContentModel model,
        ImmutableArray<MarkupChildContent> content, ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnostics)
    {
        var keys = new List<(IMarkupDictionaryKeyOperation Key, ImmutableDictionary<MarkupIfStatementSyntax, int> Guards)>();
        CollectConditionalDictionaryKeys(model, content,
            ImmutableDictionary<MarkupIfStatementSyntax, int>.Empty, keys, diagnostics);
    }

    internal void AddMarkupConditionalDestinationDiagnostics(MarkupIfStatementSyntax syntax,
        MarkupContentModel model, INamedTypeSymbol? ownerType,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnostics)
    {
        var property = model.ContentProperty.Symbol as Microsoft.CodeAnalysis.IPropertySymbol;
        var destinationType = property?.Type ?? model.ContentParameter?.Type.Symbol as ITypeSymbol ?? ownerType;
        var addOnlyProperty = model.Kind == MarkupContentKind.Property && property?.SetMethod == null &&
            destinationType is INamedTypeSymbol named && !GetMarkupContentAddMethods(named).IsDefaultOrEmpty;
        if (model.Kind is not (MarkupContentKind.Collection or MarkupContentKind.AddMethods) && !addOnlyProperty ||
            IsInsideOwnedMarkupStyle(syntax))
        {
            return;
        }

        if (destinationType is not IArrayTypeSymbol && destinationType != null &&
            IsReversibleMarkupConditionalList(destinationType, model.AllowedChildType.Symbol as ITypeSymbol))
        {
            return;
        }

        diagnostics.Add(new AkburaSemanticDiagnostic(syntax,
            ErrorCodes.AKBURA_SEMANTIC_UnsupportedConditionalContentDestination, []));
    }

    private bool IsInsideOwnedMarkupStyle(AkburaSyntax syntax)
    {
        var styleType = Compilation.CSharpCompilation.GetTypeByMetadataName("Avalonia.Styling.StyleBase");
        if (styleType == null)
        {
            return false;
        }

        for (var element = GetContainingMarkupElement(syntax); element != null;
             element = GetParentMarkupElement(element))
        {
            if (GetSymbolInfo(element).Symbol is IMarkupComponentSymbol { ComponentType: { } type } &&
                Compilation.CSharpCompilation.ClassifyConversion(type, styleType).IsImplicit)
            {
                return true;
            }
        }

        return false;
    }

    internal void AddMarkupConditionalTemplateRootDiagnostics(MarkupIfStatementSyntax syntax,
        MarkupContentModel model, ReadOnlySpan<BoundMarkupConditionalBranch> branches,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnostics)
    {
        var owner = GetContainingMarkupElement(syntax);
        if (owner != null)
        {
            AddExplicitConditionalTemplateItemDiagnostics(owner, branches, diagnostics);
        }

        var rootInfo = owner != null ? GetConditionalTemplateRootInfo(owner) : default;
        if (rootInfo.IsImplicitControlRoot && rootInfo.IsSupported)
        {
            return;
        }

        if (model.ContentProperty.Symbol is not Microsoft.CodeAnalysis.IPropertySymbol property)
        {
            return;
        }

        var unsupported = rootInfo.IsImplicitControlRoot ||
            BindingSession.MarkupTemplateContent.IsDeferredContentProperty(property);
        if (!unsupported && BindingSession.MarkupTemplateContent.IsDataTemplateProperty(property))
        {
            foreach (var branch in branches)
            {
                if (ContainsImplicitConditionalTemplateRoot(branch.Content))
                {
                    unsupported = true;
                    break;
                }
            }
        }

        if (unsupported)
        {
            diagnostics.Add(new AkburaSemanticDiagnostic(syntax,
                ErrorCodes.AKBURA_SEMANTIC_UnsupportedConditionalTemplateRoot, []));
        }
    }

    private bool ContainsImplicitConditionalTemplateRoot(ImmutableArray<MarkupChildContent> content)
    {
        var controlType = Compilation.CSharpCompilation.GetTypeByMetadataName("Avalonia.Controls.Control");
        var templateType = Compilation.CSharpCompilation.GetTypeByMetadataName("Avalonia.Controls.Templates.IDataTemplate");
        foreach (var child in content)
        {
            if (child.ConditionalOperation is { } conditional)
            {
                foreach (var branch in conditional.Branches)
                {
                    if (ContainsImplicitConditionalTemplateRoot(branch.Content))
                    {
                        return true;
                    }
                }
            }
            else if ((child.ComponentSymbol?.ComponentType ?? child.Type.Symbol as ITypeSymbol) is { } type &&
                controlType != null && Compilation.CSharpCompilation.ClassifyConversion(type, controlType).IsImplicit &&
                (templateType == null || !Compilation.CSharpCompilation.ClassifyConversion(type, templateType).IsImplicit))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsReversibleMarkupConditionalList(ITypeSymbol type, ITypeSymbol? elementType)
    {
        if (type is INamedTypeSymbol named && IsReversibleMarkupConditionalListContract(named, elementType))
        {
            return true;
        }

        foreach (var contract in type.AllInterfaces)
        {
            if (IsReversibleMarkupConditionalListContract(contract, elementType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsReversibleMarkupConditionalListContract(INamedTypeSymbol type, ITypeSymbol? elementType)
    {
        if (type.Name != "IList")
        {
            return false;
        }

        var namespaceName = type.ContainingNamespace.ToDisplayString();
        return type.Arity == 0 && namespaceName == "System.Collections" ||
            type.Arity == 1 && namespaceName == "System.Collections.Generic" &&
            SymbolEqualityComparer.Default.Equals(type.TypeArguments[0], elementType);
    }

    private static void CollectConditionalDictionaryKeys(MarkupContentModel model,
        ImmutableArray<MarkupChildContent> content, ImmutableDictionary<MarkupIfStatementSyntax, int> guards,
        List<(IMarkupDictionaryKeyOperation Key, ImmutableDictionary<MarkupIfStatementSyntax, int> Guards)> keys,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnostics)
    {
        foreach (var child in content)
        {
            if (child.ConditionalOperation is { } conditional)
            {
                for (var i = 0; i < conditional.Branches.Length; i++)
                {
                    CollectConditionalDictionaryKeys(model, conditional.Branches[i].Content,
                        guards.SetItem(conditional.Syntax, i), keys, diagnostics);
                }

                continue;
            }

            var key = child.ComponentSymbol?.AttributeOperations.OfType<IMarkupDictionaryKeyOperation>().FirstOrDefault();
            if (key == null || key.HasErrors || !key.HasConstantValue || key.ConstantValue is not { } constant ||
                (model.DictionaryShape.KeyType?.IsReferenceType == true && constant.GetType().IsValueType) ||
                model.DictionaryShape.KeyType?.SpecialType is SpecialType.System_Single or SpecialType.System_Double or
                    SpecialType.System_Decimal)
            {
                continue;
            }

            foreach (var previous in keys)
            {
                if (ReferenceEquals(previous.Guards, guards) || !Equals(previous.Key.ConstantValue, constant))
                {
                    continue;
                }

                var exclusive = false;
                foreach (var guard in guards)
                {
                    if (previous.Guards.TryGetValue(guard.Key, out var alternative) && alternative != guard.Value)
                    {
                        exclusive = true;
                        break;
                    }
                }

                if (exclusive)
                {
                    continue;
                }

                var alreadyReported = false;
                foreach (var diagnostic in diagnostics.WrittenSpan)
                {
                    if (diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_MarkupDictionaryDuplicateConstantKey &&
                        ReferenceEquals(diagnostic.Syntax, key.Syntax))
                    {
                        alreadyReported = true;
                        break;
                    }
                }

                if (!alreadyReported)
                {
                    diagnostics.Add(new AkburaSemanticDiagnostic(key.Syntax,
                        ErrorCodes.AKBURA_SEMANTIC_MarkupDictionaryDuplicateConstantKey,
                        [constant.ToString() ?? string.Empty]));
                }

                break;
            }

            keys.Add((key, guards));
        }
    }

    public ImmutableArray<CSharpSymbolReference> GetCSharpSymbolReferences(CSharpExpressionSyntax syntax)
    {
        ValidateSyntaxTreeOwnership(syntax);
        var expression = CSharpProbeBuilder.ParseMarkupCondition(syntax);
        return GetConnectedMarkupExpressionReferences(syntax, expression,
            syntax.Tokens.FullSpan.Start - expression.FullSpan.Start,
            isCondition: CSharpProbeBuilder.IsMarkupCondition(syntax));
    }

    private ImmutableArray<CSharpSymbolReference> GetConnectedMarkupExpressionReferences(AkburaSyntax syntax,
        CSharp.ExpressionSyntax expression, int sourceOffset, bool isCondition = false)
    {
        var binder = BindingSession.GetCSharpProbeBinder(syntax, BinderUsage.Markup);
        var builder = new CSharpProbeBuilder(binder);
        var root = isCondition && syntax is CSharpExpressionSyntax condition
            ? builder.CreateMarkupConditionProbe(condition, expression)
            : builder.CreateReturnExpressionProbe(syntax, expression, targetType: null);
        var semantic = CreateReferenceProbeSemanticModel(root, out var tree);
        var target = isCondition
            ? tree.GetRoot().GetAnnotatedNodes(CSharpProbeBuilder.MarkupConditionAnnotationKind)
                .OfType<CSharp.ExpressionSyntax>().Single()
            : CSharpProbeBuilder.GetReturnProbeExpression((CSharp.CompilationUnitSyntax)tree.GetRoot())!;
        var symbols = new Dictionary<string, AkburaSymbol>(System.StringComparer.Ordinal);
        var commands = new Dictionary<string, AkburaSymbol>(System.StringComparer.Ordinal);
        AddCSharpProbeRootSymbolMappings(symbols, commands);
        AddMarkupScopeSymbolMappings(syntax, symbols);
        return CollectCSharpSymbolReferences(semantic,
            [new CSharpReferenceTarget(expression, target, sourceOffset)], symbols, commands);
    }
}
