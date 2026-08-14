using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis.Text;
using AkburaSymbolKind = Akbura.Language.Symbols.SymbolKind;
using AkburaSymbol = Akbura.Language.Symbols.ISymbol;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFacts = Microsoft.CodeAnalysis.CSharp.SyntaxFacts;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

namespace Akbura.Language.Binder;

internal sealed partial class CSharpProbeBinder
{
    internal const string StateCompletionAnnotationKind =
        "AkburaCSharpCompletionState";

    internal const string ProjectedSymbolAnnotationKind =
        "AkburaProjectedSymbol";

    internal CSharpProbeScope CreateProbeScope(
        AkburaSyntax scope,
        SyntaxNode csharpNode,
        ImmutableArray<string> excludedNames = default)
    {
        if (scope == null ||
            csharpNode == null)
        {
            return CSharpProbeScope.Empty;
        }

        var names = CollectIdentifierNames(csharpNode, excludedNames);
        if (names.IsDefaultOrEmpty)
        {
            return CSharpProbeScope.Empty;
        }

        using var memberDeclarations = ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax>.Rent();
        using var localStatements = ImmutableArrayBuilder<CSharp.StatementSyntax>.Rent();
        var addedNames = new HashSet<string>(StringComparer.Ordinal);
        var diagnostics = BindingDiagnosticBag.GetInstance();

        foreach (var name in names)
        {
            if (!addedNames.Add(name))
            {
                continue;
            }

            var symbol = Next?.LookupSymbol(
                name,
                BinderLookupOptions.None,
                scope,
                diagnostics).Symbol;
            AddProbeSymbol(
                symbol,
                memberDeclarations,
                localStatements);
            if (symbol == null)
            {
                AddComponentMethodProbeMembers(
                    name,
                    memberDeclarations);
            }
        }

        return new CSharpProbeScope(
            memberDeclarations.ToImmutable(),
            localStatements.ToImmutable());
    }

    internal CSharpProbeScope CreateCompletionProbeScope(
        AkburaSyntax scope,
        SyntaxNode csharpNode,
        ImmutableArray<string> excludedNames = default)
    {
        if (scope == null ||
            csharpNode == null)
        {
            return CSharpProbeScope.Empty;
        }

        using var memberDeclarations =
            ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax>.Rent();
        using var localStatements =
            ImmutableArrayBuilder<CSharp.StatementSyntax>.Rent();
        var addedNames = new HashSet<string>(StringComparer.Ordinal);
        if (!excludedNames.IsDefaultOrEmpty)
        {
            foreach (var excludedName in excludedNames)
            {
                if (!string.IsNullOrWhiteSpace(excludedName))
                {
                    addedNames.Add(excludedName);
                }
            }
        }

        var diagnostics = BindingDiagnosticBag.GetInstance();
        try
        {
            for (var binder = Next;
                 binder != null;
                 binder = binder.Next)
            {
                var scopeDesignator = binder.ScopeDesignator;
                if (scopeDesignator == null)
                {
                    continue;
                }

                foreach (var candidate in
                         binder.GetDeclaredSymbolsForScope(
                             scopeDesignator))
                {
                    if (string.IsNullOrWhiteSpace(candidate.Name) ||
                        candidate.Kind == AkburaSymbolKind.CSharpSymbol ||
                        !addedNames.Add(candidate.Name))
                    {
                        continue;
                    }

                    var symbol = Next?.LookupSymbol(
                        candidate.Name,
                        BinderLookupOptions.None,
                        scope,
                        diagnostics).Symbol;
                    AddProbeSymbol(
                        symbol,
                        memberDeclarations,
                        localStatements);
                }
            }
        }
        finally
        {
            diagnostics.Free();
        }

        AddAllComponentMethodProbeMembers(
            memberDeclarations);

        return new CSharpProbeScope(
            memberDeclarations.ToImmutable(),
            localStatements.ToImmutable());
    }

    private void AddComponentMethodProbeMembers(
        string name,
        ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax> memberDeclarations)
    {
        foreach (var member in SemanticModel.SyntaxTree.GetRoot().Members)
        {
            if (member is CSharpStatementSyntax statement &&
                TryCreateComponentMethodProbe(statement, out var method) &&
                string.Equals(
                    method.Identifier.ValueText,
                    name,
                    StringComparison.Ordinal))
            {
                memberDeclarations.Add(method);
            }
        }
    }

