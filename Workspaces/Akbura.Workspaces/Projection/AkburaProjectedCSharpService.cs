using Akbura.Pools;
using Akbura.Workspaces.Completion;
using Akbura.Workspaces.QuickInfo;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.QuickInfo;
using Microsoft.CodeAnalysis.Tags;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Text;
using RoslynCompletionItem = Microsoft.CodeAnalysis.Completion.CompletionItem;
using RoslynCompletionService = Microsoft.CodeAnalysis.Completion.CompletionService;
using RoslynQuickInfoService = Microsoft.CodeAnalysis.QuickInfo.QuickInfoService;

namespace Akbura.Workspaces.Projection;

internal sealed class AkburaProjectedCSharpService :
    IAkburaProjectedCSharpService
{
    private static readonly Lazy<HostServices> s_hostServices =
        new(CreateHostServices, LazyThreadSafetyMode.ExecutionAndPublication);

    public async Task<AkburaProjectedCompletionResult?> GetCompletionsAsync(
        AkburaSyntacticDocument document,
        AkburaDocumentContext? semanticContext,
        int position,
        AkburaProjectedCompletionTrigger trigger,
        CancellationToken cancellationToken = default)
    {
        using var session = TryCreateSession(
            document,
            semanticContext,
            position,
            cancellationToken);
        if (session == null)
        {
            return null;
        }

        var service = RoslynCompletionService.GetService(session.Document);
        if (service == null)
        {
            return null;
        }

        var roslynTrigger = AkburaRoslynCompletionTriggerPolicy
            .CreateRoslynTrigger(
                trigger.IsExplicit,
                trigger.IsIncomplete,
                trigger.Character);
        var sourceText = await session.Document
            .GetTextAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!trigger.IsExplicit &&
            !trigger.IsIncomplete)
        {
            var supported = AkburaRoslynCompletionTriggerPolicy
                .IsSupportedInsertionCharacter(trigger.Character);
            var preflight = AkburaRoslynCompletionTriggerPolicy.Evaluate(
                isExplicit: false,
                isIncompleteSession: false,
                isSupportedInsertion: supported,
                shouldTriggerCompletion: supported &&
                    service.ShouldTriggerCompletion(
                        sourceText,
                        session.Projection.ProjectedPosition,
                        roslynTrigger));
            if (preflight is
                AkburaRoslynCompletionPreflight.UnsupportedInsertion or
                AkburaRoslynCompletionPreflight.RoslynSuppressed)
            {
                return new AkburaProjectedCompletionResult(
                    ImmutableArray<AkburaProjectedCompletionItem>.Empty,
                    isIncomplete: false);
            }
        }

        var list = await service.GetCompletionsAsync(
                session.Document,
                session.Projection.ProjectedPosition,
                roslynTrigger,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (list == null)
        {
            return new AkburaProjectedCompletionResult(
                ImmutableArray<AkburaProjectedCompletionItem>.Empty,
                isIncomplete: false);
        }

        var selection = AkburaRoslynCompletionItemSelector.Select(
            list,
            sourceText,
            session.Projection.ProjectedPosition,
            trigger.IsExplicit,
            cancellationToken);
        using var items =
            ImmutableArrayBuilder<AkburaProjectedCompletionItem>.Rent(
                selection.Items.Length);
        var rawItems = list.ItemsList;
        foreach (var item in selection.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!session.Projection.TryMapToHost(
                    item.Span,
                    out var hostSpan))
            {
                continue;
            }

            var rawIndex = FindRawIndex(rawItems, item);
            if (rawIndex < 0)
            {
                continue;
            }

            items.Add(new AkburaProjectedCompletionItem(
                item.DisplayText,
                item.DisplayText,
                string.IsNullOrEmpty(item.FilterText)
                    ? item.DisplayText
                    : item.FilterText,
                string.IsNullOrEmpty(item.SortText)
                    ? item.DisplayText
                    : item.SortText,
                GetDetail(item),
                hostSpan,
                CreateResolveKey(item, rawIndex),
                GetKind(item)));
        }

        return new AkburaProjectedCompletionResult(
            items.ToImmutable(),
            selection.IsIncomplete);
    }

    public async Task<AkburaProjectedCompletionResolution?>
        ResolveCompletionAsync(
            AkburaSyntacticDocument document,
            AkburaDocumentContext? semanticContext,
            int position,
            string resolveKey,
            CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(resolveKey))
        {
            return null;
        }

        using var session = TryCreateSession(
            document,
            semanticContext,
            position,
            cancellationToken);
        if (session == null)
        {
            return null;
        }

        var service = RoslynCompletionService.GetService(session.Document);
        if (service == null)
        {
            return null;
        }

        var list = await service.GetCompletionsAsync(
                session.Document,
                session.Projection.ProjectedPosition,
                CompletionTrigger.Invoke,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (list == null ||
            !TryResolveItem(list, resolveKey, out var item))
        {
            return null;
        }

        var completionChange = await service.GetChangeAsync(
                session.Document,
                item,
                commitCharacter: null,
                cancellationToken)
            .ConfigureAwait(false);
        var projectedText = await session.Document
            .GetTextAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!AkburaCSharpCompletionChangeMapper.TryMapCompletionChange(
                document.Text,
                projectedText,
                session.Projection,
                completionChange,
                out var mapped))
        {
            return null;
        }

        var description = await service.GetDescriptionAsync(
                session.Document,
                item,
                cancellationToken)
            .ConfigureAwait(false);
        var documentation = description == null
            ? string.Empty
            : string.Concat(description.TaggedParts.Select(
                static part => part.Text));
        var change = new AkburaCompletionChange(
            mapped.Changes,
            mapped.NewHostPosition ?? position,
            triggerNextCompletion: false);
        return new AkburaProjectedCompletionResolution(
            GetDetail(item),
            documentation,
            change);
    }

    public async Task<AkburaQuickInfo?> GetQuickInfoAsync(
        AkburaSyntacticDocument document,
        AkburaDocumentContext? semanticContext,
        int position,
        CancellationToken cancellationToken = default)
    {
        using var session = TryCreateSession(
            document,
            semanticContext,
            position,
            cancellationToken);
        if (session == null)
        {
            return null;
        }

        var service = RoslynQuickInfoService.GetService(session.Document);
        if (service == null)
        {
            return null;
        }

        var info = await service.GetQuickInfoAsync(
                session.Document,
                session.Projection.ProjectedPosition,
                cancellationToken)
            .ConfigureAwait(false);
        if (info == null ||
            !session.Projection.TryMapToHost(
                info.Span,
                out var hostSpan))
        {
            return null;
        }

        using var sections = ImmutableArrayBuilder<string>.Rent(
            info.Sections.Length);
        foreach (var section in info.Sections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = string.Concat(
                section.TaggedParts.Select(static part => part.Text));
            if (!string.IsNullOrWhiteSpace(text))
            {
                sections.Add(text);
            }
        }

        if (sections.Count == 0)
        {
            return null;
        }

        var sectionSpan = sections.WrittenSpan;
        var signature = sectionSpan[0];
        using var details = ImmutableArrayBuilder<string>.Rent(
            Math.Max(0, sections.Count - 1));
        for (var index = 1; index < sections.Count; index++)
        {
            details.Add(sectionSpan[index]);
        }

        return new AkburaQuickInfo(
            hostSpan,
            AkburaQuickInfoKind.Symbol,
            signature,
            details.ToImmutable());
    }

    private static AkburaProjectedCSharpSession? TryCreateSession(
        AkburaSyntacticDocument document,
        AkburaDocumentContext? semanticContext,
        int position,
        CancellationToken cancellationToken)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }
        if ((uint)position > (uint)document.Text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }
        if (semanticContext == null ||
            !document.TryGetEmbeddedCSharpContext(
                position,
                out var embeddedContext,
                cancellationToken) ||
            !AkburaCSharpProjectionFactory.TryCreate(
                document,
                semanticContext,
                embeddedContext,
                out var projection,
                cancellationToken))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var workspace = new AdhocWorkspace(s_hostServices.Value);
        try
        {
            var compilation = semanticContext.Project.CSharpCompilation;
            var parseOptions = compilation.SyntaxTrees
                .Select(static tree => tree.Options)
                .OfType<CSharpParseOptions>()
                .FirstOrDefault() ?? CSharpParseOptions.Default;
            var project = workspace.AddProject(ProjectInfo.Create(
                ProjectId.CreateNewId(),
                VersionStamp.Create(),
                compilation.AssemblyName ?? "AkburaProjection",
                compilation.AssemblyName ?? "AkburaProjection",
                LanguageNames.CSharp,
                parseOptions: parseOptions,
                compilationOptions: compilation.Options,
                metadataReferences: compilation.References));

            var sourceIndex = 0;
            foreach (var syntaxTree in compilation.SyntaxTrees)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sourcePath = string.IsNullOrWhiteSpace(
                        syntaxTree.FilePath)
                    ? $"Source{sourceIndex}.cs"
                    : syntaxTree.FilePath;
                project = project.AddDocument(
                        $"{sourceIndex}_{Path.GetFileName(sourcePath)}",
                        syntaxTree.GetText(cancellationToken),
                        filePath: sourcePath)
                    .Project;
                sourceIndex++;
            }

            var projectionName = Path.GetFileNameWithoutExtension(
                    document.FilePath) +
                ".AkburaProjection.cs";
            var projectedDocument = project.AddDocument(
                    projectionName,
                    SourceText.From(
                        projection.Root.ToFullString(),
                        Encoding.UTF8),
                    filePath: document.FilePath + ".projection.cs")
                .WithSyntaxRoot(projection.Root);
            return new AkburaProjectedCSharpSession(
                workspace,
                projectedDocument,
                projection);
        }
        catch (Exception exception)
            when (exception is ArgumentException or
                InvalidOperationException or
                NotSupportedException)
        {
            workspace.Dispose();
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.CSharp,
                "Projected C# workspace could not be created.",
                exception);
            return null;
        }
    }

    private static HostServices CreateHostServices()
    {
        var assemblies = MefHostServices.DefaultAssemblies.ToList();
        AddAssembly("Microsoft.CodeAnalysis.Features");
        AddAssembly("Microsoft.CodeAnalysis.CSharp.Features");
        return MefHostServices.Create(assemblies.Distinct());

        void AddAssembly(string name)
        {
            try
            {
                assemblies.Add(Assembly.Load(name));
            }
            catch (FileNotFoundException)
            {
            }
        }
    }

    private static int FindRawIndex(
        IReadOnlyList<RoslynCompletionItem> items,
        RoslynCompletionItem expected)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (ReferenceEquals(items[index], expected) ||
                items[index].Equals(expected))
            {
                return index;
            }
        }

        return -1;
    }

    private static string CreateResolveKey(
        RoslynCompletionItem item,
        int index)
    {
        return string.Concat(
            index.ToString(CultureInfo.InvariantCulture),
            "|",
            item.Span.Start.ToString(CultureInfo.InvariantCulture),
            "|",
            item.Span.Length.ToString(CultureInfo.InvariantCulture),
            "|",
            item.DisplayText,
            "|",
            item.SortText);
    }

    private static bool TryResolveItem(
        CompletionList list,
        string resolveKey,
        out RoslynCompletionItem item)
    {
        var separator = resolveKey.IndexOf('|');
        if (separator <= 0 ||
            !int.TryParse(
                resolveKey[..separator],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var index) ||
            (uint)index >= (uint)list.ItemsList.Count)
        {
            item = null!;
            return false;
        }

        item = list.ItemsList[index];
        return string.Equals(
            CreateResolveKey(item, index),
            resolveKey,
            StringComparison.Ordinal);
    }

    private static string? GetDetail(RoslynCompletionItem item)
    {
        var suffix = item.DisplayTextSuffix ?? string.Empty;
        var inline = item.InlineDescription ?? string.Empty;
        if (string.IsNullOrWhiteSpace(suffix))
        {
            return string.IsNullOrWhiteSpace(inline)
                ? null
                : inline;
        }

        return string.IsNullOrWhiteSpace(inline)
            ? suffix
            : suffix + "  " + inline;
    }

    private static AkburaProjectedCompletionKind GetKind(
        RoslynCompletionItem item)
    {
        var tags = item.Tags;
        if (tags.Contains(WellKnownTags.Method) ||
            tags.Contains(WellKnownTags.ExtensionMethod))
        {
            return AkburaProjectedCompletionKind.Method;
        }
        if (tags.Contains(WellKnownTags.Property))
        {
            return AkburaProjectedCompletionKind.Property;
        }
        if (tags.Contains(WellKnownTags.Field))
        {
            return AkburaProjectedCompletionKind.Field;
        }
        if (tags.Contains(WellKnownTags.Event))
        {
            return AkburaProjectedCompletionKind.Event;
        }
        if (tags.Contains(WellKnownTags.Class) ||
            tags.Contains(WellKnownTags.Delegate))
        {
            return AkburaProjectedCompletionKind.Class;
        }
        if (tags.Contains(WellKnownTags.Structure))
        {
            return AkburaProjectedCompletionKind.Struct;
        }
        if (tags.Contains(WellKnownTags.Interface))
        {
            return AkburaProjectedCompletionKind.Interface;
        }
        if (tags.Contains(WellKnownTags.Enum))
        {
            return AkburaProjectedCompletionKind.Enum;
        }
        if (tags.Contains(WellKnownTags.EnumMember))
        {
            return AkburaProjectedCompletionKind.EnumMember;
        }
        if (tags.Contains(WellKnownTags.Namespace))
        {
            return AkburaProjectedCompletionKind.Module;
        }
        if (tags.Contains(WellKnownTags.Constant))
        {
            return AkburaProjectedCompletionKind.Constant;
        }
        if (tags.Contains(WellKnownTags.Keyword))
        {
            return AkburaProjectedCompletionKind.Keyword;
        }
        if (tags.Contains(WellKnownTags.TypeParameter))
        {
            return AkburaProjectedCompletionKind.TypeParameter;
        }
        if (tags.Contains(WellKnownTags.Local) ||
            tags.Contains(WellKnownTags.Parameter) ||
            tags.Contains(WellKnownTags.RangeVariable))
        {
            return AkburaProjectedCompletionKind.Variable;
        }

        return AkburaProjectedCompletionKind.Text;
    }
}

internal sealed class AkburaProjectedCSharpSession : IDisposable
{
    public AkburaProjectedCSharpSession(
        AdhocWorkspace workspace,
        Document document,
        AkburaCSharpProjection projection)
    {
        Workspace = workspace;
        Document = document;
        Projection = projection;
    }

    public AdhocWorkspace Workspace { get; }

    public Document Document { get; }

    public AkburaCSharpProjection Projection { get; }

    public void Dispose()
    {
        Workspace.Dispose();
    }
}