using Akbura.Language;
using Akbura.Collections;
using Akbura.Language.Binder;
using Akbura.Language.BoundTree;
using Akbura.Language.Declarations;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using AkburaOperation = Akbura.Language.Operations.IOperation;
using AkburaOperationKind = Akbura.Language.Operations.OperationKind;
using AkburaCandidateReason = Akbura.Language.Symbols.CandidateReason;
using AkburaPropertySymbol = Akbura.Language.Symbols.IPropertySymbol;
using AkburaSymbol = Akbura.Language.Symbols.ISymbol;
using AkburaSymbolKind = Akbura.Language.Symbols.SymbolKind;
using AkburaSymbolVisitor = Akbura.Language.Symbols.SymbolVisitor;
using AkburaSyntaxKind = Akbura.Language.Syntax.SyntaxKind;
using BinderType = Akbura.Language.Binder.Binder;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

namespace Akbura.UnitTests;

public sealed class MemberSemanticModelArchitectureTests : SemanticArchitectureTestBase
{
    [Fact]
    public void AkburaCompilation_LazilyCachesSemanticModelsConcurrently()
    {
        var tree = AkburaSyntaxTree.ParseText("state int count = 0;", "Counter.akbura");
        var compilation = CreateCompilation(tree);
        var cacheField = typeof(AkburaCompilation).GetField(
            "_semanticModels",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var models = new AkburaSemanticModel[32];

        Parallel.For(0, models.Length, index =>
        {
            models[index] = compilation.GetSemanticModel(tree);
        });

        Assert.NotNull(cacheField);
        Assert.Same(
            typeof(System.Collections.Concurrent.ConcurrentDictionary<AkburaSyntaxTree, AkburaSemanticModel>),
            cacheField.FieldType);
        Assert.All(models, model => Assert.Same(models[0], model));
    }


    [Fact]
    public void MemberSemanticModel_CachesByComponentScope()
    {
        const string code =
            "state int count = 0;\n" +
            "param string Text = \"Hello\";";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var root = tree.GetRoot();
        var state = Assert.IsType<StateDeclarationSyntax>(root.Members[0]);
        var param = Assert.IsType<ParamDeclarationSyntax>(root.Members[1]);

        var rootModel = model.GetMemberSemanticModel(root);
        var stateModel = model.GetMemberSemanticModel(state);
        var paramModel = model.GetMemberSemanticModel(param);

        Assert.Same(rootModel, stateModel);
        Assert.Same(rootModel, paramModel);
        Assert.Same(root, rootModel.Scope);
        Assert.IsType<ComponentMemberSemanticModel>(rootModel);
    }


    [Fact]
    public void MemberSemanticModel_ClassesAreTopLevelAndCreatedThroughFactory()
    {
        var nestedTypes = typeof(AkburaSemanticModel).GetNestedTypes(
            BindingFlags.Public |
            BindingFlags.NonPublic);

        Assert.DoesNotContain(nestedTypes, type => type.Name.Contains("MemberSemanticModel", StringComparison.Ordinal));
        Assert.True(typeof(AkburaSemanticModel).IsAssignableFrom(typeof(MemberSemanticModel)));
        Assert.True(typeof(MemberSemanticModel).IsAssignableFrom(typeof(ComponentMemberSemanticModel)));
        Assert.True(typeof(MemberSemanticModel).IsAssignableFrom(typeof(InitializerMemberSemanticModel)));
        Assert.True(typeof(MemberSemanticModel).IsAssignableFrom(typeof(ExecutableMemberSemanticModel)));
        Assert.True(typeof(MemberSemanticModel).IsAssignableFrom(typeof(MarkupMemberSemanticModel)));
        Assert.True(typeof(MemberSemanticModel).IsAssignableFrom(typeof(AkcssMemberSemanticModel)));
        Assert.True(typeof(AkburaSemanticModel).IsAssignableFrom(typeof(PublicSemanticModel)));
        Assert.True(typeof(PublicSemanticModel).IsAssignableFrom(typeof(SyntaxTreeSemanticModel)));

        var incrementalBinder = typeof(ExecutableMemberSemanticModel).GetNestedType(
            "IncrementalBinder",
            BindingFlags.NonPublic);
        Assert.NotNull(incrementalBinder);
        Assert.True(typeof(BinderType).IsAssignableFrom(incrementalBinder));

        var semanticModelSource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Compilation",
            "AkburaSemanticModel.cs");
        var syntaxTreeSemanticModelSource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Compilation",
            "SyntaxTreeSemanticModel.cs");