    private void AddAllComponentMethodProbeMembers(
        ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax>
            memberDeclarations)
    {
        foreach (var member in
                 SemanticModel.SyntaxTree.GetRoot().Members)
        {
            if (member is CSharpStatementSyntax statement &&
                TryCreateComponentMethodProbe(
                    statement,
                    out var method))
            {
                memberDeclarations.Add(method);
            }
        }
    }

    internal static bool TryCreateComponentMethodProbe(
        CSharpStatementSyntax statement,
        out CSharp.MethodDeclarationSyntax method)
    {
        method = null!;
        if (statement.GetRawCSharpStatement() is not
            CSharp.LocalFunctionStatementSyntax)
        {
            return false;
        }

        try
        {
            var parsedMethod = CSharpSyntaxFactory.ParseMemberDeclaration(
                statement.Tokens.ToFullString() + "{}") as
                CSharp.MethodDeclarationSyntax;
            if (parsedMethod == null)
            {
                return false;
            }

            var hostOffset = statement.Tokens.FullSpan.Start;
            parsedMethod = parsedMethod.ReplaceNodes(
                parsedMethod.ParameterList.Parameters,
                (original, _) => original.WithAdditionalAnnotations(
                    CreateProjectedSymbolAnnotation(
                        AkburaSymbolKind.CSharpSymbol,
                        original.Identifier.ValueText,
                        new TextSpan(
                            hostOffset + original.Identifier.Span.Start,
                            original.Identifier.Span.Length))));
            method = parsedMethod.WithAdditionalAnnotations(
                CreateProjectedSymbolAnnotation(
                    AkburaSymbolKind.CSharpSymbol,
                    parsedMethod.Identifier.ValueText,
                    new TextSpan(
                        hostOffset + parsedMethod.Identifier.Span.Start,
                        parsedMethod.Identifier.Span.Length)));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static ImmutableArray<string> CollectIdentifierNames(
        SyntaxNode node,
        ImmutableArray<string> excludedNames)
    {
        using var builder = ImmutableArrayBuilder<string>.Rent();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (!excludedNames.IsDefaultOrEmpty)
        {
            foreach (var excludedName in excludedNames)
            {
                if (!string.IsNullOrWhiteSpace(excludedName))
                {
                    seen.Add(excludedName);
                }
            }
        }

        AddDeclaredIdentifierNames(node, seen);

        foreach (var identifier in node.DescendantNodesAndSelf().OfType<CSharp.IdentifierNameSyntax>())
        {
            var name = identifier.Identifier.ValueText;
            if (!string.IsNullOrWhiteSpace(name) &&
                seen.Add(name))
            {
                builder.Add(name);
            }
        }

        return builder.ToImmutable();
    }

    private static void AddDeclaredIdentifierNames(
        SyntaxNode node,
        HashSet<string> names)
    {
        foreach (var parameter in node.DescendantNodesAndSelf().OfType<CSharp.ParameterSyntax>())
        {
            AddDeclaredIdentifierName(names, parameter.Identifier.ValueText);
        }

        foreach (var variable in node.DescendantNodesAndSelf().OfType<CSharp.VariableDeclaratorSyntax>())
        {
            AddDeclaredIdentifierName(names, variable.Identifier.ValueText);
        }

        foreach (var foreachStatement in node.DescendantNodesAndSelf().OfType<CSharp.ForEachStatementSyntax>())
        {
            AddDeclaredIdentifierName(names, foreachStatement.Identifier.ValueText);
        }

        foreach (var designation in node.DescendantNodesAndSelf().OfType<CSharp.SingleVariableDesignationSyntax>())
        {
            AddDeclaredIdentifierName(names, designation.Identifier.ValueText);
        }
    }

    private static void AddDeclaredIdentifierName(
        HashSet<string> names,
        string name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            names.Add(name);
        }
    }

    internal static ImmutableArray<CSharp.MemberDeclarationSyntax> AddProbeMethod(
        ImmutableArray<CSharp.MemberDeclarationSyntax> memberDeclarations,
        CSharp.MethodDeclarationSyntax method)
    {
        if (memberDeclarations.IsDefaultOrEmpty)
        {
            return ImmutableArray.Create<CSharp.MemberDeclarationSyntax>(method);
        }

        using var builder = ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax>.Rent();
        foreach (var memberDeclaration in memberDeclarations)
        {
            builder.Add(memberDeclaration);
        }

        builder.Add(method);
        return builder.ToImmutable();
    }

    internal static CSharp.BlockSyntax CreateProbeBlock(
        ImmutableArray<CSharp.StatementSyntax> localStatements,
        CSharp.StatementSyntax statement)
    {
        return CreateProbeBlock(
            localStatements,
            ImmutableArray<CSharp.StatementSyntax>.Empty,
            statement);
    }

    internal static CSharp.BlockSyntax CreateProbeBlock(
        ImmutableArray<CSharp.StatementSyntax> localStatements,
        ImmutableArray<CSharp.StatementSyntax> precedingStatements,
        CSharp.StatementSyntax statement)
    {
        if (localStatements.IsDefaultOrEmpty &&
            precedingStatements.IsDefaultOrEmpty)
        {
            return CSharpSyntaxFactory.Block(statement);
        }

        using var builder = ImmutableArrayBuilder<CSharp.StatementSyntax>.Rent();
        foreach (var localStatement in localStatements)
        {
            builder.Add(localStatement);
        }

        foreach (var precedingStatement in precedingStatements)
        {
            builder.Add(precedingStatement);
        }

        builder.Add(statement);
        return CSharpSyntaxFactory.Block(CSharpSyntaxFactory.List(builder.ToImmutable()));
    }

    private static void AddProbeSymbol(
        AkburaSymbol? symbol,
        ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax> memberDeclarations,
        ImmutableArrayBuilder<CSharp.StatementSyntax> localStatements)
    {
        if (symbol == null)
        {
            return;
        }

        switch (symbol.Kind)
        {
            case AkburaSymbolKind.State:
            {
                var state = (IStateSymbol)symbol;
                AddProbeLocal(
                    localStatements,
                    state.Name,
                    state.Type,
                    symbol,
                    StateCompletionAnnotationKind);
                break;
            }

            case AkburaSymbolKind.Parameter:
            {
                var parameter = (IParamSymbol)symbol;
                AddProbeLocal(
                    localStatements,
                    parameter.Name,
                    parameter.Type,
                    symbol);
                break;
            }

            case AkburaSymbolKind.CommandParameter:
            {
                var parameter = (ICommandParameterSymbol)symbol;
                AddProbeLocal(
                    localStatements,
                    parameter.Name,
                    parameter.Type,
                    symbol);
                break;
            }

            case AkburaSymbolKind.TailwindUtilityParameter:
            {
                var parameter = (ITailwindUtilityParameterSymbol)symbol;
                AddProbeLocal(
                    localStatements,
                    parameter.Name,
                    parameter.Type,
                    symbol);
                break;
            }

            case AkburaSymbolKind.MarkupItem:
            {
                var item = (IMarkupItemSymbol)symbol;
                AddProbeLocal(
                    localStatements,
                    item.Name,
                    item.Type,
                    symbol);
                break;
            }

            case AkburaSymbolKind.MarkupName:
            {
                var markupName = (IMarkupNameSymbol)symbol;
                AddProbeLocal(
                    localStatements,
                    markupName.IdentifierText,
                    markupName.Type,
                    symbol);
                break;
            }

            case AkburaSymbolKind.InjectedService:
            {
                var inject = (IInjectSymbol)symbol;
                AddProbeLocal(
                    localStatements,
                    inject.Name,
                    inject.Type,
                    symbol);
                break;
            }

            case AkburaSymbolKind.CSharpSymbol:
            {
                var local = (CSharpLocalSymbol)symbol;
                AddProbeLocal(
                    localStatements,
                    local.Name,
                    new CSharpSymbolDefinition(local.Local.Type),
                    symbol);
                break;
            }

            case AkburaSymbolKind.Command:
            {
                var command = (ICommandSymbol)symbol;
                AddCommandProbeMembers(memberDeclarations, command);
                break;
            }
        }
    }

    private static void AddProbeLocal(
        ImmutableArrayBuilder<CSharp.StatementSyntax> localStatements,
        string name,
        CSharpSymbolDefinition type,
        AkburaSymbol? sourceSymbol,
        string? annotationKind = null)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            type.Symbol is not ITypeSymbol typeSymbol)
        {
            return;
        }

