using Akbura.Language;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using AkburaSymbolKind =
    Akbura.Language.Symbols.SymbolKind;
using RoslynSymbol =
    Microsoft.CodeAnalysis.ISymbol;
using RoslynIPropertySymbol
    = Microsoft.CodeAnalysis.IPropertySymbol;
using RoslynITypeSymbol =
    Microsoft.CodeAnalysis.ITypeSymbol;
using CSharp =
    Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akbura.Workspaces.Classification;

internal sealed class EmbeddedCSharpSemanticClassificationService
{
    private static void AddDeclaredTypeClassifications(AkburaSemanticModel semanticModel, CSharpTypeSyntax typeSyntax, TextSpan requestedSpan, ImmutableArrayBuilder<AkburaClassifiedSpan> builder)
    {
        var typeSymbol =
            GetDeclaredTypeSymbol(
                semanticModel,
                typeSyntax);

        if (typeSymbol == null)
        {
            return;
        }

        AddTypeClassifications(
            typeSyntax,
            typeSymbol,
            requestedSpan,
            builder);
    }

    internal static void AddTypeClassifications(CSharpTypeSyntax typeSyntax, RoslynITypeSymbol typeSymbol, TextSpan requestedSpan, ImmutableArrayBuilder<AkburaClassifiedSpan> builder)
    {
        if (typeSymbol.TypeKind ==
            TypeKind.Error)
        {
            return;
        }

        CSharp.TypeSyntax csharpType;

        try
        {
            csharpType =
                typeSyntax.ToCSharp();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        var sourceOffset =
            typeSyntax.Tokens.Span.Start -
            csharpType.FullSpan.Start;

        AddTypeClassifications(
            csharpType,
            typeSymbol,
            sourceOffset,
            requestedSpan,
            builder);
    }

    internal static void AddTypeClassifications(CSharp.TypeSyntax syntax, RoslynITypeSymbol typeSymbol, int sourceOffset, TextSpan requestedSpan, ImmutableArrayBuilder<AkburaClassifiedSpan> builder)
    {
        switch (syntax)
        {
            case CSharp.PredefinedTypeSyntax predefinedType:
                AddMappedClassification(
                    predefinedType.Keyword.Span,
                    sourceOffset,
                    requestedSpan,
                    AkburaClassificationKind.Keyword,
                    builder);
                return;

            case CSharp.IdentifierNameSyntax identifier:
                AddTypeNameClassification(
                    identifier.Identifier.Span,
                    typeSymbol,
                    sourceOffset,
                    requestedSpan,
                    builder);
                return;

            case CSharp.GenericNameSyntax genericName:
                AddTypeNameClassification(
                    genericName.Identifier.Span,
                    typeSymbol,
                    sourceOffset,
                    requestedSpan,
                    builder);

                AddGenericTypeArgumentClassifications(
                    genericName,
                    typeSymbol,
                    sourceOffset,
                    requestedSpan,
                    builder);
                return;

            case CSharp.NullableTypeSyntax nullableType:
                AddTypeClassifications(
                    nullableType.ElementType,
                    GetNullableElementType(
                        typeSymbol),
                    sourceOffset,
                    requestedSpan,
                    builder);
                return;

            case CSharp.ArrayTypeSyntax arrayType
                when typeSymbol is IArrayTypeSymbol arraySymbol:
                AddTypeClassifications(
                    arrayType.ElementType,
                    arraySymbol.ElementType,
                    sourceOffset,
                    requestedSpan,
                    builder);
                return;

            case CSharp.PointerTypeSyntax pointerType
                when typeSymbol is IPointerTypeSymbol pointerSymbol:
                AddTypeClassifications(
                    pointerType.ElementType,
                    pointerSymbol.PointedAtType,
                    sourceOffset,
                    requestedSpan,
                    builder);
                return;

            case CSharp.QualifiedNameSyntax qualifiedName:
                AddQualifiedTypeClassifications(
                    qualifiedName,
                    typeSymbol,
                    sourceOffset,
                    requestedSpan,
                    builder);
                return;

            case CSharp.AliasQualifiedNameSyntax aliasQualifiedName:
                AddSimpleTypeNameClassifications(
                    aliasQualifiedName.Name,
                    typeSymbol,
                    sourceOffset,
                    requestedSpan,
                    builder);
                return;

            case CSharp.TupleTypeSyntax tupleType
                when typeSymbol is INamedTypeSymbol
                {
                    IsTupleType: true
                } tupleSymbol:
                AddTupleTypeClassifications(
                    tupleType,
                    tupleSymbol,
                    sourceOffset,
                    requestedSpan,
                    builder);
                return;

            case CSharp.RefTypeSyntax refType:
                AddTypeClassifications(
                    refType.Type,
                    typeSymbol,
                    sourceOffset,
                    requestedSpan,
                    builder);
                return;
        }
    }

    private static void AddMappedClassification(TextSpan csharpSpan, int sourceOffset, TextSpan requestedSpan, AkburaClassificationKind classification, ImmutableArrayBuilder<AkburaClassifiedSpan> builder)
    {
        if (csharpSpan.Length == 0)
        {
            return;
        }

        var sourceSpan =
            new TextSpan(
                sourceOffset +
                csharpSpan.Start,
                csharpSpan.Length);

        if (!sourceSpan.OverlapsWith(requestedSpan))
        {
            return;
        }

        builder.Add(
            new AkburaClassifiedSpan(
                sourceSpan,
                classification));
    }

    private static void AddSimpleTypeNameClassifications(CSharp.SimpleNameSyntax syntax, RoslynITypeSymbol typeSymbol, int sourceOffset, TextSpan requestedSpan, ImmutableArrayBuilder<AkburaClassifiedSpan> builder)
    {
        switch (syntax)
        {
            case CSharp.IdentifierNameSyntax identifier:
                AddTypeNameClassification(
                    identifier.Identifier.Span,
                    typeSymbol,
                    sourceOffset,
                    requestedSpan,
                    builder);
                break;

            case CSharp.GenericNameSyntax genericName:
                AddTypeNameClassification(
                    genericName.Identifier.Span,
                    typeSymbol,
                    sourceOffset,
                    requestedSpan,
                    builder);

                AddGenericTypeArgumentClassifications(
                    genericName,
                    typeSymbol,
                    sourceOffset,
                    requestedSpan,
                    builder);
                break;
        }
    }

    private static void AddGenericTypeArgumentClassifications(CSharp.GenericNameSyntax genericName, RoslynITypeSymbol typeSymbol, int sourceOffset, TextSpan requestedSpan, ImmutableArrayBuilder<AkburaClassifiedSpan> builder)
    {
        if (typeSymbol is not INamedTypeSymbol namedType)
        {
            return;
        }

        var syntaxArguments =
            genericName.TypeArgumentList.Arguments;

        var symbolArguments =
            namedType.TypeArguments;

        var count =
            Math.Min(
                syntaxArguments.Count,
                symbolArguments.Length);

        for (var index = 0; index < count; index++)
        {
            AddTypeClassifications(
                syntaxArguments[index],
                symbolArguments[index],
                sourceOffset,
                requestedSpan,
                builder);
        }
    }

    private static void AddQualifiedTypeClassifications(CSharp.QualifiedNameSyntax qualifiedName, RoslynITypeSymbol typeSymbol, int sourceOffset, TextSpan requestedSpan, ImmutableArrayBuilder<AkburaClassifiedSpan> builder)
    {
        if (typeSymbol is INamedTypeSymbol namedType)
        {
            var containingSymbol = namedType.ContainingType is { } containingType
                ? (INamespaceOrTypeSymbol)containingType
                : namedType.ContainingNamespace;
            AddNamespaceOrTypeClassifications(
                qualifiedName.Left,
                containingSymbol,
                sourceOffset,
                requestedSpan,
                builder);
        }

        AddSimpleTypeNameClassifications(
            qualifiedName.Right,
            typeSymbol,
            sourceOffset,
            requestedSpan,
            builder);
    }

    private static void AddNamespaceOrTypeClassifications(CSharp.NameSyntax syntax, INamespaceOrTypeSymbol symbol, int sourceOffset, TextSpan requestedSpan, ImmutableArrayBuilder<AkburaClassifiedSpan> builder)
    {
        switch (syntax)
        {
            case CSharp.QualifiedNameSyntax qualifiedName:
            {
                var containingSymbol = symbol switch
                {
                    INamedTypeSymbol { ContainingType: { } containingType } =>
                        (INamespaceOrTypeSymbol)containingType,
                    INamedTypeSymbol namedType =>
                        namedType.ContainingNamespace,
                    INamespaceSymbol namespaceSymbol =>
                        namespaceSymbol.ContainingNamespace,
                    _ => null,
                };
                if (containingSymbol != null)
                {
                    AddNamespaceOrTypeClassifications(
                        qualifiedName.Left,
                        containingSymbol,
                        sourceOffset,
                        requestedSpan,
                        builder);
                }

                AddSimpleNamespaceOrTypeClassifications(
                    qualifiedName.Right,
                    symbol,
                    sourceOffset,
                    requestedSpan,
                    builder);
                return;
            }

            case CSharp.AliasQualifiedNameSyntax aliasQualifiedName:
                AddSimpleNamespaceOrTypeClassifications(
                    aliasQualifiedName.Name,
                    symbol,
                    sourceOffset,
                    requestedSpan,
                    builder);
                return;

            case CSharp.SimpleNameSyntax simpleName:
                AddSimpleNamespaceOrTypeClassifications(
                    simpleName,
                    symbol,
                    sourceOffset,
                    requestedSpan,
                    builder);
                return;
        }
    }

    private static void AddSimpleNamespaceOrTypeClassifications(CSharp.SimpleNameSyntax syntax, INamespaceOrTypeSymbol symbol, int sourceOffset, TextSpan requestedSpan, ImmutableArrayBuilder<AkburaClassifiedSpan> builder)
    {
        if (symbol is RoslynITypeSymbol typeSymbol)
        {
            AddSimpleTypeNameClassifications(
                syntax,
                typeSymbol,
                sourceOffset,
                requestedSpan,
                builder);
            return;
        }

        var classification = GetRoslynClassification(symbol);
        if (classification != null)
        {
            AddMappedClassification(
                syntax.Identifier.Span,
                sourceOffset,
                requestedSpan,
                classification.Value,
                builder);
        }
    }

    private static void AddTupleTypeClassifications(CSharp.TupleTypeSyntax tupleType, INamedTypeSymbol tupleSymbol, int sourceOffset, TextSpan requestedSpan, ImmutableArrayBuilder<AkburaClassifiedSpan> builder)
    {
        var syntaxElements =
            tupleType.Elements;

        var symbolElements =
            tupleSymbol.TupleElements;

        var count =
            Math.Min(
                syntaxElements.Count,
                symbolElements.Length);

        for (var index = 0; index < count; index++)
        {
            AddTypeClassifications(
                syntaxElements[index].Type,
                symbolElements[index].Type,
                sourceOffset,
                requestedSpan,
                builder);
        }
    }

    private static RoslynITypeSymbol GetNullableElementType(RoslynITypeSymbol typeSymbol)
    {
        if (typeSymbol is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType:
                    SpecialType.System_Nullable_T,
                TypeArguments.Length: 1
            } nullableType)
        {
            return nullableType.TypeArguments[0];
        }

        return typeSymbol;
    }

