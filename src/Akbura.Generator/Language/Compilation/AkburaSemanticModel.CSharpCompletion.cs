using Akbura.Language.Binder;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using System.Threading;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akbura.Language;

internal abstract partial class AkburaSemanticModel
{
    internal ImmutableArray<UseHookCompletionCandidate> LookupVisibleStateHooks(
        StateDeclarationSyntax declaration,
        string namePrefix,
        CancellationToken cancellationToken)
    {
        if (declaration == null)
        {
            throw new ArgumentNullException(nameof(declaration));
        }

        ValidateSyntaxTreeOwnership(declaration);
        return BindingSession
            .GetUseHookBinder(declaration, BinderUsage.Expression)
            .GetVisibleStateHookMethods(namePrefix, cancellationToken);
    }

    internal CSharpProbeProjection CreateCSharpCompletionProjection(CSharpExpressionSyntax expressionSyntax, int relativePosition)
    {
        if (expressionSyntax == null)
        {
            throw new ArgumentNullException(nameof(expressionSyntax));
        }

        ValidateSyntaxTreeOwnership(expressionSyntax);

        if (expressionSyntax.Parent is
                AkcssAssignmentSyntax or
                AkcssIfDirectiveSyntax)
        {
            return CreateAkcssCSharpCompletionProjection(
                expressionSyntax,
                relativePosition);
        }

        if (!EmbeddedCSharpSyntaxFacts.TryGetExpression(
                expressionSyntax,
                out var expression,
                out _))
        {
            throw new InvalidOperationException(
                "The expression could not be parsed as C#.");
        }

        var isMarkup = IsInsideMarkup(expressionSyntax);
        var scope = isMarkup
            ? GetMarkupBindingScope(expressionSyntax)
            : expressionSyntax;
        var binder = BindingSession.GetCSharpProbeBinder(
            scope,
            isMarkup
                ? BinderUsage.Markup
                : BinderUsage.Expression);

        if (CSharpProbeBuilder.IsMarkupCondition(expressionSyntax))
        {
            return new CSharpProbeBuilder(binder).CreateMarkupConditionProjection(expressionSyntax,
                expression, relativePosition);
        }

        Microsoft.CodeAnalysis.ITypeSymbol? expectedType = null;
        if (expressionSyntax.Parent is StateInitializerSyntax
            {
                Parent: StateDeclarationSyntax state,
            } initializer &&
            initializer.Kind != SyntaxKind.BindableStateInitializer &&
            GetDeclaredSymbol(state) is Symbols.IStateSymbol
            {
                HasExplicitType: true,
                Type.Symbol: ITypeSymbol stateType,
            })
        {
            expectedType = stateType;
        }

        if (isMarkup)
        {
            for (var node = expressionSyntax.Parent; node != null; node = node.Parent)
            {
                if (node is not MarkupAttributeSyntax attribute)
                {
                    continue;
                }

                if (IsMarkupDictionaryKeyDirective(attribute) &&
                    GetContainingMarkupElement(attribute) is { } child &&
                    TryGetMarkupDictionaryContext(child, out var contentModel))
                {
                    expectedType = contentModel.DictionaryShape.KeyType;
                }
                else if (TryGetMarkupCommandHandlerTargetType(
                    attribute,
                    expression,
                    out var commandHandlerType))
                {
                    expectedType = commandHandlerType;
                }

                break;
            }
        }

        return new CSharpProbeBuilder(binder)
            .CreateExpressionProjection(
                expressionSyntax,
                expression,
                relativePosition,
                expectedType);
    }

    private bool TryGetMarkupCommandHandlerTargetType(MarkupAttributeSyntax attribute, CSharp.ExpressionSyntax expression, out ITypeSymbol? targetType)
    {
        targetType = null;
        if (expression is not (CSharp.LambdaExpressionSyntax or CSharp.AnonymousMethodExpressionSyntax))
        {
            return false;
        }

        var parameterCount = expression switch
        {
            CSharp.ParenthesizedLambdaExpressionSyntax lambda => lambda.ParameterList.Parameters.Count,
            CSharp.SimpleLambdaExpressionSyntax => 1,
            CSharp.AnonymousMethodExpressionSyntax method => method.ParameterList?.Parameters.Count ?? 0,
            _ => 0,
        };
        var parameterTypes = ImmutableArray<ITypeSymbol>.Empty;
        var attributeSymbol = GetSymbolInfo(attribute).Symbol;
        if (attributeSymbol is Symbols.IPropertySymbol { Command: { } command })
        {
            parameterTypes = parameterCount == command.Parameters.Length
                ? command.Parameters.Select(static parameter => parameter.Type.Symbol)
                    .OfType<ITypeSymbol>()
                    .ToImmutableArray()
                : CreateObjectParameterTypes(parameterCount);
        }
        else if (IsICommandProperty(attributeSymbol))
        {
            var handler = AnalyzeMarkupICommandHandler(attribute, expression);
            parameterTypes = handler.ParameterTypes
                .Select(static parameter => parameter.Symbol)
                .OfType<ITypeSymbol>()
                .ToImmutableArray();
            if (parameterTypes.Length != parameterCount)
            {
                parameterTypes = CreateObjectParameterTypes(parameterCount);
            }
        }
        else
        {
            return false;
        }

        var isAsync = expression switch
        {
            CSharp.LambdaExpressionSyntax lambda => lambda.AsyncKeyword.RawKind != 0,
            CSharp.AnonymousMethodExpressionSyntax method => method.AsyncKeyword.RawKind != 0,
            _ => false,
        };
        var hasBlockBody = expression switch
        {
            CSharp.LambdaExpressionSyntax { Body: CSharp.BlockSyntax } => true,
            CSharp.AnonymousMethodExpressionSyntax => true,
            _ => false,
        };
        targetType = CreateMarkupHandlerDelegateType(parameterTypes, isAsync, hasBlockBody);
        return targetType != null;
    }