        var typeSyntax = CSharpSyntaxFactory.ParseTypeName(
            typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        var declarator = CSharpSyntaxFactory.VariableDeclarator(
                CSharpSyntaxFactory.Identifier(name))
            .WithInitializer(CSharpSyntaxFactory.EqualsValueClause(
                CSharpSyntaxFactory.LiteralExpression(
                    CSharpSyntaxKind.DefaultLiteralExpression)));
        if (!string.IsNullOrEmpty(annotationKind))
        {
            declarator = declarator.WithAdditionalAnnotations(
                new SyntaxAnnotation(annotationKind));
        }

        if (sourceSymbol != null &&
            TryCreateProjectedSymbolAnnotation(
                sourceSymbol,
                name,
                out var projectedSymbolAnnotation))
        {
            declarator = declarator.WithAdditionalAnnotations(
                projectedSymbolAnnotation);
        }

        localStatements.Add(CSharpSyntaxFactory.LocalDeclarationStatement(
            CSharpSyntaxFactory.VariableDeclaration(typeSyntax)
                .WithVariables(CSharpSyntaxFactory.SingletonSeparatedList(
                    declarator))));
    }

    private static void AddCommandProbeMembers(
        ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax> memberDeclarations,
        ICommandSymbol command)
    {
        var commandTypeName = "__AkburaCommand_" + ToCSharpIdentifier(command.Name);
        var commandType = CSharpSyntaxFactory.IdentifierName(commandTypeName);
        memberDeclarations.Add(CSharpSyntaxFactory.ClassDeclaration(commandTypeName)
            .WithModifiers(CSharpSyntaxFactory.TokenList(
                CSharpSyntaxFactory.Token(CSharpSyntaxKind.PrivateKeyword),
                CSharpSyntaxFactory.Token(CSharpSyntaxKind.SealedKeyword)))
            .WithMembers(CSharpSyntaxFactory.List(CreateCommandProbeTypeMembers(command))));
        memberDeclarations.Add(CreateProbeField(
            commandType,
            command.Name,
            command));
    }