    private static void AddTypeNameClassification(TextSpan csharpSpan, RoslynITypeSymbol typeSymbol, int sourceOffset, TextSpan requestedSpan, ImmutableArrayBuilder<AkburaClassifiedSpan> builder)
    {
        var classification =
            GetRoslynClassification(
                typeSymbol);

        if (classification is null)
        {
            return;
        }

        var sourceSpan =
            new TextSpan(
                sourceOffset +
                csharpSpan.Start,
                csharpSpan.Length);

        if (!sourceSpan.OverlapsWith(
                requestedSpan))
        {
            return;
        }

        builder.Add(
            new AkburaClassifiedSpan(
                sourceSpan,
                classification.Value));
    }

    private static RoslynITypeSymbol? GetDeclaredTypeSymbol(AkburaSemanticModel semanticModel, CSharpTypeSyntax typeSyntax)
    {
        return typeSyntax.Parent switch
        {
            ParamDeclarationSyntax declaration
                when semanticModel
                        .GetDeclaredSymbol(
                            declaration) is
                    IParamSymbol symbol =>
                symbol.Type.Symbol as
                    RoslynITypeSymbol,

            StateDeclarationSyntax declaration
                when semanticModel
                        .GetDeclaredSymbol(
                            declaration) is
                    IStateSymbol symbol =>
                symbol.Type.Symbol as
                    RoslynITypeSymbol,

            InjectDeclarationSyntax declaration
                when semanticModel
                        .GetDeclaredSymbol(
                            declaration) is
                    IInjectSymbol symbol =>
                symbol.Type.Symbol as
                    RoslynITypeSymbol,

            CommandDeclarationSyntax declaration
                when semanticModel
                        .GetDeclaredSymbol(
                            declaration) is
                    ICommandSymbol symbol =>
                symbol.ReturnType.Symbol as
                    RoslynITypeSymbol,

            _ => null,
        };
    }

