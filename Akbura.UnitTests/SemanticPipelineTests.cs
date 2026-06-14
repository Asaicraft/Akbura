using Akbura.Language;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using AkburaCandidateReason = Akbura.Language.Symbols.CandidateReason;
using AkburaPropertySymbol = Akbura.Language.Symbols.IPropertySymbol;
using AkburaSymbolKind = Akbura.Language.Symbols.SymbolKind;

namespace Akbura.UnitTests;

public class SemanticPipelineTests
{
    [Fact]
    public void SyntaxTree_ParseText_PreservesRootFullWidth()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "\n" +
            "<Button />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var root = syntaxTree.GetRoot();

        Assert.Equal(code.Length, root.FullWidth);
        Assert.Equal(code, root.ToFullString());
    }

    [Fact]
    public void SemanticModel_ResolvesMarkupComponentSymbol_FromRealAvaloniaAssembly()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "\n" +
            "<Button />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);

        var symbolInfo = semanticModel.GetSymbolInfo(element);

        var symbol = Assert.IsType<MarkupComponentSymbol>(symbolInfo.Symbol);
        Assert.IsAssignableFrom<IMarkupComponentSymbol>(symbol);
        Assert.Equal(AkburaCandidateReason.None, symbolInfo.CandidateReason);
        Assert.True(symbolInfo.CandidateSymbols.IsEmpty);
        Assert.Equal("Button", symbol.Name);

        var avaloniaButton = semanticModel.Compilation.CSharpCompilation.GetTypeByMetadataName("Avalonia.Controls.Button");
        Assert.NotNull(avaloniaButton);
        Assert.True(SymbolEqualityComparer.Default.Equals(avaloniaButton, symbol.CSharpDefinition.Symbol));

        var cachedSymbolInfo = semanticModel.GetSymbolInfo(element);
        Assert.Same(symbol, cachedSymbolInfo.Symbol);
    }

    [Fact]
    public void SemanticModel_ResolvesMarkupComponentSymbol_ThroughUsingAlias()
    {
        const string code =
            "using Btn = Avalonia.Controls.Button;\n" +
            "\n" +
            "<Btn />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);

        var symbolInfo = semanticModel.GetSymbolInfo(element);

        var symbol = Assert.IsType<MarkupComponentSymbol>(symbolInfo.Symbol);
        Assert.Equal("Btn", symbol.Name);
        AssertResolvedAvaloniaButton(semanticModel, symbol);
    }

    [Fact]
    public void SemanticModel_ResolvesMarkupComponentSymbol_FromQualifiedComponentName()
    {
        const string code = "<Avalonia.Controls.Button />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);

        var symbolInfo = semanticModel.GetSymbolInfo(element);

        var symbol = Assert.IsType<MarkupComponentSymbol>(symbolInfo.Symbol);
        Assert.Equal("Avalonia.Controls.Button", symbol.Name);
        AssertResolvedAvaloniaButton(semanticModel, symbol);
    }

    [Fact]
    public void SemanticModel_ResolvesMarkupComponentSymbol_FromGlobalAliasQualifiedComponentName()
    {
        const string code = "<global::Avalonia.Controls.Button />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);

        var symbolInfo = semanticModel.GetSymbolInfo(element);

        var symbol = Assert.IsType<MarkupComponentSymbol>(symbolInfo.Symbol);
        Assert.Equal("global::Avalonia.Controls.Button", symbol.Name);
        AssertResolvedAvaloniaButton(semanticModel, symbol);
    }

    [Fact]
    public void SemanticModel_ResolvesMarkupComponentSymbol_FromExternAliasQualifiedComponentName()
    {
        const string code = "<av::Avalonia.Controls.Button />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(
            syntaxTree,
            CSharpCompilation.Create(
                assemblyName: "TestAssembly",
                references: CreateExternAliasAvaloniaReferences("av")));
        var element = GetOnlyMarkupElement(syntaxTree);

        var symbolInfo = semanticModel.GetSymbolInfo(element);

        var symbol = Assert.IsType<MarkupComponentSymbol>(symbolInfo.Symbol);
        Assert.Equal("av::Avalonia.Controls.Button", symbol.Name);
        Assert.Equal("Button", symbol.CSharpDefinition.Name);
        Assert.Equal("Avalonia.Controls.Button", symbol.CSharpDefinition.ToDisplayString());
    }

    [Fact]
    public void SemanticModel_ResolvesMarkupComponentSymbol_FromGenericComponentName()
    {
        const string code =
            "using System.Collections.Generic;\n" +
            "\n" +
            "<List{string} />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);

        var symbolInfo = semanticModel.GetSymbolInfo(element);

        var symbol = Assert.IsType<MarkupComponentSymbol>(symbolInfo.Symbol);
        var componentType = Assert.IsAssignableFrom<INamedTypeSymbol>(symbol.CSharpDefinition.Symbol);
        Assert.Equal("List{string}", symbol.Name);
        Assert.Equal("List", componentType.Name);
        Assert.True(componentType.IsGenericType);
        Assert.Equal(SpecialType.System_String, componentType.TypeArguments.Single().SpecialType);
    }

    [Fact]
    public void SemanticModel_AmbiguousMarkupComponentName_ReturnsCandidateSymbols()
    {
        const string code =
            "using First;\n" +
            "using Second;\n" +
            "\n" +
            "<Clash />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(
            syntaxTree,
            CreateCSharpCompilation(
                "namespace First { public class Clash { } }\n" +
                "namespace Second { public class Clash { } }"));
        var element = GetOnlyMarkupElement(syntaxTree);

        var symbolInfo = semanticModel.GetSymbolInfo(element);

        Assert.Null(symbolInfo.Symbol);
        Assert.Equal(AkburaCandidateReason.Ambiguous, symbolInfo.CandidateReason);
        Assert.Equal(2, symbolInfo.CandidateSymbols.Length);
        Assert.All(symbolInfo.CandidateSymbols, symbol =>
        {
            var component = Assert.IsType<MarkupComponentSymbol>(symbol);
            Assert.Equal("Clash", component.Name);
            Assert.Equal("Clash", component.CSharpDefinition.Name);
        });
    }

    [Fact]
    public void SemanticModel_ButtonContent_AllowsTextChild()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "\n" +
            "<Button>Hello !</Button>";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);

        var symbol = Assert.IsType<MarkupComponentSymbol>(semanticModel.GetSymbolInfo(element).Symbol);
        var diagnostics = semanticModel.GetSemanticDiagnostics(element);

        Assert.True(diagnostics.IsEmpty);
        Assert.Equal("Content", symbol.ContentModel.ContentProperty.Name);
        Assert.Equal(SpecialType.System_Object, Assert.IsAssignableFrom<INamedTypeSymbol>(symbol.ContentModel.AllowedChildType.Symbol).SpecialType);
        Assert.True(symbol.ContentModel.AllowsText);
        var child = Assert.Single(symbol.Children);
        Assert.Equal(MarkupChildKind.Text, child.Kind);
        Assert.Equal("Hello !", child.Text);
    }

    [Fact]
    public void SemanticModel_BorderContent_RejectsTextChild()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "\n" +
            "<Border>Hello world!</Border>";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);

        var symbol = Assert.IsType<MarkupComponentSymbol>(semanticModel.GetSymbolInfo(element).Symbol);
        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(element));

        Assert.Equal("Child", symbol.ContentModel.ContentProperty.Name);
        Assert.Equal("Control", symbol.ContentModel.AllowedChildType.Name);
        Assert.False(symbol.ContentModel.AllowsText);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_InvalidMarkupChild, diagnostic.Code);
        Assert.Contains("string", diagnostic.Message);
        Assert.Contains("Avalonia.Controls.Control", diagnostic.Message);
    }

    [Fact]
    public void SemanticModel_BorderContent_AllowsControlChild()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "\n" +
            "<Border><TextBlock /></Border>";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);

        var symbol = Assert.IsType<MarkupComponentSymbol>(semanticModel.GetSymbolInfo(element).Symbol);

        Assert.True(semanticModel.GetSemanticDiagnostics(element).IsEmpty);
        var child = Assert.Single(symbol.Children);
        Assert.Equal(MarkupChildKind.Element, child.Kind);
        Assert.NotNull(child.ComponentSymbol);
        Assert.Equal("TextBlock", child.ComponentSymbol!.Name);
        Assert.Equal("Control", symbol.ContentModel.AllowedChildType.Name);
    }

    [Fact]
    public void SemanticModel_NonAvaloniaList_AllowsElementChildrenOfItemType()
    {
        const string code =
            "using System.Collections.Generic;\n" +
            "\n" +
            "<List{MyObject}><MyObject/><MyObject/></List>";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(
            syntaxTree,
            CreateCSharpCompilation("public class MyObject { }"));
        var element = GetOnlyMarkupElement(syntaxTree);

        var symbol = Assert.IsType<MarkupComponentSymbol>(semanticModel.GetSymbolInfo(element).Symbol);

        Assert.True(semanticModel.GetSemanticDiagnostics(element).IsEmpty);
        Assert.True(symbol.ContentModel.IsCollection);
        Assert.True(symbol.ContentModel.ContentProperty.IsDefault);
        Assert.Equal("MyObject", symbol.ContentModel.AllowedChildType.Name);
        Assert.Equal(2, symbol.Children.Length);
        Assert.All(symbol.Children, child =>
        {
            Assert.Equal(MarkupChildKind.Element, child.Kind);
            Assert.Equal("MyObject", child.ComponentSymbol?.Name);
        });
    }

    [Fact]
    public void SemanticModel_NonAvaloniaList_RejectsElementChildOfDifferentType()
    {
        const string code =
            "using System.Collections.Generic;\n" +
            "\n" +
            "<List{MyObject}><OtherObject/></List>";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(
            syntaxTree,
            CreateCSharpCompilation(
                "public class MyObject { }\n" +
                "public class OtherObject { }"));
        var element = GetOnlyMarkupElement(syntaxTree);

        var symbol = Assert.IsType<MarkupComponentSymbol>(semanticModel.GetSymbolInfo(element).Symbol);
        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(element));

        Assert.Equal("MyObject", symbol.ContentModel.AllowedChildType.Name);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_InvalidMarkupChild, diagnostic.Code);
        Assert.Contains("OtherObject", diagnostic.Message);
        Assert.Contains("MyObject", diagnostic.Message);
    }

    [Fact]
    public void SemanticModel_ResolvesStateSymbol()
    {
        const string code =
            "state bool isOpen = false;\n" +
            "\n" +
            "using Avalonia.Controls;\n" +
            "\n" +
            "<Button />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var state = Assert.IsType<StateDeclarationSyntax>(
            syntaxTree.GetRoot().Members.Single(member => member is StateDeclarationSyntax));

        var symbolInfo = semanticModel.GetSymbolInfo(state);

        var symbol = Assert.IsAssignableFrom<IStateSymbol>(symbolInfo.Symbol);
        Assert.Equal(AkburaCandidateReason.None, symbolInfo.CandidateReason);
        Assert.True(symbolInfo.CandidateSymbols.IsEmpty);
        Assert.Equal(AkburaSymbolKind.State, symbol.Kind);
        Assert.Equal(SymbolLanguage.Akbura, symbol.Language);
        Assert.Equal("isOpen", symbol.Name);
        Assert.True(symbol.HasExplicitType);
        Assert.False(symbol.IsBindable);
        Assert.Equal(StateBindingKind.None, symbol.BindingKind);
        Assert.False(symbol.Type.IsDefault);
        Assert.Equal("Boolean", symbol.Type.Name);
        Assert.False(symbol.InitializerType.IsDefault);
        Assert.Equal("Boolean", symbol.InitializerType.Name);
        Assert.True(SymbolEqualityComparer.Default.Equals(
            semanticModel.Compilation.CSharpCompilation.GetSpecialType(SpecialType.System_Boolean),
            symbol.Type.Symbol));
        Assert.True(SymbolEqualityComparer.Default.Equals(
            semanticModel.Compilation.CSharpCompilation.GetSpecialType(SpecialType.System_Boolean),
            symbol.InitializerType.Symbol));
        Assert.Same(state, symbol.DeclarationSyntax);
        Assert.Same(state.Initializer, symbol.InitializerSyntax);
        Assert.Same(state.Initializer.Expression, symbol.InitializerExpression);
        Assert.True(symbol.CSharpDefinition.IsDefault);
        Assert.Equal("state Boolean isOpen", symbol.ToDisplayString());

        var cachedSymbolInfo = semanticModel.GetSymbolInfo(state);
        Assert.Same(symbol, cachedSymbolInfo.Symbol);
    }

    [Fact]
    public void SemanticModel_ResolvesBindableImplicitStateSymbol()
    {
        const string code = "state isBusy = bind true;";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var state = Assert.IsType<StateDeclarationSyntax>(syntaxTree.GetRoot().Members.Single());

        var symbolInfo = semanticModel.GetSymbolInfo(state);

        var symbol = Assert.IsAssignableFrom<IStateSymbol>(symbolInfo.Symbol);
        Assert.Equal(AkburaCandidateReason.None, symbolInfo.CandidateReason);
        Assert.Equal("isBusy", symbol.Name);
        Assert.False(symbol.HasExplicitType);
        Assert.True(symbol.IsBindable);
        Assert.Equal(StateBindingKind.Bind, symbol.BindingKind);
        Assert.False(symbol.Type.IsDefault);
        Assert.False(symbol.InitializerType.IsDefault);
        Assert.Equal("Boolean", symbol.Type.Name);
        Assert.Equal("Boolean", symbol.InitializerType.Name);
        Assert.True(SymbolEqualityComparer.Default.Equals(
            semanticModel.Compilation.CSharpCompilation.GetSpecialType(SpecialType.System_Boolean),
            symbol.Type.Symbol));
        Assert.Same(state.Initializer, symbol.InitializerSyntax);
        Assert.Same(state.Initializer.Expression, symbol.InitializerExpression);
        Assert.Equal("state Boolean isBusy", symbol.ToDisplayString());
    }

    [Fact]
    public void SemanticModel_ResolvesDefaultParamSymbol()
    {
        const string code = "param int UserId = 1;";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var param = Assert.IsType<ParamDeclarationSyntax>(syntaxTree.GetRoot().Members.Single());

        var symbolInfo = semanticModel.GetSymbolInfo(param);

        var symbol = Assert.IsAssignableFrom<IParamSymbol>(symbolInfo.Symbol);
        Assert.Equal(AkburaCandidateReason.None, symbolInfo.CandidateReason);
        Assert.True(symbolInfo.CandidateSymbols.IsEmpty);
        Assert.Equal(AkburaSymbolKind.Parameter, symbol.Kind);
        Assert.Equal(SymbolLanguage.Akbura, symbol.Language);
        Assert.Equal("UserId", symbol.Name);
        Assert.Equal(ParamBindingKind.Default, symbol.BindingKind);
        Assert.True(symbol.ReceivesValueFromParent);
        Assert.False(symbol.SendsValueToParent);
        Assert.False(symbol.IsTwoWayBinding);
        Assert.True(symbol.HasExplicitType);
        Assert.True(symbol.HasDefaultValue);
        Assert.False(symbol.Type.IsDefault);
        Assert.False(symbol.DefaultValueType.IsDefault);
        Assert.Equal("Int32", symbol.Type.Name);
        Assert.Equal("Int32", symbol.DefaultValueType.Name);
        Assert.Same(param, symbol.DeclarationSyntax);
        Assert.Same(param.DefaultValue, symbol.DefaultValueSyntax);
        Assert.Equal("param Int32 UserId", symbol.ToDisplayString());
        Assert.True(semanticModel.GetSemanticDiagnostics(param).IsEmpty);

        var cachedSymbolInfo = semanticModel.GetSymbolInfo(param);
        Assert.Same(symbol, cachedSymbolInfo.Symbol);
    }

    [Fact]
    public void SemanticModel_ResolvesBindParamSymbol()
    {
        const string code = "param bind string Search = \"\";";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var param = Assert.IsType<ParamDeclarationSyntax>(syntaxTree.GetRoot().Members.Single());

        var symbol = Assert.IsAssignableFrom<IParamSymbol>(semanticModel.GetSymbolInfo(param).Symbol);

        Assert.Equal("Search", symbol.Name);
        Assert.Equal(ParamBindingKind.Bind, symbol.BindingKind);
        Assert.True(symbol.ReceivesValueFromParent);
        Assert.True(symbol.SendsValueToParent);
        Assert.True(symbol.IsTwoWayBinding);
        Assert.True(symbol.HasExplicitType);
        Assert.True(symbol.HasDefaultValue);
        Assert.Equal("String", symbol.Type.Name);
        Assert.Equal("String", symbol.DefaultValueType.Name);
        Assert.Equal("param bind String Search", symbol.ToDisplayString());
    }

    [Fact]
    public void SemanticModel_ResolvesOutParamSymbol()
    {
        const string code = "param out SelectedTask;";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var param = Assert.IsType<ParamDeclarationSyntax>(syntaxTree.GetRoot().Members.Single());

        var symbol = Assert.IsAssignableFrom<IParamSymbol>(semanticModel.GetSymbolInfo(param).Symbol);

        Assert.Equal("SelectedTask", symbol.Name);
        Assert.Equal(ParamBindingKind.Out, symbol.BindingKind);
        Assert.False(symbol.ReceivesValueFromParent);
        Assert.True(symbol.SendsValueToParent);
        Assert.False(symbol.IsTwoWayBinding);
        Assert.False(symbol.HasExplicitType);
        Assert.False(symbol.HasDefaultValue);
        Assert.True(symbol.Type.IsDefault);
        Assert.True(symbol.DefaultValueType.IsDefault);
        Assert.Null(symbol.DefaultValueSyntax);
        Assert.Equal("param out SelectedTask", symbol.ToDisplayString());
    }

    [Fact]
    public void SemanticModel_InfersImplicitParamTypeFromDefaultValue()
    {
        const string code = "param Search = \"\";";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var param = Assert.IsType<ParamDeclarationSyntax>(syntaxTree.GetRoot().Members.Single());

        var symbol = Assert.IsAssignableFrom<IParamSymbol>(semanticModel.GetSymbolInfo(param).Symbol);

        Assert.Equal("Search", symbol.Name);
        Assert.False(symbol.HasExplicitType);
        Assert.True(symbol.HasDefaultValue);
        Assert.Equal(ParamBindingKind.Default, symbol.BindingKind);
        Assert.Equal("String", symbol.Type.Name);
        Assert.Equal("String", symbol.DefaultValueType.Name);
        Assert.Equal("param String Search", symbol.ToDisplayString());
    }

    [Fact]
    public void SemanticModel_ResolvesInjectSymbol()
    {
        const string code = "inject ILogger<MyComponent> logger;";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(
            syntaxTree,
            CreateCSharpCompilation(
                "public interface ILogger<T> { }\n" +
                "public sealed class MyComponent { }"));
        var inject = Assert.IsType<InjectDeclarationSyntax>(syntaxTree.GetRoot().Members.Single());

        var symbolInfo = semanticModel.GetSymbolInfo(inject);

        var symbol = Assert.IsAssignableFrom<IInjectSymbol>(symbolInfo.Symbol);
        Assert.Equal(AkburaCandidateReason.None, symbolInfo.CandidateReason);
        Assert.True(symbolInfo.CandidateSymbols.IsEmpty);
        Assert.Equal(AkburaSymbolKind.InjectedService, symbol.Kind);
        Assert.Equal(SymbolLanguage.Akbura, symbol.Language);
        Assert.Equal("logger", symbol.Name);
        Assert.True(symbol.IsRequired);
        Assert.False(symbol.Type.IsDefault);
        Assert.Equal("ILogger", symbol.Type.Name);
        var loggerType = Assert.IsAssignableFrom<INamedTypeSymbol>(symbol.Type.Symbol);
        Assert.True(loggerType.IsGenericType);
        Assert.Equal("MyComponent", loggerType.TypeArguments.Single().Name);
        Assert.Same(inject, symbol.DeclarationSyntax);
        Assert.True(symbol.CSharpDefinition.IsDefault);
        Assert.Equal("inject global::ILogger<global::MyComponent> logger", symbol.ToDisplayString());
        Assert.True(semanticModel.GetSemanticDiagnostics(inject).IsEmpty);

        var cachedSymbolInfo = semanticModel.GetSymbolInfo(inject);
        Assert.Same(symbol, cachedSymbolInfo.Symbol);
    }

    [Fact]
    public void SemanticModel_ResolvesCommandSymbol()
    {
        const string code = "command System.Threading.Tasks.Task<int> Click(int a);";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var command = Assert.IsType<CommandDeclarationSyntax>(syntaxTree.GetRoot().Members.Single());

        var symbolInfo = semanticModel.GetSymbolInfo(command);

        var symbol = Assert.IsAssignableFrom<ICommandSymbol>(symbolInfo.Symbol);
        Assert.Equal(AkburaCandidateReason.None, symbolInfo.CandidateReason);
        Assert.Equal(AkburaSymbolKind.Command, symbol.Kind);
        Assert.Equal(SymbolLanguage.Akbura, symbol.Language);
        Assert.Equal("Click", symbol.Name);
        Assert.False(symbol.IsVoid);
        Assert.True(symbol.IsAsyncLike);
        Assert.True(symbol.HasResult);
        Assert.True(symbol.SupportsIsExecuting);
        Assert.Equal("Task", symbol.ReturnType.Name);
        Assert.Equal("Int32", symbol.ResultType.Name);
        Assert.Same(command, symbol.DeclarationSyntax);

        var parameter = Assert.Single(symbol.Parameters);
        Assert.Equal(0, parameter.Ordinal);
        Assert.Equal("a", parameter.Name);
        Assert.Equal("Int32", parameter.Type.Name);
        Assert.Equal("Int32 a", parameter.ToDisplayString());
        Assert.Equal("command Task Click", symbol.ToDisplayString());
        Assert.True(semanticModel.GetSemanticDiagnostics(command).IsEmpty);

        var cachedSymbolInfo = semanticModel.GetSymbolInfo(command);
        Assert.Same(symbol, cachedSymbolInfo.Symbol);
    }

    [Fact]
    public void SemanticModel_BindStateWithInpcSource_HasNoBindingDiagnostics()
    {
        const string code =
            "state MyViewModel vm = new MyViewModel();\n" +
            "state string name = bind vm.Name;";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(
            syntaxTree,
            CreateCSharpCompilation(
                "using System.ComponentModel;\n" +
                "public sealed class MyViewModel : INotifyPropertyChanged\n" +
                "{\n" +
                "    public event PropertyChangedEventHandler? PropertyChanged;\n" +
                "    public string Name { get; set; } = \"\";\n" +
                "}"));
        var state = GetStateDeclaration(syntaxTree, "name");

        var symbol = Assert.IsAssignableFrom<IStateSymbol>(semanticModel.GetSymbolInfo(state).Symbol);

        Assert.True(semanticModel.GetSemanticDiagnostics(state).IsEmpty);
        Assert.True(symbol.IsBindable);
        Assert.False(symbol.IsReadOnly);
        Assert.Equal(StateBindingKind.Bind, symbol.BindingKind);
        Assert.Equal("String", symbol.Type.Name);
        Assert.Equal("String", symbol.InitializerType.Name);
    }

    [Fact]
    public void SemanticModel_BindStateWithoutObservableSource_ProducesWarning()
    {
        const string code =
            "state MyViewModel vm = new MyViewModel();\n" +
            "state string name = bind vm.Name;";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(
            syntaxTree,
            CreateCSharpCompilation(
                "public sealed class MyViewModel\n" +
                "{\n" +
                "    public string Name { get; set; } = \"\";\n" +
                "}"));
        var state = GetStateDeclaration(syntaxTree, "name");

        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(state));

        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_StateBindingSourceNotObservable, diagnostic.Code);
        Assert.Equal(AkburaDiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("vm.Name", diagnostic.Message);
        Assert.Contains("string", diagnostic.Message);
        Assert.Contains("MyViewModel", diagnostic.Message);
    }

    [Fact]
    public void SemanticModel_OutStateWithObservableSource_IsReadonlyAndHasNoBindingDiagnostics()
    {
        const string code =
            "state MyViewModel vm = new MyViewModel();\n" +
            "state string fullName = out vm.FullName;";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(
            syntaxTree,
            CreateCSharpCompilation(
                "using System;\n" +
                "public sealed class MyViewModel\n" +
                "{\n" +
                "    public IObservable<string> FullName { get; } = null!;\n" +
                "}"));
        var state = GetStateDeclaration(syntaxTree, "fullName");

        var symbol = Assert.IsAssignableFrom<IStateSymbol>(semanticModel.GetSymbolInfo(state).Symbol);

        Assert.True(semanticModel.GetSemanticDiagnostics(state).IsEmpty);
        Assert.True(symbol.IsBindable);
        Assert.True(symbol.IsReadOnly);
        Assert.Equal(StateBindingKind.Out, symbol.BindingKind);
        Assert.Equal("String", symbol.Type.Name);
        Assert.Equal("IObservable", symbol.InitializerType.Name);
    }

    [Fact]
    public void SemanticModel_InStateWithGetOnlyProperty_ProducesWritableTargetError()
    {
        const string code =
            "state MyViewModel vm = new MyViewModel();\n" +
            "state string surname = in vm.Surname;";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(
            syntaxTree,
            CreateCSharpCompilation(
                "public sealed class MyViewModel\n" +
                "{\n" +
                "    public string Surname { get; } = \"\";\n" +
                "}"));
        var state = GetStateDeclaration(syntaxTree, "surname");

        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(state));

        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_StateBindingTargetNotWritable, diagnostic.Code);
        Assert.Equal(AkburaDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("vm.Surname", diagnostic.Message);
    }

    [Fact]
    public void SemanticModel_InStateWithReadonlyField_ProducesWritableTargetError()
    {
        const string code =
            "state MyViewModel vm = new MyViewModel();\n" +
            "state string surname = in vm.Surname;";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(
            syntaxTree,
            CreateCSharpCompilation(
                "public sealed class MyViewModel\n" +
                "{\n" +
                "    public readonly string Surname = \"\";\n" +
                "}"));
        var state = GetStateDeclaration(syntaxTree, "surname");

        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(state));

        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_StateBindingTargetNotWritable, diagnostic.Code);
        Assert.Equal(AkburaDiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void SemanticModel_InState_AllowsBindingPathExpressions()
    {
        const string code =
            "state MyViewModel vm = new MyViewModel();\n" +
            "state MyViewModel sameVm = in vm;\n" +
            "state string name = in vm.Name;\n" +
            "state string firstName = in vm.Persons[0].Name;";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(
            syntaxTree,
            CreateCSharpCompilation(
                "using System.Collections.Generic;\n" +
                "public sealed class MyViewModel\n" +
                "{\n" +
                "    public string Name { get; set; } = \"\";\n" +
                "    public List<Person> Persons { get; } = new();\n" +
                "}\n" +
                "public sealed class Person\n" +
                "{\n" +
                "    public string Name { get; set; } = \"\";\n" +
                "}"));

        Assert.True(semanticModel.GetSemanticDiagnostics(GetStateDeclaration(syntaxTree, "sameVm")).IsEmpty);
        Assert.True(semanticModel.GetSemanticDiagnostics(GetStateDeclaration(syntaxTree, "name")).IsEmpty);
        Assert.True(semanticModel.GetSemanticDiagnostics(GetStateDeclaration(syntaxTree, "firstName")).IsEmpty);
    }

    [Fact]
    public void SemanticModel_BindableStateWithNonPathExpression_ProducesBindingExpressionError()
    {
        const string code =
            "state MyViewModel vm = new MyViewModel();\n" +
            "state int age = in vm.Age + 1;";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(
            syntaxTree,
            CreateCSharpCompilation(
                "public sealed class MyViewModel\n" +
                "{\n" +
                "    public int Age { get; set; }\n" +
                "}"));
        var state = GetStateDeclaration(syntaxTree, "age");

        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(state));

        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_StateBindingExpressionExpected, diagnostic.Code);
        Assert.Equal(AkburaDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("vm.Age + 1", diagnostic.Message);
    }

    [Fact]
    public void SemanticModel_ResolvesAvaloniaMarkupPropertySymbol()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "\n" +
            "<Button Content=\"Hello\" />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var symbolInfo = semanticModel.GetSymbolInfo(attribute);

        var symbol = Assert.IsAssignableFrom<AkburaPropertySymbol>(symbolInfo.Symbol);
        Assert.Equal(AkburaCandidateReason.None, symbolInfo.CandidateReason);
        Assert.Equal(AkburaSymbolKind.Property, symbol.Kind);
        Assert.Equal(SymbolLanguage.Markup, symbol.Language);
        Assert.Equal("Content", symbol.Name);
        Assert.True(symbol.IsAvaloniaProperty);
        Assert.True(symbol.IsClrProperty);
        Assert.False(symbol.IsParameter);
        Assert.True(symbol.CanRead);
        Assert.True(symbol.CanWrite);
        Assert.Equal("ContentProperty", symbol.AvaloniaPropertyDefinition.Name);
        Assert.Equal("Content", symbol.ClrPropertyDefinition.Name);
        Assert.Equal(SpecialType.System_Object, Assert.IsAssignableFrom<INamedTypeSymbol>(symbol.Type.Symbol).SpecialType);
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);

        var cachedSymbolInfo = semanticModel.GetSymbolInfo(attribute);
        Assert.Same(symbol, cachedSymbolInfo.Symbol);
    }

    [Fact]
    public void SemanticModel_ResolvesAkburaControlAvaloniaPropertySymbol()
    {
        const string code =
            "using Akbura;\n" +
            "\n" +
            "<AkburaControl Padding=\"12\" />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var symbol = Assert.IsAssignableFrom<AkburaPropertySymbol>(semanticModel.GetSymbolInfo(attribute).Symbol);

        Assert.Equal("Padding", symbol.Name);
        Assert.True(symbol.IsAvaloniaProperty);
        Assert.True(symbol.IsClrProperty);
        Assert.False(symbol.IsParameter);
        Assert.True(symbol.CanRead);
        Assert.True(symbol.CanWrite);
        Assert.Equal("PaddingProperty", symbol.AvaloniaPropertyDefinition.Name);
        Assert.Equal("Padding", symbol.ClrPropertyDefinition.Name);
        Assert.Equal("Thickness", symbol.Type.Name);
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
    }

    [Fact]
    public void SemanticModel_ResolvesPlainCSharpMarkupPropertySymbol()
    {
        const string code = "<PlainComponent Title=\"Hello\" />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(
            syntaxTree,
            CreateCSharpCompilation(
                "public sealed class PlainComponent\n" +
                "{\n" +
                "    public string Title { get; set; } = \"\";\n" +
                "}"));
        var element = GetOnlyMarkupElement(syntaxTree);
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var symbol = Assert.IsAssignableFrom<AkburaPropertySymbol>(semanticModel.GetSymbolInfo(attribute).Symbol);

        Assert.Equal("Title", symbol.Name);
        Assert.False(symbol.IsAvaloniaProperty);
        Assert.True(symbol.IsClrProperty);
        Assert.False(symbol.IsParameter);
        Assert.True(symbol.CanRead);
        Assert.True(symbol.CanWrite);
        Assert.True(symbol.AvaloniaPropertyDefinition.IsDefault);
        Assert.Equal("Title", symbol.ClrPropertyDefinition.Name);
        Assert.Equal(SpecialType.System_String, Assert.IsAssignableFrom<INamedTypeSymbol>(symbol.Type.Symbol).SpecialType);
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
    }

    [Fact]
    public void SemanticModel_MissingMarkupProperty_ProducesDiagnostic()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "\n" +
            "<Button Missing=\"1\" />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var symbolInfo = semanticModel.GetSymbolInfo(attribute);
        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(attribute));

        Assert.Null(symbolInfo.Symbol);
        Assert.Equal(AkburaCandidateReason.NotFound, symbolInfo.CandidateReason);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_MarkupPropertyNotFound, diagnostic.Code);
        Assert.Equal(AkburaDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Missing", diagnostic.Message);
        Assert.Contains("Button", diagnostic.Message);
    }

    [Fact]
    public void SemanticModel_ResolvesAkburaComponentParamAsMarkupProperty()
    {
        const string aCode =
            "namespace SomeNs;\n" +
            "\n" +
            "param int Hello;";
        const string bCode =
            "using SomeNs;\n" +
            "\n" +
            "namespace Hi;\n" +
            "\n" +
            "<A Hello={1}/>";

        var aSyntaxTree = AkburaSyntaxTree.ParseText(aCode, "A.akbura");
        var bSyntaxTree = AkburaSyntaxTree.ParseText(bCode, "B.akbura");
        var compilation = new AkburaCompilation(CreateCSharpCompilation(), [aSyntaxTree, bSyntaxTree]);
        var semanticModel = compilation.GetSemanticModel(bSyntaxTree);
        var element = GetOnlyMarkupElement(bSyntaxTree);
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var componentSymbolInfo = semanticModel.GetSymbolInfo(element);
        var componentSymbol = Assert.IsType<AkburaMarkupComponentSymbol>(componentSymbolInfo.Symbol);
        var paramSymbol = Assert.Single(componentSymbol.Parameters);
        var propertySymbolInfo = semanticModel.GetSymbolInfo(attribute);
        var propertySymbol = Assert.IsAssignableFrom<AkburaPropertySymbol>(propertySymbolInfo.Symbol);

        Assert.Equal(AkburaCandidateReason.None, componentSymbolInfo.CandidateReason);
        Assert.Equal("A", componentSymbol.Name);
        Assert.Equal("SomeNs.A", componentSymbol.MetadataName);
        Assert.Same(aSyntaxTree, componentSymbol.SyntaxTree);
        Assert.Equal("Hello", paramSymbol.Name);
        Assert.Equal("Int32", paramSymbol.Type.Name);

        Assert.Equal(AkburaCandidateReason.None, propertySymbolInfo.CandidateReason);
        Assert.Equal("Hello", propertySymbol.Name);
        Assert.True(propertySymbol.IsParameter);
        Assert.False(propertySymbol.IsAvaloniaProperty);
        Assert.False(propertySymbol.IsClrProperty);
        Assert.Same(paramSymbol, propertySymbol.Parameter);
        Assert.Equal("Int32", propertySymbol.Type.Name);
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
    }

    [Fact]
    public void SemanticModel_ResolvesAkburaComponentCommandAsMarkupProperty()
    {
        const string aCode =
            "namespace SomeNs;\n" +
            "\n" +
            "command int Click(int a);";
        const string bCode =
            "using SomeNs;\n" +
            "\n" +
            "<A Click={x => x * 2}/>";

        var aSyntaxTree = AkburaSyntaxTree.ParseText(aCode, "A.akbura");
        var bSyntaxTree = AkburaSyntaxTree.ParseText(bCode, "B.akbura");
        var compilation = new AkburaCompilation(CreateCSharpCompilation(), [aSyntaxTree, bSyntaxTree]);
        var semanticModel = compilation.GetSemanticModel(bSyntaxTree);
        var element = GetOnlyMarkupElement(bSyntaxTree);
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var componentSymbol = Assert.IsType<AkburaMarkupComponentSymbol>(
            semanticModel.GetSymbolInfo(element).Symbol);
        var commandSymbol = Assert.Single(componentSymbol.Commands);
        var propertySymbol = Assert.IsAssignableFrom<AkburaPropertySymbol>(
            semanticModel.GetSymbolInfo(attribute).Symbol);

        Assert.Equal("Click", commandSymbol.Name);
        Assert.Equal("Int32", commandSymbol.ReturnType.Name);
        Assert.Equal("a", Assert.Single(commandSymbol.Parameters).Name);

        Assert.Equal("Click", propertySymbol.Name);
        Assert.True(propertySymbol.IsCommand);
        Assert.False(propertySymbol.IsParameter);
        Assert.False(propertySymbol.IsAvaloniaProperty);
        Assert.False(propertySymbol.IsClrProperty);
        Assert.Same(commandSymbol, propertySymbol.Command);
        Assert.Equal("Int32", propertySymbol.Type.Name);
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
    }

    [Fact]
    public void SemanticModel_UnresolvedMarkupComponent_ReturnsNotFound()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "\n" +
            "<MissingControl />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);

        var symbolInfo = semanticModel.GetSymbolInfo(element);

        Assert.Null(symbolInfo.Symbol);
        Assert.True(symbolInfo.CandidateSymbols.IsEmpty);
        Assert.Equal(AkburaCandidateReason.NotFound, symbolInfo.CandidateReason);
    }

    [Fact]
    public void Compilation_GetSemanticModel_RejectsForeignSyntaxTree()
    {
        var ownedTree = AkburaSyntaxTree.ParseText("<Button />");
        var foreignTree = AkburaSyntaxTree.ParseText("<TextBlock />");
        var compilation = new AkburaCompilation(CreateCSharpCompilation(), [ownedTree]);

        Assert.Throws<ArgumentException>(() => compilation.GetSemanticModel(foreignTree));
    }

    private static AkburaSemanticModel CreateSemanticModel(AkburaSyntaxTree syntaxTree)
    {
        return CreateSemanticModel(syntaxTree, CreateCSharpCompilation());
    }

    private static AkburaSemanticModel CreateSemanticModel(
        AkburaSyntaxTree syntaxTree,
        CSharpCompilation csharpCompilation)
    {
        var compilation = new AkburaCompilation(csharpCompilation, [syntaxTree]);
        return compilation.GetSemanticModel(syntaxTree);
    }

    private static CSharpCompilation CreateCSharpCompilation(params string[] sources)
    {
        return CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            references: SymbolTests.CreateAvaloniaReferences(),
            syntaxTrees: sources.Select(source => CSharpSyntaxTree.ParseText(
                source,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview))));
    }

    private static MarkupElementSyntax GetOnlyMarkupElement(AkburaSyntaxTree syntaxTree)
    {
        var root = syntaxTree.GetRoot();
        var markupRoot = Assert.IsType<MarkupRootSyntax>(root.Members.Single(member => member is MarkupRootSyntax));
        return markupRoot.Element;
    }

    private static StateDeclarationSyntax GetStateDeclaration(AkburaSyntaxTree syntaxTree, string name)
    {
        return Assert.IsType<StateDeclarationSyntax>(
            syntaxTree.GetRoot().Members.Single(member =>
                member is StateDeclarationSyntax state &&
                state.Name.Identifier.ValueText == name));
    }

    private static void AssertResolvedAvaloniaButton(
        AkburaSemanticModel semanticModel,
        MarkupComponentSymbol symbol)
    {
        var avaloniaButton = semanticModel.Compilation.CSharpCompilation.GetTypeByMetadataName("Avalonia.Controls.Button");
        Assert.NotNull(avaloniaButton);
        Assert.True(SymbolEqualityComparer.Default.Equals(avaloniaButton, symbol.CSharpDefinition.Symbol));
    }

    private static MetadataReference[] CreateExternAliasAvaloniaReferences(string alias)
    {
        var avaloniaControlsAssembly = typeof(Akbura.AkburaControl).BaseType!.Assembly;

        return
        [
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(
                avaloniaControlsAssembly.Location,
                new MetadataReferenceProperties(
                    MetadataImageKind.Assembly,
                    aliases: ImmutableArray.Create(alias))),
        ];
    }
}
