using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.HotReload;
using Avalonia.Controls;
using Avalonia.Media;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ComponentDictionaryRuntimeTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task GeneratedDictionary_EvaluatesKeysOnceAndRetainsChildren(bool structural, bool explicitInterface)
    {
        var source = explicitInterface
            ? """
              using Avalonia.Controls;
              using Avalonia.Media;
              using Demo;
              <Border>
                  <Border.Resources>
                      <ExplicitDictionary x.key="nested">
                          <SolidColorBrush x.key={GetKey(0)} Color="Red" />
                          <SolidColorBrush x.Key={GetKey(1)} Color="Blue" />
                      </ExplicitDictionary>
                  </Border.Resources>
              </Border>
              """
            : """
              using Avalonia.Media;
              using Demo;
              <DictionaryHost>
                  <SolidColorBrush x.key={GetKey(0)} Color="Red" />
                  <SolidColorBrush x.Key={GetKey(1)} Color="Blue" />
              </DictionaryHost>
              """;
        var type = Compile(source, structural);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(type));
            var root = Assert.IsAssignableFrom<Border>(Invoke(owner, "FirstForTest"));
            var dictionary = explicitInterface
                ? Assert.IsAssignableFrom<IDictionary<int, SolidColorBrush>>(root.Resources["nested"])
                : Assert.IsAssignableFrom<IDictionary<int, SolidColorBrush>>(
                    root.GetType().GetProperty("Entries")!.GetValue(root));
            var first = dictionary[1];
            var second = dictionary[2];
            var external = dictionary[99];
            Assert.Equal(Colors.Red, first.Color);
            Assert.Equal(Colors.Blue, second.Color);
            Assert.Equal(2, Read<int>(owner, "KeyEvaluations"));

            Invoke(owner, "SetKeys", 2, 1);
            Assert.Same(root, Invoke(owner, "UpdateForTest"));
            Assert.Equal(4, Read<int>(owner, "KeyEvaluations"));
            Assert.Same(first, dictionary[2]);
            Assert.Same(second, dictionary[1]);
            Assert.Same(external, dictionary[99]);

            Invoke(owner, "SetKeys", 3, 4);
            Assert.Same(root, Invoke(owner, "UpdateForTest"));
            Assert.Equal(6, Read<int>(owner, "KeyEvaluations"));
            Assert.False(dictionary.ContainsKey(1));
            Assert.False(dictionary.ContainsKey(2));
            Assert.Same(first, dictionary[3]);
            Assert.Same(second, dictionary[4]);
            Assert.Same(external, dictionary[99]);
            Assert.Equal(3, dictionary.Count);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public async Task GeneratedComponentDictionary_ContentHasPerInstanceBackingAndOtherParametersStayRequired(
        bool structural, bool requiredSupplied, bool propertyElement)
    {
        var source = "using Avalonia.Media; <DictionaryComponent" +
            (requiredSupplied ? " Required=\"configured\"" : string.Empty) +
            ">" + (propertyElement ? "<DictionaryComponent.Content>" : string.Empty) +
            "<SolidColorBrush x.key={1} Color=\"Red\" />" +
            (propertyElement ? "</DictionaryComponent.Content>" : string.Empty) + "</DictionaryComponent>";
        const string childSource =
            """
            using System.Collections.Generic;
            using Avalonia.Controls;
            using Avalonia.Media;
            param IDictionary<int, SolidColorBrush> Content;
            param string Required;
            <Border />
            """;
        const string partialSource =
            """
            using Akbura;
            using Akbura.Engine;
            using Avalonia.Controls;
            namespace Demo;
            public partial class PlannerView : AkburaControl
            {
                public PlannerView() : base(AkburaEngine.Empty) { }
                public Control FirstForTest() => FirstUpdate();
            }
            public partial class DictionaryComponent : AkburaControl
            {
                public DictionaryComponent() : base(AkburaEngine.Empty) { }
                public void InitializeForTest() => base.OnInitialized();
            }
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(source, partialSource);
        var childTree = AkburaSyntaxTree.ParseText(childSource, "DictionaryComponent.akbura");
        var compilation = new AkburaCompilation(fixture.CSharpCompilation,
            [fixture.ComponentTree, childTree], rootNamespace: "Demo");
        var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        if (structural)
        {
            options = options.WithPreprocessorSymbols("DEBUG");
        }

        var csharpCompilation = fixture.CSharpCompilation
            .WithAssemblyName("ComponentDictionaryParameter_" + Guid.NewGuid().ToString("N"));
        foreach (var document in new[] { fixture.ComponentTree, childTree })
        {
            var model = compilation.GetSemanticModel(document);
            var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
                model.GetSymbolInfo(document.GetRoot()).Symbol);
            var semanticDiagnostics = model.GetSemanticDiagnostics(document.GetRoot());
            if (ReferenceEquals(document, fixture.ComponentTree) && !requiredSupplied)
            {
                var required = Assert.Single(semanticDiagnostics);
                Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_MarkupRequiredParameterNotSet, required.Code);
                Assert.Equal("Required", required.Parameters[0]);
            }
            else
            {
                Assert.Empty(semanticDiagnostics);
            }
            var generated = ComponentDocumentWriter.Generate(component, model, document.FilePath,
                new Dictionary<AkburaSyntax, string>(),
                mode: structural ? ComponentGenerationMode.DebugStructural : ComponentGenerationMode.ReleaseDirect);
            csharpCompilation = csharpCompilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(generated,
                options, document.FilePath + ".g.cs"));
        }

        var errors = csharpCompilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.AsEnumerable()));
        using var output = new MemoryStream();
        var emitted = csharpCompilation.Emit(output);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        var assembly = Assembly.Load(output.ToArray());
        var type = assembly.GetType("Demo.PlannerView")!;
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(type));
            if (!requiredSupplied)
            {
                var missingOwner = Assert.IsAssignableFrom<AkburaControl>(Invoke(owner, "FirstForTest"));
                Assert.Null(missingOwner.GetType().GetProperty("Required")!.GetValue(missingOwner));
                var missing = Assert.Throws<AkburaParameterNotSettedException>(() =>
                    Invoke(missingOwner, "InitializeForTest"));
                Assert.Equal("Required", missing.Parameter.Name);
                return;
            }

            var root = Assert.IsAssignableFrom<AkburaControl>(Invoke(owner, "FirstForTest"));
            var dictionary = Read<IDictionary<int, SolidColorBrush>>(root, "Content");
            Assert.Equal(Colors.Red, Assert.Single(dictionary).Value.Color);
            Assert.Equal("configured", Read<string>(root, "Required"));
            Assert.Same(dictionary, Read<IDictionary<int, SolidColorBrush>>(root, "Content"));
            var anotherOwner = Activator.CreateInstance(assembly.GetType("Demo.DictionaryComponent")!)!;
            var anotherDictionary = Read<IDictionary<int, SolidColorBrush>>(anotherOwner, "Content");
            Assert.NotSame(dictionary, anotherDictionary);
            Assert.Empty(anotherDictionary);
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task GeneratedNamedDictionaryParameter_UsesInitializerAndReconcilesPropertyElement(
        bool structural, bool requiredSupplied)
    {
        var source = "using Avalonia.Media; <DictionaryComponent" +
            (requiredSupplied ? " Required=\"configured\"" : string.Empty) +
            "><DictionaryComponent.Entries>" +
            "<SolidColorBrush x.key={GetKeyForTest()} Color=\"Red\" />" +
            "</DictionaryComponent.Entries></DictionaryComponent>";
        const string childSource =
            """
            using System.Collections.Generic;
            using Avalonia.Controls;
            using Avalonia.Media;
            param IDictionary<int, SolidColorBrush> Entries = new Dictionary<int, SolidColorBrush>();
            param string Required;
            <Border />
            """;
        const string partialSource =
            """
            using Akbura;
            using Akbura.Engine;
            using Avalonia.Controls;
            namespace Demo;
            public partial class PlannerView : AkburaControl
            {
                public PlannerView() : base(AkburaEngine.Empty) { }
                public int KeyForTest { get; set; } = 1;
                public int KeyEvaluations { get; private set; }
                private int GetKeyForTest() { KeyEvaluations++; return KeyForTest; }
                public Control FirstForTest() => FirstUpdate();
                public Control UpdateForTest() => Update();
            }
            public partial class DictionaryComponent : AkburaControl
            {
                public DictionaryComponent() : base(AkburaEngine.Empty) { }
                public void InitializeForTest() => base.OnInitialized();
            }
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(source, partialSource);
        var childTree = AkburaSyntaxTree.ParseText(childSource, "DictionaryComponent.akbura");
        var compilation = new AkburaCompilation(fixture.CSharpCompilation,
            [fixture.ComponentTree, childTree], rootNamespace: "Demo");
        var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        if (structural)
        {
            options = options.WithPreprocessorSymbols("DEBUG");
        }

        var csharpCompilation = fixture.CSharpCompilation
            .WithAssemblyName("ComponentNamedDictionaryParameter_" + Guid.NewGuid().ToString("N"));
        foreach (var document in new[] { fixture.ComponentTree, childTree })
        {
            var model = compilation.GetSemanticModel(document);
            var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
                model.GetSymbolInfo(document.GetRoot()).Symbol);
            var semanticDiagnostics = model.GetSemanticDiagnostics(document.GetRoot());
            if (ReferenceEquals(document, fixture.ComponentTree) && !requiredSupplied)
            {
                var required = Assert.Single(semanticDiagnostics);
                Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_MarkupRequiredParameterNotSet, required.Code);
                Assert.Equal("Required", required.Parameters[0]);
            }
            else
            {
                Assert.Empty(semanticDiagnostics);
            }

            var generated = ComponentDocumentWriter.Generate(component, model, document.FilePath,
                new Dictionary<AkburaSyntax, string>(),
                mode: structural ? ComponentGenerationMode.DebugStructural : ComponentGenerationMode.ReleaseDirect);
            csharpCompilation = csharpCompilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(generated,
                options, document.FilePath + ".g.cs"));
        }

        var errors = csharpCompilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.AsEnumerable()));
        using var output = new MemoryStream();
        var emitted = csharpCompilation.Emit(output);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        var assembly = Assembly.Load(output.ToArray());
        var type = assembly.GetType("Demo.PlannerView")!;
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(type));
            var root = Assert.IsAssignableFrom<AkburaControl>(Invoke(owner, "FirstForTest"));
            var dictionary = Read<IDictionary<int, SolidColorBrush>>(root, "Entries");
            var child = Assert.Single(dictionary).Value;
            Assert.Equal(Colors.Red, child.Color);
            Assert.Same(child, dictionary[1]);
            Assert.Equal(1, Read<int>(owner, "KeyEvaluations"));
            if (!requiredSupplied)
            {
                var missing = Assert.Throws<AkburaParameterNotSettedException>(() =>
                    Invoke(root, "InitializeForTest"));
                Assert.Equal("Required", missing.Parameter.Name);
                return;
            }

            Assert.Equal("configured", Read<string>(root, "Required"));
            Invoke(root, "InitializeForTest");
            var external = new SolidColorBrush(Colors.Black);
            dictionary.Add(99, external);
            for (var key = 2; key <= 3; key++)
            {
                owner.GetType().GetProperty("KeyForTest")!.SetValue(owner, key);
                Assert.Same(root, Invoke(owner, "UpdateForTest"));
                Assert.Same(dictionary, Read<IDictionary<int, SolidColorBrush>>(root, "Entries"));
                Assert.False(dictionary.ContainsKey(key - 1));
                Assert.Same(child, dictionary[key]);
                Assert.Same(external, dictionary[99]);
                Assert.Equal(2, dictionary.Count);
                Assert.Equal(key, Read<int>(owner, "KeyEvaluations"));
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NamedDictionaryEntryContent_RequiresReceiverUnlessCallerSuppliesIt(bool receiverSupplied)
    {
        var source = "using Avalonia.Media; <DictionaryComponent Required=\"configured\"" +
            (receiverSupplied ? " Entries={EntriesForTest}" : string.Empty) +
            "><DictionaryComponent.Entries>" +
            "<SolidColorBrush x.Key={1} Color=\"Red\" />" +
            "</DictionaryComponent.Entries></DictionaryComponent>";
        const string childSource =
            """
            using System.Collections.Generic;
            using Avalonia.Controls;
            using Avalonia.Media;
            param IDictionary<int, SolidColorBrush> Entries;
            param string Required;
            <Border />
            """;
        const string partialSource =
            """
            using System.Collections.Generic;
            using Akbura;
            using Avalonia.Media;
            namespace Demo;
            public partial class PlannerView : AkburaControl
            {
                public IDictionary<int, SolidColorBrush> EntriesForTest { get; } =
                    new Dictionary<int, SolidColorBrush>();
            }
            public partial class DictionaryComponent : AkburaControl { }
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(source, partialSource);
        var childTree = AkburaSyntaxTree.ParseText(childSource, "DictionaryComponent.akbura");
        var compilation = new AkburaCompilation(fixture.CSharpCompilation,
            [fixture.ComponentTree, childTree], rootNamespace: "Demo");
        Assert.Empty(compilation.GetSemanticModel(childTree).GetSemanticDiagnostics(childTree.GetRoot()));
        var model = compilation.GetSemanticModel(fixture.ComponentTree);
        var diagnostics = model.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot());
        if (receiverSupplied)
        {
            Assert.Empty(diagnostics);
        }
        else
        {
            var required = Assert.Single(diagnostics);
            Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_MarkupRequiredParameterNotSet, required.Code);
            Assert.Equal("Entries", required.Parameters[0]);
        }
    }

    [Fact]
    public void StructuralRevision_OmittingWholeDeclarationRemovesOnlyOwnedEntries()
    {
        var state = new AkburaRenderState();
        var target = new Dictionary<int, object> { [99] = new() };
        var child = new object();
        Begin(state, "with-dictionary");
        var owner = state.GetRequired<object>(0);
        state.ReconcileDictionary<int, object>(0, "Resources", target, [new(1, child)]);
        state.CompleteRevision();

        Begin(state, "without-dictionary");
        Assert.Same(owner, state.GetRequired<object>(0));
        state.CompleteRevision();
        Assert.Single(target);
        Assert.True(target.ContainsKey(99));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GeneratedNullableKeyContent_PreservesAnnotationsAndNativeNullKeyPolicy(bool structural)
    {
        const string source =
            """
            using Avalonia.Media;
            <NullableDictionaryComponent>
                <SolidColorBrush x.Key={KeyForTest} Color="Red" />
            </NullableDictionaryComponent>
            """;
        const string childSource =
            """
            using System.Collections.Generic;
            using Avalonia.Controls;
            using Avalonia.Media;
            param IDictionary<string?, SolidColorBrush> Content;
            <Border />
            """;
        const string partialSource =
            """
            using Akbura;
            using Akbura.Engine;
            using Avalonia.Controls;
            namespace Demo;
            public partial class PlannerView : AkburaControl
            {
                public PlannerView() : base(AkburaEngine.Empty) { }
                public string? KeyForTest { get; set; } = "old";
                public Control FirstForTest() => FirstUpdate();
                public Control UpdateForTest() => Update();
            }
            public partial class NullableDictionaryComponent : AkburaControl
            {
                public NullableDictionaryComponent() : base(AkburaEngine.Empty) { }
            }
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(source, partialSource);
        var childTree = AkburaSyntaxTree.ParseText(childSource, "NullableDictionaryComponent.akbura");
        var compilation = new AkburaCompilation(fixture.CSharpCompilation,
            [fixture.ComponentTree, childTree], rootNamespace: "Demo");
        var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        if (structural)
        {
            options = options.WithPreprocessorSymbols("DEBUG");
        }

        var csharpCompilation = fixture.CSharpCompilation
            .WithAssemblyName("ComponentNullableDictionary_" + Guid.NewGuid().ToString("N"));
        foreach (var document in new[] { fixture.ComponentTree, childTree })
        {
            var model = compilation.GetSemanticModel(document);
            var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
                model.GetSymbolInfo(document.GetRoot()).Symbol);
            Assert.Empty(model.GetSemanticDiagnostics(document.GetRoot()));
            var generated = ComponentDocumentWriter.Generate(component, model, document.FilePath,
                new Dictionary<AkburaSyntax, string>(),
                mode: structural ? ComponentGenerationMode.DebugStructural : ComponentGenerationMode.ReleaseDirect);
            csharpCompilation = csharpCompilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(generated,
                options, document.FilePath + ".g.cs"));
        }

        var errors = csharpCompilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.AsEnumerable()));
        using var output = new MemoryStream();
        var emitted = csharpCompilation.Emit(output);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        var assembly = Assembly.Load(output.ToArray());
        var type = assembly.GetType("Demo.PlannerView")!;
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(type));
            var root = Assert.IsAssignableFrom<AkburaControl>(Invoke(owner, "FirstForTest"));
            var dictionary = Read<IDictionary<string?, SolidColorBrush>>(root, "Content");
            var child = Assert.Single(dictionary).Value;
            Assert.Equal(Colors.Red, child.Color);
            owner.GetType().GetProperty("KeyForTest")!.SetValue(owner, null);
            var error = Assert.Throws<ArgumentNullException>(() => Invoke(owner, "UpdateForTest"));
            Assert.Equal("key", error.ParamName);
            Assert.Same(child, dictionary["old"]);
            Assert.Single(dictionary);
        }, CancellationToken.None);
    }

    [Fact]
    public void StructuralRevision_ReceiverMigrationCanRollbackThenCommit()
    {
        var state = new AkburaRenderState();
        var child = new object();
        var first = new Dictionary<int, object> { [99] = new() };
        var second = new Dictionary<int, object> { [98] = new() };
        Begin(state, "first");
        state.ReconcileDictionary<int, object>(0, "Resources", first, [new(1, child)]);
        state.CompleteRevision();

        Begin(state, "migration");
        state.ReconcileDictionary<int, object>(0, "Resources", second, [new(2, child)]);
        Assert.False(first.ContainsKey(1));
        Assert.Same(child, second[2]);
        state.AbortRevision();
        Assert.Same(child, first[1]);
        Assert.Single(second);
        Assert.True(second.ContainsKey(98));

        Begin(state, "migration");
        state.ReconcileDictionary<int, object>(0, "Resources", second, [new(2, child)]);
        state.CompleteRevision();
        Assert.Single(first);
        Assert.True(first.ContainsKey(99));
        Assert.Same(child, second[2]);
    }

    [Fact]
    public void StructuralRevision_PreparedCompletionRejectsDictionaryMutation()
    {
        var state = new AkburaRenderState();
        var target = new Dictionary<int, object>();
        Begin(state, "first");
        state.PrepareRevisionCompletion();
        Assert.Throws<InvalidOperationException>(() =>
            state.ReconcileDictionary<int, object>(0, "Resources", target, [new(1, new())]));
        Assert.Empty(target);
        state.CompleteRevision();
    }

    private static void Begin(AkburaRenderState state, string revision) =>
        Assert.True(state.BeginRevision(revision,
            builder => builder.Add(new AkburaRenderNodeDefinition(0, -1, "$root", typeof(object),
                null, "DictionaryOwner")), _ => new object()));

    private static Type Compile(string source, bool structural)
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(source, HostSource);
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            fixture.SemanticModel.GetSymbolInfo(fixture.ComponentTree.GetRoot()).Symbol);
        var semanticErrors = fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot());
        Assert.True(semanticErrors.IsEmpty, string.Join(Environment.NewLine,
            semanticErrors.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message)));
        var generated = ComponentDocumentWriter.Generate(component, fixture.SemanticModel,
            "Views/PlannerView.akbura", new Dictionary<AkburaSyntax, string>(),
            mode: structural ? ComponentGenerationMode.DebugStructural : ComponentGenerationMode.ReleaseDirect);
        var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        if (structural)
        {
            options = options.WithPreprocessorSymbols("DEBUG");
        }

        var tree = CSharpSyntaxTree.ParseText(generated, options, "PlannerView.Dictionary.g.cs");
        var compilation = fixture.CSharpCompilation.AddSyntaxTrees(tree)
            .WithAssemblyName("ComponentDictionary_" + Guid.NewGuid().ToString("N"));
        var errors = compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.AsEnumerable()) +
            Environment.NewLine + generated);
        using var output = new MemoryStream();
        var emitted = compilation.Emit(output);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        return Assert.IsAssignableFrom<Type>(Assembly.Load(output.ToArray()).GetType("Demo.PlannerView"));
    }

    private static T Read<T>(object owner, string name) => (T)owner.GetType().GetProperty(name)!.GetValue(owner)!;

    private static object? Invoke(object owner, string name, params object?[] arguments)
    {
        try
        {
            return owner.GetType().GetMethod(name)!.Invoke(owner, arguments);
        }
        catch (TargetInvocationException error) when (error.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }

    private const string HostSource =
        """
        using System.Collections;
        using System.Collections.Generic;
        using Akbura;
        using Akbura.Engine;
        using Avalonia.Controls;
        using Avalonia.Media;
        using Avalonia.Metadata;
        namespace Demo;
        public partial class PlannerView : AkburaControl
        {
            private int _first = 1;
            private int _second = 2;
            public PlannerView() : base(AkburaEngine.Empty) { }
            public int KeyEvaluations { get; private set; }
            private int GetKey(int index) { KeyEvaluations++; return index == 0 ? _first : _second; }
            public void SetKeys(int first, int second) { _first = first; _second = second; }
            public Control FirstForTest() => FirstUpdate();
            public Control UpdateForTest() => Update();
        }
        public sealed class DictionaryHost : Border
        {
            [Content] public IDictionary<int, SolidColorBrush> Entries { get; } =
                new Dictionary<int, SolidColorBrush> { [99] = new SolidColorBrush(Colors.Black) };
        }
        public sealed class ExplicitDictionary : IDictionary<int, SolidColorBrush>
        {
            private readonly Dictionary<int, SolidColorBrush> _values =
                new() { [99] = new SolidColorBrush(Colors.Black) };
            SolidColorBrush IDictionary<int, SolidColorBrush>.this[int key]
            { get => _values[key]; set => _values[key] = value; }
            ICollection<int> IDictionary<int, SolidColorBrush>.Keys => _values.Keys;
            ICollection<SolidColorBrush> IDictionary<int, SolidColorBrush>.Values => _values.Values;
            int ICollection<KeyValuePair<int, SolidColorBrush>>.Count => _values.Count;
            bool ICollection<KeyValuePair<int, SolidColorBrush>>.IsReadOnly => false;
            void IDictionary<int, SolidColorBrush>.Add(int key, SolidColorBrush value) => _values.Add(key, value);
            bool IDictionary<int, SolidColorBrush>.ContainsKey(int key) => _values.ContainsKey(key);
            bool IDictionary<int, SolidColorBrush>.Remove(int key) => _values.Remove(key);
            bool IDictionary<int, SolidColorBrush>.TryGetValue(int key, out SolidColorBrush value) =>
                _values.TryGetValue(key, out value!);
            void ICollection<KeyValuePair<int, SolidColorBrush>>.Add(KeyValuePair<int, SolidColorBrush> value) =>
                ((ICollection<KeyValuePair<int, SolidColorBrush>>)_values).Add(value);
            void ICollection<KeyValuePair<int, SolidColorBrush>>.Clear() => _values.Clear();
            bool ICollection<KeyValuePair<int, SolidColorBrush>>.Contains(KeyValuePair<int, SolidColorBrush> value) =>
                ((ICollection<KeyValuePair<int, SolidColorBrush>>)_values).Contains(value);
            void ICollection<KeyValuePair<int, SolidColorBrush>>.CopyTo(KeyValuePair<int, SolidColorBrush>[] array, int index) =>
                ((ICollection<KeyValuePair<int, SolidColorBrush>>)_values).CopyTo(array, index);
            bool ICollection<KeyValuePair<int, SolidColorBrush>>.Remove(KeyValuePair<int, SolidColorBrush> value) =>
                ((ICollection<KeyValuePair<int, SolidColorBrush>>)_values).Remove(value);
            IEnumerator<KeyValuePair<int, SolidColorBrush>> IEnumerable<KeyValuePair<int, SolidColorBrush>>.GetEnumerator() =>
                _values.GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => _values.GetEnumerator();
        }
        """;
}