    private ImmutableArray<ITypeSymbol> CreateObjectParameterTypes(int count)
    {
        if (count == 0)
        {
            return [];
        }

        var objectType = Compilation.CSharpCompilation.GetSpecialType(SpecialType.System_Object);
        return Enumerable.Repeat<ITypeSymbol>(objectType, count).ToImmutableArray();
    }

    private ITypeSymbol? CreateMarkupHandlerDelegateType(ImmutableArray<ITypeSymbol> parameterTypes, bool isAsync, bool hasBlockBody)
    {
        if (hasBlockBody && !isAsync)
        {
            var metadataName = parameterTypes.Length == 0
                ? "System.Action"
                : "System.Action`" + parameterTypes.Length;
            var action = Compilation.CSharpCompilation.GetTypeByMetadataName(metadataName);
            return parameterTypes.Length == 0 || action == null
                ? action
                : action.Construct(parameterTypes.ToArray());
        }

        var resultType = Compilation.CSharpCompilation.GetSpecialType(SpecialType.System_Object);
        if (isAsync)
        {
            var task = Compilation.CSharpCompilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1");
            if (task == null)
            {
                return null;
            }

            resultType = task.Construct(resultType);
        }

        var func = Compilation.CSharpCompilation.GetTypeByMetadataName(
            "System.Func`" + (parameterTypes.Length + 1));
        return func?.Construct([.. parameterTypes, resultType]);
    }

    internal CSharpProbeProjection CreateCSharpCompletionProjection(CSharpStatementSyntax statementSyntax, int relativePosition)
    {
        if (statementSyntax == null)
        {
            throw new ArgumentNullException(nameof(statementSyntax));
        }

        ValidateSyntaxTreeOwnership(statementSyntax);
        if (!EmbeddedCSharpSyntaxFacts.TryGetStatement(
                statementSyntax,
                out var statement,
                out _))
        {
            throw new InvalidOperationException(
                "The statement could not be parsed as C#.");
        }

        var binder = BindingSession.GetCSharpProbeBinder(
            statementSyntax,
            BinderUsage.Expression);
        return new CSharpProbeBuilder(binder)
            .CreateStatementProjection(
                statementSyntax,
                statement,
                relativePosition);
    }

    internal CSharpProbeProjection CreateCSharpCompletionProjection(CSharpTypeSyntax typeSyntax, int relativePosition)
    {
        if (typeSyntax == null)
        {
            throw new ArgumentNullException(nameof(typeSyntax));
        }

        ValidateSyntaxTreeOwnership(typeSyntax);

        if (typeSyntax.Parent is
                AkcssStyleSelectorSyntax or
                AkcssUtilitySelectorSyntax or
                AkcssUtilityParameterSyntax or
                AkcssInterceptDirectiveSyntax)
        {
            return CreateAkcssCSharpCompletionProjection(
                typeSyntax,
                relativePosition);
        }

        if (!EmbeddedCSharpSyntaxFacts.TryGetType(
                typeSyntax,
                out var type,
                out _))
        {
            throw new InvalidOperationException(
                "The type could not be parsed as C#.");
        }

        var binder = BindingSession.GetCSharpProbeBinder(
            typeSyntax,
            BinderUsage.Expression);
        var builder = new CSharpProbeBuilder(binder);
        return typeSyntax.Parent is CommandDeclarationSyntax
            ? builder.CreateReturnTypeProjection(
                type,
                relativePosition)
            : builder.CreateTypeProjection(
                type,
                relativePosition);
    }

