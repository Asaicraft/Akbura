using System.Collections.Immutable;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.UnitTests;

public sealed class WorkspaceControlEventHookTests
{
    [Fact]
    public async Task Completion_OffersRuntimeEventHookAndKeyEventArgsMembers()
    {
        const string hookSourceWithCaret = "namespace Gallery;\r\nusing Akbura.Hooks;\r\nusing Avalonia.Controls;\r\n\r\nuseKe|\r\n\r\n<Border/>";
        using var workspace = CreateWorkspace();
        var hookContext = Open(workspace, hookSourceWithCaret, out var hookDocument, out var hookPosition);
        var hooksType = hookContext.Project.CSharpCompilation.GetTypeByMetadataName(
            "Akbura.Hooks.ControlEventHooks");
        Assert.NotNull(hooksType);
        Assert.Contains(
            hooksType!.GetMembers("useKeyDown"),
            static member => member is IMethodSymbol method && method.GetAttributes().Any(
                static attribute => attribute.AttributeClass?.Name == "UseHookAttribute"));
        var hookCompletion = workspace.LanguageServices.Completion.GetCompletions(
            hookDocument,
            hookContext,
            hookPosition);
        var hook = Assert.Single(
            hookCompletion.Items,
            static item => item.DisplayText == "useKeyDown");

        Assert.Equal(AkburaCompletionKind.Hook, hook.Kind);
        Assert.Null(hook.NamespaceImport);
        Assert.Contains("KeyEventArgs", hook.Suffix, StringComparison.Ordinal);
        Assert.Contains("key down routed event", hook.Description, StringComparison.OrdinalIgnoreCase);

        const string argsSourceWithCaret =
            "namespace Gallery;\r\n" +
            "using Akbura.Hooks;\r\n" +
            "using Avalonia.Controls;\r\n\r\n" +
            "useKeyDown(args =>\r\n" +
            "{\r\n" +
            "    args.Ha|\r\n" +
            "});\r\n\r\n" +
            "<Border/>";
        var argsContext = Open(workspace, argsSourceWithCaret, out var argsDocument, out var argsPosition);
        Assert.True(argsDocument.TryGetCSharpCompletionContext(argsPosition, out var argsCSharpContext));
        Assert.True(
            AkburaCSharpProjectionFactory.TryCreate(
                argsDocument,
                argsContext,
                argsCSharpContext,
                out var projection));
        Assert.Contains("__AkburaUseHookImports", projection.Root.ToFullString(), StringComparison.Ordinal);
        var projectionTree = CSharpSyntaxTree.Create(projection.Root);
        var projectionCompilation = argsContext.Project.CSharpCompilation.AddSyntaxTrees(projectionTree);
        var projectionModel = projectionCompilation.GetSemanticModel(projectionTree);
        var incompleteAccess = projectionTree.GetRoot()
            .DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax>()
            .Single(static access => access.Name.Identifier.ValueText == "Ha");
        var projectedArgumentType = projectionModel.GetTypeInfo(incompleteAccess.Expression).Type?.ToDisplayString();
        Assert.True(
            projectedArgumentType == "Avalonia.Input.KeyEventArgs",
            projection.Root.ToFullString() + Environment.NewLine +
            string.Join(Environment.NewLine, projectionCompilation.GetDiagnostics()));
        var argsCompletion = await workspace.LanguageServices.ProjectedCSharp.GetCompletionsAsync(
            argsDocument,
            argsContext,
            argsPosition,
            new AkburaProjectedCompletionTrigger(
                IsExplicit: true,
                IsIncomplete: false,
                Character: '\0'));

        Assert.NotNull(argsCompletion);
        Assert.Contains(
            argsCompletion!.Value.Items,
            static item => item.DisplayText == "Handled");
    }