    private static ImmutableArray<CSharp.MemberDeclarationSyntax> CreateCommandProbeTypeMembers(ICommandSymbol command)
    {
        using var builder = ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax>.Rent();

        builder.Add(CSharpSyntaxFactory.PropertyDeclaration(
                CSharpSyntaxFactory.ParseTypeName("global::System.IObservable<bool>"),
                "IsExecuting")
            .WithModifiers(CSharpSyntaxFactory.TokenList(CSharpSyntaxFactory.Token(CSharpSyntaxKind.PublicKeyword)))
            .WithExpressionBody(CSharpSyntaxFactory.ArrowExpressionClause(
                CSharpSyntaxFactory.LiteralExpression(CSharpSyntaxKind.DefaultLiteralExpression)))
            .WithSemicolonToken(CSharpSyntaxFactory.Token(CSharpSyntaxKind.SemicolonToken)));

        builder.Add(CSharpSyntaxFactory.PropertyDeclaration(
                CSharpSyntaxFactory.ParseTypeName("global::System.IObservable<bool>"),
                "CanExecute")
            .WithModifiers(CSharpSyntaxFactory.TokenList(CSharpSyntaxFactory.Token(CSharpSyntaxKind.PublicKeyword)))
            .WithExpressionBody(CSharpSyntaxFactory.ArrowExpressionClause(
                CSharpSyntaxFactory.LiteralExpression(CSharpSyntaxKind.DefaultLiteralExpression)))
            .WithSemicolonToken(CSharpSyntaxFactory.Token(CSharpSyntaxKind.SemicolonToken)));

        builder.Add(CSharpSyntaxFactory.MethodDeclaration(
                GetCommandExecuteReturnTypeSyntax(command),
                "Execute")
            .WithModifiers(CSharpSyntaxFactory.TokenList(CSharpSyntaxFactory.Token(CSharpSyntaxKind.PublicKeyword)))
            .WithParameterList(CreateCommandExecuteParameterList(command))
            .WithBody(CSharpSyntaxFactory.Block(CSharpSyntaxFactory.ThrowStatement(
                CSharpSyntaxFactory.ObjectCreationExpression(
                        CSharpSyntaxFactory.ParseTypeName("global::System.NotImplementedException"))
                    .WithArgumentList(CSharpSyntaxFactory.ArgumentList())))));

        return builder.ToImmutable();
    }

