using Akbura.Language;
using Akbura.Language.Binder;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using AkburaPropertySymbol =
    Akbura.Language.Symbols.IPropertySymbol;
using CSharp =
    Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynOperation =
    Microsoft.CodeAnalysis.IOperation;
using RoslynSymbol =
    Microsoft.CodeAnalysis.ISymbol; 

namespace Akbura.Workspaces;

internal sealed class
    AkcssSemanticClassificationService
{
    public void AddClassifications(
        AkburaSemanticModel semanticModel,
        AkburaSyntax root,
        TextSpan requestedSpan,
        ImmutableArrayBuilder<AkburaClassifiedSpan> builder,
        CancellationToken cancellationToken)
    {
        foreach (var node in
                 root.DescendantNodes())
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            if (!node.FullSpan.OverlapsWith(
                    requestedSpan))
            {
                continue;
            }

            switch (node)
            {
                case AkcssStyleRuleSyntax style:
                    AddStyleClassifications(
                        semanticModel,
                        style,
                        requestedSpan,
                        builder);
                    break;

                case AkcssUtilityDeclarationSyntax utility:
                    AddUtilityClassifications(
                        semanticModel,
                        utility,
                        requestedSpan,
                        builder);
                    break;

                case AkcssAssignmentSyntax assignment:
                    AddAssignmentClassifications(
                        semanticModel,
                        assignment,
                        requestedSpan,
                        builder,
                        cancellationToken);
                    break;

                case AkcssIfDirectiveSyntax conditional:
                    AddConditionalClassifications(
                        semanticModel,
                        conditional,
                        requestedSpan,
                        builder,
                        cancellationToken);
                    break;

                case AkcssInterceptDirectiveSyntax intercept:
                    AddInterceptClassifications(
                        semanticModel,
                        intercept,
                        requestedSpan,
                        builder);
                    break;
            }
        }
    }

    private static void AddStyleClassifications(
        AkburaSemanticModel semanticModel,
        AkcssStyleRuleSyntax style,
        TextSpan requestedSpan,
        ImmutableArrayBuilder<AkburaClassifiedSpan> builder)
    {
        if (semanticModel.GetDeclaredSymbol(
                style) is not
            IAkcssSymbol symbol)
        {
            return;
        }

        AddSelectorTargetType(
            style.Selector.TargetType,
            symbol.TargetType,
            requestedSpan,
            builder);

        if (style.Selector.Name is
            { } name)
        {
            AddClassification(
                name.Identifier.Span,
                requestedSpan,
                AkburaClassificationKind.Utility,
                builder);
        }
    }

    private static void AddUtilityClassifications(
        AkburaSemanticModel semanticModel,
        AkcssUtilityDeclarationSyntax utility,
        TextSpan requestedSpan,
        ImmutableArrayBuilder<AkburaClassifiedSpan> builder)
    {
        if (semanticModel.GetDeclaredSymbol(
                utility) is not
            ITailwindUtilitySymbol symbol)
        {
            return;
        }

        var selector =
            utility.Selector;

        AddSelectorTargetType(
            selector.TargetType,
            symbol.TargetType,
            requestedSpan,
            builder);

        AddClassification(
            selector.Name.Identifier.Span,
            requestedSpan,
            AkburaClassificationKind.Utility,
            builder);

        var syntaxParameters =
            selector.Parameters;

        var symbolParameters =
            symbol.Parameters;

        var count =
            Math.Min(
                syntaxParameters.Count,
                symbolParameters.Length);

        for (var index = 0;
             index < count;
             index++)
        {
            var syntaxParameter =
                syntaxParameters[index];

            var symbolParameter =
                symbolParameters[index];

            if (symbolParameter.Type.Symbol is
                ITypeSymbol parameterType)
            {
                EmbeddedCSharpSemanticClassificationService
                    .AddTypeClassifications(
                        syntaxParameter.Type,
                        parameterType,
                        requestedSpan,
                        builder);
            }

            AddClassification(
                syntaxParameter
                    .ParamName
                    .Identifier
                    .Span,
                requestedSpan,
                AkburaClassificationKind
                    .ParameterName,
                builder);
        }
    }

    private static void AddAssignmentClassifications(
        AkburaSemanticModel semanticModel,
        AkcssAssignmentSyntax assignment,
        TextSpan requestedSpan,
        ImmutableArrayBuilder<AkburaClassifiedSpan> builder,
        CancellationToken cancellationToken)
    {
        if (semanticModel.GetOperation(
                assignment) is not
            IAkcssPropertySetterOperation operation)
        {
            return;
        }

        if (operation.Property is
            { } property)
        {
            AddPropertyClassifications(
                assignment.PropertyName,
                property,
                requestedSpan,
                builder);
        }

        AddExpressionClassifications(
            assignment.Expression,
            operation.ValueOperation,
            requestedSpan,
            builder,
            cancellationToken);
    }

    private static void AddConditionalClassifications(
        AkburaSemanticModel semanticModel,
        AkcssIfDirectiveSyntax conditional,
        TextSpan requestedSpan,
        ImmutableArrayBuilder<AkburaClassifiedSpan> builder,
        CancellationToken cancellationToken)
    {
        if (semanticModel.GetOperation(
                conditional) is not
            IAkcssIfOperation operation)
        {
            return;
        }

        AddExpressionClassifications(
            conditional.Condition,
            operation.ConditionOperation,
            requestedSpan,
            builder,
            cancellationToken);
    }

    private static void AddInterceptClassifications(
        AkburaSemanticModel semanticModel,
        AkcssInterceptDirectiveSyntax intercept,
        TextSpan requestedSpan,
        ImmutableArrayBuilder<AkburaClassifiedSpan> builder)
    {
        if (semanticModel.GetOperation(
                intercept) is not
            IAkcssInterceptOperation operation ||
            operation.InterceptType.Symbol is not
            ITypeSymbol interceptType)
        {
            return;
        }

        EmbeddedCSharpSemanticClassificationService
            .AddTypeClassifications(
                intercept.Type,
                interceptType,
                requestedSpan,
                builder);
    }

    private static void AddSelectorTargetType(
        CSharpTypeSyntax? targetSyntax,
        CSharpSymbolDefinition targetDefinition,
        TextSpan requestedSpan,
        ImmutableArrayBuilder<AkburaClassifiedSpan> builder)
    {
        if (targetSyntax == null ||
            targetDefinition.Symbol is not
            ITypeSymbol targetType)
        {
            return;
        }

        EmbeddedCSharpSemanticClassificationService
            .AddTypeClassifications(
                targetSyntax,
                targetType,
                requestedSpan,
                builder);
    }

    private static void AddPropertyClassifications(
        CSharpTypeSyntax propertySyntax,
        AkburaPropertySymbol property,
        TextSpan requestedSpan,
        ImmutableArrayBuilder<AkburaClassifiedSpan> builder)
    {
        CSharp.TypeSyntax syntax;

        try
        {
            syntax =
                propertySyntax.ToCSharp();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        var sourceOffset =
            propertySyntax.Tokens.Span.Start -
            syntax.FullSpan.Start;

        switch (syntax)
        {
            case CSharp.IdentifierNameSyntax identifier:
                AddMappedClassification(
                    identifier.Identifier.Span,
                    sourceOffset,
                    requestedSpan,
                    AkburaClassificationKind.PropertyName,
                    builder);
                break;

            case CSharp.GenericNameSyntax genericName:
                AddMappedClassification(
                    genericName.Identifier.Span,
                    sourceOffset,
                    requestedSpan,
                    AkburaClassificationKind.PropertyName,
                    builder);
                break;

            case CSharp.QualifiedNameSyntax qualifiedName:
                AddPropertyOwnerClassification(
                    qualifiedName.Left,
                    property,
                    sourceOffset,
                    requestedSpan,
                    builder);

                AddMappedClassification(
                    qualifiedName.Right.Identifier.Span,
                    sourceOffset,
                    requestedSpan,
                    AkburaClassificationKind.PropertyName,
                    builder);
                break;

            case CSharp.AliasQualifiedNameSyntax aliasName:
                AddMappedClassification(
                    aliasName.Name.Identifier.Span,
                    sourceOffset,
                    requestedSpan,
                    AkburaClassificationKind.PropertyName,
                    builder);
                break;
        }
    }

    private static void AddPropertyOwnerClassification(
        CSharp.TypeSyntax ownerSyntax,
        AkburaPropertySymbol property,
        int sourceOffset,
        TextSpan requestedSpan,
        ImmutableArrayBuilder<AkburaClassifiedSpan> builder)
    {
        var ownerType =
            GetPropertyOwnerType(
                property);

        if (ownerType == null)
        {
            return;
        }

        EmbeddedCSharpSemanticClassificationService
            .AddTypeClassifications(
                ownerSyntax,
                ownerType,
                sourceOffset,
                requestedSpan,
                builder);
    }

    private static ITypeSymbol?
        GetPropertyOwnerType(
            AkburaPropertySymbol property)
    {
        return
            property.WriteDefinition
                .Symbol
                ?.ContainingType ??

            property.ReadDefinition
                .Symbol
                ?.ContainingType ??

            property.ClrPropertyDefinition
                .Symbol
                ?.ContainingType ??

            property.AttachedSetterDefinition
                .Symbol
                ?.ContainingType ??

            property.AttachedGetterDefinition
                .Symbol
                ?.ContainingType ??

            property.AvaloniaPropertyDefinition
                .Symbol
                ?.ContainingType;
    }

    private static void AddExpressionClassifications(
        CSharpExpressionSyntax expressionSyntax,
        CSharpOperationDefinition definition,
        TextSpan requestedSpan,
        ImmutableArrayBuilder<AkburaClassifiedSpan> builder,
        CancellationToken cancellationToken)
    {
        var rootOperation =
            definition.Operation;

        if (rootOperation == null)
        {
            return;
        }

        var sourceOffset =
            expressionSyntax
                .Tokens
                .FullSpan
                .Start -
            rootOperation
                .Syntax
                .FullSpan
                .Start;

        var seenSpans =
            new HashSet<TextSpan>();

        var pending =
            new Stack<RoslynOperation>();

        pending.Push(
            rootOperation);

        while (pending.Count > 0)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            var operation =
                pending.Pop();

            AddOperationClassification(
                operation,
                sourceOffset,
                requestedSpan,
                builder,
                seenSpans);

            foreach (var child in
                     operation.ChildOperations)
            {
                pending.Push(
                    child);
            }
        }
    }

    private static void AddOperationClassification(
        RoslynOperation operation,
        int sourceOffset,
        TextSpan requestedSpan,
        ImmutableArrayBuilder<AkburaClassifiedSpan> builder,
        HashSet<TextSpan> seenSpans)
    {
        switch (operation)
        {
            case IInvocationOperation invocation:
                AddSymbolReference(
                    invocation.Syntax,
                    invocation.TargetMethod,
                    sourceOffset,
                    requestedSpan,
                    builder,
                    seenSpans);

                AddStaticReceiverType(
                    invocation.Syntax,
                    invocation.TargetMethod,
                    sourceOffset,
                    requestedSpan,
                    builder,
                    seenSpans);
                break;

            case IPropertyReferenceOperation property:
                AddSymbolReference(
                    property.Syntax,
                    property.Property,
                    sourceOffset,
                    requestedSpan,
                    builder,
                    seenSpans);

                AddStaticReceiverType(
                    property.Syntax,
                    property.Property,
                    sourceOffset,
                    requestedSpan,
                    builder,
                    seenSpans);
                break;

            case IFieldReferenceOperation field:
                AddSymbolReference(
                    field.Syntax,
                    field.Field,
                    sourceOffset,
                    requestedSpan,
                    builder,
                    seenSpans);

                AddStaticReceiverType(
                    field.Syntax,
                    field.Field,
                    sourceOffset,
                    requestedSpan,
                    builder,
                    seenSpans);
                break;

            case IEventReferenceOperation eventReference:
                AddSymbolReference(
                    eventReference.Syntax,
                    eventReference.Event,
                    sourceOffset,
                    requestedSpan,
                    builder,
                    seenSpans);
                break;

            case IMethodReferenceOperation method:
                AddSymbolReference(
                    method.Syntax,
                    method.Method,
                    sourceOffset,
                    requestedSpan,
                    builder,
                    seenSpans);
                break;

            case ILocalReferenceOperation local:
                AddSymbolReference(
                    local.Syntax,
                    local.Local,
                    sourceOffset,
                    requestedSpan,
                    builder,
                    seenSpans);
                break;

            case IParameterReferenceOperation parameter:
                AddSymbolReference(
                    parameter.Syntax,
                    parameter.Parameter,
                    sourceOffset,
                    requestedSpan,
                    builder,
                    seenSpans);
                break;

            case IObjectCreationOperation creation
                when creation.Constructor is
                { } constructor &&
                     creation.Syntax is
                    CSharp.ObjectCreationExpressionSyntax
                        objectCreation:

                EmbeddedCSharpSemanticClassificationService
                    .AddTypeClassifications(
                        objectCreation.Type,
                        constructor.ContainingType,
                        sourceOffset,
                        requestedSpan,
                        builder);
                break;

            case ITypeOfOperation typeOf
                when typeOf.Syntax is
                    CSharp.TypeOfExpressionSyntax
                        typeOfExpression:

                EmbeddedCSharpSemanticClassificationService
                    .AddTypeClassifications(
                        typeOfExpression.Type,
                        typeOf.TypeOperand,
                        sourceOffset,
                        requestedSpan,
                        builder);
                break;
        }
    }

    private static void AddSymbolReference(
        SyntaxNode syntax,
        RoslynSymbol symbol,
        int sourceOffset,
        TextSpan requestedSpan,
        ImmutableArrayBuilder<AkburaClassifiedSpan> builder,
        HashSet<TextSpan> seenSpans)
    {
        if (!TryGetReferenceName(
                syntax,
                out var name))
        {
            return;
        }

        var classification =
            EmbeddedCSharpSemanticClassificationService
                .GetRoslynClassification(
                    symbol);

        if (classification == null)
        {
            return;
        }

        AddMappedClassification(
            name.Identifier.Span,
            sourceOffset,
            requestedSpan,
            classification.Value,
            builder,
            seenSpans);
    }

    private static void AddStaticReceiverType(
        SyntaxNode syntax,
        RoslynSymbol symbol,
        int sourceOffset,
        TextSpan requestedSpan,
        ImmutableArrayBuilder<AkburaClassifiedSpan> builder,
        HashSet<TextSpan> seenSpans)
    {
        if (!symbol.IsStatic ||
            symbol.ContainingType == null ||
            symbol is IMethodSymbol
            {
                ReducedFrom: not null,
            })
        {
            return;
        }

        CSharp.MemberAccessExpressionSyntax?
            memberAccess =
            syntax switch
            {
                CSharp.InvocationExpressionSyntax
                {
                    Expression:
                        CSharp.MemberAccessExpressionSyntax
                            access,
                } => access,

                CSharp.MemberAccessExpressionSyntax access =>
                    access,

                _ => null,
            };

        if (memberAccess == null ||
            !TryGetRightmostName(
                memberAccess.Expression,
                out var receiverName))
        {
            return;
        }

        var classification =
            EmbeddedCSharpSemanticClassificationService
                .GetRoslynClassification(
                    symbol.ContainingType);

        if (classification == null)
        {
            return;
        }

        AddMappedClassification(
            receiverName.Identifier.Span,
            sourceOffset,
            requestedSpan,
            classification.Value,
            builder,
            seenSpans);
    }

    private static bool TryGetReferenceName(
        SyntaxNode syntax,
        out CSharp.SimpleNameSyntax name)
    {
        switch (syntax)
        {
            case CSharp.SimpleNameSyntax simpleName:
                name = simpleName;
                return true;

            case CSharp.MemberAccessExpressionSyntax
                memberAccess:

                name =
                    memberAccess.Name;
                return true;

            case CSharp.MemberBindingExpressionSyntax
                memberBinding:

                name =
                    memberBinding.Name;
                return true;

            case CSharp.InvocationExpressionSyntax
                invocation:

                return TryGetReferenceName(
                    invocation.Expression,
                    out name);

            default:
                name = null!;
                return false;
        }
    }

    private static bool TryGetRightmostName(
        CSharp.ExpressionSyntax expression,
        out CSharp.SimpleNameSyntax name)
    {
        switch (expression)
        {
            case CSharp.SimpleNameSyntax simpleName:
                name = simpleName;
                return true;

            case CSharp.MemberAccessExpressionSyntax
                memberAccess:

                name =
                    memberAccess.Name;
                return true;

            default:
                name = null!;
                return false;
        }
    }

    private static void AddClassification(
        TextSpan sourceSpan,
        TextSpan requestedSpan,
        AkburaClassificationKind classification,
        ImmutableArrayBuilder<AkburaClassifiedSpan> builder)
    {
        if (sourceSpan.Length == 0 ||
            !sourceSpan.OverlapsWith(
                requestedSpan))
        {
            return;
        }

        builder.Add(
            new AkburaClassifiedSpan(
                sourceSpan,
                classification));
    }

    private static void AddMappedClassification(
        TextSpan csharpSpan,
        int sourceOffset,
        TextSpan requestedSpan,
        AkburaClassificationKind classification,
        ImmutableArrayBuilder<AkburaClassifiedSpan> builder,
        HashSet<TextSpan>? seenSpans = null)
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

        if (!sourceSpan.OverlapsWith(
                requestedSpan) ||
            seenSpans != null &&
            !seenSpans.Add(sourceSpan))
        {
            return;
        }

        builder.Add(
            new AkburaClassifiedSpan(
                sourceSpan,
                classification));
    }
}
