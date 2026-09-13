using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;
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
    public async Task RemovingLastTemplateConditional_PreservesTheLiveNativeAncestorBinding()
    {
        if (Environment.GetEnvironmentVariable(ChildProcessMarker) != "1")
        {
            await RunEnabledChildProcess(nameof(RemovingLastTemplateConditional_PreservesTheLiveNativeAncestorBinding));
            return;
        }

        Assert.True(Environment.Version.Major >= 10);
        Assert.True(MetadataUpdater.IsSupported, "This smoke must apply actual CLR metadata updates.");
        var projectDirectory = Path.Combine(Path.GetTempPath(), nameof(ConditionalClrHotReloadSmokeTests),
            Guid.NewGuid().ToString("N"));
        var componentPath = Path.Combine(projectDirectory, "Page.akbura");
        var original = new TestAdditionalText(componentPath, SourceText.From(CreateNativeNameTemplateMarkup(conditional: true)));
        var removed = new TestAdditionalText(componentPath, SourceText.From(CreateNativeNameTemplateMarkup(conditional: false)));
        var baseCompilation = CreateCompilation(NativeNameTemplateOwnerSource)
            .RemoveAllSyntaxTrees()
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(SourceText.From(NativeNameTemplateOwnerSource, Encoding.UTF8),
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview).WithPreprocessorSymbols("DEBUG"),
                Path.Combine(projectDirectory, "Page.akbura.cs")))
            .WithAssemblyName("AkburaNativeNameLastConditionalClrSmoke_" + Guid.NewGuid().ToString("N"))
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug, nullableContextOptions: NullableContextOptions.Enable));
        var driver = CreateDebugDriver(new TestAnalyzerConfigOptionsProvider("Demo", projectDirectory), original);
        driver = driver.RunGeneratorsAndUpdateCompilation(baseCompilation, out var initialCompilation, out _);
        AssertGeneratedCompilation(driver, initialCompilation);
        driver = driver.ReplaceAdditionalText(original, removed);
        driver = driver.RunGeneratorsAndUpdateCompilation(baseCompilation, out var removedCompilation, out _);
        AssertGeneratedCompilation(driver, removedCompilation);

        using var peStream = new MemoryStream();
        using var pdbStream = new MemoryStream();
        var emit = initialCompilation.Emit(peStream, pdbStream, options:
            new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));
        Assert.True(emit.Success, FormatDiagnostics(emit.Diagnostics));
        var peBytes = peStream.ToArray();
        var pdbBytes = pdbStream.ToArray();
        var peImage = ImmutableArray.Create(peBytes);
        using var module = ModuleMetadata.CreateFromImage(peImage);
        using var peReader = new PEReader(peImage);
        using var pdbProvider = MetadataReaderProvider.FromPortablePdbImage(ImmutableArray.Create(pdbBytes));
        var pdbReader = pdbProvider.GetMetadataReader();
        var baseline = EmitBaseline.CreateInitialBaseline(initialCompilation, module,
            method => ReadEditAndContinueDebugInformation(pdbReader, method),
            method => GetLocalSignature(peReader, method), hasPortableDebugInformation: true);

        // Keep the same loaded owner, presenter, template and live native binding
        // while removing the last directive. The bound TextBlock is an unchanged
        // unconditional sibling, not moved across a conditional boundary.
        var assembly = Assembly.Load(peBytes, pdbBytes);
        var ownerType = Assert.IsAssignableFrom<Type>(assembly.GetType("Demo.Page"));
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(ownerType));
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var componentRoot = Assert.IsType<StackPanel>(owner.Child);
                var outer = Assert.IsType<TextBox>(componentRoot.Children[0]);
                var host = Assert.IsType<ContentPresenter>(componentRoot.Children[1]);
                var template = Assert.IsAssignableFrom<Avalonia.Controls.Templates.IDataTemplate>(host.ContentTemplate);
                var item = Assert.IsAssignableFrom<object>(host.Content);
                Assert.IsType(assembly.GetType("Demo.Person")!, item);
                var localRoot = Assert.IsType<StackPanel>(host.Child);
                Assert.Equal(2, localRoot.Children.Count);
                var target = Assert.IsType<TextBlock>(localRoot.Children[0]);
                var oldBranch = Assert.IsType<Border>(localRoot.Children[1]);
                Assert.Same(host, localRoot.GetVisualParent());
                Assert.Equal("initial", target.Text);

                outer.Text = "before source update";
                Assert.Equal("before source update", target.Text);
                owner.InvalidState();
                Assert.Same(template, host.ContentTemplate);
                Assert.Same(localRoot, host.Child);
                Assert.Same(target, localRoot.Children[0]);
                Assert.Same(oldBranch, localRoot.Children[1]);
                Assert.Equal("before source update", target.Text);

                ApplyDelta(assembly, initialCompilation, removedCompilation, baseline,
                    preserveTemplateClosures: true);
                Assert.Same(localRoot, host.Child);
                Assert.Equal("before source update", target.Text);
                InvokeMetadataUpdateHandlers(assembly, ownerType);
                owner.InvalidState();

                Assert.Same(owner, window.Content);
                Assert.Same(assembly, owner.GetType().Assembly);
                Assert.Same(componentRoot, owner.Child);
                Assert.Same(outer, componentRoot.Children[0]);
                Assert.Same(host, componentRoot.Children[1]);
                Assert.Same(item, host.Content);
                Assert.Same(template, host.ContentTemplate);
                Assert.Same(localRoot, host.Child);
                Assert.Same(target, Assert.Single(localRoot.Children));
                Assert.Same(host, localRoot.GetVisualParent());
                Assert.Null(oldBranch.GetVisualParent());
                Assert.Equal("before source update", target.Text);

                // A real native ElementName observer must still track the actual
                // existing outer control without recreating the template root.
                outer.Text = "after source update";
                Assert.Equal("after source update", target.Text);
                ownerType.GetProperty("Enabled")!.SetValue(owner, false);
                owner.InvalidState();
                Assert.Same(localRoot, host.Child);
                Assert.Same(target, Assert.Single(localRoot.Children));
                Assert.Equal("after source update", target.Text);
                outer.Text = "current ordinary frame";
                Assert.Equal("current ordinary frame", target.Text);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static string CreateNativeNameTemplateMarkup(bool conditional) =>
        "using Avalonia.Controls;\r\n" +
        "using Avalonia.Controls.Presenters;\r\n" +
        "using Avalonia.Data;\r\n" +
        "using Demo;\r\n" +
        "namespace Demo;\r\n" +
        "<StackPanel>\r\n" +
        "    <TextBox x.Name=\"outer\" Text=\"initial\" />\r\n" +
        "    <ContentPresenter Content={Item}>\r\n" +
        "        <ContentPresenter.ContentTemplate x.DataType=\"Person\" x.ItemName=\"person\">\r\n" +
        "            <StackPanel>\r\n" +
        "                <TextBlock Text=${ReflectionBinding Path=Text, ElementName={BranchName}} />\r\n" +
        (conditional ? "                $if (Enabled) { <Border /> }\r\n" : string.Empty) +
        "            </StackPanel>\r\n" +
        "        </ContentPresenter.ContentTemplate>\r\n" +
        "    </ContentPresenter>\r\n" +
        "</StackPanel>\r\n";

    private const string NativeNameTemplateOwnerSource =
        """
        namespace Demo;
        public partial class Page : Akbura.AkburaControl
        {
            public Page() : base(Akbura.Engine.AkburaEngine.Empty) { }
            public bool Enabled { get; set; } = true;
            public string BranchName => "outer";
            public Person Item { get; } = new Person();
        }
        public sealed class Person;
        """;
}