    [Fact]
    public void QuickInfoDefinitionAndSignatureHelp_UseRuntimeHookMetadataAndXmlDocumentation()
    {
        const string sourceWithCaret =
            "namespace Gallery;\r\n" +
            "using Akbura.Hooks;\r\n" +
            "using Avalonia.Controls;\r\n\r\n" +
            "useKeyDown(args => { }|);\r\n\r\n" +
            "<Border/>";
        using var workspace = CreateWorkspace();
        var context = Open(workspace, sourceWithCaret, out var document, out var position);
        var namePosition = sourceWithCaret.IndexOf("useKeyDown", StringComparison.Ordinal);

        var signatureHelp = workspace.LanguageServices.SignatureHelp.GetSignatureHelp(
            document,
            context,
            position);
        var quickInfo = workspace.LanguageServices.QuickInfo.GetQuickInfo(
            context,
            namePosition);
        var definition = workspace.LanguageServices.Definition.GetDefinition(
            context,
            namePosition);

        Assert.NotNull(signatureHelp);
        Assert.Equal(3, signatureHelp!.Signatures.Length);
        Assert.All(
            signatureHelp.Signatures,
            static signature => Assert.Contains("useKeyDown", signature.Label, StringComparison.Ordinal));
        Assert.Contains(
            signatureHelp.Signatures,
            static signature => signature.Label.Contains("Action<KeyEventArgs>", StringComparison.Ordinal));
        Assert.NotNull(quickInfo);
        Assert.Contains("useKeyDown", quickInfo!.Signature, StringComparison.Ordinal);
        Assert.Contains(
            quickInfo.Details,
            static detail => detail.Contains("key down routed event", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(definition);
        Assert.NotNull(definition!.TargetText);
        Assert.Contains("useKeyDown", definition.TargetText.ToString(), StringComparison.Ordinal);
    }

    internal static AkburaWorkspace CreateWorkspace(string? csharpSource = null)
    {
        var paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        var references = paths
            .Append(GetRuntimeAssemblyPath("Akbura.dll"))
            .Append(GetAvaloniaReferencePath("Avalonia.Base.dll"))
            .Append(GetAvaloniaReferencePath("Avalonia.Controls.dll"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(CreateMetadataReference);
        var compilation = CSharpCompilation.Create(
            "WorkspaceControlEventHooks",
            [CSharpSyntaxTree.ParseText(csharpSource ??
                "namespace Gallery; public abstract partial class EventHooks : global::Akbura.AkburaControl { }")],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var project = new ProjectContext(
            ProjectId.CreateNewId(),
            projectFilePath: string.Empty,
            projectDirectory: Environment.CurrentDirectory,
            rootNamespace: "Gallery",
            compilation,
            ImmutableArray<ProjectReference>.Empty);
        return new AkburaWorkspace(project);
    }

    private static string GetRuntimeAssemblyPath(string fileName)
    {
        var outputDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = outputDirectory.Parent!.Name;
        var targetFramework = outputDirectory.Name;
        var sourceDirectory = outputDirectory.Parent.Parent!.Parent!.Parent!.Parent!;
        var path = Path.Combine(
            sourceDirectory.FullName,
            "Akbura",
            "bin",
            configuration,
            targetFramework,
            fileName);
        Assert.True(File.Exists(path), $"Runtime assembly was not built: {path}");
        return path;
    }

    private static string GetAvaloniaReferencePath(string fileName)
    {
        var outputDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var targetFramework = outputDirectory.Name;
        var repositoryDirectory = outputDirectory.Parent!.Parent!.Parent!.Parent!.Parent!.Parent!;
        var props = System.Xml.Linq.XDocument.Load(
            Path.Combine(repositoryDirectory.FullName, "Directory.Build.props"));
        var version = Assert.Single(
            props.Descendants("AvaloniaVersion")).Value;
        var packageDirectory = Environment.GetEnvironmentVariable("NUGET_PACKAGES") ??
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget",
                "packages");
        var path = Path.Combine(
            packageDirectory,
            "avalonia",
            version,
            "ref",
            targetFramework,
            fileName);
        Assert.True(File.Exists(path), $"Avalonia reference assembly was not restored: {path}");
        return path;
    }

    private static MetadataReference CreateMetadataReference(string path)
    {
        var documentationPath = Path.ChangeExtension(path, ".xml");
        return File.Exists(documentationPath)
            ? MetadataReference.CreateFromFile(
                path,
                documentation: XmlDocumentationProvider.CreateFromFile(documentationPath))
            : MetadataReference.CreateFromFile(path);
    }

    private static AkburaDocumentContext Open(AkburaWorkspace workspace, string sourceWithCaret, out AkburaSyntacticDocument document, out int position)
    {
        position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);
        var path = Path.GetFullPath("EventHooks.akbura");
        var text = SourceText.From(source);
        document = AkburaSyntacticDocument.Parse(text, path);
        return workspace.OpenOrChangeDocumentContext(new Uri(path), text);
    }
}