    internal CSharpProbeProjection CreateCSharpCompletionProjection(AkburaSyntax declarationSyntax, CSharp.TypeSyntax type, int relativePosition)
    {
        if (declarationSyntax == null)
        {
            throw new ArgumentNullException(nameof(declarationSyntax));
        }

        if (type == null)
        {
            throw new ArgumentNullException(nameof(type));
        }

        ValidateSyntaxTreeOwnership(declarationSyntax);
        if (declarationSyntax is not (
                StateDeclarationSyntax or
                ParamDeclarationSyntax or
                InjectDeclarationSyntax))
        {
            throw new ArgumentException(
                "Only component declarations support a synthetic type projection.",
                nameof(declarationSyntax));
        }

        var binder = BindingSession.GetCSharpProbeBinder(
            declarationSyntax,
            BinderUsage.Expression);
        return new CSharpProbeBuilder(binder)
            .CreateTypeProjection(
                type,
                relativePosition);
    }

    internal CSharpProbeProjection CreateMarkupDataTypeCompletionProjection(MarkupAttributeSyntax attribute, CSharp.TypeSyntax type, int relativePosition)
    {
        if (attribute == null)
        {
            throw new ArgumentNullException(nameof(attribute));
        }

        if (type == null)
        {
            throw new ArgumentNullException(nameof(type));
        }

        ValidateSyntaxTreeOwnership(attribute);
        if (!IsMarkupDataTypeDirective(attribute))
        {
            throw new ArgumentException(
                "Only x.DataType supports a synthetic markup type projection.",
                nameof(attribute));
        }

        var binder = BindingSession.GetCSharpProbeBinder(
            attribute,
            BinderUsage.Markup);
        return new CSharpProbeBuilder(binder)
            .CreateTypeProjection(type, relativePosition);
    }

    internal CSharpProbeProjection CreateCSharpDeclarationNameCompletionProjection(AkburaSyntax declarationSyntax, CSharp.TypeSyntax type, string name, int relativePosition)
    {
        if (declarationSyntax == null)
        {
            throw new ArgumentNullException(nameof(declarationSyntax));
        }

        if (type == null)
        {
            throw new ArgumentNullException(nameof(type));
        }

        ValidateSyntaxTreeOwnership(declarationSyntax);
        if (declarationSyntax is not (
                StateDeclarationSyntax or
                ParamDeclarationSyntax or
                InjectDeclarationSyntax))
        {
            throw new ArgumentException(
                "Only component declarations support declaration-name completion.",
                nameof(declarationSyntax));
        }

        var binder = BindingSession.GetCSharpProbeBinder(
            declarationSyntax,
            BinderUsage.Expression);
        return new CSharpProbeBuilder(binder)
            .CreateDeclarationNameProjection(
                type,
                name,
                relativePosition);
    }

    internal CSharpProbeProjection CreateCSharpCompletionProjection(UsingDirectiveSyntax usingSyntax, int relativePosition)
    {
        if (usingSyntax == null)
        {
            throw new ArgumentNullException(nameof(usingSyntax));
        }

        ValidateSyntaxTreeOwnership(usingSyntax);
        var binder = BindingSession.GetCSharpProbeBinder(
            usingSyntax,
            BinderUsage.Expression);
        return new CSharpProbeBuilder(binder)
            .CreateUsingDirectiveProjection(
                usingSyntax,
                relativePosition);
    }

    internal CSharpProbeProjection CreateCSharpCompletionProjection(AkcssUsingDirectiveSyntax usingSyntax, int relativePosition)
    {
        if (usingSyntax == null)
        {
            throw new ArgumentNullException(nameof(usingSyntax));
        }

        ValidateSyntaxTreeOwnership(usingSyntax);
        return CreateAkcssCSharpCompletionProjection(
            usingSyntax,
            relativePosition);
    }

    internal CSharpProbeProjection CreateCSharpCompletionProjection(CSharpParameterListSyntax parameterListSyntax, int relativePosition)
    {
        if (parameterListSyntax == null)
        {
            throw new ArgumentNullException(
                nameof(parameterListSyntax));
        }

        ValidateSyntaxTreeOwnership(parameterListSyntax);
        if (parameterListSyntax.Parent is not
                CommandDeclarationSyntax command ||
            !EmbeddedCSharpSyntaxFacts.TryGetParameterList(
                parameterListSyntax,
                out var parameters,
                out _))
        {
            throw new InvalidOperationException(
                "The command parameter list could not be parsed as C#.");
        }

        var binder = BindingSession.GetCSharpProbeBinder(
            parameterListSyntax,
            BinderUsage.Expression);
        return new CSharpProbeBuilder(binder)
            .CreateCommandParameterProjection(
                command,
                parameters,
                relativePosition);
    }

    private static bool IsInsideMarkup(AkburaSyntax syntax)
    {
        for (var current = syntax.Parent; current != null; current = current.Parent)
        {
            if (current is MarkupElementSyntax or MarkupRootSyntax)
            {
                return true;
            }
        }

        return false;
    }
}
