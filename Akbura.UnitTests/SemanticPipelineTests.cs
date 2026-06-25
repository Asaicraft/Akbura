using Akbura.Language;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using System.IO;
using AkburaCandidateReason = Akbura.Language.Symbols.CandidateReason;
using AkburaPropertySymbol = Akbura.Language.Symbols.IPropertySymbol;
using AkburaSymbolKind = Akbura.Language.Symbols.SymbolKind;

namespace Akbura.UnitTests;

public class SemanticPipelineTests
{
    private const string UserHookCSharpCode =
        "using Akbura.CompilerAnotations;\n" +
        "\n" +
        "namespace Hooks;\n" +
        "\n" +
        "[UserHook]\n" +
        "public struct UseNameHook\n" +
        "{\n" +
        "    public string UseHook<T>(object component, T state)\n" +
        "    {\n" +
        "        return \"name\";\n" +
        "    }\n" +
        "}";

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
    public void SemanticModel_AkburaComponentSymbol_CollectsTopLevelAndConditionalMarkup()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "\n" +
            "state bool isLoaded = true;\n" +
            "\n" +
            "if(!isLoaded)\n" +
            "{\n" +
            "    <TextBlock Text=\"Loading...\"/>\n" +
            "}\n" +
            "\n" +
            "<TextBlock Text=\"Loaded\"/>";
        var syntaxTree = AkburaSyntaxTree.ParseText(code, "Views/Counter.akbura");
        var compilation = new AkburaCompilation(
            CreateCSharpCompilation(),
            [syntaxTree],
            rootNamespace: "Demo");
        var semanticModel = compilation.GetSemanticModel(syntaxTree);

        var symbolInfo = semanticModel.GetSymbolInfo(syntaxTree.GetRoot());