    private static CSharp.ParameterListSyntax CreateCommandExecuteParameterList(ICommandSymbol command)
    {
        if (command.Parameters.IsDefaultOrEmpty)
        {
            return CSharpSyntaxFactory.ParameterList();
        }

        using var builder = ImmutableArrayBuilder<CSharp.ParameterSyntax>.Rent();
        foreach (var parameter in command.Parameters.OrderBy(parameter => parameter.Ordinal))
        {
            var type = parameter.Type.Symbol is ITypeSymbol typeSymbol
                ? CSharpSyntaxFactory.ParseTypeName(
                    typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                : CSharpSyntaxFactory.PredefinedType(CSharpSyntaxFactory.Token(CSharpSyntaxKind.ObjectKeyword));
            builder.Add(CSharpSyntaxFactory.Parameter(
                    CSharpSyntaxFactory.Identifier(parameter.Name))
                .WithType(type));
        }

        return CSharpSyntaxFactory.ParameterList(
            CSharpSyntaxFactory.SeparatedList(builder.ToImmutable()));
    }

    private static CSharp.TypeSyntax GetCommandExecuteReturnTypeSyntax(ICommandSymbol command)
    {
        if (command.HasResult &&
            command.ResultType.Symbol is ITypeSymbol resultType)
        {
            return CSharpSyntaxFactory.ParseTypeName(
                "global::System.Threading.Tasks.ValueTask<" +
                resultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                ">");
        }

        return CSharpSyntaxFactory.ParseTypeName("global::System.Threading.Tasks.ValueTask");
    }

    private static CSharp.FieldDeclarationSyntax CreateProbeField(
        CSharp.TypeSyntax type,
        string name,
        AkburaSymbol? sourceSymbol)
    {
        var declarator = CSharpSyntaxFactory.VariableDeclarator(
            CSharpSyntaxFactory.Identifier(name));
        if (sourceSymbol != null &&
            TryCreateProjectedSymbolAnnotation(
                sourceSymbol,
                name,
                out var annotation))
        {
            declarator = declarator.WithAdditionalAnnotations(annotation);
        }

        return CSharpSyntaxFactory.FieldDeclaration(
                CSharpSyntaxFactory.VariableDeclaration(type)
                    .WithVariables(CSharpSyntaxFactory.SingletonSeparatedList(
                        declarator)))
            .WithModifiers(CSharpSyntaxFactory.TokenList(CSharpSyntaxFactory.Token(CSharpSyntaxKind.PrivateKeyword)));
    }

    private static bool TryCreateProjectedSymbolAnnotation(
        AkburaSymbol symbol,
        string name,
        out SyntaxAnnotation annotation)
    {
        if (!TryGetDeclarationSpan(symbol, out var declarationSpan))
        {
            annotation = null!;
            return false;
        }

        var origin = new CSharpProbeSymbolOrigin(
            Guid.NewGuid().ToString("N"),
            symbol.Kind,
            name,
            declarationSpan);
        annotation = new SyntaxAnnotation(
            ProjectedSymbolAnnotationKind,
            origin.Serialize());
        return true;
    }

    private static SyntaxAnnotation CreateProjectedSymbolAnnotation(
        AkburaSymbolKind kind,
        string name,
        TextSpan declarationSpan)
    {
        var origin = new CSharpProbeSymbolOrigin(
            Guid.NewGuid().ToString("N"),
            kind,
            name,
            declarationSpan);
        return new SyntaxAnnotation(
            ProjectedSymbolAnnotationKind,
            origin.Serialize());
    }

    private static bool TryGetDeclarationSpan(
        AkburaSymbol symbol,
        out TextSpan span)
    {
        switch (symbol)
        {
            case IStateSymbol state:
                span = state.DeclarationSyntax.Name.Span;
                return true;
            case IParamSymbol parameter:
                span = parameter.DeclarationSyntax.Name.Span;
                return true;
            case IInjectSymbol inject:
                span = inject.DeclarationSyntax.Name.Span;
                return true;
            case ICommandSymbol command:
                span = command.DeclarationSyntax.Name.Span;
                return true;
            case ITailwindUtilityParameterSymbol
                {
                    DeclarationSyntax: { } declaration
                }:
                span = declaration.ParamName.Identifier.Span;
                return true;
            case IMarkupNameSymbol markupName:
                span = GetMarkupValueDeclarationSpan(
                    markupName.DeclarationSyntax,
                    markupName.IdentifierText);
                return true;
            case IMarkupItemSymbol item:
                span = GetMarkupValueDeclarationSpan(
                    item.DeclarationSyntax,
                    item.Name);
                return true;
            case CSharpLocalSymbol local:
                if (local.DeclarationSyntax is
                        CSharpStatementSyntax statementSyntax &&
                    EmbeddedCSharpSyntaxFacts.TryGetStatement(
                        statementSyntax,
                        out var statement,
                        out var hostSpan) &&
                    statement is
                        CSharp.LocalDeclarationStatementSyntax localDeclaration)
                {
                    var variable = localDeclaration.Declaration.Variables
                        .FirstOrDefault(candidate => string.Equals(
                            candidate.Identifier.ValueText,
                            local.Name,
                            StringComparison.Ordinal));
                    if (variable != null)
                    {
                        span = new TextSpan(
                            hostSpan.Start + variable.Identifier.Span.Start,
                            variable.Identifier.Span.Length);
                        return true;
                    }
                }

                span = local.DeclarationSyntax.Span;
                return true;
        }

        if (!symbol.DeclaringSyntaxReferences.IsDefaultOrEmpty)
        {
            span = symbol.DeclaringSyntaxReferences[0].Span;
            return true;
        }

        foreach (var location in symbol.Locations)
        {
            if (location.IsInSource)
            {
                span = location.SourceSpan;
                return true;
            }
        }

        span = default;
        return false;
    }

    private static TextSpan GetMarkupValueDeclarationSpan(
        MarkupAttachedPropertyAttributeSyntax declaration,
        string identifier)
    {
        var value = declaration.Value;
        if (value == null)
        {
            return declaration.Span;
        }

        var rawText = value.ToFullString();
        var identifierOffset = rawText.IndexOf(
            identifier,
            StringComparison.Ordinal);
        if (identifierOffset < 0)
        {
            return value.Span;
        }

        return new TextSpan(
            value.FullSpan.Start + identifierOffset,
            identifier.Length);
    }

    private static string ToCSharpIdentifier(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "_";
        }

        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            builder.Append(index == 0
                ? CSharpSyntaxFacts.IsIdentifierStartCharacter(ch) ? ch : '_'
                : CSharpSyntaxFacts.IsIdentifierPartCharacter(ch) ? ch : '_');
        }

