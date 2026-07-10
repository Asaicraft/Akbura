using Akbura.Language;
using Akbura.Language.BoundTree;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using AkburaSyntaxKind = Akbura.Language.Syntax.SyntaxKind;

namespace Akbura.Benchmarks;

[Config(typeof(SemanticSnapshotLazyBenchmarkConfig))]
[MemoryDiagnoser]
public class SemanticSnapshotLazyBenchmarks
{
    private AkburaCompilation _oldCompilation = null!;
    private AkburaSyntaxTree _newDashboardTree = null!;
    private AkburaSyntaxTree _taskCardTree = null!;
    private AkburaSyntaxTree _statusBadgeTree = null!;
    private AkcssSyntaxTree _dashboardAkcssTree = null!;
    private AkcssSyntaxTree _sharedAkcssTree = null!;
    private CSharpCompilation _csharpCompilation = null!;
    private MarkupAttributeSyntax _lazyQueryAttribute = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        var oldDashboardTree = AkburaSyntaxTree.ParseText(DashboardCode, DashboardPath);
        _taskCardTree = AkburaSyntaxTree.ParseText(TaskCardCode, TaskCardPath);
        _statusBadgeTree = AkburaSyntaxTree.ParseText(StatusBadgeCode, StatusBadgePath);
        _dashboardAkcssTree = AkcssSyntaxTree.ParseText(DashboardAkcss, DashboardAkcssPath);
        _sharedAkcssTree = AkcssSyntaxTree.ParseText(SharedAkcss, "Shared.akcss", "Demo.Styles.Shared.akcss");
        _csharpCompilation = CreateCSharpCompilation(ProjectCSharpCode);
        _oldCompilation = CreateProjectCompilation(oldDashboardTree);

        const string oldHandler = "Toggle={async item => await Refresh.Execute(UserId)}";
        const string newHandler = "Toggle={async item => await Refresh.Execute(UserId + 1)}";
        var changeStart = DashboardCode.IndexOf(oldHandler, StringComparison.Ordinal);
        if (changeStart < 0)
        {
            throw new InvalidOperationException("Could not find benchmark edit target.");
        }

        var newDashboardCode = DashboardCode.Remove(changeStart, oldHandler.Length).Insert(changeStart, newHandler);
        var change = new TextChangeRange(new TextSpan(changeStart, oldHandler.Length), newHandler.Length);
        _newDashboardTree = oldDashboardTree.WithChangedText(SourceText.From(newDashboardCode), [change]);
        _lazyQueryAttribute = GetAttribute(
            FindDescendantElement(GetRootMarkupElement(_newDashboardTree.GetRoot(), "StackPanel"), "TaskCard"),
            "Toggle");

