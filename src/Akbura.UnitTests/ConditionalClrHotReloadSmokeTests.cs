using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using static Akbura.UnitTests.AkburaBlackSilenceGeneratorTests;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed partial class ConditionalClrHotReloadSmokeTests
{
    private const string ChildProcessMarker = "AKBURA_CONDITIONAL_CLR_SMOKE_CHILD";

    [Fact]
    public async Task AppliesConditionalDeltasToTheSameLoadedOwner()
    {
        if (Environment.GetEnvironmentVariable(ChildProcessMarker) != "1")
        {
            await RunEnabledChildProcess(nameof(AppliesConditionalDeltasToTheSameLoadedOwner));
            return;
        }

        Assert.True(Environment.Version.Major >= 10, "The smoke test requires the .NET 10 test host.");
        Assert.True(MetadataUpdater.IsSupported,
            "MetadataUpdater must be enabled in the child test host; this test must not be skipped.");

        var projectDirectory = Path.Combine(Path.GetTempPath(), nameof(ConditionalClrHotReloadSmokeTests),
            Guid.NewGuid().ToString("N"));
        var componentPath = Path.Combine(projectDirectory, "Page.akbura");
        var original = new TestAdditionalText(componentPath, SourceText.From(CreateMarkup(
            "<Border Name=\"retained-branch\" />",
            "<Border Name=\"other-branch\" />")));
        var inserted = new TestAdditionalText(componentPath, SourceText.From(CreateMarkup(
            "<TextBlock Text=\"inserted\" /><Border Name=\"retained-branch\" />",
            "<Button Content=\"changed type\" />")));
        var removed = new TestAdditionalText(componentPath, SourceText.From(CreateMarkup(
            "<Border Name=\"retained-branch\" />",
            string.Empty)));
        var baseCompilation = CreateCompilation(OwnerSource)
            .RemoveAllSyntaxTrees()
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(SourceText.From(OwnerSource, Encoding.UTF8),
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview).WithPreprocessorSymbols("DEBUG"),
                Path.Combine(projectDirectory, "Page.akbura.cs")))
            .WithAssemblyName("AkburaConditionalClrSmoke_" + Guid.NewGuid().ToString("N"))
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug, nullableContextOptions: NullableContextOptions.Enable));
        var driver = CreateDebugDriver(new TestAnalyzerConfigOptionsProvider("Demo", projectDirectory), original);
        driver = driver.RunGeneratorsAndUpdateCompilation(baseCompilation, out var initialCompilation, out _);
        AssertGeneratedCompilation(driver, initialCompilation);

        driver = driver.ReplaceAdditionalText(original, inserted);
        driver = driver.RunGeneratorsAndUpdateCompilation(baseCompilation, out var insertedCompilation, out _);
        AssertGeneratedCompilation(driver, insertedCompilation);
        driver = driver.ReplaceAdditionalText(inserted, removed);
        driver = driver.RunGeneratorsAndUpdateCompilation(baseCompilation, out var removedCompilation, out _);
        AssertGeneratedCompilation(driver, removedCompilation);

        using var peStream = new MemoryStream();
        using var pdbStream = new MemoryStream();
        var emit = initialCompilation.Emit(peStream, pdbStream, options:
            new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));
        Assert.True(emit.Success, FormatDiagnostics(emit.Diagnostics));
        var peBytes = peStream.ToArray();
        var peImage = ImmutableArray.Create(peBytes);
        using var module = ModuleMetadata.CreateFromImage(peImage);
        using var peReader = new PEReader(peImage);
        var baseline = EmitBaseline.CreateInitialBaseline(initialCompilation, module,
            static _ => default, method => GetLocalSignature(peReader, method),
            hasPortableDebugInformation: true);

        // Load the initial PE exactly once. No updated assembly is loaded as a substitute.
        var assembly = Assembly.Load(peBytes, pdbStream.ToArray());
        var ownerType = Assert.IsAssignableFrom<Type>(assembly.GetType("Demo.Page"));
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(ownerType));
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var root = Assert.IsType<StackPanel>(owner.Child);
                Assert.Equal(3, root.Children.Count);
                var prefix = Assert.IsType<TextBlock>(root.Children[0]);
                var initialBranch = Assert.IsType<Border>(root.Children[1]);
                var suffix = Assert.IsType<TextBlock>(root.Children[2]);
                Assert.Equal("7", prefix.Text);
                var hookStates = owner.GetDiagnosticStates();
                Assert.Equal(14, ReadDoubled(ownerType, owner));
                Invoke(ownerType, owner, "IncreaseForTest");
                owner.InvalidState();
                Assert.Equal("8", prefix.Text);
                Assert.Equal(16, ReadDoubled(ownerType, owner));
                Assert.True(hookStates == owner.GetDiagnosticStates());

                baseline = ApplyDelta(assembly, initialCompilation, insertedCompilation, baseline);
                Assert.Equal(3, root.Children.Count);
                InvokeMetadataUpdateHandlers(assembly, ownerType);
                owner.InvalidState();
                AssertSameOwner(window, owner, ownerType, assembly, root, prefix, suffix);
                Assert.Equal(4, root.Children.Count);
                Assert.Equal("inserted", Assert.IsType<TextBlock>(root.Children[1]).Text);
                Assert.Same(initialBranch, root.Children[2]);
                Assert.Equal("8", prefix.Text);
                Assert.Equal(8, ReadCount(ownerType, owner));
                // Metadata refresh intentionally rebinds composable hooks under the
                // existing runtime contract. It must retain the ordinary parent state.
                var firstUpdatedStates = owner.GetDiagnosticStates();
                Assert.Same(hookStates[0], firstUpdatedStates[0]);
                Assert.NotSame(hookStates[1], firstUpdatedStates[1]);
                Assert.False(hookStates[1].IsAttached);
                Assert.True(firstUpdatedStates[1].IsAttached);
                Assert.Equal(16, ReadDoubled(ownerType, owner));

                ownerType.GetProperty("Choice")!.SetValue(owner, 1);
                owner.InvalidState();
                Assert.Equal(3, root.Children.Count);
                var changedType = Assert.IsType<Button>(root.Children[1]);
                Assert.Equal("changed type", changedType.Content);
                Assert.DoesNotContain(initialBranch, root.Children);
                AssertSameOwner(window, owner, ownerType, assembly, root, prefix, suffix);
                Assert.True(firstUpdatedStates == owner.GetDiagnosticStates());
                Assert.Equal(16, ReadDoubled(ownerType, owner));

                ApplyDelta(assembly, insertedCompilation, removedCompilation, baseline);
                Assert.Same(changedType, root.Children[1]);
                InvokeMetadataUpdateHandlers(assembly, ownerType);
                owner.InvalidState();
                Assert.Equal(2, root.Children.Count);
                Assert.DoesNotContain(changedType, root.Children);
                AssertSameOwner(window, owner, ownerType, assembly, root, prefix, suffix);
                Assert.Equal(8, ReadCount(ownerType, owner));
                var secondUpdatedStates = owner.GetDiagnosticStates();
                Assert.Same(hookStates[0], secondUpdatedStates[0]);
                Assert.NotSame(firstUpdatedStates[1], secondUpdatedStates[1]);
                Assert.False(firstUpdatedStates[1].IsAttached);
                Assert.True(secondUpdatedStates[1].IsAttached);
                Assert.Equal(16, ReadDoubled(ownerType, owner));

                ownerType.GetProperty("Choice")!.SetValue(owner, 0);
                owner.InvalidState();
                Assert.Equal(3, root.Children.Count);
                Assert.IsType<Border>(root.Children[1]);
                Assert.True(secondUpdatedStates == owner.GetDiagnosticStates());
                Invoke(ownerType, owner, "IncreaseForTest");
                owner.InvalidState();
                Assert.Equal("9", prefix.Text);
                Assert.Equal(9, ReadCount(ownerType, owner));
                Assert.Equal(18, ReadDoubled(ownerType, owner));
                AssertSameOwner(window, owner, ownerType, assembly, root, prefix, suffix);
                Assert.True(secondUpdatedStates == owner.GetDiagnosticStates());
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task AppliesConditionalTemplateRootDeltasToTheSameNativeHost()
    {
        if (Environment.GetEnvironmentVariable(ChildProcessMarker) != "1")
        {
            await RunEnabledChildProcess(nameof(AppliesConditionalTemplateRootDeltasToTheSameNativeHost));
            return;
        }

        Assert.True(Environment.Version.Major >= 10);
        Assert.True(MetadataUpdater.IsSupported, "The CLR Hot Reload child must support actual metadata updates.");
        var projectDirectory = Path.Combine(Path.GetTempPath(), nameof(ConditionalClrHotReloadSmokeTests),
            Guid.NewGuid().ToString("N"));
        var componentPath = Path.Combine(projectDirectory, "Page.akbura");
        var original = new TestAdditionalText(componentPath, SourceText.From(CreateTemplateMarkup(
            "<CountingTextBlock Text={person.Name} />")));
        var valueEdited = new TestAdditionalText(componentPath, SourceText.From(CreateTemplateMarkup(
            "<CountingTextBlock Text={person.Name + \" source edit\"} />")));
        var changed = new TestAdditionalText(componentPath, SourceText.From(CreateTemplateMarkup(
            "<CountingButton Content={person.Name} />")));
        var emptied = new TestAdditionalText(componentPath, SourceText.From(CreateTemplateMarkup(string.Empty)));
        var baseCompilation = CreateCompilation(TemplateOwnerSource)
            .RemoveAllSyntaxTrees()
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(SourceText.From(TemplateOwnerSource, Encoding.UTF8),
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview).WithPreprocessorSymbols("DEBUG"),
                Path.Combine(projectDirectory, "Page.akbura.cs")))
            .WithAssemblyName("AkburaConditionalTemplateClrSmoke_" + Guid.NewGuid().ToString("N"))
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug, nullableContextOptions: NullableContextOptions.Enable));
        var driver = CreateDebugDriver(new TestAnalyzerConfigOptionsProvider("Demo", projectDirectory), original);
        driver = driver.RunGeneratorsAndUpdateCompilation(baseCompilation, out var initialCompilation, out _);
        AssertGeneratedCompilation(driver, initialCompilation);
        driver = driver.ReplaceAdditionalText(original, valueEdited);
        driver = driver.RunGeneratorsAndUpdateCompilation(baseCompilation, out var valueCompilation, out _);
        AssertGeneratedCompilation(driver, valueCompilation);
        driver = driver.ReplaceAdditionalText(valueEdited, changed);
        driver = driver.RunGeneratorsAndUpdateCompilation(baseCompilation, out var changedCompilation, out _);
        AssertGeneratedCompilation(driver, changedCompilation);
        driver = driver.ReplaceAdditionalText(changed, emptied);
        driver = driver.RunGeneratorsAndUpdateCompilation(baseCompilation, out var emptyCompilation, out _);
        AssertGeneratedCompilation(driver, emptyCompilation);
        AssertTemplateFactoryType(initialCompilation, "Demo.CountingTextBlock");
        AssertTemplateFactoryType(valueCompilation, "Demo.CountingTextBlock");
        AssertTemplateFactoryType(changedCompilation, "Demo.CountingButton");
        AssertTemplateFactoryType(emptyCompilation, expectedType: null);

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
        var metadataReader = peReader.GetMetadataReader();
        var templateMethod = Assert.Single(metadataReader.MethodDefinitions,
            handle => metadataReader.GetString(metadataReader.GetMethodDefinition(handle).Name)
                .StartsWith("__BuildConditionalTemplate", StringComparison.Ordinal));
        var lambdaMap = Assert.Single(pdbReader.GetCustomDebugInformation(templateMethod),
            handle => pdbReader.GetGuid(pdbReader.GetCustomDebugInformation(handle).Kind) ==
                new Guid("A643004C-0240-496F-A783-30D64F4979DE"));
        Assert.NotEmpty(pdbReader.GetBlobBytes(pdbReader.GetCustomDebugInformation(lambdaMap).Value));
        // Capturing template factories use real Portable PDB EnC maps, unlike
        // the noncapturing structural smoke's intentionally minimal baseline.
        var baseline = EmitBaseline.CreateInitialBaseline(initialCompilation, module,
            method => ReadEditAndContinueDebugInformation(pdbReader, method),
            method => GetLocalSignature(peReader, method), hasPortableDebugInformation: true);
        var assembly = Assembly.Load(peBytes, pdbBytes);
        var ownerType = Assert.IsAssignableFrom<Type>(assembly.GetType("Demo.Page"));
        var textType = Assert.IsAssignableFrom<Type>(assembly.GetType("Demo.CountingTextBlock"));
        var buttonType = Assert.IsAssignableFrom<Type>(assembly.GetType("Demo.CountingButton"));
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(ownerType));
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var host = Assert.IsType<ContentPresenter>(owner.Child);
                var item = Assert.IsAssignableFrom<object>(host.Content);
                Assert.IsType(assembly.GetType("Demo.Person")!, item);
                Assert.Null(host.Child);
                Assert.Equal(0, ReadConstructed(textType));
                Assert.Equal(0, ReadConstructed(buttonType));

                ownerType.GetProperty("Enabled")!.SetValue(owner, true);
                owner.InvalidState();
                var text = Assert.IsAssignableFrom<TextBlock>(host.Child);
                var coordinator = GetTemplateCoordinator(host);
                Assert.Same(textType, text.GetType());
                Assert.Equal("current item", text.Text);
                Assert.Same(host, text.GetVisualParent());
                Assert.Equal(1, ReadConstructed(textType));
                item.GetType().GetProperty("Name")!.SetValue(item, "updated item");
                owner.InvalidState();
                Assert.Same(text, host.Child);
                Assert.Equal("updated item", text.Text);
                Assert.Equal(1, ReadConstructed(textType));

                baseline = ApplyDelta(assembly, initialCompilation, valueCompilation, baseline,
                    preserveTemplateClosures: true);
                Assert.Same(text, host.Child);
                Assert.Equal("updated item", text.Text);
                InvokeMetadataUpdateHandlers(assembly, ownerType);
                owner.InvalidState();
                Assert.Same(owner, window.Content);
                Assert.Same(host, owner.Child);
                Assert.Same(item, host.Content);
                Assert.Same(coordinator, GetTemplateCoordinator(host));
                Assert.Same(text, host.Child);
                Assert.Equal("updated item source edit", text.Text);
                Assert.Same(host, text.GetVisualParent());
                Assert.Equal(1, ReadConstructed(textType));
                Assert.Equal(0, ReadConstructed(buttonType));

                baseline = ApplyDelta(assembly, valueCompilation, changedCompilation, baseline,
                    preserveTemplateClosures: true);
                Assert.Same(text, host.Child);
                InvokeMetadataUpdateHandlers(assembly, ownerType);
                owner.InvalidState();
                Assert.Same(owner, window.Content);
                Assert.Same(assembly, owner.GetType().Assembly);
                Assert.Same(host, owner.Child);
                Assert.Same(item, host.Content);
                Assert.Same(coordinator, GetTemplateCoordinator(host));
                var button = Assert.IsAssignableFrom<Button>(host.Child);
                Assert.Same(buttonType, button.GetType());
                Assert.Equal("updated item", button.Content);
                Assert.Same(host, button.GetVisualParent());
                Assert.Null(text.GetVisualParent());
                Assert.Equal(1, ReadConstructed(textType));
                Assert.Equal(1, ReadConstructed(buttonType));

                ApplyDelta(assembly, changedCompilation, emptyCompilation, baseline,
                    preserveTemplateClosures: true);
                Assert.Same(button, host.Child);
                InvokeMetadataUpdateHandlers(assembly, ownerType);
                owner.InvalidState();
                Assert.Same(owner, window.Content);
                Assert.Same(host, owner.Child);
                Assert.Same(item, host.Content);
                Assert.Same(coordinator, GetTemplateCoordinator(host));
                Assert.Null(host.Child);
                Assert.Null(button.GetVisualParent());
                ownerType.GetProperty("Enabled")!.SetValue(owner, false);
                owner.InvalidState();
                Assert.Null(host.Child);
                ownerType.GetProperty("Enabled")!.SetValue(owner, true);
                owner.InvalidState();
                host.UpdateChild();
                Assert.Null(host.Child);
                Assert.Same(host, owner.Child);
                Assert.Equal(1, ReadConstructed(textType));
                Assert.Equal(1, ReadConstructed(buttonType));
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static int ReadConstructed(Type type) =>
        Assert.IsType<int>(type.GetField("Constructed")!.GetValue(null));

    private static object GetTemplateCoordinator(ContentPresenter host)
    {
        var template = Assert.IsAssignableFrom<object>(host.ContentTemplate);
        return Assert.IsAssignableFrom<object>(template.GetType()
            .GetField("_instance", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(template));
    }

    private static void AssertTemplateFactoryType(Compilation compilation, string? expectedType)
    {
        var owner = Assert.IsAssignableFrom<INamedTypeSymbol>(compilation.GetTypeByMetadataName("Demo.Page"));
        var method = Assert.Single(owner.GetMembers().OfType<IMethodSymbol>(),
            static member => member.Name.StartsWith("__BuildConditionalTemplate", StringComparison.Ordinal));
        var syntax = Assert.Single(method.DeclaringSyntaxReferences).GetSyntax();
        var factory = Assert.Single(syntax.DescendantNodes().OfType<LambdaExpressionSyntax>(),
            static lambda => lambda is SimpleLambdaExpressionSyntax simple &&
                simple.Parameter.Identifier.ValueText == "__localId");
        var semanticModel = compilation.GetSemanticModel(syntax.SyntaxTree);
        var constructedTypes = factory.Body.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
            .Select(creation => semanticModel.GetTypeInfo(creation).Type?.ToDisplayString())
            .Where(static name => name is "Demo.CountingTextBlock" or "Demo.CountingButton")
            .ToArray();
        Assert.Equal(expectedType == null ? [] : new[] { expectedType }, constructedTypes);
    }

    private static async Task RunEnabledChildProcess(string testMethod)
    {
        var assemblyPath = typeof(ConditionalClrHotReloadSmokeTests).Assembly.Location;
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(assemblyPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("vstest");
        start.ArgumentList.Add(assemblyPath);
        start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" +
            "Akbura.UnitTests.ConditionalClrHotReloadSmokeTests." + testMethod);
        start.ArgumentList.Add("/logger:console;verbosity=normal");
        start.Environment[ChildProcessMarker] = "1";
        start.Environment["DOTNET_MODIFIABLE_ASSEMBLIES"] = "debug";
        using var process = Assert.IsAssignableFrom<Process>(Process.Start(start));
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
            Assert.Fail("The enabled CLR Hot Reload child process timed out.\r\n" +
                await stdout + "\r\n" + await stderr);
        }

        Assert.True(process.ExitCode == 0,
            "The enabled CLR Hot Reload child process failed.\r\n" + await stdout + "\r\n" + await stderr);
    }

    internal static EmitBaseline ApplyDelta(Assembly assembly, Compilation previous, Compilation updated,
        EmitBaseline baseline, bool preserveTemplateClosures = false)
    {
        var edits = GetChangedGeneratedMethodEdits(previous, updated, "Demo.Page");
        Assert.NotEmpty(edits);
        if (preserveTemplateClosures)
        {
            edits = edits.Select(edit => new SemanticEdit(edit.Kind, edit.OldSymbol, edit.NewSymbol,
                syntaxMap: CreateStableFactorySyntaxMap(edit))).ToArray();
            Assert.All(edits, static edit => Assert.True(edit.PreserveLocalVariables));
        }
        using var metadataDelta = new MemoryStream();
        using var ilDelta = new MemoryStream();
        using var pdbDelta = new MemoryStream();
        var difference = updated.EmitDifference(baseline, edits, static _ => false,
            metadataDelta, ilDelta, pdbDelta, CancellationToken.None);
        Assert.True(difference.Success, FormatDiagnostics(difference.Diagnostics));
        Assert.NotEmpty(difference.UpdatedMethods);
        Assert.NotEqual(0, metadataDelta.Length);
        Assert.NotEqual(0, ilDelta.Length);
        Assert.NotEqual(0, pdbDelta.Length);
        MetadataUpdater.ApplyUpdate(assembly, metadataDelta.ToArray(), ilDelta.ToArray(), pdbDelta.ToArray());
        return Assert.IsAssignableFrom<EmitBaseline>(difference.Baseline);
    }

    internal static EditAndContinueMethodDebugInformation ReadEditAndContinueDebugInformation(
        MetadataReader reader, MethodDefinitionHandle method)
    {
        // Roslyn's PortableCustomDebugInfoKinds; the blobs are read from the
        // emitted initial PDB, not synthesized as if closures were absent.
        var slotKind = new Guid("755F52A8-91C5-45BE-B4B8-209571E552BD");
        var lambdaKind = new Guid("A643004C-0240-496F-A783-30D64F4979DE");
        var stateKind = new Guid("8B78CD68-2EDE-420B-980B-E15884B8AAA3");
        var slots = ImmutableArray<byte>.Empty;
        var lambdas = ImmutableArray<byte>.Empty;
        var states = ImmutableArray<byte>.Empty;
        foreach (var handle in reader.GetCustomDebugInformation(method))
        {
            var info = reader.GetCustomDebugInformation(handle);
            var kind = reader.GetGuid(info.Kind);
            if (kind == slotKind)
            {
                slots = ImmutableArray.Create(reader.GetBlobBytes(info.Value));
            }
            else if (kind == lambdaKind)
            {
                lambdas = ImmutableArray.Create(reader.GetBlobBytes(info.Value));
            }
            else if (kind == stateKind)
            {
                states = ImmutableArray.Create(reader.GetBlobBytes(info.Value));
            }
        }

        return EditAndContinueMethodDebugInformation.Create(slots, lambdas, states);
    }

    private static Func<SyntaxNode, SyntaxNode?> CreateStableFactorySyntaxMap(SemanticEdit edit)
    {
        var oldSyntax = Assert.Single(edit.OldSymbol!.DeclaringSyntaxReferences).GetSyntax();
        var newSyntax = Assert.Single(edit.NewSymbol!.DeclaringSyntaxReferences).GetSyntax();
        var map = new Dictionary<SyntaxNode, SyntaxNode> { [newSyntax] = oldSyntax };
        if (oldSyntax is MethodDeclarationSyntax { Body: { } oldBody } &&
            newSyntax is MethodDeclarationSyntax { Body: { } newBody })
        {
            map[newBody] = oldBody;
        }

        MapStableDeclarations<VariableDeclaratorSyntax>(oldSyntax, newSyntax, map,
            static node => node.Identifier.ValueText);
        MapStableDeclarations<ParameterSyntax>(oldSyntax, newSyntax, map,
            static node => node.Identifier.ValueText);
        MapStableDeclarations<LocalFunctionStatementSyntax>(oldSyntax, newSyntax, map,
            static node => node.Identifier.ValueText);

        // This smoke keeps each factory's lambda/local-function skeleton stable.
        // Parameter names identify the describe, factory and typed-build roles;
        // changed branch bodies are not matched by source offset or text equality.
        MapStableDeclarations<LambdaExpressionSyntax>(oldSyntax, newSyntax, map, static node => node switch
        {
            SimpleLambdaExpressionSyntax simple => simple.Parameter.Identifier.ValueText,
            ParenthesizedLambdaExpressionSyntax parenthesized => string.Join(",",
                parenthesized.ParameterList.Parameters.Select(static parameter => parameter.Identifier.ValueText)),
            _ => throw new InvalidOperationException(),
        });
        foreach (var pair in map.ToArray())
        {
            if (pair.Key is LambdaExpressionSyntax newLambda && pair.Value is LambdaExpressionSyntax oldLambda)
            {
                map[newLambda.Body] = oldLambda.Body;
            }
            else if (pair.Key is LocalFunctionStatementSyntax { Body: { } newFunctionBody } &&
                pair.Value is LocalFunctionStatementSyntax { Body: { } oldFunctionBody })
            {
                map[newFunctionBody] = oldFunctionBody;
            }
        }

        return node => map.GetValueOrDefault(node);
    }

    private static void MapStableDeclarations<TSyntax>(SyntaxNode oldSyntax, SyntaxNode newSyntax,
        Dictionary<SyntaxNode, SyntaxNode> map, Func<TSyntax, string> key)
        where TSyntax : SyntaxNode
    {
        var oldNodes = oldSyntax.DescendantNodes().OfType<TSyntax>().GroupBy(key, StringComparer.Ordinal)
            .Where(static group => group.Count() == 1).ToDictionary(static group => group.Key,
                static group => group.Single(), StringComparer.Ordinal);
        foreach (var group in newSyntax.DescendantNodes().OfType<TSyntax>().GroupBy(key, StringComparer.Ordinal))
        {
            if (group.Count() == 1 && oldNodes.TryGetValue(group.Key, out var oldNode))
            {
                map[group.Single()] = oldNode;
            }
        }
    }

    internal static void InvokeMetadataUpdateHandlers(Assembly assembly, Type ownerType)
    {
        // ApplyUpdate does not dispatch SDK metadata handlers. Run their documented
        // two phases explicitly, on the same UI thread as the live component.
        var handlers = assembly.GetCustomAttributes<MetadataUpdateHandlerAttribute>().ToArray();
        Assert.NotEmpty(handlers);
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var handler in handlers)
        {
            Assert.IsAssignableFrom<MethodInfo>(handler.HandlerType.GetMethod("ClearCache", flags))
                .Invoke(null, [new Type[] { ownerType }]);
        }
        foreach (var handler in handlers)
        {
            Assert.IsAssignableFrom<MethodInfo>(handler.HandlerType.GetMethod("UpdateApplication", flags))
                .Invoke(null, [new Type[] { ownerType }]);
        }
    }

    private static void AssertSameOwner(Window window, AkburaControl owner, Type ownerType, Assembly assembly,
        StackPanel root, TextBlock prefix, TextBlock suffix)
    {
        Assert.Same(owner, window.Content);
        Assert.Same(ownerType, owner.GetType());
        Assert.Same(assembly, owner.GetType().Assembly);
        Assert.Same(root, owner.Child);
        Assert.Same(prefix, root.Children[0]);
        Assert.Same(suffix, root.Children[^1]);
    }

    private static void Invoke(Type type, object owner, string name) =>
        Assert.IsAssignableFrom<MethodInfo>(type.GetMethod(name)).Invoke(owner, null);

    private static int ReadCount(Type type, object owner) =>
        Assert.IsType<int>(type.GetProperty("CountForTest")!.GetValue(owner));

    private static int ReadDoubled(Type type, object owner) =>
        Assert.IsType<int>(type.GetProperty("DoubledForTest")!.GetValue(owner));

    private static string CreateMarkup(string firstBranch, string otherBranch) =>
        "using Avalonia.Controls;\r\n" +
        "using Akbura.Hooks;\r\n" +
        "namespace Demo;\r\n" +
        "state int count = 7;\r\n" +
        "state int doubled = useSelect(count, value => value * 2);\r\n" +
        "<StackPanel>\r\n" +
        "    <TextBlock Text={count.ToString()} />\r\n" +
        "    $if (Choice == 0) { " + firstBranch + " }\r\n" +
        "    $else { " + otherBranch + " }\r\n" +
        "    <TextBlock Text=\"suffix\" />\r\n" +
        "</StackPanel>\r\n";

    private const string OwnerSource =
        """
        namespace Demo;
        public partial class Page : Akbura.AkburaControl
        {
            public Page() : base(Akbura.Engine.AkburaEngine.Empty) { }
            public int Choice { get; set; }
            public int CountForTest => count;
            public int DoubledForTest => doubled;
            public void IncreaseForTest() => count++;
        }
        """;

    private static string CreateTemplateMarkup(string branch) =>
        "using Avalonia.Controls;\r\n" +
        "using Avalonia.Controls.Presenters;\r\n" +
        "using Demo;\r\n" +
        "namespace Demo;\r\n" +
        "<ContentPresenter Content={Item}>\r\n" +
        "    <ContentPresenter.ContentTemplate x.DataType=\"Person\" x.ItemName=\"person\">\r\n" +
        "        $if (person != null && Enabled) { " + branch + " }\r\n" +
        "    </ContentPresenter.ContentTemplate>\r\n" +
        "</ContentPresenter>\r\n";

    private const string TemplateOwnerSource =
        """
        namespace Demo;
        public partial class Page : Akbura.AkburaControl
        {
            public Page() : base(Akbura.Engine.AkburaEngine.Empty) { }
            public bool Enabled { get; set; }
            public Person Item { get; } = new Person();
        }
        public sealed class Person
        {
            public string Name { get; set; } = "current item";
        }
        public sealed class CountingTextBlock : Avalonia.Controls.TextBlock
        {
            public static int Constructed;
            public CountingTextBlock() => Constructed++;
        }
        public sealed class CountingButton : Avalonia.Controls.Button
        {
            public static int Constructed;
            public CountingButton() => Constructed++;
        }
        """;
}