        Assert.DoesNotContain(nameof(MemberSemanticModelFactory), semanticModelSource);
        Assert.DoesNotContain("new ComponentMemberSemanticModel", semanticModelSource);
        Assert.Contains(nameof(MemberSemanticModelFactory), syntaxTreeSemanticModelSource);
        Assert.Contains("internal sealed class SyntaxTreeSemanticModel : PublicSemanticModel", syntaxTreeSemanticModelSource);
    }


    [Fact]
    public void Compilation_GetSemanticModel_ReturnsSyntaxTreeSemanticModel()
    {
        var tree = AkburaSyntaxTree.ParseText("<Button />", "Button.akbura");
        var compilation = CreateCompilation(tree);

        var semanticModel = compilation.GetSemanticModel(tree);

        Assert.IsType<SyntaxTreeSemanticModel>(semanticModel);
        Assert.IsAssignableFrom<PublicSemanticModel>(semanticModel);
        Assert.IsAssignableFrom<AkburaSemanticModel>(semanticModel);
    }


    [Fact]
    public void CompilationFacade_FilesLiveInCompilationFolder()
    {
        var compilationSource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Compilation",
            "AkburaCompilation.cs");
        var semanticModelSource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Compilation",
            "AkburaSemanticModel.cs");
        var syntaxTreeSemanticModelSource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Compilation",
            "SyntaxTreeSemanticModel.cs");
        var publicSemanticModelSource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Compilation",
            "PublicSemanticModel.cs");
        var csharpReferencesSource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Compilation",
            "AkburaSemanticModel.CSharpReferences.cs");
        var markupOperationsSource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Compilation",
            "AkburaSemanticModel.MarkupOperations.cs");

        Assert.Contains("internal sealed class AkburaCompilation", compilationSource);
        Assert.Contains("internal partial class AkburaSemanticModel", semanticModelSource);
        Assert.Contains("internal abstract class PublicSemanticModel", publicSemanticModelSource);
        Assert.Contains("internal sealed class SyntaxTreeSemanticModel", syntaxTreeSemanticModelSource);
        Assert.Contains("partial class AkburaSemanticModel", csharpReferencesSource);
        Assert.Contains("partial class AkburaSemanticModel", markupOperationsSource);
    }


    [Fact]
    public void MemberSemanticModel_ClassesLiveInCompilationFolder()
    {
        var baseModelSource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Compilation",
            "MemberSemanticModel.cs");
        var componentModelSource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Compilation",
            "ComponentMemberSemanticModel.cs");
        var factorySource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Compilation",
            "MemberSemanticModelFactory.cs");
        var syntaxTreeSemanticModelSource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Compilation",
            "SyntaxTreeSemanticModel.cs");
        var binderBackedSource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Compilation",
            "BinderBackedMemberSemanticModel.cs");
        var initializerSource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Compilation",
            "InitializerMemberSemanticModel.cs");
        var markupSource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Compilation",
            "MarkupMemberSemanticModel.cs");
        var akcssSource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Compilation",
            "AkcssMemberSemanticModel.cs");
        var executableSource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Compilation",
            "ExecutableMemberSemanticModel.cs");

        Assert.Contains("internal abstract class MemberSemanticModel : AkburaSemanticModel", baseModelSource);
        Assert.DoesNotContain("ComponentMemberSemanticModel", baseModelSource);
        Assert.Contains("internal sealed class ComponentMemberSemanticModel", componentModelSource);
        Assert.DoesNotContain("internal abstract class MemberSemanticModel", componentModelSource);
        Assert.Contains("internal sealed class MemberSemanticModelFactory", factorySource);
        Assert.DoesNotContain("Dictionary<", factorySource);
        Assert.DoesNotContain("MemberSemanticModelCacheKey", factorySource);
        Assert.DoesNotContain("internal abstract class BinderBackedMemberSemanticModel", factorySource);
        Assert.Contains("ImmutableDictionary<AkburaSyntax, MemberSemanticModel> _memberModels", syntaxTreeSemanticModelSource);
        Assert.Contains("ImmutableInterlocked.GetOrAdd", syntaxTreeSemanticModelSource);
        Assert.Contains("internal abstract class BinderBackedMemberSemanticModel", binderBackedSource);
        Assert.Contains("internal sealed class InitializerMemberSemanticModel", initializerSource);
        Assert.Contains("internal sealed class MarkupMemberSemanticModel", markupSource);
        Assert.Contains("internal sealed class AkcssMemberSemanticModel", akcssSource);
        Assert.Contains("internal sealed class ExecutableMemberSemanticModel", executableSource);
    }


    [Fact]
    public void MemberSemanticModel_BoundNodeMapUsesReaderWriterLock()
    {
        var source = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Compilation",
            "MemberSemanticModel.cs");

        Assert.Contains("ReaderWriterLockSlim", source);
        Assert.Contains("LockRecursionPolicy.NoRecursion", source);
        Assert.Contains("EnterReadLock", source);
        Assert.Contains("EnterWriteLock", source);
        Assert.Contains("ExitReadLock", source);
        Assert.Contains("ExitWriteLock", source);
        Assert.Contains("Dictionary<AkburaSyntax, OneOrMany<BoundNode>> _guardedBoundNodeMap", source);
        Assert.Contains("new();", source);
        Assert.DoesNotContain("SmallDictionary<AkburaSyntax, OneOrMany<BoundNode>>", source);
        Assert.DoesNotContain("AkburaSyntaxGreenComparer", source);
        Assert.DoesNotContain("private readonly object _", source);
    }


    [Fact]
    public void MemberSemanticModel_NodeMapBuilderUsesOrderPreservingAdditionMap()
    {
        var memberModelSource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Compilation",
            "MemberSemanticModel.cs");
        var collectionSource = ReadRepositoryFile(
            "Akbura.Generator",
            "Collections",
            "OrderPreservingMultiDictionary.cs");

        Assert.Contains("private sealed class NodeMapBuilder : BoundTreeWalker", memberModelSource);
        Assert.Contains("OrderPreservingMultiDictionary<AkburaSyntax, BoundNode>.GetInstance()", memberModelSource);
        Assert.Contains("GetAsOneOrMany", memberModelSource);
        Assert.Contains("map.ContainsKey(root.Syntax)", memberModelSource);
        Assert.Contains("map.ContainsKey(key)", memberModelSource);
        Assert.Contains("additionMap.Free()", memberModelSource);
        Assert.Contains("AddBoundTreeToMap(boundNode);", memberModelSource);
        Assert.DoesNotContain("AddBoundNodeToMap", memberModelSource);
        Assert.Contains("internal sealed class OrderPreservingMultiDictionary", collectionSource);
        Assert.Contains("ObjectPool<OrderPreservingMultiDictionary<TKey, TValue>>", collectionSource);
        Assert.Contains("public OneOrMany<TValue> GetAsOneOrMany", collectionSource);
    }


    [Fact]
    public void SemanticInfrastructure_DoesNotDependOnGreenNodes()
    {
        var paths = new List<string>
        {
            GetRepositoryPath("Akbura.Generator", "Language", "SemanticSyntaxIdentity.cs"),
        };
        foreach (var folder in new[]
        {
            GetRepositoryPath("Akbura.Generator", "Language", "Compilation"),
            GetRepositoryPath("Akbura.Generator", "Language", "Binder"),
            GetRepositoryPath("Akbura.Generator", "Language", "Declarations"),
            GetRepositoryPath("Akbura.Generator", "Language", "Symbols"),
        })
        {
            paths.AddRange(Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories));
        }

        foreach (var path in paths)
        {
            var source = File.ReadAllText(path);
            Assert.DoesNotContain("using Akbura.Language.Syntax.Green", source);
            Assert.DoesNotContain("GreenNode", source);
            Assert.DoesNotContain("GreenRoot", source);
            Assert.DoesNotContain(".Green", source);
        }

        var identitySource = File.ReadAllText(paths[0]);
        Assert.Contains("return ReferenceEquals(left, right);", identitySource);
        Assert.Contains("RuntimeHelpers.GetHashCode(syntax)", identitySource);
        Assert.DoesNotContain("Position", identitySource);
        Assert.DoesNotContain("FullWidth", identitySource);
        Assert.DoesNotContain("RawKind", identitySource);
    }


    [Fact]
    public void IncrementalBinder_RewrapsNestedBinders()
    {
        var binderSource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Binder",
            "Binder.cs");
        var executableBinderSource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Binder",
            "ExecutableCodeBinder.cs");
        var executableMemberModelSource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Compilation",
            "ExecutableMemberSemanticModel.cs");

        Assert.Contains("public virtual Binder? GetBinder", binderSource);
        Assert.Contains("public override Binder? GetBinder", executableBinderSource);
        Assert.Contains("public override Akbura.Language.Binder.Binder? GetBinder", executableMemberModelSource);
        Assert.Contains("Debug.Assert(binder is not IncrementalBinder)", executableMemberModelSource);
        Assert.Contains("return new IncrementalBinder(_memberModel, binder)", executableMemberModelSource);
    }


    [Fact]
    public void MemberSemanticModelFactory_RoutesSyntaxToSpecializedModels()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "param int UserId = 1;\n" +
            "state int count = 0;\n" +
            "useEffect(count) { Console.WriteLine(count); }\n" +
            "<TextBlock Text={count.ToString()} />\n" +
            "@akcss { .card { Width: 10; } }";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var root = tree.GetRoot();
        var param = Assert.IsType<ParamDeclarationSyntax>(root.Members[1]);
        var state = Assert.IsType<StateDeclarationSyntax>(root.Members[2]);
        var useEffect = Assert.IsType<UseEffectDeclarationSyntax>(root.Members[3]);
        var markupRoot = Assert.IsType<MarkupRootSyntax>(root.Members[4]);
        var attribute = Assert.Single(markupRoot.Element.StartTag!.Attributes);
        var akcssBlock = Assert.IsType<InlineAkcssBlockSyntax>(root.Members[5]);
        var style = Assert.IsType<AkcssStyleRuleSyntax>(akcssBlock.Members[0]);
        var assignment = Assert.IsType<AkcssAssignmentSyntax>(style.Members[0]);

        Assert.IsType<ComponentMemberSemanticModel>(model.GetMemberSemanticModel(root));
        Assert.IsType<ComponentMemberSemanticModel>(model.GetMemberSemanticModel(state));
        Assert.IsType<ComponentMemberSemanticModel>(model.GetMemberSemanticModel(param));
        Assert.IsType<InitializerMemberSemanticModel>(model.GetMemberSemanticModel(state.Initializer));
        Assert.IsType<InitializerMemberSemanticModel>(model.GetMemberSemanticModel(param.DefaultValue!));
        Assert.IsType<ExecutableMemberSemanticModel>(model.GetMemberSemanticModel(useEffect.Body));
        Assert.IsType<MarkupMemberSemanticModel>(model.GetMemberSemanticModel(markupRoot));
        Assert.IsType<MarkupMemberSemanticModel>(model.GetMemberSemanticModel(markupRoot.Element));
        Assert.IsType<MarkupMemberSemanticModel>(model.GetMemberSemanticModel(attribute));
        Assert.IsType<AkcssMemberSemanticModel>(model.GetMemberSemanticModel(akcssBlock));
        Assert.IsType<AkcssMemberSemanticModel>(model.GetMemberSemanticModel(style));
        Assert.IsType<AkcssMemberSemanticModel>(model.GetMemberSemanticModel(assignment));

        var stateInitializerModel = model.GetMemberSemanticModel(state.Initializer);
        var paramDefaultModel = model.GetMemberSemanticModel(param.DefaultValue!);
        Assert.IsType<BoundStateInitializer>(stateInitializerModel.BindSemanticSyntax(state.Initializer));
        Assert.IsType<BoundParamDefaultValue>(paramDefaultModel.BindSemanticSyntax(param.DefaultValue!));
        Assert.Same(
            stateInitializerModel.BindSemanticSyntax(state.Initializer),
            stateInitializerModel.BindSemanticSyntax(state.Initializer));
    }


    [Fact]
    public void MemberSemanticModel_DifferentComponentsUseDifferentModels()
    {
        var firstTree = AkburaSyntaxTree.ParseText("state int first = 0;", "First.akbura");
        var secondTree = AkburaSyntaxTree.ParseText("state int second = 0;", "Second.akbura");
        var compilation = new AkburaCompilation(
            CreateCSharpCompilation(),
            [firstTree, secondTree],
            ImmutableArray<AkcssSyntaxTree>.Empty,
            rootNamespace: "Demo",
            projectDirectory: Environment.CurrentDirectory);
        var firstModel = compilation.GetSemanticModel(firstTree);
        var secondModel = compilation.GetSemanticModel(secondTree);

        Assert.NotSame(
            firstModel.GetMemberSemanticModel(firstTree.GetRoot()),
            secondModel.GetMemberSemanticModel(secondTree.GetRoot()));
    }


    [Fact]
    public void MemberSemanticModel_DeclarationSymbolInfoRoutesThroughMemberModel()
    {
        const string code =
            "state int count = 0;\n" +
            "command int Refresh(int userId);";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var root = tree.GetRoot();
        var state = Assert.IsType<StateDeclarationSyntax>(root.Members[0]);
        var command = Assert.IsType<CommandDeclarationSyntax>(root.Members[1]);
        var memberModel = model.GetMemberSemanticModel(state);

        var stateFromMemberModel = Assert.IsAssignableFrom<IStateSymbol>(
            memberModel.GetSymbolInfo(state).Symbol);
        var stateFromFacade = Assert.IsAssignableFrom<IStateSymbol>(
            model.GetSymbolInfo(state).Symbol);
        var commandFromMemberModel = Assert.IsAssignableFrom<ICommandSymbol>(
            memberModel.GetSymbolInfo(command).Symbol);
        var commandFromFacade = Assert.IsAssignableFrom<ICommandSymbol>(
            model.GetSymbolInfo(command).Symbol);

        Assert.Same(stateFromMemberModel, stateFromFacade);
        Assert.Same(commandFromMemberModel, commandFromFacade);
        Assert.Equal("Int32", stateFromFacade.Type.Name);
        Assert.Equal("Refresh", commandFromFacade.Name);
        Assert.Equal("Int32", commandFromFacade.ResultType.Name);
        Assert.Single(commandFromFacade.Parameters);
    }


    [Fact]
    public void MemberSemanticModel_ComponentSymbolExposesDeclarationMembers()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "namespace Demo.Pages;\n" +
            "\n" +
            "@akcss { .card { Background: White; } }\n" +
            "inject object service;\n" +
            "param bind string Text = \"Hello\";\n" +
            "state int count = 0;\n" +
            "command int Refresh(int id);\n" +
            "useEffect(Text, count, service, Refresh.IsExecuting) { }\n" +
            "<TextBlock Text={Text} />";
        var tree = AkburaSyntaxTree.ParseText(code, "Pages/Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var root = tree.GetRoot();

        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            model.GetMemberSemanticModel(root).GetSymbolInfo(root).Symbol);
        var state = Assert.Single(component.States);
        var parameter = Assert.Single(component.Parameters);
        var injectedService = Assert.Single(component.InjectedServices);
        var command = Assert.Single(component.Commands);
        var useEffect = Assert.Single(component.UseEffects);

        Assert.Same(component, model.GetSymbolInfo(root).Symbol);
        Assert.Equal("Counter", component.Name);
        Assert.Equal("Demo.Pages", component.NamespaceName);
        Assert.Equal("count", state.Name);
        Assert.Equal(StateBindingKind.None, state.BindingKind);
        Assert.Equal("Text", parameter.Name);
        Assert.Equal(ParamBindingKind.Bind, parameter.BindingKind);
        Assert.Equal("service", injectedService.Name);
        Assert.Equal("Refresh", command.Name);
        Assert.Equal("Int32", command.ResultType.Name);
        Assert.Equal(4, useEffect.Dependencies.Length);
        Assert.Single(component.MarkupRoots);
        Assert.Single(component.AkcssModules);
    }


    [Fact]
    public void MemberSemanticModel_MarkupOnlyEditRebindsDeclarationSymbolsInNewSnapshot()
    {
        const string oldCode =
            "using Avalonia.Controls;\n" +
            "state int count = 0;\n" +
            "param string Text = \"Hello\";\n" +
            "<TextBlock Text=\"Old\" />";
        const string newCode =
            "using Avalonia.Controls;\n" +
            "state int count = 0;\n" +
            "param string Text = \"Hello\";\n" +
            "<TextBlock Text=\"New\" />";
        var changeStart = oldCode.IndexOf("Old", StringComparison.Ordinal);
        var change = new TextChangeRange(new TextSpan(changeStart, 3), newLength: 3);
        var oldTree = AkburaSyntaxTree.ParseText(oldCode, "Counter.akbura");
        var oldCompilation = CreateCompilation(oldTree);
        var oldModel = oldCompilation.GetSemanticModel(oldTree);
        var oldRoot = oldTree.GetRoot();
        var oldState = Assert.IsType<StateDeclarationSyntax>(oldRoot.Members[1]);
        var oldParam = Assert.IsType<ParamDeclarationSyntax>(oldRoot.Members[2]);
        var oldStateSymbol = oldModel.GetSymbolInfo(oldState).Symbol;
        var oldParamSymbol = oldModel.GetSymbolInfo(oldParam).Symbol;

        var newTree = oldTree.WithChangedText(SourceText.From(newCode), [change]);
        var newCompilation = oldCompilation.WithSyntaxTrees([newTree]);
        var newModel = newCompilation.GetSemanticModel(newTree);
        var newRoot = newTree.GetRoot();
        var newState = Assert.IsType<StateDeclarationSyntax>(newRoot.Members[1]);
        var newParam = Assert.IsType<ParamDeclarationSyntax>(newRoot.Members[2]);
        var newStateSymbol = newModel.GetSymbolInfo(newState).Symbol;
        var newParamSymbol = newModel.GetSymbolInfo(newParam).Symbol;

        Assert.Same(oldState.Green, newState.Green);
        Assert.Same(oldParam.Green, newParam.Green);
        Assert.NotSame(oldStateSymbol, newStateSymbol);
        Assert.NotSame(oldParamSymbol, newParamSymbol);
        Assert.Same(newStateSymbol, newModel.GetSymbolInfo(newState).Symbol);
        Assert.Same(newParamSymbol, newModel.GetSymbolInfo(newParam).Symbol);
        Assert.Equal(newCode.Length, newRoot.FullWidth);
    }

}
