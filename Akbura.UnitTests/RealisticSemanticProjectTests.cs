using Akbura.Language;
using Akbura.Language.Binder;
using Akbura.Language.BoundTree;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using System.IO;
using AkburaPropertySymbol = Akbura.Language.Symbols.IPropertySymbol;
using AkburaSymbolKind = Akbura.Language.Symbols.SymbolKind;

namespace Akbura.UnitTests;

public sealed class RealisticSemanticProjectTests
{
    [Fact]
    public void RealisticProject_ResolvesAllSymbolsAndOperations()
    {
        var project = CreateProject();
        var semanticModel = project.Compilation.GetSemanticModel(project.DashboardTree);
        var root = project.DashboardTree.GetRoot();

        AssertFullWidth(project.DashboardTree);
        AssertFullWidth(project.TaskCardTree);
        AssertFullWidth(project.StatusBadgeTree);
        AssertFullWidth(project.DashboardAkcssTree);
        AssertFullWidth(project.SharedAkcssTree);

        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            semanticModel.GetSymbolInfo(root).Symbol);

        Assert.Equal(AkburaSymbolKind.AkburaComponent, component.Kind);
        Assert.Equal("DashboardPage", component.Name);
        Assert.Equal("Demo.Pages.DashboardPage", component.MetadataName);
        Assert.Equal("Demo.Pages", component.NamespaceName);
        Assert.Same(project.DashboardTree, component.SyntaxTree);
        Assert.Equal("Demo.Pages.DashboardPage", Assert.Single(component.PartialTypes).ToDisplayString());
        Assert.Equal(2, component.InjectedServices.Length);
        Assert.Equal(3, component.Parameters.Length);
        Assert.Equal(6, component.States.Length);
        Assert.Single(component.Commands);
        Assert.Single(component.AkcssModules);
        Assert.Equal(2, component.MarkupRoots.Length);

        AssertRootParameters(component);
        AssertRootInjectedServices(component);
        AssertRootStates(component);
        AssertRootCommand(component);
        AssertRootInlineAkcssModule(component, semanticModel);

        var stackPanel = GetRootMarkupElement(root, "StackPanel");
        var stackPanelSymbol = AssertMarkupComponent(
            semanticModel,
            stackPanel,
            "StackPanel",
            "Avalonia.Controls.StackPanel");
        Assert.Equal(3, stackPanelSymbol.AttributeOperations.Length);
        AssertTailwindOperation(semanticModel, GetAttribute(stackPanel, "w"), "w", "30", hasCondition: false);
        AssertTailwindOperation(semanticModel, GetAttribute(stackPanel, "opacity"), "opacity", "1", hasCondition: false);
        AssertTailwindOperation(semanticModel, GetAttribute(stackPanel, "hidden"), "hidden", expectedArgument: null, hasCondition: true);

        var textBlock = FindDescendantElement(stackPanel, "TextBlock");
        AssertMarkupComponent(semanticModel, textBlock, "TextBlock", "Avalonia.Controls.TextBlock");
        AssertMarkupProperty(semanticModel, GetAttribute(textBlock, "Text"), "Text", MarkupAttributeBindingKind.None, MarkupAttributeValueKind.Literal);

        var textBox = FindDescendantElement(stackPanel, "TextBox");
        AssertMarkupComponent(semanticModel, textBox, "TextBox", "Avalonia.Controls.TextBox");
        AssertMarkupProperty(semanticModel, GetAttribute(textBox, "Text"), "Text", MarkupAttributeBindingKind.Bind, MarkupAttributeValueKind.DynamicExpression);
        AssertMarkupProperty(semanticModel, GetAttribute(textBox, "Watermark"), "Watermark", MarkupAttributeBindingKind.None, MarkupAttributeValueKind.Literal);

        var button = FindDescendantElement(stackPanel, "Button");
        AssertMarkupComponent(semanticModel, button, "Button", "Avalonia.Controls.Button");
        AssertMarkupRoutedEvent(semanticModel, GetAttribute(button, "Click"), "Click", parameterCount: 2);
        AssertMarkupProperty(semanticModel, GetAttribute(button, "Content"), "Content", MarkupAttributeBindingKind.None, MarkupAttributeValueKind.Literal);

