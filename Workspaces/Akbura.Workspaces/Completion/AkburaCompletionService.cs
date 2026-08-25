using Akbura.Language;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using RoslynPropertySymbol = Microsoft.CodeAnalysis.IPropertySymbol;

namespace Akbura.Workspaces.Completion;

internal sealed class AkburaCompletionService : IAkburaCompletionService
{
    private const int MaximumCompletionItems = 50;
    private const string HooksNamespace = "Akbura.Hooks";
    private const string EffectHooksTypeName =
        HooksNamespace + ".EffectHooks";

    private static readonly ImmutableArray<TopLevelCompletionDescriptor>
        TopLevelItems =
        [
            new(
                "state",
                "state ",
                "Declares reactive component state."),
            new(
                "param",
                "param ",
                "Declares a public component parameter."),
            new(
                "inject",
                "inject ",
                "Declares an injected service."),
            new(
                "command",
                "command ",
                "Declares a component command contract."),
        ];

    private static readonly ImmutableArray<TopLevelCompletionDescriptor>
        ParamModifierItems =
        [
            new(
                "bind",
                "bind ",
                "Declares a two-way bindable parameter."),
            new(
                "out",
                "out ",
                "Declares an output parameter."),
        ];

    private readonly AkcssCompletionService _akcssCompletionService =
        new();

    private static readonly ConditionalWeakTable<
        AkburaSemanticModel,
        SemanticModelCompletionCache> CompletionCaches = new();

    public AkburaCompletionResult GetCompletions(
        AkburaSyntacticDocument document,
        AkburaDocumentContext? semanticContext,
        int position,
        CancellationToken cancellationToken = default)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var isAkcssRegion = document.TryGetAkcssCompletionRegion(
            position,
            out _);
        var akcssContext = document.GetAkcssCompletionContext(
            position,
            cancellationToken);
        if (isAkcssRegion)
        {
            return _akcssCompletionService.GetCompletions(
                document,
                semanticContext,
                akcssContext,
                cancellationToken);
        }

        if (document.TryGetCSharpCompletionContext(
                position,
                out var csharpContext,
                cancellationToken) &&
            csharpContext.Kind ==
                AkburaCSharpCompletionContextKind.UsingDirectiveName)
        {
            return CreateAkcssModuleImportResult(
                document,
                semanticContext,
                csharpContext,
                cancellationToken);
        }

        var syntaxContext = document.GetCompletionContext(
            position,
            cancellationToken);
        if (syntaxContext.IsDefault)
        {
            return new AkburaCompletionResult(
                syntaxContext.ApplicableSpan,
                ImmutableArray<AkburaCompletionItem>.Empty);
        }

        if (syntaxContext.Kind ==
                AkburaCompletionContextKind.ClosingComponentName)
        {
            return CreateClosingTagResult(syntaxContext);
        }

        if (syntaxContext.Kind ==
                AkburaCompletionContextKind.TopLevel)
        {
            return CreateTopLevelResult(
                document,
                semanticContext,
                syntaxContext,
                position,
                cancellationToken);
        }

        if (syntaxContext.Kind ==
                AkburaCompletionContextKind.DeclarationModifier)
        {
            return CreateDescriptorResult(
                syntaxContext,
                ParamModifierItems);
        }

        if (semanticContext == null ||
            semanticContext.Document.SyntaxTree.Kind ==
                SyntaxTreeKind.Akcss)
        {
            return new AkburaCompletionResult(
                syntaxContext.ApplicableSpan,
                ImmutableArray<AkburaCompletionItem>.Empty,
                isIncomplete: IsSemanticCompletionContext(
                    syntaxContext.Kind));
        }

        var semanticModel = semanticContext.Project.Compilation
            .GetSemanticModel(
                semanticContext.Document.SyntaxTree);
        var items = syntaxContext.Kind switch
        {
            AkburaCompletionContextKind.ComponentName =>
                GetComponentItems(
                    semanticModel,
                    syntaxContext.Prefix,
                    cancellationToken),

            AkburaCompletionContextKind.AttributeName =>
                GetAttributeItems(
                    semanticModel,
                    syntaxContext,
                    cancellationToken),

            AkburaCompletionContextKind.PropertyElementName =>
                GetMemberItems(
                    semanticModel,
                    syntaxContext,
                    propertyElements: true,
                    cancellationToken),

            AkburaCompletionContextKind.MarkupExtensionType =>
                GetMarkupExtensionItems(
                    semanticModel,
                    syntaxContext.Prefix,
                    cancellationToken),

            _ => ImmutableArray<AkburaCompletionItem>.Empty,
        };