        var oldQueryAttribute = GetAttribute(
            FindDescendantElement(GetRootMarkupElement(oldDashboardTree.GetRoot(), "StackPanel"), "TaskCard"),
            "Toggle");
        var oldModel = _oldCompilation.GetSemanticModel(oldDashboardTree);
        _ = _oldCompilation.DeclarationTable;
        _ = oldModel.GetOperation(oldQueryAttribute);
        _ = oldModel.GetSemanticDiagnostics(oldQueryAttribute);
    }

    [Benchmark(Baseline = true)]
    public int FullSemanticQueryAfterEdit()
    {
        var compilation = CreateProjectCompilation(_newDashboardTree);
        return ForceFullSemanticQuery(compilation);
    }

    [Benchmark]
    public int CrossSnapshotLazyFirstQueryAfterEdit()
    {
        var compilation = _oldCompilation.WithSyntaxTrees(
            [_newDashboardTree, _taskCardTree, _statusBadgeTree]);
        var model = compilation.GetSemanticModel(_newDashboardTree);
        var operation = model.GetOperation(_lazyQueryAttribute);
        var diagnostics = model.GetSemanticDiagnostics(_lazyQueryAttribute);

        var checksum = _newDashboardTree.GetRootSyntax().FullWidth + diagnostics.Length;
        if (operation != null)
        {
            checksum += (int)operation.Kind;
            checksum += operation.Children.Length * 17;
            checksum += operation.HasErrors ? 1 : 0;
        }

        return checksum;
    }

    private AkburaCompilation CreateProjectCompilation(AkburaSyntaxTree dashboardTree)
    {
        return new AkburaCompilation(
            _csharpCompilation,
            [dashboardTree, _taskCardTree, _statusBadgeTree],
            [_dashboardAkcssTree, _sharedAkcssTree],
            rootNamespace: "Demo",
            projectDirectory: ProjectDirectory);
    }

    private int ForceFullSemanticQuery(AkburaCompilation compilation)
    {
        var checksum = 0;
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            checksum += ForceAkburaTree(model, tree.GetRoot());
        }

        var dashboardModel = compilation.GetSemanticModel(_newDashboardTree);
        foreach (var tree in compilation.AkcssSyntaxTrees)
        {
            checksum += ForceExternalAkcssTree(dashboardModel, tree.GetRoot());
        }

        return checksum;
    }

    private static int ForceAkburaTree(AkburaSemanticModel model, AkburaDocumentSyntax root)
    {
        var checksum = root.FullWidth;
        checksum += model.GetSemanticDiagnostics(root).Length;

        foreach (var syntax in SelfAndDescendants(root))
        {
            if (IsSymbolSyntax(syntax))
            {
                var symbolInfo = model.GetSymbolInfo(syntax);
                checksum += symbolInfo.Symbol?.Name.Length ?? 0;
                checksum += symbolInfo.CandidateSymbols.Length;
            }

            if (IsOperationSyntax(syntax))
            {
                var operation = model.GetOperation(syntax);
                checksum += operation == null ? 0 : (int)operation.Kind + operation.Children.Length;
            }
        }

        return checksum;
    }

    private static int ForceExternalAkcssTree(AkburaSemanticModel model, AkcssDocumentSyntax root)
    {
        var boundRoot = model.BindingSession.BindSemanticSyntax(root);
        var checksum = ForceBoundTree(boundRoot);
        var module = model.GetDeclaredSymbol(root) as IAkcssModuleSymbol;
        if (module != null)
        {
            checksum += module.AkcssSymbols.Length * 31;
            foreach (var symbol in module.AkcssSymbols)
            {
                checksum += symbol.Name.Length;
                checksum += symbol.Operations.Length * 17;
            }
        }

        foreach (var syntax in root.DescendantNodes())
        {
            if (IsOperationSyntax(syntax))
            {
                checksum += ForceBoundTree(model.BindingSession.BindOperationSyntax(syntax));
            }
        }

        return checksum;
    }

    private static int ForceBoundTree(BoundNode node)
    {
        var checksum = (int)node.Kind + node.Diagnostics.Length;
        foreach (var child in node.Children)
        {
            checksum += ForceBoundTree(child);
        }

        return checksum;
    }

    private static IEnumerable<AkburaSyntax> SelfAndDescendants(AkburaSyntax root)
    {
        yield return root;
        foreach (var descendant in root.DescendantNodes())
        {
            yield return descendant;
        }
    }

    private static bool IsSymbolSyntax(AkburaSyntax syntax)
    {
        return syntax.Kind is
            AkburaSyntaxKind.AkburaDocumentSyntax or
            AkburaSyntaxKind.StateDeclarationSyntax or
            AkburaSyntaxKind.ParamDeclarationSyntax or
            AkburaSyntaxKind.InjectDeclarationSyntax or
            AkburaSyntaxKind.CommandDeclarationSyntax or
            AkburaSyntaxKind.UseEffectDeclarationSyntax or
            AkburaSyntaxKind.InlineAkcssBlockSyntax or
            AkburaSyntaxKind.AkcssStyleRuleSyntax or
            AkburaSyntaxKind.AkcssUtilityDeclarationSyntax or
            AkburaSyntaxKind.MarkupElementSyntax or
            AkburaSyntaxKind.MarkupPlainAttributeSyntax or
            AkburaSyntaxKind.MarkupPrefixedAttributeSyntax or
            AkburaSyntaxKind.TailwindFlagAttributeSyntax or
            AkburaSyntaxKind.TailwindFullAttributeSyntax;
    }

    private static bool IsOperationSyntax(AkburaSyntax syntax)
    {
        return syntax.Kind is
            AkburaSyntaxKind.MarkupPlainAttributeSyntax or
            AkburaSyntaxKind.MarkupPrefixedAttributeSyntax or
            AkburaSyntaxKind.TailwindFlagAttributeSyntax or
            AkburaSyntaxKind.TailwindFullAttributeSyntax or
            AkburaSyntaxKind.AkcssAssignmentSyntax or
            AkburaSyntaxKind.AkcssIfDirectiveSyntax or
            AkburaSyntaxKind.AkcssApplyDirectiveSyntax or
            AkburaSyntaxKind.AkcssInterceptDirectiveSyntax;
    }

    private static CSharpCompilation CreateCSharpCompilation(params string[] sources)
    {
        return CSharpCompilation.Create(
            assemblyName: "SemanticSnapshotLazyBenchmarks",
            references: CreateAvaloniaReferences(),
            syntaxTrees: sources.Select(source => CSharpSyntaxTree.ParseText(
                source,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview))));
    }

    private static MetadataReference[] CreateAvaloniaReferences()
    {
        var avaloniaRefDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget",
            "packages",
            "avalonia",
            "12.0.4",
            "ref",
            "net10.0");

        return
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.ComponentModel.INotifyPropertyChanged).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ImmutableArray<>).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(avaloniaRefDirectory, "Avalonia.dll")),
            MetadataReference.CreateFromFile(Path.Combine(avaloniaRefDirectory, "Avalonia.Base.dll")),
            MetadataReference.CreateFromFile(Path.Combine(avaloniaRefDirectory, "Avalonia.Controls.dll")),
        ];
    }

    private static MarkupElementSyntax GetRootMarkupElement(
        AkburaDocumentSyntax root,
        string name)
    {
        return root.Members
            .OfType<MarkupRootSyntax>()
            .Select(markupRoot => markupRoot.Element)
            .Single(element => GetElementName(element) == name);
    }

    private static MarkupElementSyntax FindDescendantElement(
        MarkupElementSyntax root,
        string name)
    {
        return DescendantElements(root)
            .Single(element => GetElementName(element) == name);
    }

    private static IEnumerable<MarkupElementSyntax> DescendantElements(MarkupElementSyntax root)
    {
        foreach (var content in root.Body)
        {
            if (content is not MarkupElementContentSyntax elementContent)
            {
                continue;
            }

            yield return elementContent.Element;
            foreach (var child in DescendantElements(elementContent.Element))
            {
                yield return child;
            }
        }
    }

    private static MarkupAttributeSyntax GetAttribute(
        MarkupElementSyntax element,
        string name)
    {
        return element.StartTag!.Attributes.Single(attribute => GetAttributeName(attribute) == name);
    }

    private static string GetElementName(MarkupElementSyntax element)
    {
        return element.StartTag?.Name.ToFullString().Trim() ?? string.Empty;
    }

    private static string GetAttributeName(MarkupAttributeSyntax attribute)
    {
        return attribute.Kind switch
        {
            AkburaSyntaxKind.MarkupPlainAttributeSyntax => ((MarkupPlainAttributeSyntax)attribute).Name.Identifier.ValueText,
            AkburaSyntaxKind.MarkupPrefixedAttributeSyntax => ((MarkupPrefixedAttributeSyntax)attribute).Name.Identifier.ValueText,
            AkburaSyntaxKind.TailwindFlagAttributeSyntax => ((TailwindFlagAttributeSyntax)attribute).Name.Identifier.ValueText,
            AkburaSyntaxKind.TailwindFullAttributeSyntax => ((TailwindFullAttributeSyntax)attribute).Name.Identifier.ValueText,
            _ => attribute.ToFullString().Trim()
        };
    }

    private static readonly string ProjectDirectory = Path.Combine("C:\\Project");
    private static readonly string DashboardPath = Path.Combine(ProjectDirectory, "Pages", "DashboardPage.akbura");
    private static readonly string DashboardAkcssPath = Path.Combine(ProjectDirectory, "Pages", "DashboardPage.akcss");
    private static readonly string TaskCardPath = Path.Combine(ProjectDirectory, "Components", "TaskCard.akbura");
    private static readonly string StatusBadgePath = Path.Combine(ProjectDirectory, "Components", "StatusBadge.akbura");

    private static readonly string DashboardCode =
        """
        using Avalonia.Controls;
        using Demo.Components;
        using Demo.Logging;
        using Demo.Models;
        using Demo.Services;
        using Demo.Styles.Shared.akcss;
        using Hooks;
        using System.Threading.Tasks;

        namespace Demo.Pages;

        @akcss {
            @using Demo.Styles;
            @using Demo.Styles.Shared.akcss;

            Button.primary {
                Background: White;
                Padding: (10, 20);
                Width: Amx.DynamicResource<double>("--dashboard-width");
                @apply sharedStyle surface;
                @if(true) {
                    Opacity: 1;
                }
                @intercept DashboardStyle;
            }

            .inlinePanel {
                Opacity: 1;
            }

            @utilities {
                .inlineFade { Opacity: 0.75; }
            }
        }

        inject ILogger<DashboardPage> logger;
        inject DashboardVm viewModel;

        param int UserId = 1;
        param bind string Search = "";
        param out TaskItem SelectedTask;

        state bool isOpen = false;
        state DashboardVm vm = new DashboardVm();
        state bool isBusy = bind vm.IsBusy;
        state TaskItem activeTask = in vm.ActiveTask;
        state TaskItem selectedTask = out vm.SelectedTask;
        state string searchName = useName(Search);

        command int Refresh(int userId);

        useEffect(UserId, isBusy, viewModel.IsBusy, Refresh.IsExecuting) {
            logger.LogInformation("Loading {0}", UserId);
        }
        cancel {
            logger.LogInformation("Cancelled");
        }
        finally {
            logger.LogInformation("Done");
        }

        if(isOpen)
        {
            logger.LogInformation("Open {0}", Search);

            <TextBlock Text="Opened" />
        }

        <StackPanel w-30 opacity-1 {isBusy}:hidden>
            <TextBlock Text="Dashboard"/>
            <TextBox bind:Text={Search} Watermark="Search tasks"/>
            <Button Click={(sender, args) => { isOpen = true; }} Content="Open"/>
            <TaskCard Item={activeTask} bind:IsSelected={isOpen} out:SelectedItem={SelectedTask} Toggle={async item => await Refresh.Execute(UserId)}/>
            <StatusBadge Text={searchName}/>
            <Border IsVisible={isOpen}/>
        </StackPanel>
        """;

    private static readonly string TaskCardCode =
        """
        using Avalonia.Controls;
        using Demo.Models;
        using Demo.Styles.Shared.akcss;

        namespace Demo.Components;

        param TaskItem Item;
        param bind bool IsSelected = false;
        param out TaskItem SelectedItem;

        command int Toggle(TaskItem item);

        <Border IsVisible={Item != null} p-4>
            <StackPanel>
                <TextBlock Text={Item.Title}/>
                <Button Click={(sender, args) => { Toggle.Execute(Item); }} Content="Select"/>
                <StatusBadge Text={Item.Status}/>
            </StackPanel>
        </Border>
        """;

    private static readonly string StatusBadgeCode =
        """
        using Avalonia.Controls;

        namespace Demo.Components;

        param string Text;

        <Border IsVisible={true}>
            <TextBlock Text={Text}/>
        </Border>
        """;

    private static readonly string DashboardAkcss =
        """
        @using Demo.Styles;
        @using Demo.Styles.Shared.akcss;

        Button.primary {
            Background: White;
            Padding: (10, 20);
            Width: Amx.DynamicResource<double>("--dashboard-width");
            @apply sharedStyle surface;
            @if(true) {
                Opacity: 1;
            }
            @intercept DashboardStyle;
        }

        @utilities {
            .w-(double value) { Width: value; }
            .hidden { IsVisible: false; }
        }
        """;

    private static readonly string SharedAkcss =
        """
        .sharedStyle {
            Opacity: 1;
        }

        @utilities {
            .surface { Opacity: 1; }
            .opacity-(double value) { Opacity: value; }
            .p-(double value) { Padding: value; }
        }
        """;

    private static readonly string ProjectCSharpCode =
        """
        using Akbura.CompilerAnotations;
        using System;
        using System.ComponentModel;

        namespace Akbura.CompilerAnotations
        {
            [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class)]
            public sealed class UserHookAttribute : Attribute { }
        }

        namespace Akbura
        {
            public static class Amx
            {
                public static T DynamicResource<T>(object? key) => default!;
            }
        }

        namespace Akbura.Akcss
        {
            public abstract class AkcssStyle { }
            public abstract class AkcssClass : AkcssStyle
            {
                public abstract void Update(object control);
            }
        }

        namespace Demo.Logging
        {
            public interface ILogger<T>
            {
                void LogInformation(string message, params object[] args);
            }
        }

        namespace Demo.Models
        {
            public sealed class TaskItem
            {
                public string Title { get; set; } = "";
                public string Status { get; set; } = "";
            }
        }

        namespace Demo.Services
        {
            using Demo.Models;

            public sealed class DashboardVm : INotifyPropertyChanged
            {
                public event PropertyChangedEventHandler? PropertyChanged;
                public bool IsBusy { get; set; }
                public TaskItem ActiveTask { get; set; } = new();
                public IObservable<TaskItem> SelectedTask { get; } = null!;
                public void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBusy)));
            }
        }

        namespace Demo.Components
        {
            public partial class TaskCard : Avalonia.Controls.Control { }
            public partial class StatusBadge : Avalonia.Controls.Control { }
        }

        namespace Demo.Pages
        {
            public partial class DashboardPage
            {
                public void FirstPartial() { }
            }
        }

        namespace Demo.Styles
        {
            public sealed class DashboardStyle : Akbura.Akcss.AkcssClass
            {
                public override void Update(object control) { }
            }
        }

        namespace Hooks
        {
            [UserHook]
            public struct UseNameHook
            {
                public string UseHook<T>(object component, T state) => "state-name";
            }
        }
        """;
}

public sealed class SemanticSnapshotLazyBenchmarkConfig : ManualConfig
{
    public SemanticSnapshotLazyBenchmarkConfig()
    {
        AddJob(
            Job.ShortRun.WithMsBuildArguments(
                "/p:EnableQuickScanBenchmark=true",
                "/p:EnableAkburaStats=true",
                "/p:DefineConstants=ENABLE_QUICK_SCAN_BENCHMARK",
                "/p:EnableSourceLink=false",
                "/p:ContinuousIntegrationBuild=false"));
    }
}