        return builder.ToString();
    }

}

internal readonly struct CSharpProbeSymbolOrigin
{
    private const char Separator = ';';

    public CSharpProbeSymbolOrigin(
        string annotationId,
        AkburaSymbolKind kind,
        string name,
        TextSpan declarationSpan)
    {
        AnnotationId = annotationId;
        Kind = kind;
        Name = name;
        DeclarationSpan = declarationSpan;
    }

    public string AnnotationId { get; }

    public AkburaSymbolKind Kind { get; }

    public string Name { get; }

    public TextSpan DeclarationSpan { get; }

    public string Serialize()
    {
        var encodedName = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(Name));
        return string.Join(
            Separator.ToString(),
            AnnotationId,
            ((int)Kind).ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            DeclarationSpan.Start.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            DeclarationSpan.Length.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            encodedName);
    }

    public static bool TryParse(
        string? value,
        out CSharpProbeSymbolOrigin origin)
    {
        var parts = value?.Split(Separator);
        if (parts == null ||
            parts.Length != 5 ||
            string.IsNullOrWhiteSpace(parts[0]) ||
            !int.TryParse(
                parts[1],
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var kindValue) ||
            !int.TryParse(
                parts[2],
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var start) ||
            !int.TryParse(
                parts[3],
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var length) ||
            start < 0 ||
            length < 0)
        {
            origin = default;
            return false;
        }

        try
        {
            origin = new CSharpProbeSymbolOrigin(
                parts[0],
                (AkburaSymbolKind)kindValue,
                Encoding.UTF8.GetString(
                    Convert.FromBase64String(parts[4])),
                new TextSpan(start, length));
            return true;
        }
        catch (FormatException)
        {
            origin = default;
            return false;
        }
    }
}

internal readonly struct CSharpProbeScope
{
    public static readonly CSharpProbeScope Empty = new(
        ImmutableArray<CSharp.MemberDeclarationSyntax>.Empty,
        ImmutableArray<CSharp.StatementSyntax>.Empty);

    public CSharpProbeScope(
        ImmutableArray<CSharp.MemberDeclarationSyntax> memberDeclarations,
        ImmutableArray<CSharp.StatementSyntax> localStatements)
    {
        MemberDeclarations = memberDeclarations.IsDefault
            ? ImmutableArray<CSharp.MemberDeclarationSyntax>.Empty
            : memberDeclarations;
        LocalStatements = localStatements.IsDefault
            ? ImmutableArray<CSharp.StatementSyntax>.Empty
            : localStatements;
    }

    public ImmutableArray<CSharp.MemberDeclarationSyntax> MemberDeclarations { get; }

    public ImmutableArray<CSharp.StatementSyntax> LocalStatements { get; }
}