        var taskCard = FindDescendantElement(stackPanel, "TaskCard");
        var taskCardUsage = Assert.IsAssignableFrom<IMarkupComponentSymbol>(
            semanticModel.GetSymbolInfo(taskCard).Symbol);
        var taskCardComponent = Assert.IsAssignableFrom<IAkburaComponentSymbol>(taskCardUsage.AkburaComponent);
        Assert.Equal("TaskCard", taskCardUsage.Name);
        Assert.Equal("Demo.Components.TaskCard", taskCardUsage.MetadataName);
        Assert.Same(project.TaskCardTree, taskCardComponent.SyntaxTree);
        Assert.Equal(4, taskCardUsage.AttributeOperations.Length);
        AssertAkburaParamAttribute(semanticModel, GetAttribute(taskCard, "Item"), "Item", ParamBindingKind.Default, MarkupAttributeBindingKind.None);
        AssertAkburaParamAttribute(semanticModel, GetAttribute(taskCard, "IsSelected"), "IsSelected", ParamBindingKind.Bind, MarkupAttributeBindingKind.Bind);
        AssertAkburaParamAttribute(semanticModel, GetAttribute(taskCard, "SelectedItem"), "SelectedItem", ParamBindingKind.Out, MarkupAttributeBindingKind.Out);
        AssertMarkupCommand(semanticModel, GetAttribute(taskCard, "Toggle"), "Toggle", expectedResultType: "Int32");

        var statusBadgeUsage = FindDescendantElement(stackPanel, "StatusBadge");
        var statusBadgeSymbol = Assert.IsAssignableFrom<IMarkupComponentSymbol>(
            semanticModel.GetSymbolInfo(statusBadgeUsage).Symbol);
        Assert.Equal("StatusBadge", statusBadgeSymbol.Name);
        Assert.Equal("Demo.Components.StatusBadge", statusBadgeSymbol.MetadataName);
        Assert.NotNull(statusBadgeSymbol.AkburaComponent);
        AssertMarkupProperty(semanticModel, GetAttribute(statusBadgeUsage, "Text"), "Text", MarkupAttributeBindingKind.None, MarkupAttributeValueKind.DynamicExpression);

        var border = FindDescendantElement(stackPanel, "Border");
        AssertMarkupComponent(semanticModel, border, "Border", "Avalonia.Controls.Border");
        AssertMarkupProperty(semanticModel, GetAttribute(border, "IsVisible"), "IsVisible", MarkupAttributeBindingKind.None, MarkupAttributeValueKind.DynamicExpression);

