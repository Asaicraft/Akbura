using Avalonia.Controls;
using Avalonia.Media;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using static Akbura.UnitTests.AkburaBlackSilenceGeneratorTests;

namespace Akbura.UnitTests;

public sealed partial class ConditionalClrHotReloadSmokeTests
{
    [Fact]
    public async Task ReactiveUtilityConditions_ApplyRealDeltasWithoutStaleSubscriptions()
    {
        if (Environment.GetEnvironmentVariable(ChildProcessMarker) != "1")
        {
            await RunEnabledChildProcess(nameof(ReactiveUtilityConditions_ApplyRealDeltasWithoutStaleSubscriptions));
            return;
        }

        Assert.True(MetadataUpdater.IsSupported, "This test must apply real CLR metadata updates.");
        var directory = Path.Combine(Path.GetTempPath(), nameof(ConditionalClrHotReloadSmokeTests), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "Page.akbura");
        // Native Name preserves the element's name without generating a public
        // typed x.Name getter. Its return-type change would require an unrelated
        // public-API delete/insert delta in this method-update-only smoke harness.
        var bodies = new[]
        {
            "<TextBlock Name=\"target\" Width=\"10\" ${x.bind Vm.Enabled}:hidden/>",
            "<TextBlock Name=\"target\" Width=\"20\" ${x.bind Vm.Enabled}:hidden/>",
            "<TextBlock Name=\"target\" Width=\"20\" ${x.bind Vm.Busy}:hidden/>",
            "<TextBlock Name=\"target\" Width=\"20\" ${x.bind Vm.Busy}:font-bold/>",
            "<TextBlock Name=\"target\" Width=\"20\"/>",
            "<TextBlock Name=\"target\" Width=\"20\" ${x.bind Vm.Busy}:hidden/>",
            "<Border Name=\"target\" Width=\"20\" ${x.bind Vm.Busy}:hidden/>",
        };
        var texts = bodies.Select(body => new TestAdditionalText(path, SourceText.From(
            "using Avalonia.Controls; using Akbura.Styles.akcss; using Demo; state UtilityModel Vm = new(); " +
            "MarkRender(); <StackPanel>" + body + "</StackPanel>"))).ToArray();
        var compilation = CreateCompilation(ReactiveUtilityOwnerSource).RemoveAllSyntaxTrees()
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(SourceText.From(ReactiveUtilityOwnerSource, Encoding.UTF8),
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview).WithPreprocessorSymbols("DEBUG"),
                Path.Combine(directory, "Page.akbura.cs")))
            .WithAssemblyName("AkburaReactiveUtilityClrSmoke_" + Guid.NewGuid().ToString("N"))
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug, nullableContextOptions: NullableContextOptions.Enable));
        var revisions = new List<Compilation>();
        var driver = CreateDebugDriver(new TestAnalyzerConfigOptionsProvider("Demo", directory), texts[0]);
        for (var index = 0; index < texts.Length; index++)
        {
            if (index != 0) driver = driver.ReplaceAdditionalText(texts[index - 1], texts[index]);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var revision, out _);
            AssertGeneratedCompilation(driver, revision);
            revisions.Add(revision);
        }

        using var pe = new MemoryStream();
        using var pdb = new MemoryStream();
        var emitted = revisions[0].Emit(pe, pdb, options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));
        Assert.True(emitted.Success, FormatDiagnostics(emitted.Diagnostics));
        var peBytes = pe.ToArray();
        var pdbBytes = pdb.ToArray();
        var image = ImmutableArray.Create(peBytes);
        using var module = ModuleMetadata.CreateFromImage(image);
        using var peReader = new PEReader(image);
        using var pdbProvider = MetadataReaderProvider.FromPortablePdbImage(ImmutableArray.Create(pdbBytes));
        var pdbReader = pdbProvider.GetMetadataReader();
        var baseline = EmitBaseline.CreateInitialBaseline(revisions[0], module,
            method => ReadEditAndContinueDebugInformation(pdbReader, method),
            method => GetLocalSignature(peReader, method), hasPortableDebugInformation: true);
        // The initial assembly is loaded exactly once; every later revision is a delta.
        var assembly = Assembly.Load(peBytes, pdbBytes);
        var ownerType = assembly.GetType("Demo.Page")!;
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var owner = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(ownerType));
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                dynamic model = ownerType.GetProperty("ModelForTest")!.GetValue(owner)!;
                var root = Assert.IsType<StackPanel>(owner.Child);
                var original = Assert.IsType<TextBlock>(Assert.Single(root.Children));
                original.Resources["--font-weight-bold"] = FontWeight.Bold;
                int Renders() => (int)ownerType.GetField("RenderCalls")!.GetValue(owner)!;
                void Change(Action change, Action verify)
                {
                    var renders = Renders();
                    change();
                    verify();
                    Assert.Equal(renders, Renders());
                }
                void Reload(int index)
                {
                    baseline = ApplyReactiveUtilityDelta(assembly, revisions[index - 1], revisions[index], baseline);
                    InvokeMetadataUpdateHandlers(assembly, ownerType);
                    owner.InvalidState();
                    Assert.Same(owner, window.Content);
                    Assert.Same(assembly, owner.GetType().Assembly);
                    Assert.Same(root, owner.Child);
                }
                Assert.True(original.IsVisible);
                Change(() => model.Enabled = true, () => Assert.False(original.IsVisible));
                Reload(1); // unrelated property edit
                Assert.Same(original, Assert.Single(root.Children));
                Assert.Equal(20d, original.Width);
                Assert.False(original.IsVisible);
                Change(() => model.Enabled = false, () => Assert.True(original.IsVisible));
                Change(() => model.Enabled = true, () => Assert.False(original.IsVisible));

                Reload(2); // Enabled -> Busy: the old notification must not affect the UI
                Assert.Same(original, Assert.Single(root.Children));
                Assert.True(original.IsVisible);
                Change(() => model.Enabled = false, () => Assert.True(original.IsVisible));
                Change(() => model.Busy = true, () => Assert.False(original.IsVisible));

                var baselineWeight = original.FontWeight;
                Reload(3); // hidden -> font-bold
                Assert.True(original.IsVisible);
                Assert.Equal(FontWeight.Bold, original.FontWeight);
                Change(() => model.Busy = false, () => Assert.Equal(baselineWeight, original.FontWeight));
                Change(() => model.Busy = true, () => Assert.Equal(FontWeight.Bold, original.FontWeight));

                Reload(4); // remove the last reactive candidate
                Assert.Equal(baselineWeight, original.FontWeight);
                Assert.Equal((int)model.Adds, (int)model.Removes);
                Change(() => model.Busy = false, () => Assert.True(original.IsVisible));
                Reload(5); // reinstall before replacing the tracked element
                Change(() => model.Busy = true, () => Assert.False(original.IsVisible));
                Reload(6);
                var replacement = Assert.IsType<Border>(Assert.Single(root.Children));
                Assert.False(replacement.IsVisible);
                var retiredVisibility = original.IsVisible;
                Change(() => model.Busy = false, () => Assert.True(replacement.IsVisible));
                Assert.Equal(retiredVisibility, original.IsVisible);
                Change(() => model.Busy = true, () => Assert.False(replacement.IsVisible));
                window.Close();
                Assert.Equal((int)model.Adds, (int)model.Removes);
                return true;
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static EmitBaseline ApplyReactiveUtilityDelta(Assembly assembly, Compilation previous,
        Compilation updated, EmitBaseline baseline)
    {
        var edits = GetChangedGeneratedMethodEdits(previous, updated, "Demo.Page");
        Assert.NotEmpty(edits);
        // As in ClrHotReloadSession's AKCSS harness, apply method edits separately:
        // Roslyn otherwise emits duplicate deleted-lambda rows for cascade factories
        // present in both initialization and Update. Refresh only after all deltas.
        foreach (var edit in edits)
        {
            using var metadata = new MemoryStream();
            using var il = new MemoryStream();
            using var pdb = new MemoryStream();
            var mapped = new SemanticEdit(edit.Kind, edit.OldSymbol, edit.NewSymbol,
                syntaxMap: CreateStableFactorySyntaxMap(edit));
            var difference = updated.EmitDifference(baseline, [mapped], static _ => false,
                metadata, il, pdb, CancellationToken.None);
            Assert.True(difference.Success, FormatDiagnostics(difference.Diagnostics));
            Assert.NotEqual(0, metadata.Length);
            Assert.NotEqual(0, il.Length);
            MetadataUpdater.ApplyUpdate(assembly, metadata.ToArray(), il.ToArray(), pdb.ToArray());
            baseline = Assert.IsAssignableFrom<EmitBaseline>(difference.Baseline);
        }
        return baseline;
    }

    private const string ReactiveUtilityOwnerSource = """
        using System;
        using System.ComponentModel;
        using Akbura;
        namespace Demo;
        public partial class Page : AkburaControl
        {
            public Page() : base(Akbura.Engine.AkburaEngine.Empty) { }
            public int RenderCalls;
            public void MarkRender() => RenderCalls++;
            public UtilityModel ModelForTest => Vm;
        }
        public sealed class UtilityModel : INotifyPropertyChanged
        {
            private PropertyChangedEventHandler? _changed;
            public int Adds, Removes;
            public event PropertyChangedEventHandler? PropertyChanged
            {
                add { Adds++; _changed += value; }
                remove { Removes++; _changed -= value; }
            }
            private bool _enabled, _busy;
            public bool Enabled { get => _enabled; set { _enabled = value; _changed?.Invoke(this, new(nameof(Enabled))); } }
            public bool Busy { get => _busy; set { _busy = value; _changed?.Invoke(this, new(nameof(Busy))); } }
        }
        """;
}