    public void AddClassifications(AkburaSemanticModel semanticModel, AkburaSyntax root, TextSpan requestedSpan, ImmutableArrayBuilder<AkburaClassifiedSpan> builder, CancellationToken cancellationToken)
    {
        foreach (var node in root.DescendantNodes())
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            if (!node.FullSpan.OverlapsWith(
                    requestedSpan))
            {
                continue;
            }

            ImmutableArray<CSharpSymbolReference>
                references;

            switch (node)
            {
                case CommandDeclarationSyntax declaration:
                    AddCommandDeclarationClassification(
                        semanticModel,
                        declaration,
                        requestedSpan,
                        builder);
                    AddCommandParameterClassifications(
                        semanticModel,
                        declaration,
                        requestedSpan,
                        builder);
                    continue;

                case StateDeclarationSyntax declaration
                    when declaration.Initializer.FullSpan.OverlapsWith(requestedSpan):
                    references = semanticModel.GetCSharpSymbolReferences(
                        declaration);
                    break;

                case CSharpExpressionSyntax condition when condition.Parent is
                    MarkupIfStatementSyntax or MarkupElseIfClauseSyntax or MarkupCodeIfStatementSyntax or MarkupForeachKeyClauseSyntax:
                    references = semanticModel.GetCSharpSymbolReferences(condition);
                    break;

                case MarkupForeachHeaderSyntax header:
                    references = semanticModel.GetCSharpSymbolReferences(header);
                    break;

                case MarkupCodeStatementSyntax code:
                    references = semanticModel.GetCSharpSymbolReferences(code);
                    break;

                case CSharpStatementSyntax statement:
                    references =
                        semanticModel
                            .GetCSharpSymbolReferences(
                                statement);
                    break;

                case MarkupAttributeSyntax attribute:
                    references =
                        semanticModel
                            .GetCSharpSymbolReferences(
                                attribute);
                    break;

                case MarkupInlineExpressionSyntax content:
                    references =
                        semanticModel
                            .GetCSharpSymbolReferences(
                                content.Expression);
                    break;

                case CSharpTypeSyntax type:
                    AddDeclaredTypeClassifications(
                        semanticModel,
                        type,
                        requestedSpan,
                        builder);
                    continue;

                default:
                    continue;
            }

            AddReferences(
                references,
                requestedSpan,
                builder);
        }
    }

    private static void AddCommandDeclarationClassification(AkburaSemanticModel semanticModel, CommandDeclarationSyntax declaration, TextSpan requestedSpan, ImmutableArrayBuilder<AkburaClassifiedSpan> builder)
    {
        if (!declaration.Name.Span.OverlapsWith(requestedSpan) ||
            semanticModel.GetDeclaredSymbol(declaration) is not ICommandSymbol)
        {
            return;
        }

        builder.Add(new AkburaClassifiedSpan(
            declaration.Name.Span,
            AkburaClassificationKind.MethodName));
    }

    private static void AddCommandParameterClassifications(AkburaSemanticModel semanticModel, CommandDeclarationSyntax declaration, TextSpan requestedSpan, ImmutableArrayBuilder<AkburaClassifiedSpan> builder)
    {
        if (!declaration.Parameters.FullSpan.OverlapsWith(requestedSpan) ||
            !EmbeddedCSharpSyntaxFacts.TryGetParameterList(
                declaration.Parameters,
                out var parameterList,
                out var hostSpan))
        {
            return;
        }

        var sourceOffset = hostSpan.Start - parameterList.FullSpan.Start;
        var command = semanticModel.GetDeclaredSymbol(declaration) as ICommandSymbol;

        for (var index = 0; index < parameterList.Parameters.Count; index++)
        {
            var parameterSyntax = parameterList.Parameters[index];
            var identifier = parameterSyntax.Identifier;

            if (!identifier.IsMissing)
            {
                AddMappedClassification(
                    identifier.Span,
                    sourceOffset,
                    requestedSpan,
                    AkburaClassificationKind.ParameterName,
                    builder);
            }

            if (parameterSyntax.Type == null ||
                command == null ||
                FindCommandParameter(command, index, identifier.ValueText) is not { } parameter ||
                parameter.Type.Symbol is not RoslynITypeSymbol typeSymbol)
            {
                continue;
            }

            AddTypeClassifications(
                parameterSyntax.Type,
                typeSymbol,
                sourceOffset,
                requestedSpan,
                builder);
        }
    }

    private static ICommandParameterSymbol? FindCommandParameter(ICommandSymbol command, int ordinal, string name)
    {
        foreach (var parameter in command.Parameters)
        {
            if (parameter.Ordinal == ordinal &&
                string.Equals(parameter.Name, name, StringComparison.Ordinal))
            {
                return parameter;
            }
        }

        return null;
    }

    private static void AddReferences(ImmutableArray<CSharpSymbolReference> references, TextSpan requestedSpan, ImmutableArrayBuilder<AkburaClassifiedSpan> builder)
    {
        foreach (var reference in references)
        {
            if (!reference.SourceSpan
                    .OverlapsWith(requestedSpan))
            {
                continue;
            }

            var classification =
                GetClassification(reference);

            if (classification is null)
            {
                continue;
            }

            builder.Add(
                new AkburaClassifiedSpan(
                    reference.SourceSpan,
                    classification.Value));
        }
    }

    private static AkburaClassificationKind? GetClassification(CSharpSymbolReference reference)
    {
        if (reference.AkburaSymbol is { } akburaSymbol)
        {
            var akburaClassification =
                GetAkburaClassification(
                    akburaSymbol.Kind);

            if (akburaClassification is not null)
            {
                return akburaClassification;
            }
        }

        return GetRoslynClassification(
            reference.CSharpDefinition.Symbol);
    }

    private static AkburaClassificationKind? GetAkburaClassification(AkburaSymbolKind kind)
    {
        return kind switch
        {
            AkburaSymbolKind.Namespace =>
                AkburaClassificationKind.Namespace,

            AkburaSymbolKind.Component or
            AkburaSymbolKind.AkburaComponent or
            AkburaSymbolKind.MarkupComponent =>
                AkburaClassificationKind.ClassName,

            AkburaSymbolKind.Property or
            AkburaSymbolKind.State or
            AkburaSymbolKind.Parameter =>
                AkburaClassificationKind.PropertyName,

            AkburaSymbolKind.Event =>
                AkburaClassificationKind.EventName,

            AkburaSymbolKind.InjectedService =>
                AkburaClassificationKind.FieldName,

            AkburaSymbolKind.Command or
            AkburaSymbolKind.Function or
            AkburaSymbolKind.UseHook =>
                AkburaClassificationKind.MethodName,

            AkburaSymbolKind.CommandParameter or
            AkburaSymbolKind.TailwindUtilityParameter =>
                AkburaClassificationKind.ParameterName,

            AkburaSymbolKind.MarkupItem or
            AkburaSymbolKind.MarkupName =>
                AkburaClassificationKind.LocalName,

            _ => null,
        };
    }

    internal static AkburaClassificationKind? GetRoslynClassification(RoslynSymbol? symbol)
    {
        if (symbol is IAliasSymbol alias)
        {
            symbol = alias.Target;
        }

        return symbol switch
        {
            INamespaceSymbol =>
                AkburaClassificationKind.Namespace,

            INamedTypeSymbol type =>
                GetNamedTypeClassification(type),

            ITypeParameterSymbol =>
                AkburaClassificationKind.TypeParameterName,

            IMethodSymbol
            {
                MethodKind:
                    MethodKind.Constructor or
                    MethodKind.StaticConstructor
            } constructor =>
                GetNamedTypeClassification(
                    constructor.ContainingType),

            IMethodSymbol
            {
                IsExtensionMethod: true
            } =>
                AkburaClassificationKind
                    .ExtensionMethodName,

            IMethodSymbol =>
                AkburaClassificationKind.MethodName,

            RoslynIPropertySymbol =>
                AkburaClassificationKind.PropertyName,

            IEventSymbol =>
                AkburaClassificationKind.EventName,

            IFieldSymbol field
                when field.ContainingType?.TypeKind ==
                     TypeKind.Enum =>
                AkburaClassificationKind.EnumMemberName,

            IFieldSymbol { IsConst: true } =>
                AkburaClassificationKind.ConstantName,

            IFieldSymbol =>
                AkburaClassificationKind.FieldName,

            ILocalSymbol { IsConst: true } =>
                AkburaClassificationKind.ConstantName,

            ILocalSymbol =>
                AkburaClassificationKind.LocalName,

            IParameterSymbol =>
                AkburaClassificationKind.ParameterName,

            ILabelSymbol =>
                AkburaClassificationKind.LabelName,

            IRangeVariableSymbol =>
                AkburaClassificationKind.LocalName,

            _ => null,
        };
    }

    private static AkburaClassificationKind GetNamedTypeClassification(INamedTypeSymbol type)
    {
        return type.TypeKind switch
        {
            TypeKind.Class =>
                AkburaClassificationKind.ClassName,

            TypeKind.Struct =>
                AkburaClassificationKind.StructName,

            TypeKind.Interface =>
                AkburaClassificationKind.InterfaceName,

            TypeKind.Enum =>
                AkburaClassificationKind.EnumName,

            TypeKind.Delegate =>
                AkburaClassificationKind.DelegateName,

            _ => AkburaClassificationKind.Type,
        };
    }
}