        Assert.True(semanticModel.GetSemanticDiagnostics(root).IsEmpty);
        Assert.All(component.States, state =>
        {
            var diagnostics = semanticModel.GetSemanticDiagnostics(state.DeclarationSyntax);
            var diagnosticsText = string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
            Assert.True(diagnostics.IsEmpty, diagnosticsText);
        });
    }

    [Fact]
    public void RealisticProject_CommandAndStateReferences_MapBackToAkburaSymbols()
    {
        var project = CreateProject();
        var semanticModel = project.Compilation.GetSemanticModel(project.DashboardTree);
        var root = project.DashboardTree.GetRoot();
        var effectStatement = root.Members
            .OfType<CSharpStatementSyntax>()
            .Single(static statement => statement.Tokens.ToFullString().TrimStart().StartsWith("useEffect", StringComparison.Ordinal));

        var references = semanticModel.GetCSharpSymbolReferences(effectStatement);

        var loggerReference = Assert.Single(references, reference => reference.Name == "logger");
        Assert.IsAssignableFrom<ILocalSymbol>(loggerReference.CSharpDefinition.Symbol);
        Assert.IsAssignableFrom<IInjectSymbol>(loggerReference.AkburaSymbol);

        var userIdReference = Assert.Single(references, reference => reference.Name == "UserId");
        Assert.IsAssignableFrom<ILocalSymbol>(userIdReference.CSharpDefinition.Symbol);
        Assert.IsAssignableFrom<IParamSymbol>(userIdReference.AkburaSymbol);

        var methodReference = Assert.Single(references, reference => reference.Name == "LogInformation");
        Assert.IsAssignableFrom<IMethodSymbol>(methodReference.CSharpDefinition.Symbol);
        Assert.Null(methodReference.AkburaSymbol);

        var conditional = root.Members
            .OfType<CSharpStatementSyntax>()
            .Single(static statement => statement.Tokens.ToFullString().TrimStart().StartsWith("if", StringComparison.Ordinal));
        var conditionalStatement = Assert.IsType<CSharpStatementSyntax>(
            conditional.Body!.Tokens.Single(member => member is CSharpStatementSyntax));
        var conditionalReferences = semanticModel.GetCSharpSymbolReferences(conditionalStatement);

        Assert.IsAssignableFrom<IInjectSymbol>(
            Assert.Single(conditionalReferences, reference => reference.Name == "logger").AkburaSymbol);
        Assert.IsAssignableFrom<IParamSymbol>(
            Assert.Single(conditionalReferences, reference => reference.Name == "Search").AkburaSymbol);

        var operation = Assert.IsAssignableFrom<IUseHookOperation>(
            semanticModel.GetOperation(effectStatement));
        Assert.Equal(Akbura.Language.Operations.OperationKind.UseHook, operation.Kind);
        Assert.Equal("useEffect", operation.Hook.InvocationName);
        Assert.Equal("useEffect", operation.Method.Name);
        Assert.True(operation.HasSyntheticSelf);
        Assert.Equal(UseHookSelfKind.Implicit, operation.SelfKind);
        Assert.NotNull(operation.InvocationOperation);

        Assert.IsAssignableFrom<IInjectSymbol>(
            Assert.Single(references, reference => reference.Name == "viewModel").AkburaSymbol);
        Assert.IsAssignableFrom<IParamSymbol>(
            Assert.Single(references, reference => reference.Name == "UserId").AkburaSymbol);
        Assert.IsAssignableFrom<IStateSymbol>(
            Assert.Single(references, reference => reference.Name == "isBusy").AkburaSymbol);
        Assert.IsAssignableFrom<ICommandSymbol>(
            Assert.Single(references, reference => reference.Name == "Refresh").AkburaSymbol);
    }

    [Fact]
    public void RealisticProject_AkcssImportsAndCompanionStyles_ResolveUtilities()
    {
        var project = CreateProject();
        var semanticModel = project.Compilation.GetSemanticModel(project.DashboardTree);
        var inlineAkcss = project.DashboardTree.GetRoot().Members
            .OfType<InlineAkcssBlockSyntax>()
            .Single();
        var primaryRule = inlineAkcss.Members
            .OfType<AkcssStyleRuleSyntax>()
            .Single(rule => rule.Selector.Name?.Identifier.ValueText == "primary");

        var primarySymbol = Assert.IsAssignableFrom<IAkcssSymbol>(
            semanticModel.GetSymbolInfo(primaryRule).Symbol);

        Assert.Equal("primary", primarySymbol.ClassName);
        Assert.Equal("Button.primary", primarySymbol.MetadataName);
        Assert.Equal("Button", primarySymbol.TargetType.Name);
        Assert.True(primarySymbol.IsIntercepted);
        Assert.Equal("Demo.Styles.DashboardStyle",
            primarySymbol.InterceptType.Symbol?.ToDisplayString());
        var intercept = Assert.IsAssignableFrom<IAkcssInterceptOperation>(Assert.Single(primarySymbol.Operations));
        Assert.Same(primarySymbol.InterceptType.Symbol, intercept.InterceptType.Symbol);
        Assert.False(intercept.HasErrors);

        var ignoredMembers = primaryRule.Members
            .Where(static member => member is not AkcssInterceptDirectiveSyntax)
            .ToArray();
        Assert.Equal(5, ignoredMembers.Length);
        foreach (var ignoredMember in ignoredMembers)
        {
            var diagnostic = Assert.Single(semanticModel.GetSemanticDiagnostics(ignoredMember));
            Assert.Equal(ErrorCodes.AKBURA_SEMANTIC_AkcssInterceptIgnoresMember, diagnostic.Code);
            Assert.Equal(AkburaDiagnosticSeverity.Warning, diagnostic.Severity);
            Assert.Null(semanticModel.GetOperation(ignoredMember));
        }

        var stackPanel = GetRootMarkupElement(project.DashboardTree.GetRoot(), "StackPanel");
        AssertTailwindOperation(semanticModel, GetAttribute(stackPanel, "w"), "w", "30", hasCondition: false);
        AssertTailwindOperation(semanticModel, GetAttribute(stackPanel, "opacity"), "opacity", "1", hasCondition: false);
        AssertTailwindOperation(semanticModel, GetAttribute(stackPanel, "hidden"), "hidden", expectedArgument: null, hasCondition: true);
    }

    [Fact]
    public void RealisticProject_BoundTreeAudit_CoversSemanticBearingSyntax()
    {
        var project = CreateProject();
        var dashboardModel = project.Compilation.GetSemanticModel(project.DashboardTree);
        var taskCardModel = project.Compilation.GetSemanticModel(project.TaskCardTree);
        var statusBadgeModel = project.Compilation.GetSemanticModel(project.StatusBadgeTree);

        AssertComponentBoundCoverage(dashboardModel, project.DashboardTree.GetRoot());
        AssertComponentBoundCoverage(taskCardModel, project.TaskCardTree.GetRoot());
        AssertComponentBoundCoverage(statusBadgeModel, project.StatusBadgeTree.GetRoot());
        AssertAkcssDocumentBoundCoverage(dashboardModel, project.DashboardAkcssTree.GetRoot());
        AssertAkcssDocumentBoundCoverage(dashboardModel, project.SharedAkcssTree.GetRoot());
    }

    private static void AssertComponentBoundCoverage(
        AkburaSemanticModel semanticModel,
        AkburaDocumentSyntax root)
    {
        var component = AssertBoundSemantic<BoundComponentDeclaration>(
            semanticModel,
            root,
            BoundKind.ComponentDeclaration);
        Assert.DoesNotContain(component.Children, child => child.Kind == BoundKind.Declaration);

        foreach (var member in root.Members)
        {
            switch (member)
            {
                case InjectDeclarationSyntax inject:
                    AssertBoundSemantic<BoundInjectDeclaration>(
                        semanticModel,
                        inject,
                        BoundKind.InjectDeclaration);
                    break;

                case ParamDeclarationSyntax param:
                    AssertParamBoundCoverage(semanticModel, param);
                    break;

                case StateDeclarationSyntax state:
                    AssertStateBoundCoverage(semanticModel, state);
                    break;

                case CommandDeclarationSyntax command:
                    AssertBoundSemantic<BoundCommandDeclaration>(
                        semanticModel,
                        command,
                        BoundKind.CommandDeclaration);
                    break;

                case InlineAkcssBlockSyntax inlineAkcss:
                    AssertAkcssModuleBoundCoverage(semanticModel, inlineAkcss);
                    break;

                case MarkupRootSyntax markup:
                    AssertMarkupRootBoundCoverage(semanticModel, markup);
                    break;

                case CSharpStatementSyntax statement:
                    if (statement.Tokens.ToFullString().TrimStart().StartsWith("useEffect", StringComparison.Ordinal))
                    {
                        var boundStatement = AssertBoundSemantic<BoundUseHookStatement>(
                            semanticModel,
                            statement,
                            BoundKind.UseHookStatement);
                        Assert.Equal(BoundKind.UseHookInvocation, boundStatement.Invocation.Kind);
                        Assert.IsAssignableFrom<IUseHookOperation>(semanticModel.GetOperation(statement));
                        break;
                    }

                    foreach (var markup in EnumerateMarkupRoots(statement.Body))
                    {
                        AssertMarkupRootBoundCoverage(semanticModel, markup);
                    }

                    break;
            }
        }
    }

    private static void AssertStateBoundCoverage(
        AkburaSemanticModel semanticModel,
        StateDeclarationSyntax state)
    {
        var boundState = AssertBoundSemantic<BoundStateDeclaration>(
            semanticModel,
            state,
            BoundKind.StateDeclaration);
        var initializer = Assert.IsType<BoundStateInitializer>(Assert.Single(boundState.Children));

        Assert.Same(state.Initializer, initializer.Syntax);
        Assert.Same(initializer, semanticModel.BindingSession.BindSemanticSyntax(state.Initializer));
    }

    private static void AssertParamBoundCoverage(
        AkburaSemanticModel semanticModel,
        ParamDeclarationSyntax param)
    {
        var boundParam = AssertBoundSemantic<BoundParamDeclaration>(
            semanticModel,
            param,
            BoundKind.ParamDeclaration);
        if (param.DefaultValue == null)
        {
            Assert.Empty(boundParam.Children);
            return;
        }

        var defaultValue = Assert.IsType<BoundParamDefaultValue>(Assert.Single(boundParam.Children));
        Assert.Same(param.DefaultValue, defaultValue.Syntax);
        Assert.Same(defaultValue, semanticModel.BindingSession.BindSemanticSyntax(param.DefaultValue));
    }

    private static void AssertMarkupRootBoundCoverage(
        AkburaSemanticModel semanticModel,
        MarkupRootSyntax markupRoot)
    {
        var boundRoot = AssertBoundSemantic<BoundMarkupRoot>(
            semanticModel,
            markupRoot,
            BoundKind.MarkupRoot);
        Assert.Single(boundRoot.Children);
        AssertMarkupElementBoundCoverage(semanticModel, markupRoot.Element);
    }

    private static void AssertMarkupElementBoundCoverage(
        AkburaSemanticModel semanticModel,
        MarkupElementSyntax element)
    {
        var boundElement = AssertBoundSemantic<BoundMarkupComponent>(
            semanticModel,
            element,
            BoundKind.MarkupComponent);
        Assert.DoesNotContain(boundElement.Children, child => child.Kind == BoundKind.Declaration);

        if (element.StartTag != null)
        {
            foreach (var attribute in element.StartTag.Attributes)
            {
                AssertMarkupAttributeBoundCoverage(semanticModel, attribute);
            }
        }

        foreach (var content in element.Body)
        {
            var boundContent = AssertBoundSemantic<BoundMarkupContent>(
                semanticModel,
                content,
                BoundKind.MarkupContent);
            if (content is MarkupElementContentSyntax elementContent)
            {
                Assert.Contains(boundContent.Children, child => child.Kind == BoundKind.MarkupComponent);
                AssertMarkupElementBoundCoverage(semanticModel, elementContent.Element);
            }
        }
    }

    private static void AssertMarkupAttributeBoundCoverage(
        AkburaSemanticModel semanticModel,
        MarkupAttributeSyntax attribute)
    {
        if (attribute is TailwindAttributeSyntax)
        {
            AssertBoundOperation<BoundTailwindUtilityAttribute>(
                semanticModel,
                attribute,
                BoundKind.TailwindUtilityAttribute);
            return;
        }

        var symbol = semanticModel.GetSymbolInfo(attribute).Symbol;
        if (symbol is IRoutedEventSymbol)
        {
            AssertBoundOperation<BoundMarkupRoutedEventBinding>(
                semanticModel,
                attribute,
                BoundKind.MarkupRoutedEventBinding);
            return;
        }

        if (symbol is AkburaPropertySymbol { Command: not null })
        {
            AssertBoundOperation<BoundMarkupCommandBinding>(
                semanticModel,
                attribute,
                BoundKind.MarkupCommandBinding);
            return;
        }

        AssertBoundOperation<BoundMarkupPropertySetter>(
            semanticModel,
            attribute,
            BoundKind.MarkupPropertySetter);
    }

    private static void AssertAkcssDocumentBoundCoverage(
        AkburaSemanticModel semanticModel,
        AkcssDocumentSyntax document)
    {
        AssertAkcssModuleBoundCoverage(
            semanticModel,
            document,
            document.Members);
    }

    private static void AssertAkcssModuleBoundCoverage(
        AkburaSemanticModel semanticModel,
        InlineAkcssBlockSyntax inlineAkcss)
    {
        AssertAkcssModuleBoundCoverage(
            semanticModel,
            inlineAkcss,
            inlineAkcss.Members);
    }

    private static void AssertAkcssModuleBoundCoverage(
        AkburaSemanticModel semanticModel,
        AkburaSyntax moduleSyntax,
        Akbura.Language.Syntax.SyntaxList<AkcssTopLevelMemberSyntax> members)
    {
        var boundModule = AssertBoundSemantic<BoundAkcssModule>(
            semanticModel,
            moduleSyntax,
            BoundKind.AkcssModule);
        Assert.DoesNotContain(boundModule.Children, child => child.Kind == BoundKind.Declaration);

        foreach (var member in members)
        {
            switch (member)
            {
                case AkcssStyleRuleSyntax style:
                    AssertAkcssStyleBoundCoverage(semanticModel, style);
                    break;

                case AkcssUtilitiesSectionSyntax utilities:
                    foreach (var utility in utilities.Utilities)
                    {
                        AssertAkcssUtilityBoundCoverage(semanticModel, utility);
                    }

                    break;
            }
        }
    }

    private static void AssertAkcssStyleBoundCoverage(
        AkburaSemanticModel semanticModel,
        AkcssStyleRuleSyntax style)
    {
        var boundStyle = AssertBoundSemantic<BoundAkcssStyle>(
            semanticModel,
            style,
            BoundKind.AkcssStyle);
        AssertAkcssBodyMembersBoundCoverage(semanticModel, style.Members, boundStyle.Children);
    }

    private static void AssertAkcssUtilityBoundCoverage(
        AkburaSemanticModel semanticModel,
        AkcssUtilityDeclarationSyntax utility)
    {
        var boundUtility = AssertBoundSemantic<BoundAkcssUtility>(
            semanticModel,
            utility,
            BoundKind.AkcssUtility);
        AssertAkcssBodyMembersBoundCoverage(semanticModel, utility.Members, boundUtility.Children);
    }

    private static void AssertAkcssBodyMembersBoundCoverage(
        AkburaSemanticModel semanticModel,
        Akbura.Language.Syntax.SyntaxList<AkcssBodyMemberSyntax> members,
        ImmutableArray<BoundNode> boundChildren)
    {
        foreach (var member in members)
        {
            switch (member)
            {
                case AkcssAssignmentSyntax assignment:
                    AssertBoundOperation<BoundAkcssPropertySetter>(
                        semanticModel,
                        assignment,
                        BoundKind.AkcssPropertySetter);
                    Assert.Contains(boundChildren, child => ReferenceEquals(child.Syntax, assignment));
                    break;

                case AkcssIfDirectiveSyntax ifDirective:
                    var boundIf = AssertBoundOperation<BoundAkcssIf>(
                        semanticModel,
                        ifDirective,
                        BoundKind.AkcssIf);
                    Assert.Contains(boundChildren, child => ReferenceEquals(child.Syntax, ifDirective));
                    AssertAkcssBodyMembersBoundCoverage(
                        semanticModel,
                        ifDirective.Members,
                        ImmutableArray<BoundNode>.CastUp(boundIf.Operations));
                    break;

                case AkcssApplyDirectiveSyntax apply:
                    AssertBoundOperation<BoundAkcssApply>(
                        semanticModel,
                        apply,
                        BoundKind.AkcssApply);
                    Assert.Contains(boundChildren, child => ReferenceEquals(child.Syntax, apply));
                    break;

                case AkcssInterceptDirectiveSyntax intercept:
                    AssertBoundOperation<BoundAkcssIntercept>(
                        semanticModel,
                        intercept,
                        BoundKind.AkcssIntercept);
                    Assert.Contains(boundChildren, child => ReferenceEquals(child.Syntax, intercept));
                    break;
            }
        }
    }

    private static IEnumerable<MarkupRootSyntax> EnumerateMarkupRoots(CSharpBlockSyntax? block)
    {
        if (block == null)
        {
            yield break;
        }

        foreach (var token in block.Tokens)
        {
            switch (token)
            {
                case MarkupRootSyntax markup:
                    yield return markup;
                    break;

                case CSharpStatementSyntax statement:
                    foreach (var nested in EnumerateMarkupRoots(statement.Body))
                    {
                        yield return nested;
                    }

                    break;
            }
        }
    }

    private static TBound AssertBoundSemantic<TBound>(
        AkburaSemanticModel semanticModel,
        AkburaSyntax syntax,
        BoundKind expectedKind)
        where TBound : BoundNode
    {
        var boundNode = Assert.IsType<TBound>(
            semanticModel.BindingSession.BindSemanticSyntax(syntax));
        Assert.Equal(expectedKind, boundNode.Kind);
        Assert.Same(syntax, boundNode.Syntax);
        return boundNode;
    }

    private static TBound AssertBoundOperation<TBound>(
        AkburaSemanticModel semanticModel,
        AkburaSyntax syntax,
        BoundKind expectedKind)
        where TBound : BoundNode
    {
        var boundNode = Assert.IsType<TBound>(
            semanticModel.BindingSession.BindOperationSyntax(syntax));
        Assert.Equal(expectedKind, boundNode.Kind);
        Assert.Same(syntax, boundNode.Syntax);
        return boundNode;
    }

    private static void AssertRootParameters(IAkburaComponentSymbol component)
    {
        Assert.Equal(["UserId", "Search", "SelectedTask"], component.Parameters.Select(static parameter => parameter.Name));
        Assert.Equal(ParamBindingKind.Default, component.Parameters[0].BindingKind);
        Assert.Equal(ParamBindingKind.Bind, component.Parameters[1].BindingKind);
        Assert.Equal(ParamBindingKind.Out, component.Parameters[2].BindingKind);
        Assert.Equal("Int32", component.Parameters[0].Type.Name);
        Assert.Equal("String", component.Parameters[1].Type.Name);
        Assert.Equal("TaskItem", component.Parameters[2].Type.Name);
    }

    private static void AssertRootInjectedServices(IAkburaComponentSymbol component)
    {
        Assert.Equal(["logger", "viewModel"], component.InjectedServices.Select(static service => service.Name));
        Assert.Equal("ILogger", component.InjectedServices[0].Type.Name);
        Assert.Equal("DashboardVm", component.InjectedServices[1].Type.Name);
        Assert.True(component.InjectedServices.All(static service => service.IsRequired));
    }

    private static void AssertRootStates(IAkburaComponentSymbol component)
    {
        Assert.Equal(["isOpen", "vm", "isBusy", "activeTask", "selectedTask", "searchName"], component.States.Select(static state => state.Name));
        Assert.Equal(StateBindingKind.None, component.States[0].BindingKind);
        Assert.Equal(StateBindingKind.None, component.States[1].BindingKind);
        Assert.Equal(StateBindingKind.Bind, component.States[2].BindingKind);
        Assert.Equal(StateBindingKind.In, component.States[3].BindingKind);
        Assert.Equal(StateBindingKind.Out, component.States[4].BindingKind);
        Assert.Equal(StateBindingKind.None, component.States[5].BindingKind);
        Assert.False(component.States[2].IsReadOnly);
        Assert.True(component.States[4].IsReadOnly);
        Assert.Equal("Boolean", component.States[0].Type.Name);
        Assert.Equal("DashboardVm", component.States[1].Type.Name);
        Assert.Equal("Boolean", component.States[2].Type.Name);
        Assert.Equal("TaskItem", component.States[3].Type.Name);
        Assert.Equal("TaskItem", component.States[4].Type.Name);
        Assert.Equal("String", component.States[5].Type.Name);
        Assert.NotNull(component.States[5].UseHook);
        Assert.Equal("useName", component.States[5].UseHook!.InvocationName);
    }

    private static void AssertRootCommand(IAkburaComponentSymbol component)
    {
        var command = Assert.Single(component.Commands);

        Assert.Equal("Refresh", command.Name);
        Assert.Equal("Int32", command.ReturnType.Name);
        Assert.Equal("Int32", command.ResultType.Name);
        Assert.True(command.IsAsyncLike);
        Assert.True(command.HasResult);
        Assert.True(command.SupportsIsExecuting);
        var parameter = Assert.Single(command.Parameters);
        Assert.Equal("userId", parameter.Name);
        Assert.Equal("Int32", parameter.Type.Name);
    }

    private static void AssertRootInlineAkcssModule(
        IAkburaComponentSymbol component,
        AkburaSemanticModel semanticModel)
    {
        var module = Assert.Single(component.AkcssModules);

        Assert.True(module.IsInlined);
        Assert.Null(module.Path);
        Assert.Same(component, module.ContainingSymbol);
        Assert.Equal(AkburaSymbolKind.AkcssModule, module.Kind);
        Assert.Equal(3, module.AkcssSymbols.Length);
        Assert.Contains(module.AkcssSymbols, symbol => symbol.ClassName == "primary");
        Assert.Contains(module.AkcssSymbols, symbol => symbol.ClassName == "inlinePanel");
        Assert.Contains(module.AkcssSymbols, symbol => symbol.Name == "inlineFade");
        Assert.True(semanticModel.GetSemanticDiagnostics(module.DeclaringSyntax).IsEmpty);
    }

    private static MarkupComponentSymbol AssertMarkupComponent(
        AkburaSemanticModel semanticModel,
        MarkupElementSyntax element,
        string expectedName,
        string expectedCSharpType)
    {
        var symbol = Assert.IsType<MarkupComponentSymbol>(
            semanticModel.GetSymbolInfo(element).Symbol);

        Assert.Equal(expectedName, symbol.Name);
        Assert.Equal(expectedCSharpType, symbol.CSharpDefinition.Symbol?.ToDisplayString());
        Assert.Equal(AkburaSymbolKind.MarkupComponent, symbol.Kind);
        Assert.True(semanticModel.GetSemanticDiagnostics(element).IsEmpty);
        return symbol;
    }

    private static IMarkupPropertySetterOperation AssertMarkupProperty(
        AkburaSemanticModel semanticModel,
        MarkupAttributeSyntax attribute,
        string expectedPropertyName,
        MarkupAttributeBindingKind expectedBindingKind,
        MarkupAttributeValueKind expectedValueKind)
    {
        var property = Assert.IsAssignableFrom<AkburaPropertySymbol>(
            semanticModel.GetSymbolInfo(attribute).Symbol);
        var operation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            semanticModel.GetOperation(attribute));

        Assert.Equal(expectedPropertyName, property.Name);
        Assert.Equal(expectedPropertyName, operation.Property?.Name);
        Assert.Equal(expectedBindingKind, operation.BindingKind);
        Assert.Equal(expectedValueKind, operation.ValueKind);
        Assert.False(operation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
        return operation;
    }

    private static void AssertMarkupRoutedEvent(
        AkburaSemanticModel semanticModel,
        MarkupAttributeSyntax attribute,
        string expectedEventName,
        int parameterCount)
    {
        var symbol = Assert.IsAssignableFrom<IRoutedEventSymbol>(
            semanticModel.GetSymbolInfo(attribute).Symbol);
        var operation = Assert.IsAssignableFrom<IMarkupRoutedEventBindingOperation>(
            semanticModel.GetOperation(attribute));

        Assert.Equal(expectedEventName, symbol.Name);
        Assert.Equal(expectedEventName, operation.Event.Name);
        Assert.Equal(MarkupAttributeBindingKind.None, operation.BindingKind);
        Assert.Equal(MarkupCommandHandlerKind.Lambda, operation.HandlerKind);
        Assert.Equal(parameterCount, operation.HandlerParameterCount);
        Assert.False(operation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
    }

    private static void AssertAkburaParamAttribute(
        AkburaSemanticModel semanticModel,
        MarkupAttributeSyntax attribute,
        string expectedName,
        ParamBindingKind expectedParamBindingKind,
        MarkupAttributeBindingKind expectedMarkupBindingKind)
    {
        var property = Assert.IsAssignableFrom<AkburaPropertySymbol>(
            semanticModel.GetSymbolInfo(attribute).Symbol);
        var operation = Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            semanticModel.GetOperation(attribute));

        Assert.Equal(expectedName, property.Name);
        Assert.True(property.IsParameter);
        Assert.Equal(expectedParamBindingKind, property.Parameter!.BindingKind);
        Assert.Equal(expectedMarkupBindingKind, operation.BindingKind);
        var diagnostics = semanticModel.GetSemanticDiagnostics(attribute);
        var diagnosticsText = string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
        Assert.False(operation.HasErrors, diagnosticsText);
        Assert.True(diagnostics.IsEmpty, diagnosticsText);
    }

    private static void AssertMarkupCommand(
        AkburaSemanticModel semanticModel,
        MarkupAttributeSyntax attribute,
        string expectedCommandName,
        string expectedResultType)
    {
        var property = Assert.IsAssignableFrom<AkburaPropertySymbol>(
            semanticModel.GetSymbolInfo(attribute).Symbol);
        var operation = Assert.IsAssignableFrom<IMarkupCommandBindingOperation>(
            semanticModel.GetOperation(attribute));

        Assert.True(property.IsCommand);
        Assert.Equal(expectedCommandName, property.Command!.Name);
        Assert.Equal(expectedCommandName, operation.Command.Name);
        Assert.Equal(expectedResultType, operation.ResultType.Name);
        Assert.Equal(MarkupCommandHandlerKind.Lambda, operation.HandlerKind);
        Assert.Equal(MarkupCommandArgumentMode.ReceivesCommandArgument, operation.ArgumentMode);
        Assert.Equal(MarkupCommandResultMode.ReturnsResult, operation.ResultMode);
        Assert.True(operation.IsAsync);
        Assert.False(operation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
    }

    private static void AssertTailwindOperation(
        AkburaSemanticModel semanticModel,
        MarkupAttributeSyntax attribute,
        string expectedUtilityName,
        string? expectedArgument,
        bool hasCondition)
    {
        var operation = Assert.IsAssignableFrom<ITailwindUtilityAttributeOperation>(
            semanticModel.GetOperation(attribute));

        Assert.Equal(expectedUtilityName, operation.UtilityName);
        Assert.NotNull(operation.Utility);
        Assert.Equal(expectedUtilityName, operation.Utility!.Name);
        Assert.Equal(hasCondition, operation.HasCondition);
        if (expectedArgument == null)
        {
            Assert.True(operation.Arguments.IsEmpty);
        }
        else
        {
            Assert.Equal(expectedArgument, Assert.Single(operation.Arguments).Text);
        }

        Assert.False(operation.HasErrors);
        Assert.True(semanticModel.GetSemanticDiagnostics(attribute).IsEmpty);
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
        return attribute switch
        {
            MarkupPlainAttributeSyntax plain => plain.Name.Identifier.ValueText,
            MarkupPrefixedAttributeSyntax prefixed => prefixed.Name.Identifier.ValueText,
            TailwindFlagAttributeSyntax flag => flag.Name.Identifier.ValueText,
            TailwindFullAttributeSyntax full => full.Name.Identifier.ValueText,
            _ => attribute.ToFullString().Trim()
        };
    }

    private static void AssertFullWidth(AkburaSyntaxTree tree)
    {
        var text = tree.Text.ToString();
        var root = tree.GetRoot();
        Assert.Equal(text.Length, root.FullWidth);
        Assert.Equal(text, root.ToFullString());
    }

    private static void AssertFullWidth(AkcssSyntaxTree tree)
    {
        var text = tree.Text.ToString();
        var root = tree.GetRoot();
        Assert.Equal(text.Length, root.FullWidth);
        Assert.Equal(text, root.ToFullString());
    }

    private static ProjectFixture CreateProject()
    {
        var dashboardTree = AkburaSyntaxTree.ParseText(DashboardCode, DashboardPath);
        var taskCardTree = AkburaSyntaxTree.ParseText(TaskCardCode, TaskCardPath);
        var statusBadgeTree = AkburaSyntaxTree.ParseText(StatusBadgeCode, StatusBadgePath);
        var dashboardAkcssTree = AkcssSyntaxTree.ParseText(DashboardAkcss, DashboardAkcssPath);
        var sharedAkcssTree = AkcssSyntaxTree.ParseText(SharedAkcss, "Shared.akcss", "Demo.Styles.Shared.akcss");
        var compilation = new AkburaCompilation(
            CreateCSharpCompilation(ProjectCSharpCode),
            [dashboardTree, taskCardTree, statusBadgeTree],
            [dashboardAkcssTree, sharedAkcssTree],
            rootNamespace: "Demo",
            projectDirectory: ProjectDirectory);

        return new ProjectFixture(
            compilation,
            dashboardTree,
            taskCardTree,
            statusBadgeTree,
            dashboardAkcssTree,
            sharedAkcssTree);
    }

    private static CSharpCompilation CreateCSharpCompilation(params string[] sources)
    {
        return CSharpCompilation.Create(
            assemblyName: "RealisticSemanticProject",
            references: SymbolTests.CreateAvaloniaReferences(),
            syntaxTrees: sources.Select(source => CSharpSyntaxTree.ParseText(
                source,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview))));
    }

    private static readonly string ProjectDirectory = Path.Combine("C:\\Project");
    private static readonly string DashboardPath = Path.Combine(ProjectDirectory, "Pages", "DashboardPage.akbura");
    private static readonly string DashboardAkcssPath = Path.Combine(ProjectDirectory, "Pages", "DashboardPage.akcss");
    private static readonly string TaskCardPath = Path.Combine(ProjectDirectory, "Components", "TaskCard.akbura");
    private static readonly string StatusBadgePath = Path.Combine(ProjectDirectory, "Components", "StatusBadge.akbura");

    private static readonly string DashboardCode =
        """
    using Avalonia.Controls;
    using Akbura.Hooks;
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

    useEffect(
        () => logger.LogInformation("Loading"),
        [UserId, isBusy, viewModel.IsBusy, Refresh.IsExecuting]);

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
        public static class NameHooks
        {
            [Akbura.CompilerAnotations.UseHook]
            public static Akbura.ComponentTree.State<string> useName<T>(
                [Akbura.CompilerAnotations.Self] object component,
                T state) => null!;
        }
    }
    """;

    private sealed record ProjectFixture(
        AkburaCompilation Compilation,
        AkburaSyntaxTree DashboardTree,
        AkburaSyntaxTree TaskCardTree,
        AkburaSyntaxTree StatusBadgeTree,
        AkcssSyntaxTree DashboardAkcssTree,
        AkcssSyntaxTree SharedAkcssTree);
}