        var symbol = Assert.IsAssignableFrom<IAkburaComponentSymbol>(symbolInfo.Symbol);
        Assert.Equal(AkburaSymbolKind.AkburaComponent, symbol.Kind);
        Assert.Equal("Counter", symbol.Name);
        Assert.Equal("Demo.Views.Counter", symbol.MetadataName);
        Assert.Equal("Demo.Views", symbol.NamespaceName);
        Assert.Equal(2, symbol.MarkupRoots.Length);
        Assert.All(symbol.MarkupRoots, markupRoot => Assert.Equal("TextBlock", markupRoot.Name));
        Assert.Single(symbol.States);
        Assert.Equal("isLoaded", symbol.States[0].Name);
    }

    [Fact]
    public void SemanticModel_AkburaComponentSymbol_ResolvesDefaultNamespacePartialType()
    {
        const string code =
            "inject ILogger<Counter> logger;\n" +
            "\n" +
            "<Button />";
        const string csharpCode =
            "namespace RootNamespace.MyNamespace;\n" +
            "\n" +
            "public interface ILogger<T> { }\n" +
            "\n" +
            "public partial class Counter\n" +
            "{\n" +
            "    public void First() { }\n" +
            "}\n" +
            "\n" +
            "public partial class Counter\n" +
            "{\n" +
            "    public void Second() { }\n" +
            "}";
        var syntaxTree = AkburaSyntaxTree.ParseText(code, "MyNamespace/Counter.akbura");
        var compilation = new AkburaCompilation(
            CreateCSharpCompilation(csharpCode),
            [syntaxTree],
            rootNamespace: "RootNamespace");
        var semanticModel = compilation.GetSemanticModel(syntaxTree);

        var symbolInfo = semanticModel.GetSymbolInfo(syntaxTree.GetRoot());

        var symbol = Assert.IsAssignableFrom<IAkburaComponentSymbol>(symbolInfo.Symbol);
        var partialType = Assert.Single(symbol.PartialTypes);
        Assert.Equal("RootNamespace.MyNamespace.Counter", partialType.ToDisplayString());
        Assert.Equal(2, partialType.DeclaringSyntaxReferences.Length);
        Assert.True(SymbolEqualityComparer.Default.Equals(partialType, symbol.CSharpDefinition.Symbol));

        var inject = Assert.Single(symbol.InjectedServices);
        Assert.Contains("RootNamespace.MyNamespace.Counter", inject.Type.ToDisplayString());
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
    public void SemanticModel_StateInitializerUserHook_ResolvesCSharpHookSymbol()
    {
        const string code =
            "using Hooks;\n" +
            "\n" +
            "state string query = \"\";\n" +
            "state string name = useName(query);";
        const string csharpCode =
            "using Akbura.CompilerAnotations;\n" +
            "\n" +
            "namespace Hooks;\n" +
            "\n" +
            "[UserHook]\n" +
            "public struct UseNameHook\n" +
            "{\n" +
            "    public string UseHook<T>(object component, T state)\n" +
            "    {\n" +
            "        return \"name\";\n" +
            "    }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(csharpCode));
        var state = GetStateDeclaration(syntaxTree, "name");

        var symbol = Assert.IsAssignableFrom<IStateSymbol>(semanticModel.GetSymbolInfo(state).Symbol);
        var hook = Assert.IsAssignableFrom<IUserHookSymbol>(symbol.UserHook);
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            semanticModel.GetSymbolInfo(syntaxTree.GetRoot()).Symbol);

        Assert.Equal(AkburaSymbolKind.UserHook, hook.Kind);
        Assert.Equal("useName", hook.InvocationName);
        Assert.Equal("useName", hook.Name);
        Assert.Equal("UseNameHook", hook.HookType.Name);
        Assert.Equal("UseHook", hook.UseHookMethod.Name);
        Assert.Equal("String", hook.ReturnType.Name);
        Assert.Equal("String", symbol.Type.Name);
        Assert.Same(hook, component.States.Single(stateSymbol => stateSymbol.Name == "name").UserHook);
    }

    [Fact]
    public void SemanticModel_StateInitializerUserHook_RequiresUsePrefixedHookType()
    {
        const string code =
            "using Hooks;\n" +
            "\n" +
            "state string name = useName();";
        const string csharpCode =
            "using Akbura.CompilerAnotations;\n" +
            "\n" +
            "namespace Hooks;\n" +
            "\n" +
            "[UserHook]\n" +
            "public struct NameHook\n" +
            "{\n" +
            "    public string UseHook()\n" +
            "    {\n" +
            "        return \"name\";\n" +
            "    }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(csharpCode));
        var state = GetStateDeclaration(syntaxTree, "name");

        var symbol = Assert.IsAssignableFrom<IStateSymbol>(semanticModel.GetSymbolInfo(state).Symbol);

        Assert.Null(symbol.UserHook);
    }

    [Fact]
    public void SemanticModel_UserHookInsideIfBlock_ProducesDiagnostic()
    {
        const string code =
            "using Hooks;\n" +
            "\n" +
            "state string query = \"\";\n" +
            "\n" +
            "if(true)\n" +
            "{\n" +
            "    useName(query);\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(UserHookCSharpCode));
        var ifStatement = Assert.IsType<CSharpStatementSyntax>(
            syntaxTree.GetRoot().Members.Single(member => member is CSharpStatementSyntax));
        var hookStatement = Assert.IsType<CSharpStatementSyntax>(Assert.Single(ifStatement.Body!.Tokens));

        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(hookStatement));

        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_UserHookMustBeTopLevel, diagnostic.Code);
        Assert.Contains("useName", diagnostic.Message);
    }

    [Fact]
    public void SemanticModel_UserHookInsideForeachBlock_ProducesDiagnostic()
    {
        const string code =
            "using Hooks;\n" +
            "\n" +
            "foreach(var item in items)\n" +
            "{\n" +
            "    useName(item);\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(UserHookCSharpCode));
        var foreachStatement = Assert.IsType<CSharpStatementSyntax>(
            syntaxTree.GetRoot().Members.Single(member => member is CSharpStatementSyntax));
        var hookStatement = Assert.IsType<CSharpStatementSyntax>(Assert.Single(foreachStatement.Body!.Tokens));

        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(hookStatement));

        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_UserHookMustBeTopLevel, diagnostic.Code);
        Assert.Contains("useName", diagnostic.Message);
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
    public void SemanticModel_ResolvesCurrentComponentType_FromFileNameInInject()
    {
        const string code = "inject ILogger<Counter> logger;";

        var syntaxTree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var semanticModel = CreateSemanticModel(
            syntaxTree,
            CreateCSharpCompilation("public interface ILogger<T> { }"));
        var inject = Assert.IsType<InjectDeclarationSyntax>(syntaxTree.GetRoot().Members.Single());

        var symbol = Assert.IsAssignableFrom<IInjectSymbol>(
            semanticModel.GetSymbolInfo(inject).Symbol);

        Assert.Equal("Counter", syntaxTree.ComponentName);
        Assert.Equal("ILogger", symbol.Type.Name);
        var loggerType = Assert.IsAssignableFrom<INamedTypeSymbol>(symbol.Type.Symbol);
        var componentType = Assert.IsAssignableFrom<INamedTypeSymbol>(loggerType.TypeArguments.Single());
        Assert.Equal("Counter", componentType.Name);
        Assert.Equal("Counter", componentType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
        Assert.True(semanticModel.GetSemanticDiagnostics(inject).IsEmpty);
    }

    [Fact]
    public void SemanticModel_CSharpStatementReferences_MapBackToAkburaSymbols()
    {
        const string code =
            "using Demo;\n" +
            "\n" +
            "inject ILogger<Counter> logger;\n" +
            "\n" +
            "state count = 0;\n" +
            "\n" +
            "logger.LogInformation(\"Counts is {0}\", count);";
        const string csharpCode =
            "namespace Demo\n" +
            "{\n" +
            "    public interface ILogger<T>\n" +
            "    {\n" +
            "        void LogInformation(string message, params object[] args);\n" +
            "    }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(csharpCode));
        var root = syntaxTree.GetRoot();
        var inject = Assert.IsType<InjectDeclarationSyntax>(root.Members[1]);
        var state = Assert.IsType<StateDeclarationSyntax>(root.Members[2]);
        var statement = Assert.IsType<CSharpStatementSyntax>(root.Members[3]);

        var references = semanticModel.GetCSharpSymbolReferences(statement);

        var loggerReference = Assert.Single(references, reference => reference.Name == "logger");
        var loggerLocal = Assert.IsAssignableFrom<ILocalSymbol>(loggerReference.CSharpDefinition.Symbol);
        var loggerSymbol = Assert.IsAssignableFrom<IInjectSymbol>(loggerReference.AkburaSymbol);
        Assert.Equal("logger", loggerLocal.Name);
        Assert.Same(semanticModel.GetSymbolInfo(inject).Symbol, loggerSymbol);
        var loggerType = Assert.IsAssignableFrom<INamedTypeSymbol>(loggerSymbol.Type.Symbol);
        Assert.Equal("Counter", loggerType.TypeArguments.Single().Name);

        var countReference = Assert.Single(references, reference => reference.Name == "count");
        var countLocal = Assert.IsAssignableFrom<ILocalSymbol>(countReference.CSharpDefinition.Symbol);
        var countSymbol = Assert.IsAssignableFrom<IStateSymbol>(countReference.AkburaSymbol);
        Assert.Equal("count", countLocal.Name);
        Assert.Same(semanticModel.GetSymbolInfo(state).Symbol, countSymbol);
        Assert.Equal("Int32", countSymbol.Type.Name);

        var methodReference = Assert.Single(references, reference => reference.Name == "LogInformation");
        Assert.IsAssignableFrom<IMethodSymbol>(methodReference.CSharpDefinition.Symbol);
        Assert.Null(methodReference.AkburaSymbol);
    }

    [Fact]
    public void SemanticModel_CSharpStatementReferences_MapCommandFacadeMembersBackToCommandSymbol()
    {
        const string code =
            "command bool IsHi();\n" +
            "\n" +
            "var result = IsHi.Execute();\n" +
            "var canExecute = IsHi.CanExecute;\n" +
            "var isExecuting = IsHi.IsExecuting;";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var root = syntaxTree.GetRoot();
        var command = Assert.IsType<CommandDeclarationSyntax>(root.Members[0]);
        var commandSymbol = Assert.IsAssignableFrom<ICommandSymbol>(
            semanticModel.GetSymbolInfo(command).Symbol);

        var references = root.Members
            .OfType<CSharpStatementSyntax>()
            .SelectMany(statement => semanticModel.GetCSharpSymbolReferences(statement))
            .ToArray();

        var commandReceivers = references.Where(reference => reference.Name == "IsHi").ToArray();
        Assert.Equal(3, commandReceivers.Length);
        Assert.All(commandReceivers, reference =>
        {
            Assert.Same(commandSymbol, reference.AkburaSymbol);
            Assert.True(reference.CSharpDefinition.Symbol is IFieldSymbol);
        });

        var execute = Assert.Single(references, reference => reference.Name == "Execute");
        Assert.IsAssignableFrom<IMethodSymbol>(execute.CSharpDefinition.Symbol);
        Assert.Same(commandSymbol, execute.AkburaSymbol);

        var canExecute = Assert.Single(references, reference => reference.Name == "CanExecute");
        var canExecuteProperty = Assert.IsAssignableFrom<Microsoft.CodeAnalysis.IPropertySymbol>(
            canExecute.CSharpDefinition.Symbol);
        Assert.Equal("IObservable", canExecuteProperty.Type.Name);
        Assert.Same(commandSymbol, canExecute.AkburaSymbol);

        var isExecuting = Assert.Single(references, reference => reference.Name == "IsExecuting");
        var isExecutingProperty = Assert.IsAssignableFrom<Microsoft.CodeAnalysis.IPropertySymbol>(
            isExecuting.CSharpDefinition.Symbol);
        Assert.Equal("IObservable", isExecutingProperty.Type.Name);
        Assert.Same(commandSymbol, isExecuting.AkburaSymbol);
    }

    [Fact]
    public void SemanticModel_ResolvesCommandSymbol()
    {
        const string code = "command int Click(int a);";

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
        Assert.Equal("Int32", symbol.ReturnType.Name);
        Assert.Equal("Int32", symbol.ResultType.Name);
        Assert.Same(command, symbol.DeclarationSyntax);

        var parameter = Assert.Single(symbol.Parameters);
        Assert.Equal(0, parameter.Ordinal);
        Assert.Equal("a", parameter.Name);
        Assert.Equal("Int32", parameter.Type.Name);
        Assert.Equal("Int32 a", parameter.ToDisplayString());
        Assert.Equal("command Int32 Click", symbol.ToDisplayString());
        Assert.True(semanticModel.GetSemanticDiagnostics(command).IsEmpty);

        var cachedSymbolInfo = semanticModel.GetSymbolInfo(command);
        Assert.Same(symbol, cachedSymbolInfo.Symbol);
    }

    [Fact]
    public void SemanticModel_ResolvesUseEffectSymbol()
    {
        const string code =
            "param int UserId = 1;\n" +
            "state int count = 0;\n" +
            "inject IService service;\n" +
            "command void Refresh(int userId);\n" +
            "\n" +
            "useEffect(UserId, count, service.Value, Refresh.IsExecuting) {\n" +
            "    System.Console.WriteLine(count);\n" +
            "} cancel {\n" +
            "    System.Console.WriteLine(\"cancel\");\n" +
            "} finally {\n" +
            "    System.Console.WriteLine(\"finally\");\n" +
            "}";
        const string csharpCode =
            "public interface IService\n" +
            "{\n" +
            "    int Value { get; }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(csharpCode));
        var root = syntaxTree.GetRoot();
        var useEffect = Assert.IsType<UseEffectDeclarationSyntax>(root.Members[4]);

        var symbolInfo = semanticModel.GetSymbolInfo(useEffect);

        var symbol = Assert.IsAssignableFrom<IUseEffectSymbol>(symbolInfo.Symbol);
        Assert.Equal(AkburaCandidateReason.None, symbolInfo.CandidateReason);
        Assert.Equal(AkburaSymbolKind.UseEffect, symbol.Kind);
        Assert.Equal(SymbolLanguage.Akbura, symbol.Language);
        Assert.Equal("useEffect", symbol.Name);
        Assert.Same(useEffect, symbol.DeclarationSyntax);
        Assert.Same(useEffect.Arguments, symbol.ArgumentsSyntax);
        Assert.Same(useEffect.Body, symbol.Body);
        Assert.True(symbol.HasCancelBlock);
        Assert.True(symbol.HasFinallyBlock);
        Assert.NotNull(symbol.CancelBlock);
        Assert.NotNull(symbol.FinallyBlock);

        Assert.Equal(
            ["UserId", "count", "service.Value", "Refresh.IsExecuting"],
            symbol.Dependencies.Select(static dependency => dependency.ExpressionText));
        Assert.IsAssignableFrom<IParamSymbol>(symbol.Dependencies[0].AkburaSymbol);
        Assert.IsAssignableFrom<IStateSymbol>(symbol.Dependencies[1].AkburaSymbol);
        Assert.IsAssignableFrom<IInjectSymbol>(symbol.Dependencies[2].AkburaSymbol);
        Assert.IsAssignableFrom<ICommandSymbol>(symbol.Dependencies[3].AkburaSymbol);
        Assert.Equal("UserId", symbol.Dependencies[0].CSharpDefinition.Name);
        Assert.Equal("count", symbol.Dependencies[1].CSharpDefinition.Name);
        Assert.Equal("Value", symbol.Dependencies[2].CSharpDefinition.Name);
        Assert.True(symbol.Dependencies[3].IsResolved);
        Assert.Equal("useEffect(UserId, count, service.Value, Refresh.IsExecuting)", symbol.ToDisplayString());
        Assert.True(semanticModel.GetSemanticDiagnostics(useEffect).IsEmpty);

        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            semanticModel.GetSymbolInfo(root).Symbol);
        Assert.Same(symbol, Assert.Single(component.UseEffects));

        var cachedSymbolInfo = semanticModel.GetSymbolInfo(useEffect);
        Assert.Same(symbol, cachedSymbolInfo.Symbol);
    }

    [Fact]
    public void SemanticModel_ResolvesAkcssStyleSymbols()
    {
        const string code =
            "@akcss {\n" +
            "    .hello {\n" +
            "        Background: \"Red\";\n" +
            "    }\n" +
            "\n" +
            "    Button.hello {\n" +
            "        Background: \"Blue\";\n" +
            "    }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var inlineAkcss = Assert.IsType<InlineAkcssBlockSyntax>(syntaxTree.GetRoot().Members.Single());
        var globalRule = Assert.IsType<AkcssStyleRuleSyntax>(inlineAkcss.Members[0]);
        var buttonRule = Assert.IsType<AkcssStyleRuleSyntax>(inlineAkcss.Members[1]);

        var globalSymbolInfo = semanticModel.GetSymbolInfo(globalRule);
        var globalSymbol = Assert.IsType<AkcssStyleSymbol>(globalSymbolInfo.Symbol);
        Assert.IsAssignableFrom<IAkcssSymbol>(globalSymbol);
        Assert.Equal(AkburaCandidateReason.None, globalSymbolInfo.CandidateReason);
        Assert.Equal(AkburaSymbolKind.AkcssClass, globalSymbol.Kind);
        Assert.Equal(SymbolLanguage.Akcss, globalSymbol.Language);
        Assert.Equal("hello", globalSymbol.Name);
        Assert.Equal("hello", globalSymbol.MetadataName);
        Assert.False(globalSymbol.HasTargetType);
        Assert.True(globalSymbol.TargetType.IsDefault);
        Assert.Same(globalRule, globalSymbol.DeclarationSyntax);
        Assert.Equal("style hello", globalSymbol.ToDisplayString());
        Assert.True(semanticModel.GetSemanticDiagnostics(globalRule).IsEmpty);

        var buttonSymbolInfo = semanticModel.GetSymbolInfo(buttonRule);
        var buttonSymbol = Assert.IsType<AkcssStyleSymbol>(buttonSymbolInfo.Symbol);
        Assert.Equal(AkburaCandidateReason.None, buttonSymbolInfo.CandidateReason);
        Assert.True(buttonSymbol.HasTargetType);
        Assert.Equal("Button", buttonSymbol.TargetType.Name);
        Assert.Equal("Button.hello", buttonSymbol.MetadataName);
        Assert.Equal("style Button.hello", buttonSymbol.ToDisplayString());
        AssertResolvedAvaloniaButton(semanticModel, buttonSymbol.TargetType);

        var cachedSymbolInfo = semanticModel.GetSymbolInfo(buttonRule);
        Assert.Same(buttonSymbol, cachedSymbolInfo.Symbol);
    }

    [Fact]
    public void SemanticModel_ResolvesInlineAkcssModuleSymbol()
    {
        const string code =
            "namespace Demo;\n" +
            "\n" +
            "@akcss {\n" +
            "    .hello {\n" +
            "        Background: \"Red\";\n" +
            "    }\n" +
            "\n" +
            "    @utilities {\n" +
            "        .w-(double value) {\n" +
            "            Width: value;\n" +
            "        }\n" +
            "    }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var semanticModel = CreateSemanticModel(syntaxTree);
        var root = syntaxTree.GetRoot();
        var inlineAkcss = Assert.IsType<InlineAkcssBlockSyntax>(
            root.Members.Single(member => member is InlineAkcssBlockSyntax));

        var moduleInfo = semanticModel.GetSymbolInfo(inlineAkcss);

        var module = Assert.IsAssignableFrom<IAkcssModuleSymbol>(moduleInfo.Symbol);
        Assert.Equal(AkburaCandidateReason.None, moduleInfo.CandidateReason);
        Assert.Equal(AkburaSymbolKind.AkcssModule, module.Kind);
        Assert.True(module.IsInlined);
        Assert.Null(module.Path);
        Assert.Same(inlineAkcss, module.DeclaringSyntax);
        Assert.Equal(2, module.AkcssSymbols.Length);
        Assert.Contains(module.AkcssSymbols, symbol => symbol is AkcssStyleSymbol { Name: "hello" });
        Assert.Contains(module.AkcssSymbols, symbol => symbol is ITailwindUtilitySymbol { Name: "w" });

        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            semanticModel.GetSymbolInfo(root).Symbol);
        var componentModule = Assert.Single(component.AkcssModules);
        Assert.Same(module, componentModule);
        Assert.Same(component, module.ContainingSymbol);
    }

    [Fact]
    public void SemanticModel_AkcssStyleTargetType_MustBeAvaloniaControl()
    {
        const string code =
            "@akcss {\n" +
            "    PlainObject.hello {\n" +
            "        Background: \"Red\";\n" +
            "    }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(
            syntaxTree,
            CreateCSharpCompilation("public sealed class PlainObject { }"));
        var inlineAkcss = Assert.IsType<InlineAkcssBlockSyntax>(syntaxTree.GetRoot().Members.Single());
        var rule = Assert.IsType<AkcssStyleRuleSyntax>(Assert.Single(inlineAkcss.Members));

        var symbolInfo = semanticModel.GetSymbolInfo(rule);

        Assert.Null(symbolInfo.Symbol);
        Assert.Equal(AkburaCandidateReason.NotFound, symbolInfo.CandidateReason);
        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(rule));
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_AkcssSelectorTargetNotFound, diagnostic.Code);
    }

    [Fact]
    public void SemanticModel_ResolvesTailwindUtilitySymbols()
    {
        const string code =
            "@akcss {\n" +
            "    @utilities {\n" +
            "        .w-(double value) {\n" +
            "            Width: value;\n" +
            "        }\n" +
            "\n" +
            "        Button.btn-(string variant) {\n" +
            "            Width: 10;\n" +
            "        }\n" +
            "    }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var inlineAkcss = Assert.IsType<InlineAkcssBlockSyntax>(syntaxTree.GetRoot().Members.Single());
        var utilities = Assert.IsType<AkcssUtilitiesSectionSyntax>(Assert.Single(inlineAkcss.Members));

        var widthUtilityInfo = semanticModel.GetSymbolInfo(utilities.Utilities[0]);
        var widthUtility = Assert.IsAssignableFrom<ITailwindUtilitySymbol>(widthUtilityInfo.Symbol);
        Assert.IsAssignableFrom<IAkcssSymbol>(widthUtility);
        Assert.Equal(AkburaCandidateReason.None, widthUtilityInfo.CandidateReason);
        Assert.Equal(AkburaSymbolKind.AkcssUtility, widthUtility.Kind);
        Assert.Equal(SymbolLanguage.Akcss, widthUtility.Language);
        Assert.Equal("w", widthUtility.Name);
        Assert.Equal("w", widthUtility.MetadataName);
        Assert.False(widthUtility.HasTargetType);
        Assert.True(widthUtility.TargetType.IsDefault);
        Assert.Same(utilities.Utilities[0], widthUtility.DeclarationSyntax);
        var widthParameter = Assert.Single(widthUtility.Parameters);
        Assert.Equal("value", widthParameter.Name);
        Assert.Equal(0, widthParameter.Ordinal);
        Assert.Equal(SymbolLanguage.Akcss, widthParameter.Language);
        Assert.Equal("Double", widthParameter.Type.Name);
        Assert.NotNull(widthParameter.CSharpParameter);
        Assert.Equal("Double value", widthParameter.ToDisplayString());
        Assert.Equal("utility w/1", widthUtility.ToDisplayString());
        Assert.True(semanticModel.GetSemanticDiagnostics(utilities.Utilities[0]).IsEmpty);

        var buttonUtility = Assert.IsAssignableFrom<ITailwindUtilitySymbol>(
            semanticModel.GetSymbolInfo(utilities.Utilities[1]).Symbol);
        Assert.Equal("btn", buttonUtility.Name);
        Assert.True(buttonUtility.HasTargetType);
        Assert.Equal("Button", buttonUtility.TargetType.Name);
        AssertResolvedAvaloniaButton(semanticModel, buttonUtility.TargetType);
        Assert.Equal("Button.btn", buttonUtility.MetadataName);
        var variantParameter = Assert.Single(buttonUtility.Parameters);
        Assert.Equal("variant", variantParameter.Name);
        Assert.Equal("String", variantParameter.Type.Name);
        Assert.NotNull(variantParameter.CSharpParameter);
    }

    [Fact]
    public void SemanticModel_AkcssDefaultStyle_BackgroundWhite_CreatesColorSetterOperation()
    {
        const string code =
            "@akcss {\n" +
            "    .hello {\n" +
            "        Background: White;\n" +
            "    }\n" +
            "}";

        var (semanticModel, assignment, operation, symbol) = GetSingleStyleOperation(code);

        Assert.False(symbol.HasTargetType);
        Assert.Equal("Background", operation.Property?.Name);
        Assert.Equal(SymbolLanguage.Akcss, operation.Property?.Language);
        Assert.True(operation.Property?.IsAvaloniaProperty);
        Assert.Equal("BackgroundProperty", operation.Property?.AvaloniaPropertyDefinition.Name);
        Assert.Equal(AkcssPropertyValueKind.ColorLiteral, operation.ValueKind);
        Assert.True(operation.RequiresBrushConversion);
        Assert.Equal("Color", operation.ValueType.Name);
        var colorSymbol = Assert.IsType<CSharpSymbolDefinition>(operation.ConvertedValue);
        Assert.Equal("White", colorSymbol.Name);
        AssertAvaloniaColorsProperty(colorSymbol, "White");
        Assert.False(operation.ValueOperation.IsDefault);
        Assert.False(operation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(assignment).IsEmpty);
    }

    [Fact]
    public void SemanticModel_AkcssButtonStyle_BackgroundWhite_ResolvesTargetAndProperty()
    {
        const string code =
            "@akcss {\n" +
            "    Button.hello {\n" +
            "        Background: White;\n" +
            "    }\n" +
            "}";

        var (semanticModel, _, operation, symbol) = GetSingleStyleOperation(code);

        Assert.True(symbol.HasTargetType);
        AssertResolvedAvaloniaButton(semanticModel, symbol.TargetType);
        Assert.Equal("Background", operation.Property?.Name);
        Assert.Equal("BackgroundProperty", operation.Property?.AvaloniaPropertyDefinition.Name);
        Assert.Equal(AkcssPropertyValueKind.ColorLiteral, operation.ValueKind);
        var colorSymbol = Assert.IsType<CSharpSymbolDefinition>(operation.ConvertedValue);
        AssertAvaloniaColorsProperty(colorSymbol, "White");
        Assert.False(operation.HasErrors);
    }

    [Fact]
    public void SemanticModel_AkcssUsing_ResolvesSelectorAndQualifiedProperty()
    {
        const string code =
            "@akcss {\n" +
            "    @using MyComponents;\n" +
            "    @using MyNs;\n" +
            "\n" +
            "    MyComponent.multiClass {\n" +
            "        MyClass.MyProperty: Expression.Hello;\n" +
            "    }\n" +
            "}";
        const string csharpCode =
            "namespace MyComponents\n" +
            "{\n" +
            "    public sealed class MyComponent : Avalonia.Controls.Control { }\n" +
            "}\n" +
            "\n" +
            "namespace MyNs\n" +
            "{\n" +
            "    public static class MyClass\n" +
            "    {\n" +
            "        public static readonly Avalonia.StyledProperty<double> MyProperty = null!;\n" +
            "    }\n" +
            "    public static class Expression\n" +
            "    {\n" +
            "        public static double Hello => 1;\n" +
            "    }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(csharpCode));
        var rule = GetOnlyAkcssStyleRule(syntaxTree);
        var symbol = Assert.IsAssignableFrom<IAkcssSymbol>(semanticModel.GetSymbolInfo(rule).Symbol);
        var operation = Assert.IsAssignableFrom<IAkcssPropertySetterOperation>(
            Assert.Single(symbol.Operations));

        Assert.True(symbol.HasTargetType);
        Assert.Equal("multiClass", symbol.ClassName);
        Assert.Equal("global::MyComponents.MyComponent",
            symbol.TargetType.Symbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        Assert.Equal("MyProperty", operation.Property?.Name);
        Assert.Equal("MyProperty", operation.Property?.AvaloniaPropertyDefinition.Name);
        Assert.Equal("Double", operation.ValueType.Name);
        Assert.False(operation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(operation.Syntax).IsEmpty);
    }

    [Fact]
    public void SemanticModel_AkcssParenthesizedSelectors_ResolveTypedAndTypedClassStyles()
    {
        const string code =
            "@akcss {\n" +
            "    (global::MyComponents.MyComponent) {\n" +
            "    }\n" +
            "\n" +
            "    (global::MyComponents.MyComponent).withClass {\n" +
            "    }\n" +
            "}";
        const string csharpCode =
            "namespace MyComponents;\n" +
            "public sealed class MyComponent : Avalonia.Controls.Control { }";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(csharpCode));
        var inlineAkcss = Assert.IsType<InlineAkcssBlockSyntax>(syntaxTree.GetRoot().Members.Single());
        var rules = inlineAkcss.Members.OfType<AkcssStyleRuleSyntax>().ToArray();

        var typedOnly = Assert.IsAssignableFrom<IAkcssSymbol>(
            semanticModel.GetSymbolInfo(rules[0]).Symbol);
        var typedWithClass = Assert.IsAssignableFrom<IAkcssSymbol>(
            semanticModel.GetSymbolInfo(rules[1]).Symbol);

        Assert.True(typedOnly.HasTargetType);
        Assert.Null(typedOnly.ClassName);
        Assert.Equal("MyComponent", typedOnly.Name);
        Assert.True(typedWithClass.HasTargetType);
        Assert.Equal("withClass", typedWithClass.ClassName);
        Assert.Equal("MyComponent.withClass", typedWithClass.MetadataName);
    }

    [Fact]
    public void SemanticModel_AkcssApply_ResolvesLocalThenImportedStylesAndUtilities()
    {
        const string code =
            "@akcss {\n" +
            "    @using Shared.Styles.akcss;\n" +
            "\n" +
            "    .local {\n" +
            "    }\n" +
            "\n" +
            "    .multi {\n" +
            "        @apply local imported w-5;\n" +
            "    }\n" +
            "}";
        const string importedAkcss =
            ".imported {\n" +
            "}\n" +
            "\n" +
            "@utilities {\n" +
            "    .w-(double value) { Width: value; }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var akcssTree = AkcssSyntaxTree.ParseText(
            importedAkcss,
            "Styles.akcss",
            "Shared.Styles.akcss");
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(), [akcssTree]);
        var inlineAkcss = Assert.IsType<InlineAkcssBlockSyntax>(syntaxTree.GetRoot().Members.Single());
        var multiRule = inlineAkcss.Members
            .OfType<AkcssStyleRuleSyntax>()
            .Single(rule => rule.Selector.Name?.Identifier.ValueText == "multi");
        var symbol = Assert.IsAssignableFrom<IAkcssSymbol>(semanticModel.GetSymbolInfo(multiRule).Symbol);
        var apply = Assert.IsAssignableFrom<IAkcssApplyOperation>(Assert.Single(symbol.Operations));

        Assert.Equal(["local", "imported", "w-5"], apply.Items.AsEnumerable());
        Assert.Equal(3, apply.AppliedSymbols.Length);
        Assert.Equal("local", apply.AppliedSymbols[0].ClassName);
        Assert.Equal("imported", apply.AppliedSymbols[1].ClassName);
        Assert.IsAssignableFrom<ITailwindUtilitySymbol>(apply.AppliedSymbols[2]);
        Assert.False(apply.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(apply.Syntax).IsEmpty);
    }

    [Fact]
    public void SemanticModel_AkcssApply_DuplicateSameLayerCandidatesProduceDiagnostic()
    {
        const string code =
            "@akcss {\n" +
            "    .dup { }\n" +
            "    .dup { }\n" +
            "    .target { @apply dup; }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var inlineAkcss = Assert.IsType<InlineAkcssBlockSyntax>(syntaxTree.GetRoot().Members.Single());
        var targetRule = inlineAkcss.Members
            .OfType<AkcssStyleRuleSyntax>()
            .Single(rule => rule.Selector.Name?.Identifier.ValueText == "target");
        var symbol = Assert.IsAssignableFrom<IAkcssSymbol>(semanticModel.GetSymbolInfo(targetRule).Symbol);
        var apply = Assert.IsAssignableFrom<IAkcssApplyOperation>(Assert.Single(symbol.Operations));
        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(apply.Syntax));

        Assert.True(apply.HasErrors);
        Assert.Empty(apply.AppliedSymbols);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_AkcssApplyItemAmbiguous, diagnostic.Code);
    }

    [Fact]
    public void SemanticModel_AkcssIntercept_ResolvesAkcssStyleSubtype()
    {
        const string code =
            "@akcss {\n" +
            "    @using MyStyles;\n" +
            "\n" +
            "    .myVeryComplexClass {\n" +
            "        @intercept MyVeryComplexClass;\n" +
            "    }\n" +
            "}";
        const string csharpCode =
            "namespace MyStyles;\n" +
            "public sealed class MyVeryComplexClass : Akbura.Akcss.AkcssClass\n" +
            "{\n" +
            "    public override void Update(object control) { }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(csharpCode));
        var rule = GetOnlyAkcssStyleRule(syntaxTree);
        var symbol = Assert.IsAssignableFrom<IAkcssSymbol>(semanticModel.GetSymbolInfo(rule).Symbol);
        var operation = Assert.IsAssignableFrom<IAkcssInterceptOperation>(Assert.Single(symbol.Operations));

        Assert.True(symbol.IsIntercepted);
        Assert.Equal("global::MyStyles.MyVeryComplexClass",
            symbol.InterceptType.Symbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        Assert.Same(symbol.InterceptType.Symbol, operation.InterceptType.Symbol);
        Assert.False(operation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(operation.Syntax).IsEmpty);
    }

    [Fact]
    public void SemanticModel_AkcssIntercept_IgnoresOtherStyleMembersWithWarnings()
    {
        const string code =
            "@akcss {\n" +
            "    @using MyStyles;\n" +
            "\n" +
            "    .myVeryComplexClass {\n" +
            "        Background: White;\n" +
            "        @apply other;\n" +
            "        @if(true) {\n" +
            "            Opacity: 1;\n" +
            "        }\n" +
            "        @intercept MyVeryComplexClass;\n" +
            "    }\n" +
            "\n" +
            "    .other { Opacity: 1; }\n" +
            "}";
        const string csharpCode =
            "namespace MyStyles;\n" +
            "public sealed class MyVeryComplexClass : Akbura.Akcss.AkcssClass\n" +
            "{\n" +
            "    public override void Update(object control) { }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(csharpCode));
        var rule = GetOnlyAkcssStyleRule(syntaxTree, "myVeryComplexClass");
        var symbol = Assert.IsAssignableFrom<IAkcssSymbol>(semanticModel.GetSymbolInfo(rule).Symbol);
        var intercept = Assert.IsAssignableFrom<IAkcssInterceptOperation>(Assert.Single(symbol.Operations));

        Assert.True(symbol.IsIntercepted);
        Assert.False(intercept.HasErrors);

        var ignoredMembers = rule.Members
            .Where(static member => member is not AkcssInterceptDirectiveSyntax)
            .ToArray();
        Assert.Equal(3, ignoredMembers.Length);
        foreach (var ignoredMember in ignoredMembers)
        {
            var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(ignoredMember));
            Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_AkcssInterceptIgnoresMember, diagnostic.Code);
            Assert.Equal(AkburaDiagnosticSeverity.Warning, diagnostic.Severity);
            Assert.Null(semanticModel.GetOperation(ignoredMember));
        }
    }

    [Fact]
    public void SemanticModel_AkcssUtilityIntercept_RequiresAkcssUtilitySubtype()
    {
        const string code =
            "@akcss {\n" +
            "    @using MyStyles;\n" +
            "\n" +
            "    @utilities {\n" +
            "        .w-(double value) {\n" +
            "            Width: value;\n" +
            "            @intercept WidthUtility;\n" +
            "        }\n" +
            "    }\n" +
            "}";
        const string csharpCode =
            "namespace MyStyles;\n" +
            "public abstract class WidthUtility : Akbura.Akcss.AkcssUtility<double> { }";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(csharpCode));
        var utility = GetOnlyAkcssUtility(syntaxTree);
        var symbol = Assert.IsAssignableFrom<ITailwindUtilitySymbol>(
            semanticModel.GetSymbolInfo(utility).Symbol);
        var intercept = Assert.IsAssignableFrom<IAkcssInterceptOperation>(Assert.Single(symbol.Operations));
        var ignoredMember = Assert.IsType<AkcssAssignmentSyntax>(
            Assert.Single(utility.Members, static member => member is AkcssAssignmentSyntax));
        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(ignoredMember));

        Assert.True(symbol.IsIntercepted);
        Assert.Equal("global::MyStyles.WidthUtility",
            symbol.InterceptType.Symbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        Assert.Same(symbol.InterceptType.Symbol, intercept.InterceptType.Symbol);
        Assert.False(intercept.HasErrors);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_AkcssInterceptIgnoresMember, diagnostic.Code);
    }

    [Fact]
    public void SemanticModel_AkcssIntercept_InvalidTypeProducesDiagnostic()
    {
        const string code =
            "@akcss {\n" +
            "    .myVeryComplexClass {\n" +
            "        @intercept global::MyStyles.NotAStyle;\n" +
            "    }\n" +
            "}";
        const string csharpCode =
            "namespace MyStyles;\n" +
            "public sealed class NotAStyle { }";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(csharpCode));
        var rule = GetOnlyAkcssStyleRule(syntaxTree);
        var symbol = Assert.IsAssignableFrom<IAkcssSymbol>(semanticModel.GetSymbolInfo(rule).Symbol);
        var operation = Assert.IsAssignableFrom<IAkcssInterceptOperation>(Assert.Single(symbol.Operations));
        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(operation.Syntax));

        Assert.False(symbol.IsIntercepted);
        Assert.True(operation.HasErrors);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_AkcssInterceptTypeInvalid, diagnostic.Code);
    }

    [Fact]
    public void SemanticModel_AkcssQualifiedProperty_MissingOwnerPropertyProducesDiagnostic()
    {
        const string code =
            "@akcss {\n" +
            "    .hello {\n" +
            "        global::MyNs.MyClass.MissingProperty: 1;\n" +
            "    }\n" +
            "}";
        const string csharpCode =
            "namespace MyNs;\n" +
            "public static class MyClass { }";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(csharpCode));
        var rule = GetOnlyAkcssStyleRule(syntaxTree);
        var symbol = Assert.IsAssignableFrom<IAkcssSymbol>(semanticModel.GetSymbolInfo(rule).Symbol);
        var operation = Assert.IsAssignableFrom<IAkcssPropertySetterOperation>(Assert.Single(symbol.Operations));
        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(operation.Syntax));

        Assert.Null(operation.Property);
        Assert.True(operation.HasErrors);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_AkcssPropertyNotFound, diagnostic.Code);
    }

    [Fact]
    public void SemanticModel_GetAkcssOperationBeforeSymbolInfo_ReusesSymbolOperation()
    {
        const string code =
            "@akcss {\n" +
            "    .hello {\n" +
            "        Background: White;\n" +
            "    }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var rule = GetOnlyAkcssStyleRule(syntaxTree);
        var assignment = Assert.IsType<AkcssAssignmentSyntax>(Assert.Single(rule.Members));

        var operation = Assert.IsAssignableFrom<IAkcssPropertySetterOperation>(
            semanticModel.GetOperation(assignment));
        var symbol = Assert.IsAssignableFrom<IAkcssSymbol>(
            semanticModel.GetSymbolInfo(rule).Symbol);

        Assert.Same(operation, Assert.Single(symbol.Operations));
    }

    [Fact]
    public void SemanticModel_AkcssColorStringLiteral_ParsesColorValue()
    {
        const string code =
            "@akcss {\n" +
            "    .hello {\n" +
            "        Background: \"#FFAA\";\n" +
            "    }\n" +
            "}";

        var (semanticModel, _, operation, _) = GetSingleStyleOperation(code);

        Assert.Equal(AkcssPropertyValueKind.ColorLiteral, operation.ValueKind);
        var color = Assert.IsType<AkcssColorValue>(operation.ConvertedValue);
        Assert.Equal(0xFFFFAAAAu, color.ToUInt32());
        Assert.False(operation.HasErrors);
    }

    [Fact]
    public void SemanticModel_AkcssNamedColorStringLiteral_ResolvesAvaloniaColorsSymbol()
    {
        const string code =
            "@akcss {\n" +
            "    .hello {\n" +
            "        Background: \"DodgerBlue\";\n" +
            "    }\n" +
            "}";

        var (semanticModel, _, operation, _) = GetSingleStyleOperation(code);

        Assert.Equal(AkcssPropertyValueKind.ColorLiteral, operation.ValueKind);
        var colorSymbol = Assert.IsType<CSharpSymbolDefinition>(operation.ConvertedValue);
        AssertAvaloniaColorsProperty(colorSymbol, "DodgerBlue");
        Assert.False(operation.ValueOperation.IsDefault);
        Assert.False(operation.HasErrors);
    }

    [Fact]
    public void SemanticModel_AkcssColorCSharpExpression_BindsThroughImplicitAvaloniaMediaUsing()
    {
        const string code =
            "@akcss {\n" +
            "    .hello {\n" +
            "        Foreground: Color.FromRgb(33, 11, 231);\n" +
            "    }\n" +
            "}";

        var (_, _, operation, _) = GetSingleStyleOperation(code);

        Assert.Equal(AkcssPropertyValueKind.CSharpExpression, operation.ValueKind);
        Assert.True(operation.RequiresBrushConversion);
        Assert.Equal("Color", operation.ValueType.Name);
        Assert.False(operation.ValueOperation.IsDefault);
        Assert.False(operation.HasErrors);
    }

    [Fact]
    public void SemanticModel_AkcssExpectedTypeStaticMember_BindsBareFontWeight()
    {
        const string code =
            """
            @akcss {
                TextBlock.hello {
                    FontWeight: Bold;
                }
            }
            """;

        var (semanticModel, _, operation, _) = GetSingleStyleOperation(code);

        Assert.Equal("FontWeight", operation.Property?.Name);
        Assert.Equal("FontWeight", operation.ValueType.Name);
        Assert.False(operation.ValueOperation.IsDefault);
        var member = Assert.IsType<CSharpSymbolDefinition>(operation.ConvertedValue);
        Assert.Equal("Bold", member.Name);
        Assert.False(operation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(operation.Syntax).IsEmpty);
    }

    [Fact]
    public void SemanticModel_AkcssEnumValues_BindQualifiedAndCastExpressions()
    {
        const string code =
            """
            @akcss {
                TextBlock.hello {
                    FontWeight: FontWeight.Bold;
                    FontWeight: (FontWeight)700;
                }
            }
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var rule = GetOnlyAkcssStyleRule(syntaxTree);
        var symbol = Assert.IsAssignableFrom<IAkcssSymbol>(semanticModel.GetSymbolInfo(rule).Symbol);
        var operations = symbol.Operations
            .Cast<IAkcssPropertySetterOperation>()
            .ToArray();

        Assert.Equal(2, operations.Length);
        Assert.All(operations, operation =>
        {
            Assert.Equal("FontWeight", operation.Property?.Name);
            Assert.Equal("FontWeight", operation.ValueType.Name);
            Assert.False(operation.ValueOperation.IsDefault);
            Assert.False(operation.HasErrors);
        });
    }

    [Fact]
    public void SemanticModel_AkcssEnumValues_BindAvaloniaAlignmentProperties()
    {
        const string code =
            """
            @akcss {
                Button.hello {
                    HorizontalAlignment: Center;
                    HorizontalAlignment: HorizontalAlignment.Right;
                    VerticalAlignment: (VerticalAlignment)2;
                }
            }
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var rule = GetOnlyAkcssStyleRule(syntaxTree);
        var symbol = Assert.IsAssignableFrom<IAkcssSymbol>(semanticModel.GetSymbolInfo(rule).Symbol);
        var operations = symbol.Operations
            .Cast<IAkcssPropertySetterOperation>()
            .ToArray();

        Assert.Equal(3, operations.Length);
        Assert.Equal("HorizontalAlignment", operations[0].Property?.Name);
        Assert.Equal("HorizontalAlignment", operations[0].ValueType.Name);
        var horizontalMember = Assert.IsType<CSharpSymbolDefinition>(operations[0].ConvertedValue);
        Assert.Equal("Center", horizontalMember.Name);

        Assert.Equal("HorizontalAlignment", operations[1].Property?.Name);
        Assert.Equal("HorizontalAlignment", operations[1].ValueType.Name);

        Assert.Equal("VerticalAlignment", operations[2].Property?.Name);
        Assert.Equal("VerticalAlignment", operations[2].ValueType.Name);

        Assert.All(operations, operation =>
        {
            Assert.False(operation.ValueOperation.IsDefault);
            Assert.False(operation.HasErrors);
        });
    }

    [Fact]
    public void SemanticModel_AkcssEnumValues_BindCustomEnumProperties()
    {
        const string code =
            """
            @akcss {
                @using MyControls;

                MyControl.hello {
                    Density: Compact;
                    Density: MyControls.MyEnums.Density.Comfortable;
                    Density: (global::MyControls.MyEnums.Density)1;
                }
            }
            """;
        const string csharpCode =
            """
            namespace MyControls
            {
                public sealed class MyControl : Avalonia.Controls.Control
                {
                    public static readonly Avalonia.StyledProperty<MyEnums.Density> DensityProperty = null!;
                }
            }

            namespace MyControls.MyEnums
            {
                public enum Density
                {
                    Compact,
                    Comfortable
                }
            }
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(csharpCode));
        var rule = GetOnlyAkcssStyleRule(syntaxTree);
        var symbol = Assert.IsAssignableFrom<IAkcssSymbol>(semanticModel.GetSymbolInfo(rule).Symbol);
        var operations = symbol.Operations
            .Cast<IAkcssPropertySetterOperation>()
            .ToArray();

        Assert.Equal(3, operations.Length);
        var member = Assert.IsType<CSharpSymbolDefinition>(operations[0].ConvertedValue);
        Assert.Equal("Compact", member.Name);
        Assert.Equal(AkcssPropertyValueKind.CSharpExpression, operations[1].ValueKind);
        Assert.Equal(AkcssPropertyValueKind.CSharpExpression, operations[2].ValueKind);
        Assert.All(operations, operation =>
        {
            Assert.Equal("Density", operation.Property?.Name);
            Assert.Equal("Density", operation.ValueType.Name);
            Assert.False(operation.ValueOperation.IsDefault);
            Assert.False(operation.HasErrors);
        });
    }

    [Fact]
    public void SemanticModel_AkcssInvalidCSharpExpression_ProducesDiagnostic()
    {
        const string code =
            """
            @akcss {
                .myText {
                    Padding: FontWeight.Bold * NotExisting.Constant;
                }
            }
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var rule = GetOnlyAkcssStyleRule(syntaxTree);
        var symbol = Assert.IsAssignableFrom<IAkcssSymbol>(semanticModel.GetSymbolInfo(rule).Symbol);
        var operation = Assert.IsAssignableFrom<IAkcssPropertySetterOperation>(
            Assert.Single(symbol.Operations));
        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(operation.Syntax));

        Assert.Equal("Padding", operation.Property?.Name);
        Assert.True(operation.HasErrors);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_AkcssExpressionError, diagnostic.Code);
        Assert.Contains("NotExisting", diagnostic.Message);
    }

    [Fact]
    public void SemanticModel_AkcssThicknessTuples_CreateConvertedValues()
    {
        const string code =
            "@akcss {\n" +
            "    Button.hello {\n" +
            "        Padding: (10, 20);\n" +
            "        Margin: (10, 20, 30, 40);\n" +
            "        Padding: (top: 10, bottom: 30);\n" +
            "        Margin: (vertical: 5);\n" +
            "    }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var rule = GetOnlyAkcssStyleRule(syntaxTree);
        var symbol = Assert.IsAssignableFrom<IAkcssSymbol>(semanticModel.GetSymbolInfo(rule).Symbol);

        Assert.Equal(4, symbol.Operations.Length);
        AssertThickness(symbol.Operations[0], 10, 20, 10, 20);
        AssertThickness(symbol.Operations[1], 10, 20, 30, 40);
        AssertThickness(symbol.Operations[2], 0, 10, 0, 30);
        AssertThickness(symbol.Operations[3], 0, 5, 0, 5);
        Assert.All(rule.Members.OfType<AkcssAssignmentSyntax>(), assignment =>
            Assert.True(semanticModel.GetSemanticDiagnostics(assignment).IsEmpty));
    }

    [Fact]
    public void SemanticModel_AkcssThicknessTuple_AllowsRuntimeSideExpressions()
    {
        const string code =
            "@akcss {\n" +
            "    @utilities {\n" +
            "        .px-(double width) {\n" +
            "            Padding: (horizontal: Amx.DynamicResource<double>(\"--spacing\") * width);\n" +
            "        }\n" +
            "    }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var utility = GetOnlyAkcssUtility(syntaxTree);
        var symbol = Assert.IsAssignableFrom<ITailwindUtilitySymbol>(
            semanticModel.GetSymbolInfo(utility).Symbol);
        var operation = Assert.IsAssignableFrom<IAkcssPropertySetterOperation>(
            Assert.Single(symbol.Operations));

        Assert.Equal(AkcssPropertyValueKind.ThicknessTuple, operation.ValueKind);
        Assert.Equal("Thickness", operation.ValueType.Name);
        var value = Assert.IsType<AkcssThicknessExpressionValue>(operation.ConvertedValue);
        Assert.Equal("Amx.DynamicResource<double>(\"--spacing\") * width", value.Left.ToString());
        Assert.Equal("0", value.Top.ToString());
        Assert.Equal("Amx.DynamicResource<double>(\"--spacing\") * width", value.Right.ToString());
        Assert.Equal("0", value.Bottom.ToString());
        Assert.False(operation.HasErrors);
    }

    [Fact]
    public void SemanticModel_AkcssThicknessCSharpExpression_RemainsCSharpExpression()
    {
        const string code =
            "@akcss {\n" +
            "    Button.hello {\n" +
            "        Margin: new Thickness(10, 5) - new Thickness(3, 17);\n" +
            "    }\n" +
            "}";

        var (_, _, operation, _) = GetSingleStyleOperation(code);

        Assert.Equal(AkcssPropertyValueKind.CSharpExpression, operation.ValueKind);
        Assert.Equal("Thickness", operation.ValueType.Name);
        Assert.False(operation.ValueOperation.IsDefault);
        Assert.False(operation.HasErrors);
    }

    [Fact]
    public void SemanticModel_AkcssUtilityAssignment_ResolvesParameterAndDetectsAmxInvocation()
    {
        const string code =
            "@akcss {\n" +
            "    @utilities {\n" +
            "        .w-(double value) {\n" +
            "            Width: value * Amx.DynamicResource<double>(\"--spacing\");\n" +
            "        }\n" +
            "    }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var utility = GetOnlyAkcssUtility(syntaxTree);
        var symbol = Assert.IsAssignableFrom<ITailwindUtilitySymbol>(
            semanticModel.GetSymbolInfo(utility).Symbol);
        var operation = Assert.IsAssignableFrom<IAkcssPropertySetterOperation>(
            Assert.Single(symbol.Operations));

        Assert.Equal("value", Assert.Single(symbol.Parameters).Name);
        Assert.Equal("Width", operation.Property?.Name);
        Assert.Equal(AkcssPropertyValueKind.AmxInvocation, operation.ValueKind);
        Assert.Equal("Double", operation.ValueType.Name);
        var amx = Assert.IsType<AkcssAmxInvocationValue>(operation.ConvertedValue);
        Assert.Equal(AkcssAmxInvocationKind.DynamicResource, amx.Kind);
        Assert.Equal("Double", amx.TypeArgument.Name);
        Assert.Equal("\"--spacing\"", Assert.Single(amx.Arguments).ToString());
        Assert.False(operation.HasErrors);
    }

    [Fact]
    public void SemanticModel_AkcssIfDirective_CreatesNestedOperations()
    {
        const string code =
            "@akcss {\n" +
            "    .hello {\n" +
            "        @if(true) {\n" +
            "            Background: White;\n" +
            "        }\n" +
            "    }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var rule = GetOnlyAkcssStyleRule(syntaxTree);
        var ifDirective = Assert.IsType<AkcssIfDirectiveSyntax>(Assert.Single(rule.Members));
        var symbol = Assert.IsAssignableFrom<IAkcssSymbol>(semanticModel.GetSymbolInfo(rule).Symbol);
        var ifOperation = Assert.IsAssignableFrom<IAkcssIfOperation>(Assert.Single(symbol.Operations));

        Assert.Same(ifDirective, ifOperation.Syntax);
        Assert.Same(ifOperation, semanticModel.GetOperation(ifDirective));
        Assert.Equal(AkcssPropertyValueKind.ColorLiteral,
            Assert.IsAssignableFrom<IAkcssPropertySetterOperation>(Assert.Single(ifOperation.Operations)).ValueKind);
        Assert.Equal("Boolean", ifOperation.ConditionType.Name);
        Assert.False(ifOperation.ConditionOperation.IsDefault);
        Assert.Single(ifOperation.Children);
        Assert.False(ifOperation.HasErrors);
        Assert.Equal("@if(true)", ifOperation.ToDisplayString());
    }

    [Fact]
    public void SemanticModel_AkcssUtilityIfDirective_BindsConditionInUtilityParameterScope()
    {
        const string code =
            "@akcss {\n" +
            "    @utilities {\n" +
            "        .w-(double value) {\n" +
            "            @if(value > 0) {\n" +
            "                Width: value;\n" +
            "            }\n" +
            "        }\n" +
            "    }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var utility = GetOnlyAkcssUtility(syntaxTree);
        var ifDirective = Assert.IsType<AkcssIfDirectiveSyntax>(Assert.Single(utility.Members));
        var symbol = Assert.IsAssignableFrom<ITailwindUtilitySymbol>(
            semanticModel.GetSymbolInfo(utility).Symbol);
        var ifOperation = Assert.IsAssignableFrom<IAkcssIfOperation>(Assert.Single(symbol.Operations));

        Assert.Equal("value", Assert.Single(symbol.Parameters).Name);
        Assert.Same(ifDirective, ifOperation.Syntax);
        Assert.Equal("Boolean", ifOperation.ConditionType.Name);
        Assert.False(ifOperation.ConditionOperation.IsDefault);
        var setter = Assert.IsAssignableFrom<IAkcssPropertySetterOperation>(Assert.Single(ifOperation.Operations));
        Assert.Equal("Width", setter.Property?.Name);
        Assert.Equal("Double", setter.ValueType.Name);
        Assert.False(ifOperation.HasErrors);
    }

    [Fact]
    public void SemanticModel_AkcssInvalidValues_ProduceDiagnostics()
    {
        const string code =
            "@akcss {\n" +
            "    .hello {\n" +
            "        Missing: 1;\n" +
            "        Background: \"#not\";\n" +
            "        Padding: (foo: 1);\n" +
            "    }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var rule = GetOnlyAkcssStyleRule(syntaxTree);
        var symbol = Assert.IsAssignableFrom<IAkcssSymbol>(semanticModel.GetSymbolInfo(rule).Symbol);

        Assert.Equal(3, symbol.Operations.Length);
        var assignments = rule.Members.OfType<AkcssAssignmentSyntax>().ToArray();

        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_AkcssPropertyNotFound,
            Assert.Single(semanticModel.GetSemanticDiagnostics(assignments[0])).Code);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_AkcssInvalidColor,
            Assert.Single(semanticModel.GetSemanticDiagnostics(assignments[1])).Code);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_AkcssInvalidThickness,
            Assert.Single(semanticModel.GetSemanticDiagnostics(assignments[2])).Code);
        Assert.All(symbol.Operations, operation => Assert.True(operation.HasErrors));
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
    public void SemanticModel_MarkupPlainAttribute_CreatesPropertySetterOperation()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "\n" +
            "<Button Content=\"Hello\" />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var operation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            semanticModel.GetOperation(attribute));

        Assert.Equal(Akbura.Language.Operations.OperationKind.MarkupAttribute, operation.Kind);
        Assert.Equal(OperationLanguage.Markup, operation.Language);
        Assert.Equal(MarkupAttributeBindingKind.None, operation.BindingKind);
        Assert.Equal(MarkupAttributeValueKind.Literal, operation.ValueKind);
        Assert.Equal("Hello", operation.LiteralValue);
        Assert.Equal("Content", operation.Property?.Name);
        Assert.Equal(SpecialType.System_String, Assert.IsAssignableFrom<INamedTypeSymbol>(operation.ValueType.Symbol).SpecialType);
        Assert.False(operation.HasErrors);
        Assert.Same(operation, semanticModel.GetOperation(attribute));
    }

    [Fact]
    public void SemanticModel_MarkupDynamicAttribute_BindsInComponentScope()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "\n" +
            "state bool isOpen = false;\n" +
            "\n" +
            "<Button IsVisible={isOpen} />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var operation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            semanticModel.GetOperation(attribute));

        Assert.Equal(MarkupAttributeValueKind.DynamicExpression, operation.ValueKind);
        Assert.Equal("IsVisible", operation.Property?.Name);
        Assert.Equal(SpecialType.System_Boolean, Assert.IsAssignableFrom<INamedTypeSymbol>(operation.ValueType.Symbol).SpecialType);
        Assert.False(operation.ValueOperation.IsDefault);
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
    }

    [Fact]
    public void SemanticModel_MarkupDynamicAttribute_BindsCSharpBlockScope()
    {
        const string code =
            """
            using Avalonia.Controls;

            state int total = 1;

            if(total > 0)
            {
                int local = 2;
                <TextBlock Tag={local + total} />
            }
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var ifStatement = Assert.IsType<CSharpStatementSyntax>(syntaxTree.GetRoot().Members[2]);
        Assert.NotNull(ifStatement.Body);
        var markup = Assert.IsType<MarkupRootSyntax>(ifStatement.Body!.Tokens[1]);
        var attribute = Assert.Single(markup.Element.StartTag!.Attributes);

        var operation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            semanticModel.GetOperation(attribute));

        Assert.Equal(MarkupAttributeValueKind.DynamicExpression, operation.ValueKind);
        Assert.Equal("Tag", operation.Property?.Name);
        Assert.Equal(SpecialType.System_Int32, Assert.IsAssignableFrom<INamedTypeSymbol>(operation.ValueType.Symbol).SpecialType);
        Assert.False(operation.ValueOperation.IsDefault);
        Assert.False(operation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
    }

    [Fact]
    public void SemanticModel_TailwindUtilityAttribute_BindsCSharpBlockScope()
    {
        const string code =
            """
            using Avalonia.Controls;

            @akcss {
                @utilities {
                    .w-(double value) { Width: value; }
                }
            }

            state int total = 1;

            if(total > 0)
            {
                int local = 2;
                <Button w-{local + total} {total > local}:w-5 />
            }
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var ifStatement = Assert.IsType<CSharpStatementSyntax>(syntaxTree.GetRoot().Members[3]);
        Assert.NotNull(ifStatement.Body);
        var markup = Assert.IsType<MarkupRootSyntax>(ifStatement.Body!.Tokens[1]);
        var attributes = markup.Element.StartTag!.Attributes;
        var expressionAttribute = Assert.IsAssignableFrom<TailwindAttributeSyntax>(attributes[0]);
        var conditionalAttribute = Assert.IsAssignableFrom<TailwindAttributeSyntax>(attributes[1]);

        var expressionOperation = Assert.IsAssignableFrom<ITailwindUtilityAttributeOperation>(
            semanticModel.GetOperation(expressionAttribute));
        var conditionalOperation = Assert.IsAssignableFrom<ITailwindUtilityAttributeOperation>(
            semanticModel.GetOperation(conditionalAttribute));

        var argument = Assert.Single(expressionOperation.Arguments);
        Assert.Equal("w", expressionOperation.UtilityName);
        Assert.Equal(SpecialType.System_Int32, Assert.IsAssignableFrom<INamedTypeSymbol>(argument.Type.Symbol).SpecialType);
        Assert.False(argument.ValueOperation.IsDefault);
        Assert.False(expressionOperation.HasErrors);

        Assert.True(conditionalOperation.HasCondition);
        Assert.Equal(SpecialType.System_Boolean, Assert.IsAssignableFrom<INamedTypeSymbol>(conditionalOperation.ConditionType.Symbol).SpecialType);
        Assert.False(conditionalOperation.ConditionOperation.IsDefault);
        Assert.False(conditionalOperation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(expressionAttribute).IsEmpty);
        Assert.True(semanticModel.GetSemanticDiagnostics(conditionalAttribute).IsEmpty);
    }

    [Fact]
    public void SemanticModel_MarkupInterpolatedStringAndEventExpression_BindComponentScope()
    {
        const string code =
            """
            using Avalonia.Controls;

            state int count = 0;

            <StackPanel>
                <TextBlock Text={$"Count: {count}"}/>
                <Button Click={count++}>Increment</Button>
            </StackPanel>
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var attributes = syntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<MarkupPlainAttributeSyntax>()
            .ToArray();
        var textAttribute = Assert.Single(attributes, attribute => attribute.Name.Identifier.ValueText == "Text");
        var clickAttribute = Assert.Single(attributes, attribute => attribute.Name.Identifier.ValueText == "Click");

        var textOperation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            semanticModel.GetOperation(textAttribute));
        var clickOperation = Assert.IsAssignableFrom<IMarkupRoutedEventBindingOperation>(
            semanticModel.GetOperation(clickAttribute));

        Assert.Equal(MarkupAttributeValueKind.DynamicExpression, textOperation.ValueKind);
        Assert.Equal(SpecialType.System_String, Assert.IsAssignableFrom<INamedTypeSymbol>(textOperation.ValueType.Symbol).SpecialType);
        Assert.False(textOperation.ValueOperation.IsDefault);
        Assert.False(textOperation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(textAttribute).IsEmpty);

        Assert.Equal(MarkupCommandHandlerKind.Expression, clickOperation.HandlerKind);
        Assert.False(clickOperation.HandlerOperation.IsDefault);
        Assert.False(clickOperation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(clickAttribute).IsEmpty);
    }

    [Fact]
    public void SemanticModel_MarkupInvalidCSharpExpression_ProducesDiagnostic()
    {
        const string code =
            """
            using Avalonia.Controls;

            <TextBlock Text={$"Count: {missing}"} />
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var operation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            semanticModel.GetOperation(attribute));
        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(attribute));

        Assert.True(operation.HasErrors);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_MarkupExpressionError, diagnostic.Code);
        Assert.Contains("missing", diagnostic.Message);
    }

    [Fact]
    public void SemanticModel_ResolvesAvaloniaRoutedEventSymbol()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "\n" +
            "state int count = 0;\n" +
            "\n" +
            "<Button Click={count++} />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var symbol = Assert.IsAssignableFrom<IRoutedEventSymbol>(
            semanticModel.GetSymbolInfo(attribute).Symbol);

        Assert.Equal(AkburaSymbolKind.Event, symbol.Kind);
        Assert.Equal(SymbolLanguage.Markup, symbol.Language);
        Assert.Equal("Click", symbol.Name);
        Assert.True(symbol.IsAvaloniaRoutedEvent);
        Assert.True(symbol.IsClrEvent);
        Assert.Equal("ClickEvent", symbol.RoutedEventDefinition.Name);
        Assert.Equal("Click", symbol.ClrEventDefinition.Name);
        Assert.Equal("EventHandler", symbol.HandlerType.Name);
        Assert.Equal("RoutedEventArgs", symbol.EventArgsType.Name);
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
    }

    [Fact]
    public void SemanticModel_MarkupRoutedEventAttribute_BindsSimpleExpressionHandler()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "\n" +
            "state int count = 0;\n" +
            "\n" +
            "<Button Click={count++} />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var operation = Assert.IsAssignableFrom<IMarkupRoutedEventBindingOperation>(
            semanticModel.GetOperation(attribute));

        Assert.Equal(Akbura.Language.Operations.OperationKind.MarkupEventBinding, operation.Kind);
        Assert.Equal("Click", operation.Event.Name);
        Assert.Same(operation.Event, operation.TargetSymbol);
        Assert.Equal(MarkupAttributeValueKind.DynamicExpression, operation.ValueKind);
        Assert.Equal(MarkupCommandHandlerKind.Expression, operation.HandlerKind);
        Assert.Equal(MarkupCommandArgumentMode.IgnoresCommandArgument, operation.ArgumentMode);
        Assert.Equal(0, operation.HandlerParameterCount);
        Assert.False(operation.IsAsync);
        Assert.False(operation.ContainsAwait);
        Assert.False(operation.HandlerOperation.IsDefault);
        Assert.Equal("RoutedEventArgs", operation.EventArgsType.Name);
        Assert.False(operation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
    }

    [Theory]
    [InlineData("(sender, args) => count++", 2, false)]
    [InlineData("(_, args) => { if(count == 5) { Console.WriteLine(\"Hello!\"); } count++; }", 2, false)]
    public void SemanticModel_MarkupRoutedEventAttribute_BindsLambdaHandlers(
        string handler,
        int expectedParameterCount,
        bool expectedAsync)
    {
        var code =
            "using Avalonia.Controls;\n" +
            "using System;\n" +
            "\n" +
            "state int count = 0;\n" +
            "\n" +
            "<Button Click={" + handler + "} />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var operation = Assert.IsAssignableFrom<IMarkupRoutedEventBindingOperation>(
            semanticModel.GetOperation(attribute));

        Assert.Equal("Click", operation.Event.Name);
        Assert.Equal(MarkupCommandHandlerKind.Lambda, operation.HandlerKind);
        Assert.Equal(MarkupCommandArgumentMode.ReceivesCommandArgument, operation.ArgumentMode);
        Assert.Equal(expectedParameterCount, operation.HandlerParameterCount);
        Assert.Equal(expectedAsync, operation.IsAsync);
        Assert.False(operation.HandlerOperation.IsDefault);
        Assert.Equal("EventHandler", operation.HandlerType.Name);
        Assert.Equal("RoutedEventArgs", operation.EventArgsType.Name);
        Assert.False(operation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
    }

    [Theory]
    [InlineData("bind:Click={count++}")]
    [InlineData("out:Click={count++}")]
    public void SemanticModel_MarkupRoutedEventAttribute_RejectsDirectionalBinding(string attributeText)
    {
        var code =
            "using Avalonia.Controls;\n" +
            "\n" +
            "state int count = 0;\n" +
            "\n" +
            "<Button " + attributeText + " />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);
        var attribute = Assert.IsAssignableFrom<MarkupAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var operation = Assert.IsAssignableFrom<IMarkupRoutedEventBindingOperation>(
            semanticModel.GetOperation(attribute));
        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(attribute));

        Assert.True(operation.HasErrors);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_MarkupEventBindingNotAllowed, diagnostic.Code);
        Assert.Contains("Click", diagnostic.Message);
    }

    [Theory]
    [InlineData("(sender) => count++")]
    [InlineData("(string sender, args) => count++")]
    public void SemanticModel_MarkupRoutedEventAttribute_RejectsInvalidHandlerSignature(string handler)
    {
        var code =
            "using Avalonia.Controls;\n" +
            "\n" +
            "state int count = 0;\n" +
            "\n" +
            "<Button Click={" + handler + "} />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var operation = Assert.IsAssignableFrom<IMarkupRoutedEventBindingOperation>(
            semanticModel.GetOperation(attribute));
        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(attribute));

        Assert.True(operation.HasErrors);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_MarkupEventHandlerSignatureMismatch, diagnostic.Code);
        Assert.Contains("Click", diagnostic.Message);
        Assert.Contains("EventArgs", diagnostic.Message);
    }

    [Fact]
    public void SemanticModel_MarkupPrefixedAttributes_PreserveBindingKind()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "\n" +
            "state string name = \"\";\n" +
            "\n" +
            "<TextBox bind:Text={name} out:Watermark={name} />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);
        var attributes = element.StartTag!.Attributes;

        var bindOperation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            semanticModel.GetOperation(attributes[0]));
        var outOperation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            semanticModel.GetOperation(attributes[1]));

        Assert.Equal(MarkupAttributeBindingKind.Bind, bindOperation.BindingKind);
        Assert.Equal("Text", bindOperation.Property?.Name);
        Assert.Equal(MarkupAttributeBindingKind.Out, outOperation.BindingKind);
        Assert.Equal("Watermark", outOperation.Property?.Name);
    }

    [Fact]
    public void SemanticModel_TailwindUtilityAttribute_ResolvesFromCompanionAkcss()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "\n" +
            "<Button w-30 />";
        const string akcss =
            "@utilities {\n" +
            "    .w-(double value) { Width: value; }\n" +
            "}";

        var componentPath = Path.Combine("C:\\Project", "Button.akbura");
        var akcssPath = Path.Combine("C:\\Project", "Button.akcss");
        var syntaxTree = AkburaSyntaxTree.ParseText(code, componentPath);
        var akcssTree = AkcssSyntaxTree.ParseText(akcss, akcssPath);
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(), [akcssTree]);
        var element = GetOnlyMarkupElement(syntaxTree);
        var attribute = Assert.IsAssignableFrom<TailwindFullAttributeSyntax>(
            Assert.Single(element.StartTag!.Attributes));

        var operation = Assert.IsAssignableFrom<ITailwindUtilityAttributeOperation>(
            semanticModel.GetOperation(attribute));

        Assert.Equal("w", operation.UtilityName);
        Assert.NotNull(operation.Utility);
        Assert.Equal("w", operation.Utility!.Name);
        Assert.Single(operation.Arguments);
        Assert.Equal("30", operation.Arguments[0].Text);
        Assert.False(operation.HasCondition);
        Assert.False(operation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
    }

    [Fact]
    public void SemanticModel_TailwindUtilityAttribute_ResolvesFromAkcssUsingImport()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "using My.NameSpace.FileName.akcss;\n" +
            "\n" +
            "<Button flex />";
        const string akcss =
            "@utilities {\n" +
            "    .flex { Width: 10; }\n" +
            "}";

        var syntaxTree = AkburaSyntaxTree.ParseText(code, "Root.akbura");
        var akcssTree = AkcssSyntaxTree.ParseText(
            akcss,
            "FileName.akcss",
            "My.NameSpace.FileName.akcss");
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(), [akcssTree]);
        var element = GetOnlyMarkupElement(syntaxTree);
        var attribute = Assert.IsAssignableFrom<TailwindFlagAttributeSyntax>(
            Assert.Single(element.StartTag!.Attributes));

        var componentSymbol = Assert.IsAssignableFrom<IMarkupComponentSymbol>(
            semanticModel.GetSymbolInfo(element).Symbol);
        var operation = Assert.IsAssignableFrom<ITailwindUtilityAttributeOperation>(
            semanticModel.GetOperation(attribute));

        Assert.Equal("Button", componentSymbol.Name);
        Assert.Equal("flex", operation.UtilityName);
        Assert.NotNull(operation.Utility);
        Assert.Equal("flex", operation.Utility!.Name);
        Assert.True(operation.Arguments.IsEmpty);
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
    }

    [Fact]
    public void SemanticModel_TailwindUtilityAttribute_LocalAkcssWinsOverImportedAkcss()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "using Shared.Utilities.akcss;\n" +
            "\n" +
            "<Button w-30 />";
        const string localAkcss =
            "@utilities { .w-(double value) { Width: value; } }";
        const string importedAkcss =
            "@utilities { .w-(double value) { Height: value; } }";

        var componentPath = Path.Combine("C:\\Project", "Button.akbura");
        var syntaxTree = AkburaSyntaxTree.ParseText(code, componentPath);
        var localTree = AkcssSyntaxTree.ParseText(
            localAkcss,
            Path.Combine("C:\\Project", "Button.akcss"));
        var importedTree = AkcssSyntaxTree.ParseText(
            importedAkcss,
            "Utilities.akcss",
            "Shared.Utilities.akcss");
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(), [localTree, importedTree]);
        var element = GetOnlyMarkupElement(syntaxTree);
        var attribute = Assert.IsAssignableFrom<TailwindFullAttributeSyntax>(
            Assert.Single(element.StartTag!.Attributes));
        var localUtility = Assert.Single(Assert.IsType<AkcssUtilitiesSectionSyntax>(
            Assert.Single(localTree.GetRoot().Members)).Utilities);

        var operation = Assert.IsAssignableFrom<ITailwindUtilityAttributeOperation>(
            semanticModel.GetOperation(attribute));

        Assert.NotNull(operation.Utility);
        Assert.Same(localUtility, operation.Utility!.DeclarationSyntax);
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
    }

    [Fact]
    public void SemanticModel_TailwindUtilityAttribute_DuplicateLocalUtilitiesProduceAmbiguity()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "\n" +
            "@akcss { @utilities { .w-(double value) { Width: value; } } }\n" +
            "\n" +
            "<Button w-30 />";
        const string companionAkcss =
            "@utilities { .w-(double value) { Height: value; } }";

        var componentPath = Path.Combine("C:\\Project", "Button.akbura");
        var syntaxTree = AkburaSyntaxTree.ParseText(code, componentPath);
        var companionTree = AkcssSyntaxTree.ParseText(
            companionAkcss,
            Path.Combine("C:\\Project", "Button.akcss"));
        var semanticModel = CreateSemanticModel(syntaxTree, CreateCSharpCompilation(), [companionTree]);
        var element = GetOnlyMarkupElement(syntaxTree);
        var attribute = Assert.IsAssignableFrom<TailwindFullAttributeSyntax>(
            Assert.Single(element.StartTag!.Attributes));

        var operation = Assert.IsAssignableFrom<ITailwindUtilityAttributeOperation>(
            semanticModel.GetOperation(attribute));
        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(attribute));

        Assert.Null(operation.Utility);
        Assert.True(operation.HasErrors);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_TailwindUtilityAmbiguous, diagnostic.Code);
    }

    [Fact]
    public void SemanticModel_TailwindUtilityAttribute_MissingAkcssImportProducesDiagnostic()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "using Missing.Utilities.akcss;\n" +
            "\n" +
            "<Button flex />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code, "Root.akbura");
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);
        var attribute = Assert.IsAssignableFrom<TailwindFlagAttributeSyntax>(
            Assert.Single(element.StartTag!.Attributes));

        var operation = Assert.IsAssignableFrom<ITailwindUtilityAttributeOperation>(
            semanticModel.GetOperation(attribute));
        var diagnostics = semanticModel.GetSemanticDiagnostics(attribute);

        Assert.Null(operation.Utility);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_AkcssImportNotFound);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_TailwindUtilityNotFound);
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
        var componentSymbol = Assert.IsAssignableFrom<IMarkupComponentSymbol>(componentSymbolInfo.Symbol);
        var akburaComponent = Assert.IsAssignableFrom<IAkburaComponentSymbol>(componentSymbol.AkburaComponent);
        var paramSymbol = Assert.Single(akburaComponent.Parameters);
        var propertySymbolInfo = semanticModel.GetSymbolInfo(attribute);
        var propertySymbol = Assert.IsAssignableFrom<AkburaPropertySymbol>(propertySymbolInfo.Symbol);

        Assert.Equal(AkburaCandidateReason.None, componentSymbolInfo.CandidateReason);
        Assert.Equal("A", componentSymbol.Name);
        Assert.Equal("SomeNs.A", componentSymbol.MetadataName);
        Assert.Same(aSyntaxTree, akburaComponent.SyntaxTree);
        Assert.Single(componentSymbol.AttributeOperations);
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
    public void SemanticModel_OutParam_IsReadonlyForPlainMarkupAttribute()
    {
        const string aCode =
            "namespace SomeNs;\n" +
            "\n" +
            "param out int Result;";
        const string bCode =
            "using SomeNs;\n" +
            "\n" +
            "<A Result={1}/>";

        var aSyntaxTree = AkburaSyntaxTree.ParseText(aCode, "A.akbura");
        var bSyntaxTree = AkburaSyntaxTree.ParseText(bCode, "B.akbura");
        var compilation = new AkburaCompilation(CreateCSharpCompilation(), [aSyntaxTree, bSyntaxTree]);
        var semanticModel = compilation.GetSemanticModel(bSyntaxTree);
        var element = GetOnlyMarkupElement(bSyntaxTree);
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var property = Assert.IsAssignableFrom<AkburaPropertySymbol>(
            semanticModel.GetSymbolInfo(attribute).Symbol);
        var operation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            semanticModel.GetOperation(attribute));
        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(attribute));

        Assert.True(property.IsParameter);
        Assert.Equal(ParamBindingKind.Out, property.Parameter!.BindingKind);
        Assert.False(property.CanWrite);
        Assert.True(property.CanRead);
        Assert.True(operation.HasErrors);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_MarkupAttributeBindingNotAllowed, diagnostic.Code);
        Assert.Contains("param out", diagnostic.Message);
    }

    [Fact]
    public void SemanticModel_OutPrefixedAttribute_AllowsOutParam()
    {
        const string aCode =
            "namespace SomeNs;\n" +
            "\n" +
            "param out int Result;";
        const string bCode =
            "using SomeNs;\n" +
            "\n" +
            "state int value = 0;\n" +
            "\n" +
            "<A out:Result={value}/>";

        var aSyntaxTree = AkburaSyntaxTree.ParseText(aCode, "A.akbura");
        var bSyntaxTree = AkburaSyntaxTree.ParseText(bCode, "B.akbura");
        var compilation = new AkburaCompilation(CreateCSharpCompilation(), [aSyntaxTree, bSyntaxTree]);
        var semanticModel = compilation.GetSemanticModel(bSyntaxTree);
        var element = GetOnlyMarkupElement(bSyntaxTree);
        var attribute = Assert.IsType<MarkupPrefixedAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var property = Assert.IsAssignableFrom<AkburaPropertySymbol>(
            semanticModel.GetSymbolInfo(attribute).Symbol);
        var operation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            semanticModel.GetOperation(attribute));

        Assert.Equal(ParamBindingKind.Out, property.Parameter!.BindingKind);
        Assert.Equal(MarkupAttributeBindingKind.Out, operation.BindingKind);
        Assert.False(operation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
    }

    [Fact]
    public void SemanticModel_BindPrefixedAttribute_RejectsDefaultParam()
    {
        const string aCode =
            "namespace SomeNs;\n" +
            "\n" +
            "param int Value;";
        const string bCode =
            "using SomeNs;\n" +
            "\n" +
            "state int value = 0;\n" +
            "\n" +
            "<A bind:Value={value}/>";

        var aSyntaxTree = AkburaSyntaxTree.ParseText(aCode, "A.akbura");
        var bSyntaxTree = AkburaSyntaxTree.ParseText(bCode, "B.akbura");
        var compilation = new AkburaCompilation(CreateCSharpCompilation(), [aSyntaxTree, bSyntaxTree]);
        var semanticModel = compilation.GetSemanticModel(bSyntaxTree);
        var element = GetOnlyMarkupElement(bSyntaxTree);
        var attribute = Assert.IsType<MarkupPrefixedAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var property = Assert.IsAssignableFrom<AkburaPropertySymbol>(
            semanticModel.GetSymbolInfo(attribute).Symbol);
        var operation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            semanticModel.GetOperation(attribute));
        var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(attribute));

        Assert.Equal(ParamBindingKind.Default, property.Parameter!.BindingKind);
        Assert.True(property.CanWrite);
        Assert.False(property.CanRead);
        Assert.Equal(MarkupAttributeBindingKind.Bind, operation.BindingKind);
        Assert.True(operation.HasErrors);
        Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_MarkupAttributeBindingNotAllowed, diagnostic.Code);
        Assert.Contains("param", diagnostic.Message);
    }

    [Theory]
    [InlineData("Text=\"Hello\"", (int)MarkupAttributeBindingKind.None)]
    [InlineData("bind:Text={text}", (int)MarkupAttributeBindingKind.Bind)]
    [InlineData("out:Text={text}", (int)MarkupAttributeBindingKind.Out)]
    public void SemanticModel_BindParam_AllowsEveryMarkupBindingDirection(
        string attributeText,
        int expectedBindingKind)
    {
        const string aCode =
            "namespace SomeNs;\n" +
            "\n" +
            "param bind string Text = \"\";";
        const string bCode =
            "using SomeNs;\n" +
            "\n" +
            "state string text = \"\";\n" +
            "\n" +
            "<A ";

        var aSyntaxTree = AkburaSyntaxTree.ParseText(aCode, "A.akbura");
        var bSyntaxTree = AkburaSyntaxTree.ParseText(bCode + attributeText + "/>", "B.akbura");
        var compilation = new AkburaCompilation(CreateCSharpCompilation(), [aSyntaxTree, bSyntaxTree]);
        var semanticModel = compilation.GetSemanticModel(bSyntaxTree);
        var element = GetOnlyMarkupElement(bSyntaxTree);
        var attribute = Assert.Single(element.StartTag!.Attributes);

        var property = Assert.IsAssignableFrom<AkburaPropertySymbol>(
            semanticModel.GetSymbolInfo(attribute).Symbol);
        var operation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            semanticModel.GetOperation(attribute));

        Assert.True(property.IsParameter);
        Assert.Equal(ParamBindingKind.Bind, property.Parameter!.BindingKind);
        Assert.True(property.CanRead);
        Assert.True(property.CanWrite);
        Assert.Equal((MarkupAttributeBindingKind)expectedBindingKind, operation.BindingKind);
        Assert.False(operation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
    }

    [Theory]
    [InlineData("<TextBlock bind:Text={mystate} out:Text={myotherState}/>", false)]
    [InlineData("<TextBlock bind:Text={mystate} Text={myotherState}/>", true)]
    [InlineData("<TextBlock Text={mystate} Text={myotherState}/>", true)]
    [InlineData("<TextBlock Text={mystate} out:Text={myotherState}/>", false)]
    [InlineData("<TextBlock out:Text={mystate} out:Text={myotherState}/>", false)]
    public void SemanticModel_DetectsOnlyDuplicateMarkupPropertySetters(
        string markup,
        bool expectDuplicateSetter)
    {
        var code =
            "using Avalonia.Controls;\n" +
            "\n" +
            "state string mystate = \"\";\n" +
            "state string myotherState = \"\";\n" +
            "\n" +
            markup;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);
        var attributes = element.StartTag!.Attributes;

        Assert.Equal(2, attributes.Count);

        var firstDiagnostics = semanticModel.GetSemanticDiagnostics(attributes[0]);
        var secondDiagnostics = semanticModel.GetSemanticDiagnostics(attributes[1]);

        Assert.DoesNotContain(firstDiagnostics, diagnostic =>
            diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_MarkupDuplicatePropertySetter);

        if (expectDuplicateSetter)
        {
            var diagnostic = Assert.Single(secondDiagnostics, diagnostic =>
                diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_MarkupDuplicatePropertySetter);
            Assert.Contains("Text", diagnostic.Message);
        }
        else
        {
            Assert.DoesNotContain(secondDiagnostics, diagnostic =>
                diagnostic.Code == ErrorCodes.AKBURA_SEMANTIC_MarkupDuplicatePropertySetter);
        }
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

        var componentSymbol = Assert.IsAssignableFrom<IMarkupComponentSymbol>(
            semanticModel.GetSymbolInfo(element).Symbol);
        var akburaComponent = Assert.IsAssignableFrom<IAkburaComponentSymbol>(componentSymbol.AkburaComponent);
        var commandSymbol = Assert.Single(akburaComponent.Commands);
        var propertySymbol = Assert.IsAssignableFrom<AkburaPropertySymbol>(
            semanticModel.GetSymbolInfo(attribute).Symbol);

        Assert.Single(componentSymbol.AttributeOperations);
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
    public void SemanticModel_MarkupCommandAttribute_CreatesCommandBindingOperation()
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

        var operation = Assert.IsAssignableFrom<IMarkupCommandBindingOperation>(
            semanticModel.GetOperation(attribute));

        Assert.Equal(Akbura.Language.Operations.OperationKind.MarkupCommandBinding, operation.Kind);
        Assert.Equal("Click", operation.Command.Name);
        Assert.Same(operation.Command, operation.TargetSymbol);
        Assert.True(operation.Property.IsCommand);
        Assert.Same(operation.Command, operation.Property.Command);
        Assert.Equal("Int32", operation.ReturnType.Name);
        Assert.Equal("Int32", operation.ResultType.Name);
        Assert.Equal("a", Assert.Single(operation.Parameters).Name);
        Assert.Equal(MarkupAttributeBindingKind.None, operation.BindingKind);
        Assert.Equal(MarkupAttributeValueKind.DynamicExpression, operation.ValueKind);
        Assert.Equal(MarkupCommandHandlerKind.Lambda, operation.HandlerKind);
        Assert.Equal(MarkupCommandArgumentMode.ReceivesCommandArgument, operation.ArgumentMode);
        Assert.Equal(MarkupCommandResultMode.ReturnsResult, operation.ResultMode);
        Assert.Equal(1, operation.HandlerParameterCount);
        Assert.False(operation.IsAsync);
        Assert.False(operation.ContainsAwait);
        Assert.Equal("Int32", operation.HandlerResultType.Name);
        Assert.Same(attribute.Value, operation.ValueSyntax);
        Assert.False(operation.HasErrors);
        Assert.Same(operation, semanticModel.GetOperation(attribute));
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
    }

    [Theory]
    [InlineData("() => Vm.Notify()", (int)MarkupCommandHandlerKind.Lambda, (int)MarkupCommandArgumentMode.IgnoresCommandArgument, (int)MarkupCommandResultMode.NoResult, 0, false, false, null)]
    [InlineData("x => Vm.NotifyWith(x)", (int)MarkupCommandHandlerKind.Lambda, (int)MarkupCommandArgumentMode.ReceivesCommandArgument, (int)MarkupCommandResultMode.NoResult, 1, false, false, null)]
    [InlineData("x => x * 2", (int)MarkupCommandHandlerKind.Lambda, (int)MarkupCommandArgumentMode.ReceivesCommandArgument, (int)MarkupCommandResultMode.ReturnsResult, 1, false, false, "Int32")]
    [InlineData("async x => await Vm.Fetch(x)", (int)MarkupCommandHandlerKind.Lambda, (int)MarkupCommandArgumentMode.ReceivesCommandArgument, (int)MarkupCommandResultMode.ReturnsResult, 1, true, true, "Int32")]
    [InlineData("Vm.MyCommand", (int)MarkupCommandHandlerKind.DirectReference, (int)MarkupCommandArgumentMode.None, (int)MarkupCommandResultMode.Unknown, 0, false, false, null)]
    public void SemanticModel_MarkupCommandAttribute_AnalyzesHandlerShape(
        string handler,
        int expectedHandlerKind,
        int expectedArgumentMode,
        int expectedResultMode,
        int expectedParameterCount,
        bool expectedIsAsync,
        bool expectedContainsAwait,
        string? expectedResultTypeName)
    {
        const string aCode =
            "namespace SomeNs;\n" +
            "\n" +
            "command int Click(int a);";
        var bCode =
            "using SomeNs;\n" +
            "\n" +
            "inject RootVm Vm;\n" +
            "\n" +
            "<A Click={" + handler + "}/>";
        const string csharpCode =
            "public sealed class RootVm\n" +
            "{\n" +
            "    public void Notify() { }\n" +
            "    public void NotifyWith(int value) { }\n" +
            "    public System.Threading.Tasks.Task<int> Fetch(int value) => System.Threading.Tasks.Task.FromResult(value);\n" +
            "    public System.Func<int, int> MyCommand { get; } = value => value * 2;\n" +
            "}";

        var aSyntaxTree = AkburaSyntaxTree.ParseText(aCode, "A.akbura");
        var bSyntaxTree = AkburaSyntaxTree.ParseText(bCode, "B.akbura");
        var compilation = new AkburaCompilation(CreateCSharpCompilation(csharpCode), [aSyntaxTree, bSyntaxTree]);
        var semanticModel = compilation.GetSemanticModel(bSyntaxTree);
        var element = GetOnlyMarkupElement(bSyntaxTree);
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var operation = Assert.IsAssignableFrom<IMarkupCommandBindingOperation>(
            semanticModel.GetOperation(attribute));

        Assert.Equal((MarkupCommandHandlerKind)expectedHandlerKind, operation.HandlerKind);
        Assert.Equal((MarkupCommandArgumentMode)expectedArgumentMode, operation.ArgumentMode);
        Assert.Equal((MarkupCommandResultMode)expectedResultMode, operation.ResultMode);
        Assert.Equal(expectedParameterCount, operation.HandlerParameterCount);
        Assert.Equal(expectedIsAsync, operation.IsAsync);
        Assert.Equal(expectedContainsAwait, operation.ContainsAwait);
        if (expectedResultTypeName == null)
        {
            Assert.True(operation.HandlerResultType.IsDefault);
        }
        else
        {
            Assert.Equal(expectedResultTypeName, operation.HandlerResultType.Name);
        }
    }

    [Fact]
    public void SemanticModel_LocalCommandIsExecuting_BindsInMarkupExpression()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "\n" +
            "command int CustomClick(int a);\n" +
            "\n" +
            "<TextBlock Tag={CustomClick.IsExecuting} />";

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var element = GetOnlyMarkupElement(syntaxTree);
        var attributes = element.StartTag!.Attributes;

        var tag = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            semanticModel.GetOperation(Assert.Single(attributes)));

        Assert.Equal("IObservable", tag.ValueType.Name);
        var observable = Assert.IsAssignableFrom<INamedTypeSymbol>(tag.ValueType.Symbol);
        Assert.Equal("Boolean", Assert.Single(observable.TypeArguments).Name);
        Assert.True(semanticModel.GetSemanticDiagnostics(Assert.Single(attributes)).IsEmpty);
    }

    [Fact]
    public void SemanticModel_LocalCommandExecute_BindsAsAwaitableInCommandHandler()
    {
        const string aCode =
            "namespace SomeNs;\n" +
            "\n" +
            "command int Click(int a);";
        const string bCode =
            "using SomeNs;\n" +
            "\n" +
            "command int CustomClick(int a);\n" +
            "state int clicked = 0;\n" +
            "\n" +
            "<A Click={async x => await CustomClick.Execute(clicked)}/>";

        var aSyntaxTree = AkburaSyntaxTree.ParseText(aCode, "A.akbura");
        var bSyntaxTree = AkburaSyntaxTree.ParseText(bCode, "B.akbura");
        var compilation = new AkburaCompilation(CreateCSharpCompilation(), [aSyntaxTree, bSyntaxTree]);
        var semanticModel = compilation.GetSemanticModel(bSyntaxTree);
        var element = GetOnlyMarkupElement(bSyntaxTree);
        var attribute = Assert.IsType<MarkupPlainAttributeSyntax>(Assert.Single(element.StartTag!.Attributes));

        var operation = Assert.IsAssignableFrom<IMarkupCommandBindingOperation>(
            semanticModel.GetOperation(attribute));

        Assert.Equal(MarkupCommandHandlerKind.Lambda, operation.HandlerKind);
        Assert.Equal(MarkupCommandArgumentMode.ReceivesCommandArgument, operation.ArgumentMode);
        Assert.Equal(MarkupCommandResultMode.ReturnsResult, operation.ResultMode);
        Assert.True(operation.IsAsync);
        Assert.True(operation.ContainsAwait);
        Assert.Equal("Int32", operation.HandlerResultType.Name);
        Assert.False(operation.HasErrors);
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

    private static (
        AkburaSemanticModel SemanticModel,
        AkcssAssignmentSyntax Assignment,
        IAkcssPropertySetterOperation Operation,
        IAkcssSymbol Symbol) GetSingleStyleOperation(string code)
    {
        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var rule = GetOnlyAkcssStyleRule(syntaxTree);
        var symbol = Assert.IsAssignableFrom<IAkcssSymbol>(semanticModel.GetSymbolInfo(rule).Symbol);
        var assignment = Assert.IsType<AkcssAssignmentSyntax>(Assert.Single(rule.Members));
        var operation = Assert.IsAssignableFrom<IAkcssPropertySetterOperation>(
            Assert.Single(symbol.Operations));

        Assert.Same(assignment, operation.Syntax);
        Assert.Same(operation, semanticModel.GetOperation(assignment));
        return (semanticModel, assignment, operation, symbol);
    }

    private static AkcssStyleRuleSyntax GetOnlyAkcssStyleRule(AkburaSyntaxTree syntaxTree)
    {
        var inlineAkcss = Assert.IsType<InlineAkcssBlockSyntax>(
            syntaxTree.GetRoot().Members.Single(member => member is InlineAkcssBlockSyntax));
        return Assert.IsType<AkcssStyleRuleSyntax>(
            Assert.Single(inlineAkcss.Members.OfType<AkcssStyleRuleSyntax>()));
    }

    private static AkcssStyleRuleSyntax GetOnlyAkcssStyleRule(
        AkburaSyntaxTree syntaxTree,
        string className)
    {
        var inlineAkcss = Assert.IsType<InlineAkcssBlockSyntax>(
            syntaxTree.GetRoot().Members.Single(member => member is InlineAkcssBlockSyntax));
        return Assert.Single(
            inlineAkcss.Members.OfType<AkcssStyleRuleSyntax>(),
            rule => rule.Selector.Name?.Identifier.ValueText == className);
    }

    private static AkcssUtilityDeclarationSyntax GetOnlyAkcssUtility(AkburaSyntaxTree syntaxTree)
    {
        var inlineAkcss = Assert.IsType<InlineAkcssBlockSyntax>(
            syntaxTree.GetRoot().Members.Single(member => member is InlineAkcssBlockSyntax));
        var utilities = Assert.IsType<AkcssUtilitiesSectionSyntax>(
            Assert.Single(inlineAkcss.Members.OfType<AkcssUtilitiesSectionSyntax>()));
        return Assert.Single(utilities.Utilities);
    }

    private static void AssertThickness(
        IAkcssOperation operation,
        double left,
        double top,
        double right,
        double bottom)
    {
        var setter = Assert.IsAssignableFrom<IAkcssPropertySetterOperation>(operation);
        Assert.Equal(AkcssPropertyValueKind.ThicknessTuple, setter.ValueKind);
        var value = Assert.IsType<AkcssThicknessValue>(setter.ConvertedValue);
        Assert.Equal(left, value.Left);
        Assert.Equal(top, value.Top);
        Assert.Equal(right, value.Right);
        Assert.Equal(bottom, value.Bottom);
        Assert.False(setter.HasErrors);
    }

    private static void AssertAvaloniaColorsProperty(
        CSharpSymbolDefinition definition,
        string name)
    {
        var property = Assert.IsAssignableFrom<Microsoft.CodeAnalysis.IPropertySymbol>(definition.Symbol);
        Assert.Equal(name, property.Name);
        Assert.Equal("global::Avalonia.Media.Colors",
            property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
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

    private static AkburaSemanticModel CreateSemanticModel(
        AkburaSyntaxTree syntaxTree,
        CSharpCompilation csharpCompilation,
        IEnumerable<AkcssSyntaxTree> akcssSyntaxTrees)
    {
        var compilation = new AkburaCompilation(csharpCompilation, [syntaxTree], akcssSyntaxTrees);
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
        AssertResolvedAvaloniaButton(semanticModel, symbol.CSharpDefinition);
    }

    private static void AssertResolvedAvaloniaButton(
        AkburaSemanticModel semanticModel,
        CSharpSymbolDefinition definition)
    {
        var avaloniaButton = semanticModel.Compilation.CSharpCompilation.GetTypeByMetadataName("Avalonia.Controls.Button");
        Assert.NotNull(avaloniaButton);
        Assert.True(SymbolEqualityComparer.Default.Equals(avaloniaButton, definition.Symbol));
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
