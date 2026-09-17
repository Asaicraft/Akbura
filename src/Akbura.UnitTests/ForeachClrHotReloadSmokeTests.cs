using Avalonia.Controls;
using Avalonia.VisualTree;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using static Akbura.UnitTests.AkburaBlackSilenceGeneratorTests;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ForeachClrHotReloadSmokeTests
{
    private const string ChildProcessMarker = "AKBURA_FOREACH_CLR_SMOKE_CHILD";

    [Fact]
    public async Task ActualClrDeltas_RetainIterationNodesAndRetryPendingSourceEvents()
    {
        if (Environment.GetEnvironmentVariable(ChildProcessMarker) != "1")
        {
            await RunEnabledChildProcess();
            return;
        }

        Assert.True(Environment.Version.Major >= 10);
        Assert.True(MetadataUpdater.IsSupported, "Actual CLR metadata updates must be enabled, not skipped.");
        var projectDirectory = Path.Combine(Path.GetTempPath(), nameof(ForeachClrHotReloadSmokeTests),
            Guid.NewGuid().ToString("N"));
        var componentPath = Path.Combine(projectDirectory, "Page.akbura");
        var original = new TestAdditionalText(componentPath, SourceText.From(CreateMarkup("original", secondRoot: false)));
        var valueEdited = new TestAdditionalText(componentPath, SourceText.From(CreateMarkup("edited", secondRoot: false)));
        var added = new TestAdditionalText(componentPath, SourceText.From(CreateMarkup("edited", secondRoot: true)));
        var removed = new TestAdditionalText(componentPath, SourceText.From(CreateMarkup("edited", secondRoot: false)));
        var baseCompilation = CreateCompilation(OwnerSource)
            .RemoveAllSyntaxTrees()
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(SourceText.From(OwnerSource, Encoding.UTF8),
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview).WithPreprocessorSymbols("DEBUG"),
                Path.Combine(projectDirectory, "Page.akbura.cs")))
            .WithAssemblyName("AkburaForeachClrSmoke_" + Guid.NewGuid().ToString("N"))
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug, nullableContextOptions: NullableContextOptions.Enable));
        var driver = CreateDebugDriver(new TestAnalyzerConfigOptionsProvider("Demo", projectDirectory), original);
        driver = driver.RunGeneratorsAndUpdateCompilation(baseCompilation, out var initialCompilation, out _);
        AssertGeneratedCompilation(driver, initialCompilation);
        driver = driver.ReplaceAdditionalText(original, valueEdited);
        driver = driver.RunGeneratorsAndUpdateCompilation(baseCompilation, out var valueCompilation, out _);
        AssertGeneratedCompilation(driver, valueCompilation);
        driver = driver.ReplaceAdditionalText(valueEdited, added);
        driver = driver.RunGeneratorsAndUpdateCompilation(baseCompilation, out var addedCompilation, out _);
        AssertGeneratedCompilation(driver, addedCompilation);
        driver = driver.ReplaceAdditionalText(added, removed);
        driver = driver.RunGeneratorsAndUpdateCompilation(baseCompilation, out var removedCompilation, out _);
        AssertGeneratedCompilation(driver, removedCompilation);

        // Adding a template declaration changes runtime instances, not one field per occurrence.
        var originalFields = OwnerFields(initialCompilation);
        Assert.Equal(originalFields, OwnerFields(valueCompilation));
        Assert.Equal(originalFields, OwnerFields(addedCompilation));
        Assert.Equal(originalFields, OwnerFields(removedCompilation));

        using var peStream = new MemoryStream();
        using var pdbStream = new MemoryStream();
        var emit = initialCompilation.Emit(peStream, pdbStream,
            options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));
        Assert.True(emit.Success, FormatDiagnostics(emit.Diagnostics));
        var peBytes = peStream.ToArray();
        var pdbBytes = pdbStream.ToArray();
        var peImage = ImmutableArray.Create(peBytes);
        using var module = ModuleMetadata.CreateFromImage(peImage);
        using var peReader = new PEReader(peImage);
        using var pdbProvider = MetadataReaderProvider.FromPortablePdbImage(ImmutableArray.Create(pdbBytes));
        var pdbReader = pdbProvider.GetMetadataReader();
        var lambdaMapKind = new Guid("A643004C-0240-496F-A783-30D64F4979DE");
        Assert.Contains(pdbReader.CustomDebugInformation, handle =>
            pdbReader.GetGuid(pdbReader.GetCustomDebugInformation(handle).Kind) == lambdaMapKind);
        var baseline = EmitBaseline.CreateInitialBaseline(initialCompilation, module,
            method => ConditionalClrHotReloadSmokeTests.ReadEditAndContinueDebugInformation(pdbReader, method),
            method => GetLocalSignature(peReader, method), hasPortableDebugInformation: true);

        // Exactly one initial PE is loaded; every following version is a real EnC delta.
        var assembly = Assembly.Load(peBytes, pdbBytes);
        var ownerType = Assert.IsAssignableFrom<Type>(assembly.GetType("Demo.Page"));
        var textType = Assert.IsAssignableFrom<Type>(assembly.GetType("Demo.CountingTextBlock"));
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var owner = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(ownerType));
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var root = Assert.IsType<StackPanel>(owner.Child);
                AssertTexts(root, "prefix", "1 original", "2 original", "suffix");
                var prefix = root.Children[0];
                var first = Assert.IsAssignableFrom<TextBlock>(root.Children[1]);
                var second = Assert.IsAssignableFrom<TextBlock>(root.Children[2]);
                var suffix = root.Children[3];
                Assert.Equal(2, Constructed(textType));
                Assert.Equal(0, Read<int>(ownerType, owner, "Notifications"));

                baseline = ConditionalClrHotReloadSmokeTests.ApplyDelta(assembly, initialCompilation, valueCompilation,
                    baseline, preserveTemplateClosures: true);
                Assert.Equal("1 original", first.Text);
                ConditionalClrHotReloadSmokeTests.InvokeMetadataUpdateHandlers(assembly, ownerType);
                owner.InvalidState();
                AssertTexts(root, "prefix", "1 edited", "2 edited", "suffix");
                Assert.Same(first, root.Children[1]);
                Assert.Same(second, root.Children[2]);
                Assert.Equal(2, Constructed(textType));
                Assert.Equal(0, Read<int>(ownerType, owner, "Notifications"));

                baseline = ConditionalClrHotReloadSmokeTests.ApplyDelta(assembly, valueCompilation, addedCompilation,
                    baseline, preserveTemplateClosures: true);
                ConditionalClrHotReloadSmokeTests.InvokeMetadataUpdateHandlers(assembly, ownerType);
                owner.InvalidState();
                Assert.Equal(6, root.Children.Count);
                Assert.Same(first, root.Children[1]);
                var firstButton = Assert.IsAssignableFrom<Button>(root.Children[2]);
                Assert.Same(second, root.Children[3]);
                var secondButton = Assert.IsAssignableFrom<Button>(root.Children[4]);
                Assert.Equal("1", firstButton.Content);
                Assert.Equal("2", secondButton.Content);
                Assert.Equal(2, Constructed(textType));

                baseline = ConditionalClrHotReloadSmokeTests.ApplyDelta(assembly, addedCompilation, removedCompilation,
                    baseline, preserveTemplateClosures: true);
                ConditionalClrHotReloadSmokeTests.InvokeMetadataUpdateHandlers(assembly, ownerType);
                owner.InvalidState();
                AssertTexts(root, "prefix", "1 edited", "2 edited", "suffix");
                Assert.Same(first, root.Children[1]);
                Assert.Same(second, root.Children[2]);
                Assert.Null(firstButton.GetVisualParent());
                Assert.Null(secondButton.GetVisualParent());

                // The notification predates the new template, but neither packet nor revision
                // may be acknowledged by a failed render. Source mutations are not rolled back.
                var pause = Read<IDisposable>(ownerType, owner, "PauseUpdatesForTest", method: true);
                var items = Read<ObservableCollection<int>>(ownerType, owner, "Items");
                items.Add(3);
                Assert.Equal(1, Read<int>(ownerType, owner, "Notifications"));
                Assert.Equal(4, root.Children.Count);
                ownerType.GetProperty("FailForTest")!.SetValue(owner, true);
                ConditionalClrHotReloadSmokeTests.ApplyDelta(assembly, removedCompilation, addedCompilation,
                    baseline, preserveTemplateClosures: true);
                ConditionalClrHotReloadSmokeTests.InvokeMetadataUpdateHandlers(assembly, ownerType);
                Assert.Throws<InvalidOperationException>(() => pause.Dispose());
                Assert.Equal(3, items.Count);
                Assert.Equal(4, root.Children.Count);
                Assert.Same(first, root.Children[1]);
                Assert.Same(second, root.Children[2]);

                ownerType.GetProperty("FailForTest")!.SetValue(owner, false);
                owner.InvalidState();
                Assert.Equal(8, root.Children.Count);
                Assert.Same(first, root.Children[1]);
                Assert.Same(second, root.Children[3]);
                Assert.Equal("3 edited", Assert.IsAssignableFrom<TextBlock>(root.Children[5]).Text);
                Assert.Equal("3", Assert.IsAssignableFrom<Button>(root.Children[6]).Content);
                Assert.Equal(3, Constructed(textType));
                Assert.Equal(1, Read<int>(ownerType, owner, "Notifications"));
                Assert.Same(owner, window.Content);
                Assert.Same(ownerType, owner.GetType());
                Assert.Same(assembly, owner.GetType().Assembly);
                Assert.Same(root, owner.Child);
                Assert.Same(prefix, root.Children[0]);
                Assert.Same(suffix, root.Children[^1]);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static string[] OwnerFields(Compilation compilation)
    {
        var ownerType = Assert.IsType<INamedTypeSymbol>(
            compilation.GetTypeByMetadataName("Demo.Page"),
            exactMatch: false);

        return
        [
            .. ownerType
            .GetMembers()
            .OfType<IFieldSymbol>()
            .Select(field => field.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
        ];
    }

    private static void AssertTexts(StackPanel panel, params string[] expected)
    {
        var actual = panel.Children
            .Select(child =>
                Assert.IsType<TextBlock>(
                    child,
                    exactMatch: false).Text)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    private static int Constructed(Type type)
    {
        var field = type.GetField("Constructed")!;
        var value = field.GetValue(null);

        return Assert.IsType<int>(value);
    }

    private static T Read<T>(
        Type type,
        object owner,
        string name,
        bool method = false)
    {
        object? value;

        if (method)
        {
            var targetMethod = type.GetMethod(name)!;
            value = targetMethod.Invoke(owner, null);
        }
        else
        {
            var property = type.GetProperty(name)!;
            value = property.GetValue(owner);
        }

        return Assert.IsType<T>(
            value,
            exactMatch: false);
    }

    private static string CreateMarkup(string label, bool secondRoot) =>
        $$"""
        using Avalonia.Controls;
        using Demo;
        namespace Demo;

        <StackPanel>
            <TextBlock Text="prefix"/>

            $foreach (var item in Items)
            {
                <CountingTextBlock Text={Format(item) + " {{label}}"}/>
                {{(secondRoot ? "<CountingButton Content={item.ToString()}/>" : string.Empty)}}
            }

            <TextBlock Text="suffix"/>
        </StackPanel>
        """;

    private static async Task RunEnabledChildProcess()
    {
        var assemblyPath = typeof(ForeachClrHotReloadSmokeTests).Assembly.Location;
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(assemblyPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("vstest");
        start.ArgumentList.Add(assemblyPath);
        start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" + typeof(ForeachClrHotReloadSmokeTests).FullName +
            "." + nameof(ActualClrDeltas_RetainIterationNodesAndRetryPendingSourceEvents));
        start.ArgumentList.Add("/logger:console;verbosity=normal");
        start.Environment[ChildProcessMarker] = "1";
        start.Environment["DOTNET_MODIFIABLE_ASSEMBLIES"] = "debug";
        using var process = Assert.IsType<Process>(Process.Start(start), exactMatch: false);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            Assert.Fail("The enabled foreach CLR Hot Reload child timed out.\r\n" + await stdout + "\r\n" + await stderr);
        }

        Assert.True(process.ExitCode == 0, "The enabled foreach CLR Hot Reload child failed.\r\n" + await stdout + "\r\n" + await stderr);
    }

    private const string OwnerSource =
        """
        namespace Demo;
        public partial class Page : Akbura.AkburaControl
        {
            public Page() : base(Akbura.Engine.AkburaEngine.Empty)
            {
                Items.CollectionChanged += (_, _) => Notifications++;
            }
            public System.Collections.ObjectModel.ObservableCollection<int> Items { get; } = [1, 2];
            public int Notifications { get; private set; }
            public bool FailForTest { get; set; }
            public System.IDisposable PauseUpdatesForTest() => SuppressUpdates();
            public string Format(int item) => FailForTest && item == 2
                ? throw new System.InvalidOperationException("expected pending-frame failure")
                : item.ToString();
        }
        public sealed class CountingTextBlock : Avalonia.Controls.TextBlock
        {
            public static int Constructed;
            public CountingTextBlock() => Constructed++;
        }
        public sealed class CountingButton : Avalonia.Controls.Button;
        """;
}