        return new AkburaCompletionResult(
            syntaxContext.ApplicableSpan,
            items,
            isIncomplete: IsSemanticCompletionContext(
                syntaxContext.Kind));
    }

    private static AkburaCompletionResult CreateClosingTagResult(
        AkburaSyntacticCompletionContext context)
    {
        if (string.IsNullOrWhiteSpace(
                context.ParentComponentName))
        {
            return new AkburaCompletionResult(
                context.ApplicableSpan,
                ImmutableArray<AkburaCompletionItem>.Empty);
        }

        var name = context.ParentComponentName!;
        if (!MatchesPrefix(name, context.Prefix))
        {
            return new AkburaCompletionResult(
                context.ApplicableSpan,
                ImmutableArray<AkburaCompletionItem>.Empty);
        }

        return new AkburaCompletionResult(
            context.ApplicableSpan,
            ImmutableArray.Create(
                new AkburaCompletionItem(
                    name,
                    name,
                    AkburaCompletionKind.ClosingTag,
                    $"Close '{name}'.")));
    }

    private static AkburaCompletionResult CreateTopLevelResult(
        AkburaSyntacticDocument document,
        AkburaDocumentContext? semanticContext,
        AkburaSyntacticCompletionContext context,
        int position,
        CancellationToken cancellationToken)
    {
        using var items =
            ImmutableArrayBuilder<AkburaCompletionItem>.Rent(
                TopLevelItems.Length + 3);
        AddDescriptorItems(
            items,
            context.Prefix,
            TopLevelItems);
        AddUseEffectItems(
            items,
            document,
            semanticContext,
            context,
            position,
            cancellationToken);

        return new AkburaCompletionResult(
            context.ApplicableSpan,
            items.ToImmutable());
    }

    private static AkburaCompletionResult CreateDescriptorResult(
        AkburaSyntacticCompletionContext context,
        ImmutableArray<TopLevelCompletionDescriptor> descriptors)
    {
        using var items =
            ImmutableArrayBuilder<AkburaCompletionItem>.Rent(
                descriptors.Length);
        AddDescriptorItems(
            items,
            context.Prefix,
            descriptors);
        return new AkburaCompletionResult(
            context.ApplicableSpan,
            items.ToImmutable());
    }

    private static void AddDescriptorItems(
        ImmutableArrayBuilder<AkburaCompletionItem> items,
        string prefix,
        ImmutableArray<TopLevelCompletionDescriptor> descriptors)
    {
        foreach (var descriptor in descriptors)
        {
            if (!MatchesPrefix(descriptor.DisplayText, prefix))
            {
                continue;
            }

            items.Add(new AkburaCompletionItem(
                descriptor.DisplayText,
                descriptor.InsertText,
                AkburaCompletionKind.Keyword,
                descriptor.Description,
                descriptionFactory: null,
                priority: 0,
                triggerCompletionAfterInsert: true));
        }
    }

    private static void AddUseEffectItems(
        ImmutableArrayBuilder<AkburaCompletionItem> items,
        AkburaSyntacticDocument document,
        AkburaDocumentContext? semanticContext,
        AkburaSyntacticCompletionContext context,
        int position,
        CancellationToken cancellationToken)
    {
        const string filterText = "useEffect";
        if (!MatchesPrefix(filterText, context.Prefix))
        {
            return;
        }

        var newLine = GetNewLine(document.Text);
        var indentation = GetIndentation(
            document.Text,
            context.ApplicableSpan.Start);
        var bodyIndentation = indentation +
            GetIndentationUnit(document.Text, position, indentation);
        var namespaceImport = IsUseEffectVisible(
                document,
                semanticContext,
                cancellationToken)
            ? null
            : HooksNamespace;

        AddHookItem(
            items,
            displayText: "useEffect",
            suffix: "every update",
            beforeCaret: string.Concat(
                "useEffect(() =>",
                newLine,
                indentation,
                "{",
                newLine,
                bodyIndentation),
            afterCaret: string.Concat(
                newLine,
                indentation,
                "});"),
            description: "Runs an effect after every component update.",
            sortText: "01_useEffect_0",
            namespaceImport);
        AddHookItem(
            items,
            displayText: "useEffect with dependencies",
            suffix: "dependency list",
            beforeCaret: string.Concat(
                "useEffect(() =>",
                newLine,
                indentation,
                "{",
                newLine,
                bodyIndentation),
            afterCaret: string.Concat(
                newLine,
                indentation,
                "}, []);"),
            description: "Runs an effect when one of its dependencies changes.",
            sortText: "01_useEffect_1",
            namespaceImport);
        AddHookItem(
            items,
            displayText: "useEffect async",
            suffix: "async with cancellation",
            beforeCaret: string.Concat(
                "useEffect(async cancellationToken =>",
                newLine,
                indentation,
                "{",
                newLine,
                bodyIndentation),
            afterCaret: string.Concat(
                newLine,
                indentation,
                "}, []);"),
            description: "Runs an asynchronous effect with cancellation.",
            sortText: "01_useEffect_2",
            namespaceImport);
    }

    private static void AddHookItem(
        ImmutableArrayBuilder<AkburaCompletionItem> items,
        string displayText,
        string suffix,
        string beforeCaret,
        string afterCaret,
        string description,
        string sortText,
        string? namespaceImport)
    {
        items.Add(new AkburaCompletionItem(
            displayText,
            beforeCaret + afterCaret,
            AkburaCompletionKind.Hook,
            description,
            descriptionFactory: null,
            filterText: "useEffect",
            sortText,
            suffix,
            priority: 1,
            caretOffsetFromEnd: afterCaret.Length,
            namespaceImport: namespaceImport));
    }

    private static bool IsUseEffectVisible(
        AkburaSyntacticDocument document,
        AkburaDocumentContext? semanticContext,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var node in document.SyntaxTree
                     .GetRootSyntax()
                     .DescendantNodesAndSelf())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node is NamespaceDeclarationSyntax namespaceDeclaration &&
                IsHooksNamespace(namespaceDeclaration))
            {
                return true;
            }

            if (node is UsingDirectiveSyntax usingDirective &&
                !AkburaUsingEditService.IsAkcssUsingDirective(
                    usingDirective) &&
                IsUseEffectUsing(usingDirective))
            {
                return true;
            }
        }

        if (semanticContext == null)
        {
            return false;
        }

        foreach (var usingDirective in semanticContext.Project
                     .Compilation
                     .GlobalAkburaUsingDirectives)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!AkburaUsingEditService.IsAkcssUsingDirective(
                    usingDirective) &&
                IsUseEffectUsing(usingDirective))
            {
                return true;
            }
        }

        foreach (var usingDirective in semanticContext.Project
                     .Compilation
                     .GlobalCSharpUsingDirectives)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsUseEffectUsing(usingDirective))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsHooksNamespace(
        NamespaceDeclarationSyntax namespaceDeclaration)
    {
        try
        {
            return IsQualifiedName(
                namespaceDeclaration.ToCSharp().Name.ToString(),
                HooksNamespace);
        }
        catch (Exception exception)
            when (exception is InvalidOperationException or
                  ArgumentException or InvalidCastException)
        {
            return false;
        }
    }

    private static bool IsUseEffectUsing(
        UsingDirectiveSyntax usingDirective)
    {
        try
        {
            return IsUseEffectUsing(usingDirective.ToCSharp());
        }
        catch (Exception exception)
            when (exception is InvalidOperationException or
                  ArgumentException or InvalidCastException)
        {
            return false;
        }
    }

    private static bool IsUseEffectUsing(
        Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax
            usingDirective)
    {
        if (usingDirective.Alias != null ||
            usingDirective.Name == null)
        {
            return false;
        }

        return IsQualifiedName(
            usingDirective.Name.ToString(),
            usingDirective.StaticKeyword.RawKind != 0
                ? EffectHooksTypeName
                : HooksNamespace);
    }

    private static bool IsQualifiedName(
        string name,
        string expectedName)
    {
        const string globalAlias = "global::";
        var normalizedName = name.Trim();
        if (normalizedName.StartsWith(
                globalAlias,
                StringComparison.Ordinal))
        {
            normalizedName = normalizedName[globalAlias.Length..];
        }

        return string.Equals(
            normalizedName,
            expectedName,
            StringComparison.Ordinal);
    }

    private static string GetNewLine(SourceText text)
    {
        foreach (var line in text.Lines)
        {
            var lineBreakLength =
                line.EndIncludingLineBreak - line.End;
            if (lineBreakLength > 0)
            {
                return text.ToString(new TextSpan(
                    line.End,
                    lineBreakLength));
            }
        }

        return Environment.NewLine;
    }

    private static string GetIndentation(
        SourceText text,
        int position)
    {
        var line = text.Lines.GetLineFromPosition(
            Math.Min(position, text.Length));
        var length = Math.Max(0, position - line.Start);
        for (var index = 0; index < length; index++)
        {
            if (!char.IsWhiteSpace(text[line.Start + index]))
            {
                return string.Empty;
            }
        }

        return text.ToString(new TextSpan(line.Start, length));
    }

    private static string GetIndentationUnit(
        SourceText text,
        int position,
        string currentIndentation)
    {
        if (currentIndentation.IndexOf('	') >= 0)
        {
            return "	";
        }

        var currentLine = text.Lines.GetLinePosition(
            Math.Min(position, text.Length)).Line;
        for (var distance = 1;
             distance < text.Lines.Count;
             distance++)
        {
            var previous = currentLine - distance;
            if (previous >= 0 &&
                TryGetIndentationDelta(
                    text,
                    text.Lines[previous],
                    currentIndentation,
                    out var previousDelta))
            {
                return previousDelta;
            }

            var next = currentLine + distance;
            if (next < text.Lines.Count &&
                TryGetIndentationDelta(
                    text,
                    text.Lines[next],
                    currentIndentation,
                    out var nextDelta))
            {
                return nextDelta;
            }
        }

        return "    ";
    }

    private static bool TryGetIndentationDelta(
        SourceText text,
        TextLine line,
        string currentIndentation,
        out string delta)
    {
        var position = line.Start;
        while (position < line.End &&
               text[position] is ' ' or '	')
        {
            position++;
        }

        if (position == line.End)
        {
            delta = string.Empty;
            return false;
        }

        var indentation = text.ToString(TextSpan.FromBounds(
            line.Start,
            position));
        if (indentation.Length > currentIndentation.Length &&
            indentation.StartsWith(
                currentIndentation,
                StringComparison.Ordinal))
        {
            delta = indentation[currentIndentation.Length..];
            return true;
        }

        delta = string.Empty;
        return false;
    }

    private static AkburaCompletionResult CreateAkcssModuleImportResult(
        AkburaSyntacticDocument document,
        AkburaDocumentContext? semanticContext,
        AkburaCSharpCompletionContext context,
        CancellationToken cancellationToken)
    {
        var applicableSpan = TextSpan.FromBounds(
            context.HostSpan.Start,
            context.HostPosition);
        var usingDirective = document.SyntaxTree
            .GetRootSyntax()
            .DescendantNodes()
            .OfType<UsingDirectiveSyntax>()
            .FirstOrDefault(candidate =>
                candidate.FullSpan == context.OwnerSpan);
        if (usingDirective == null ||
            usingDirective.Alias != null ||
            usingDirective.StaticKeyword.RawKind != 0 ||
            usingDirective.UnsafeKeyword.RawKind != 0 ||
            semanticContext == null)
        {
            return new AkburaCompletionResult(
                applicableSpan,
                ImmutableArray<AkburaCompletionItem>.Empty,
                isIncomplete: true);
        }

        var prefix = document.Text.ToString(applicableSpan);
        using var items =
            ImmutableArrayBuilder<AkburaCompletionItem>.Rent();
        foreach (var name in semanticContext.Project.Compilation
                     .GetAvailableAkcssModuleNames(cancellationToken)
                     .OrderBy(
                         static name => name,
                         StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!MatchesPrefix(name, prefix))
            {
                continue;
            }

            const int priority = 5;
            items.Add(new AkburaCompletionItem(
                name,
                name,
                AkburaCompletionKind.AkcssModule,
                $"Imports AKCSS module '{name}'.",
                descriptionFactory: null,
                filterText: name,
                sortText: $"{priority:D2}_{name}",
                suffix: "AKCSS module",
                priority: priority));
            if (items.Count == MaximumCompletionItems)
            {
                break;
            }
        }

        return new AkburaCompletionResult(
            applicableSpan,
            items.ToImmutable(),
            isIncomplete: true);
    }

    private static ImmutableArray<AkburaCompletionItem>
        GetComponentItems(
            AkburaSemanticModel semanticModel,
            string prefix,
            CancellationToken cancellationToken)
    {
        // Display names are not unique across imported namespaces.
        var items = new Dictionary<string, AkburaCompletionItem>(
            StringComparer.Ordinal);

        foreach (var candidate in
                 semanticModel.LookupMarkupComponents(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!MatchesPrefix(candidate.DisplayName, prefix))
            {
                continue;
            }

            var priority = GetComponentPriority(candidate);
            var key = "visible\0" +
                candidate.DisplayName +
                "\0" +
                candidate.MetadataName;
            items.Add(
                key,
                new AkburaCompletionItem(
                    candidate.DisplayName,
                    candidate.DisplayName,
                    AkburaCompletionKind.Component,
                    description: string.Empty,
                    descriptionFactory: () =>
                        candidate.ComponentType?.ToDisplayString(
                            SymbolDisplayFormat.FullyQualifiedFormat) ??
                        candidate.MetadataName,
                    filterText: candidate.DisplayName,
                    sortText:
                        $"{priority:D2}_{candidate.DisplayName}",
                    suffix: GetComponentSuffix(candidate),
                    priority: priority,
                    triggerCompletionAfterInsert: true));
        }

        foreach (var candidate in semanticModel
                     .LookupMarkupComponentCompletionImports(
                         cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var displayName = candidate.Type?.Name ??
                candidate.TypeDisplay;
            if (!MatchesPrefix(displayName, prefix))
            {
                continue;
            }

            var priority = 30 + candidate.Priority;
            var key = "import\0" +
                candidate.AssemblyName +
                "\0" +
                candidate.NamespaceName +
                "\0" +
                displayName;
            items.Add(
                key,
                new AkburaCompletionItem(
                    displayName,
                    displayName,
                    AkburaCompletionKind.Component,
                    description: string.Empty,
                    descriptionFactory: () =>
                    {
                        var typeName = candidate.Type?
                                .ToDisplayString(
                                    SymbolDisplayFormat
                                        .FullyQualifiedFormat) ??
                            candidate.NamespaceName +
                            "." +
                            displayName;

                        return typeName +
                            Environment.NewLine +
                            "Adds using " +
                            candidate.NamespaceName +
                            ".";
                    },
                    filterText: displayName,
                    sortText:
                        $"{priority:D2}_{displayName}_" +
                        candidate.NamespaceName,
                    suffix:
                        candidate.NamespaceName +
                        "  (using)",
                    priority: priority,
                    triggerCompletionAfterInsert: true,
                    namespaceImport: candidate.NamespaceName));
        }

        return OrderCompletionItems(items.Values, prefix);
    }

    private static ImmutableArray<AkburaCompletionItem>
        GetMarkupExtensionItems(
            AkburaSemanticModel semanticModel,
            string prefix,
            CancellationToken cancellationToken)
    {
        var items = new Dictionary<string, AkburaCompletionItem>(
            StringComparer.Ordinal);

        foreach (var candidate in
                 semanticModel.LookupMarkupExtensions(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!MatchesPrefix(candidate.DisplayName, prefix) ||
                items.ContainsKey(candidate.DisplayName))
            {
                continue;
            }

            var priority = GetMarkupExtensionPriority(candidate);
            var insertion = GetMarkupExtensionInsertion(candidate);
            var caretOffset = candidate.ExtensionType.Arity == 0
                ? 0
                : candidate.ExtensionType.Arity;
            items.Add(
                candidate.DisplayName,
                new AkburaCompletionItem(
                    candidate.DisplayName,
                    insertion,
                    AkburaCompletionKind.MarkupExtension,
                    description: string.Empty,
                    descriptionFactory: () =>
                        GetMarkupExtensionDescription(candidate),
                    sortText:
                        $"{priority:D2}_{candidate.DisplayName}",
                    suffix: GetMarkupExtensionSuffix(candidate),
                    priority: priority,
                    caretOffsetFromEnd: caretOffset));
        }

        return OrderCompletionItems(items.Values, prefix);
    }

    private static ImmutableArray<AkburaCompletionItem>
        GetAttributeItems(
            AkburaSemanticModel semanticModel,
            AkburaSyntacticCompletionContext context,
            CancellationToken cancellationToken)
    {
        var members = GetMemberItems(
            semanticModel,
            context,
            propertyElements: false,
            cancellationToken);
        var attachedProperties = GetAttachedPropertyItems(
            semanticModel,
            context,
            cancellationToken);
        var utilities = GetTailwindUtilityItems(
            semanticModel,
            context,
            cancellationToken);

        return OrderCompletionItems(
            members
                .Concat(attachedProperties)
                .Concat(utilities),
            context.Prefix);
    }

    private static ImmutableArray<AkburaCompletionItem>
        GetAttachedPropertyItems(
            AkburaSemanticModel semanticModel,
            AkburaSyntacticCompletionContext context,
            CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.ComponentName))
        {
            return [];
        }

        var existing = new HashSet<string>(
            context.ExistingAttributeNames,
            StringComparer.Ordinal);
        using var items =
            ImmutableArrayBuilder<AkburaCompletionItem>.Rent();

        foreach (var candidate in semanticModel
                     .LookupMarkupAttachedPropertiesForCompletion(
                         context.ComponentName!,
                         cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (existing.Contains(candidate.DisplayName) ||
                !MatchesPrefix(
                    candidate.DisplayName,
                    context.Prefix))
            {
                continue;
            }

            const int priority = 40;
            items.Add(
                new AkburaCompletionItem(
                    candidate.DisplayName,
                    candidate.DisplayName + "=\"\"",
                    AkburaCompletionKind.Property,
                    candidate.TypeDisplay +
                        " " +
                        candidate.DisplayName +
                        Environment.NewLine +
                        "Attached property declared by " +
                        candidate.OwnerTypeDisplay +
                        ".",
                    descriptionFactory: null,
                    filterText: candidate.DisplayName,
                    sortText:
                        $"{priority:D2}_{candidate.DisplayName}",
                    suffix:
                        candidate.TypeDisplay +
                        " (attached)",
                    priority: priority,
                    caretOffsetFromEnd: 1));
        }

        return items.ToImmutable();
    }

    private static ImmutableArray<AkburaCompletionItem>
        GetTailwindUtilityItems(
            AkburaSemanticModel semanticModel,
            AkburaSyntacticCompletionContext context,
            CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.ComponentName))
        {
            return ImmutableArray<AkburaCompletionItem>.Empty;
        }

        using var items =
            ImmutableArrayBuilder<AkburaCompletionItem>.Rent();
        foreach (var candidate in
                 semanticModel.LookupTailwindUtilities(
                     context.ComponentName!,
                     cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var insertion = candidate.Name +
                (candidate.Parameters.Length == 0 ? string.Empty : "-");
            if (!MatchesTailwindUtilityPrefix(
                    insertion,
                    context.Prefix))
            {
                continue;
            }

            var display = GetTailwindUtilityDisplay(candidate);
            var insertText = context.Prefix.Length > insertion.Length &&
                context.Prefix.StartsWith(
                    insertion,
                    StringComparison.OrdinalIgnoreCase)
                    ? context.Prefix
                    : insertion;
            const int priority = 60;
            items.Add(
                new AkburaCompletionItem(
                    display,
                    insertText,
                    AkburaCompletionKind.TailwindUtility,
                    description: string.Empty,
                    descriptionFactory: () =>
                        GetTailwindUtilityDescription(candidate),
                    filterText: insertion,
                    sortText:
                        $"{priority:D2}_{candidate.Name}_" +
                        $"{candidate.Parameters.Length:D2}_{display}",
                    suffix: string.IsNullOrWhiteSpace(
                            candidate.TargetTypeDisplay)
                        ? "all targets"
                        : candidate.TargetTypeDisplay,
                    priority: priority));
        }

        return items.AsEnumerable()
            .OrderBy(static item => item.SortText,
                StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static string GetTailwindUtilityDisplay(
        TailwindUtilityLookupCandidate candidate)
    {
        var builder = new System.Text.StringBuilder(
            candidate.Name);
        foreach (var parameter in candidate.Parameters)
        {
            builder.Append("-(");
            if (!string.IsNullOrWhiteSpace(parameter.TypeDisplay))
            {
                builder.Append(parameter.TypeDisplay);
                builder.Append(' ');
            }

            builder.Append(parameter.Name);
            if (parameter.IsOptional)
            {
                builder.Append(" = default");
            }

            builder.Append(')');
        }

        return builder.ToString();
    }

    private static string GetTailwindUtilityDescription(
        TailwindUtilityLookupCandidate candidate)
    {
        var display = GetTailwindUtilityDisplay(candidate);
        return string.IsNullOrWhiteSpace(candidate.TargetTypeDisplay)
            ? $"AKCSS utility {display}"
            : $"AKCSS utility {display}{Environment.NewLine}" +
                $"Target: {candidate.TargetTypeDisplay}";
    }

    private static bool MatchesTailwindUtilityPrefix(
        string insertion,
        string prefix)
    {
        return MatchesPrefix(insertion, prefix) ||
            prefix.StartsWith(
                insertion,
                StringComparison.OrdinalIgnoreCase);
    }

    private static ImmutableArray<AkburaCompletionItem>
        GetMemberItems(
            AkburaSemanticModel semanticModel,
            AkburaSyntacticCompletionContext context,
            bool propertyElements,
            CancellationToken cancellationToken)
    {
        var componentName = propertyElements
            ? context.ParentComponentName
            : context.ComponentName;
        if (string.IsNullOrWhiteSpace(componentName))
        {
            return ImmutableArray<AkburaCompletionItem>.Empty;
        }

        var existing = new HashSet<string>(
            context.ExistingAttributeNames,
            StringComparer.Ordinal);
        var cache = CompletionCaches.GetValue(
            semanticModel,
            static _ => new SemanticModelCompletionCache());
        var catalog = cache.GetOrCreate(
            componentName!,
            propertyElements,
            () => CreateMemberCatalog(
                semanticModel,
                componentName!,
                propertyElements,
                cancellationToken),
            cancellationToken);

        return catalog
            .Where(candidate =>
                !existing.Contains(candidate.MemberName) &&
                MatchesPrefix(
                    candidate.Item.DisplayText,
                    context.Prefix))
            .Select(static candidate => candidate.Item)
            .OrderBy(static item => item.SortText,
                StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static ImmutableArray<AkburaCompletionItem>
        OrderCompletionItems(
            IEnumerable<AkburaCompletionItem> items,
            string prefix)
    {
        var ordered = items.OrderBy(
            static item => item.SortText,
            StringComparer.Ordinal);

        // VS keeps filtering the original session after trigger characters.
        return string.IsNullOrEmpty(prefix)
            ? ordered.ToImmutableArray()
            : ordered
                .Take(MaximumCompletionItems)
                .ToImmutableArray();
    }

    private static ImmutableArray<CompletionMemberCandidate> CreateMemberCatalog(
        AkburaSemanticModel semanticModel,
        string componentName,
        bool propertyElements,
        CancellationToken cancellationToken)
    {
        if (!semanticModel.TryResolveMarkupComponentForCompletion(
                componentName,
                out var target))
        {
            return [];
        }

        var items = new Dictionary<string, AkburaCompletionItem>(
            StringComparer.Ordinal);
        var ownerName = GetSimpleName(componentName);

        if (!propertyElements)
        {
            items.Add(
                "x.Name",
                new AkburaCompletionItem(
                    "x.Name",
                    "x.Name=\"\"",
                    AkburaCompletionKind.Property,
                    "Names this element in the current Akbura component.",
                    descriptionFactory: null,
                    caretOffsetFromEnd: 1));
        }

        if (target.AkburaComponent != null)
        {
            foreach (var parameter in
                     target.AkburaComponent.Parameters)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!parameter.ReceivesValueFromParent)
                {
                    continue;
                }

                AddMemberItem(
                    items,
                    ownerName,
                    parameter.Name,
                    AkburaCompletionKind.Parameter,
                    parameter.Type.ToDisplayString(
                        SymbolDisplayFormat.MinimallyQualifiedFormat),
                    propertyElements);
            }

            if (!propertyElements)
            {
                foreach (var command in
                         target.AkburaComponent.Commands)
                {
                    AddMemberItem(
                        items,
                        ownerName,
                        command.Name,
                        AkburaCompletionKind.Command,
                        command.ToDisplayString(),
                        propertyElements: false);
                }
            }
        }

        if (target.ComponentType != null)
        {
            AddClrMembers(
                items,
                ownerName,
                target.ComponentType,
                EmptyMemberNames,
                propertyElements,
                cancellationToken);
        }

        return items.Values
            .OrderBy(static item => item.SortText,
                StringComparer.Ordinal)
            .Select(item => new CompletionMemberCandidate(
                GetMemberName(item.DisplayText, propertyElements),
                item))
            .ToImmutableArray();
    }

    private static readonly HashSet<string> EmptyMemberNames =
        new(StringComparer.Ordinal);

    private static void AddClrMembers(
        Dictionary<string, AkburaCompletionItem> items,
        string ownerName,
        INamedTypeSymbol componentType,
        HashSet<string> existing,
        bool propertyElements,
        CancellationToken cancellationToken)
    {
        var visitedTypes = new HashSet<INamedTypeSymbol>(
            SymbolEqualityComparer.Default);
        for (var current = componentType;
             current != null;
             current = current.BaseType)
        {
            AddClrMembersFromType(
                items,
                ownerName,
                current,
                existing,
                propertyElements,
                cancellationToken);
            visitedTypes.Add(current);
        }

        foreach (var @interface in componentType.AllInterfaces)
        {
            if (visitedTypes.Add(@interface))
            {
                AddClrMembersFromType(
                    items,
                    ownerName,
                    @interface,
                    existing,
                    propertyElements,
                    cancellationToken);
            }
        }
    }

    private static void AddClrMembersFromType(
        Dictionary<string, AkburaCompletionItem> items,
        string ownerName,
        INamedTypeSymbol type,
        HashSet<string> existing,
        bool propertyElements,
        CancellationToken cancellationToken)
    {
        foreach (var member in type.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (member)
            {
                case RoslynPropertySymbol property
                    when !property.IsStatic &&
                         !property.IsIndexer &&
                         property.DeclaredAccessibility ==
                             Accessibility.Public &&
                         !existing.Contains(property.Name):
                    AddMemberItem(
                        items,
                        ownerName,
                        property.Name,
                        AkburaCompletionKind.Property,
                        property.Type.ToDisplayString(
                            SymbolDisplayFormat.MinimallyQualifiedFormat),
                        propertyElements);
                    break;

                case IEventSymbol @event
                    when !propertyElements &&
                         !@event.IsStatic &&
                         @event.DeclaredAccessibility ==
                             Accessibility.Public &&
                         !existing.Contains(@event.Name):
                    AddMemberItem(
                        items,
                        ownerName,
                        @event.Name,
                        AkburaCompletionKind.Event,
                        @event.Type.ToDisplayString(
                            SymbolDisplayFormat.MinimallyQualifiedFormat),
                        propertyElements: false);
                    break;

                case IFieldSymbol field
                    when field.IsStatic &&
                         field.DeclaredAccessibility ==
                             Accessibility.Public &&
                         field.Name.EndsWith(
                             "Property",
                             StringComparison.Ordinal):
                    var propertyName = field.Name[..^"Property".Length];
                    if (!existing.Contains(propertyName))
                    {
                        AddMemberItem(
                            items,
                            ownerName,
                            propertyName,
                            AkburaCompletionKind.Property,
                            field.Type.ToDisplayString(
                                SymbolDisplayFormat.MinimallyQualifiedFormat),
                            propertyElements);
                    }

                    break;
            }
        }
    }

    private static void AddMemberItem(
        Dictionary<string, AkburaCompletionItem> items,
        string ownerName,
        string memberName,
        AkburaCompletionKind kind,
        string typeDisplay,
        bool propertyElements)
    {
        var displayName = propertyElements
            ? ownerName + "." + memberName
            : memberName;

        if (items.ContainsKey(displayName))
        {
            return;
        }

        var insertText = displayName;
        var caretOffsetFromEnd = 0;
        var triggerCompletionAfterInsert = false;

        if (!propertyElements)
        {
            if (kind is
                AkburaCompletionKind.Event or
                AkburaCompletionKind.Command)
            {
                insertText += "={}";
                caretOffsetFromEnd = 1;
                triggerCompletionAfterInsert = true;
            }
            else
            {
                insertText += "=\"\"";
                caretOffsetFromEnd = 1;
            }
        }

        items.Add(
            displayName,
            new AkburaCompletionItem(
                displayName,
                insertText,
                propertyElements
                    ? AkburaCompletionKind.PropertyElement
                    : kind,
                typeDisplay.Length == 0
                    ? memberName
                    : typeDisplay + " " + memberName,
                descriptionFactory: null,
                suffix: typeDisplay,
                caretOffsetFromEnd:
                    caretOffsetFromEnd,
                triggerCompletionAfterInsert:
                    triggerCompletionAfterInsert));
    }

    private static bool IsSemanticCompletionContext(
        AkburaCompletionContextKind kind)
    {
        return kind is
            AkburaCompletionContextKind.ComponentName or
            AkburaCompletionContextKind.AttributeName or
            AkburaCompletionContextKind.PropertyElementName or
            AkburaCompletionContextKind.MarkupExtensionType;
    }

    private static bool MatchesPrefix(
        string value,
        string prefix)
    {
        return prefix.Length == 0 ||
            value.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase);
    }

    private static int GetComponentPriority(
        MarkupComponentLookupCandidate candidate)
    {
        if (candidate.IsAkburaComponent)
        {
            return 0;
        }

        var metadataName = candidate.ComponentType?.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat) ??
            candidate.MetadataName;
        if (metadataName.StartsWith(
                "global::Avalonia.Controls.",
                StringComparison.Ordinal))
        {
            return 10;
        }

        return metadataName.StartsWith(
            "global::Avalonia.",
            StringComparison.Ordinal)
                ? 20
                : 90;
    }

    private static string GetComponentSuffix(
        MarkupComponentLookupCandidate candidate)
    {
        if (candidate.IsAkburaComponent)
        {
            return "Akbura component";
        }

        return candidate.ComponentType?
                .ContainingNamespace
                .ToDisplayString() ??
            string.Empty;
    }

    private static int GetMarkupExtensionPriority(
        MarkupExtensionLookupCandidate candidate)
    {
        if (candidate.IsAvaloniaBinding ||
            candidate.DisplayName is
                "StaticResource" or
                "DynamicResource")
        {
            return 0;
        }

        return candidate.IsUtilityVariant ? 5 : 20;
    }

    private static string GetMarkupExtensionInsertion(
        MarkupExtensionLookupCandidate candidate)
    {
        var arity = candidate.ExtensionType.Arity;
        return arity == 0
            ? candidate.DisplayName
            : candidate.DisplayName + "<" +
                new string(',', arity - 1) + ">";
    }

    private static string GetMarkupExtensionSuffix(
        MarkupExtensionLookupCandidate candidate)
    {
        if (candidate.IsAvaloniaBinding)
        {
            return "Avalonia binding";
        }

        if (candidate.IsUtilityVariant)
        {
            return "utility variant";
        }

        return candidate.ProvideValueMethod?.ReturnType
                .ToDisplayString(
                    SymbolDisplayFormat.MinimallyQualifiedFormat) ??
            candidate.ExtensionType.ContainingNamespace.ToDisplayString();
    }

    private static string GetMarkupExtensionDescription(
        MarkupExtensionLookupCandidate candidate)
    {
        var typeName = candidate.ExtensionType.ToDisplayString(
            SymbolDisplayFormat.FullyQualifiedFormat);
        if (candidate.IsAvaloniaBinding)
        {
            return typeName +
                " (handled by the Avalonia binding markup binder)";
        }

        var provideValue = candidate.ProvideValueMethod;
        return provideValue == null
            ? typeName
            : typeName + Environment.NewLine +
                provideValue.ToDisplayString(
                    SymbolDisplayFormat.MinimallyQualifiedFormat);
    }

    private static string GetSimpleName(string componentName)
    {
        var name = componentName;
        var aliasSeparator = name.LastIndexOf("::", StringComparison.Ordinal);
        if (aliasSeparator >= 0)
        {
            name = name[(aliasSeparator + 2)..];
        }

        var namespaceSeparator = name.LastIndexOf('.');
        return namespaceSeparator < 0
            ? name
            : name[(namespaceSeparator + 1)..];
    }

    private static string GetMemberName(
        string displayName,
        bool propertyElements)
    {
        if (!propertyElements)
        {
            return displayName;
        }

        var separator = displayName.LastIndexOf('.');
        return separator < 0
            ? displayName
            : displayName[(separator + 1)..];
    }

    private readonly record struct TopLevelCompletionDescriptor(
        string DisplayText,
        string InsertText,
        string Description);

    private readonly struct CompletionMemberCandidate
    {
        public CompletionMemberCandidate(
            string memberName,
            AkburaCompletionItem item)
        {
            MemberName = memberName;
            Item = item;
        }

        public string MemberName { get; }

        public AkburaCompletionItem Item { get; }
    }

    private sealed class SemanticModelCompletionCache
    {
        private ImmutableDictionary<CompletionMemberCatalogKey, ImmutableArray<CompletionMemberCandidate>> _catalogs =
            ImmutableDictionary<CompletionMemberCatalogKey, ImmutableArray<CompletionMemberCandidate>>.Empty;

        public ImmutableArray<CompletionMemberCandidate> GetOrCreate(
            string componentName,
            bool propertyElements,
            Func<ImmutableArray<CompletionMemberCandidate>> factory,
            CancellationToken cancellationToken)
        {
            var key = new CompletionMemberCatalogKey(
                componentName,
                propertyElements);

            var snapshot = Volatile.Read(ref _catalogs);

            if (snapshot.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var created = factory();

            cancellationToken.ThrowIfCancellationRequested();

            return ImmutableInterlocked.GetOrAdd(
                ref _catalogs,
                key,
                created);
        }

        private readonly record struct CompletionMemberCatalogKey(
            string ComponentName,
            bool PropertyElements);
    }

}
