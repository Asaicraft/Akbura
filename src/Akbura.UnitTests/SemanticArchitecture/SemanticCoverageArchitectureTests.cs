using Akbura.Language;
using Akbura.Collections;
using Akbura.Language.Binder;
using Akbura.Language.BoundTree;
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

public sealed class SemanticCoverageArchitectureTests : SemanticArchitectureTestBase
{
    [Fact]
    public void BindingSession_BindsSemanticSyntaxCoverageTree()
    {
        const string code =
            """
            using Avalonia.Controls;
            using Akbura.Hooks;

            state int count = 0;
            param string Title = "Dashboard";

            useEffect(() => {
                System.Console.WriteLine(Title);
            }, [count]);

            @akcss {
                Button.card {
                    Background: White;
                    @if(true) {
                        Opacity: 1;
                    }
                    @apply card;
                }

                @utilities {
                    .w-(double value) {
                        Width: value;
                    }
                }
            }

            <StackPanel>
                <TextBlock Text="Hello"/>
            </StackPanel>
            """;

        var tree = AkburaSyntaxTree.ParseText(code, "Dashboard.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var root = tree.GetRoot();

        var component = Assert.IsType<BoundComponentDeclaration>(
            model.BindingSession.BindSemanticSyntax(root));
        Assert.Contains(component.Children, child => child.Kind == BoundKind.StateDeclaration);
        Assert.Contains(component.Children, child => child.Kind == BoundKind.ParamDeclaration);
        Assert.True(
            component.Children.Any(child => child.Kind == BoundKind.UseHookStatement),
            string.Join(Environment.NewLine, component.Children.SelectMany(static child => child.Diagnostics)
                .Select(static diagnostic => diagnostic.Message)));
        Assert.Contains(component.Children, child => child.Kind == BoundKind.AkcssModule);
        Assert.Contains(component.Children, child => child.Kind == BoundKind.MarkupRoot);

        var state = Assert.IsType<StateDeclarationSyntax>(root.Members[2]);
        var boundState = Assert.IsType<BoundStateDeclaration>(
            model.BindingSession.BindSemanticSyntax(state));
        Assert.Single(boundState.Children);
        Assert.IsType<BoundStateInitializer>(boundState.Children[0]);

        var param = Assert.IsType<ParamDeclarationSyntax>(root.Members[3]);
        var boundParam = Assert.IsType<BoundParamDeclaration>(
            model.BindingSession.BindSemanticSyntax(param));
        Assert.Single(boundParam.Children);
        Assert.IsType<BoundParamDefaultValue>(boundParam.Children[0]);

        var useEffect = Assert.IsType<CSharpStatementSyntax>(root.Members[4]);
        var boundUseEffect = Assert.IsType<BoundUseHookStatement>(
            model.BindingSession.BindSemanticSyntax(useEffect));
        Assert.Equal(BoundKind.UseHookInvocation, boundUseEffect.Invocation.Kind);
        Assert.IsAssignableFrom<IUseHookSymbol>(model.GetSymbolInfo(useEffect).Symbol);
        Assert.IsAssignableFrom<IUseHookOperation>(model.GetOperation(useEffect));

        var akcss = Assert.IsType<InlineAkcssBlockSyntax>(root.Members[5]);
        var boundModule = Assert.IsType<BoundAkcssModule>(
            model.BindingSession.BindSemanticSyntax(akcss));
        Assert.Contains(boundModule.Children, child => child.Kind == BoundKind.AkcssStyle);
        Assert.Contains(boundModule.Children, child => child.Kind == BoundKind.AkcssUtility);

        var style = Assert.IsType<BoundAkcssStyle>(
            Assert.Single(boundModule.Children, child => child.Kind == BoundKind.AkcssStyle));
        Assert.Contains(style.Children, child => child.Kind == BoundKind.AkcssPropertySetter);
        Assert.Contains(style.Children, child => child.Kind == BoundKind.AkcssIf);
        Assert.Contains(style.Children, child => child.Kind == BoundKind.AkcssApply);

        var utility = Assert.IsType<BoundAkcssUtility>(
            Assert.Single(boundModule.Children, child => child.Kind == BoundKind.AkcssUtility));
        Assert.Contains(utility.Children, child => child.Kind == BoundKind.AkcssPropertySetter);

        var markup = Assert.IsType<MarkupRootSyntax>(root.Members[6]);
        var boundMarkupRoot = Assert.IsType<BoundMarkupRoot>(
            model.BindingSession.BindSemanticSyntax(markup));
        var boundMarkupComponent = Assert.IsType<BoundMarkupComponent>(
            Assert.Single(boundMarkupRoot.Children));
        Assert.Contains(boundMarkupComponent.Children, child => child.Kind == BoundKind.MarkupContent);
        var nestedContent = Assert.IsType<BoundMarkupContent>(
            Assert.Single(boundMarkupComponent.Children, child => child.Kind == BoundKind.MarkupContent));
        Assert.Contains(nestedContent.Children, child => child.Kind == BoundKind.MarkupComponent);
    }


    [Fact]
    public void SemanticApiAudit_CoversSymbolBoundAndOperationSyntax()
    {
        const string dashboardCode =
            """
            using Avalonia.Controls;
            using Akbura.Hooks;
            using Demo.Components;

            namespace Demo.Pages;

            @akcss {
                Button.card {
                    Background: White;
                    @if(true) {
                        Opacity: 1;
                    }
                    @apply card;
                }

                Button.managed {
                    @intercept global::Demo.Styles.DashboardStyle;
                }

                @utilities {
                    .w-(double value) {
                        Width: value;
                    }

                    .hidden {
                        IsVisible: false;
                    }
                }
            }

            inject int service;
            param string Title = "Dashboard";
            state int count = 0;
            command int Refresh(int id);

            useEffect(() => {
                System.Console.WriteLine(Title);
            }, [count, Refresh.IsExecuting]);

            <StackPanel>
                <TextBlock Text={Title} w-30 {count > 0}:hidden/>
                <Button Click={(sender, args) => { count++; }} Content="Run"/>
                <TaskCard Toggle={id => id + count}/>
            </StackPanel>
            """;
        const string taskCardCode =
            """
            namespace Demo.Components;

            command int Toggle(int id);

            <TextBlock Text="Task"/>
            """;
        const string csharpCode =
            """
            namespace Akbura.Akcss
            {
                public abstract class AkcssStyle { }

                public abstract class AkcssClass : AkcssStyle
                {
                    public abstract void Update(object control);
                }
            }

            namespace Demo.Styles
            {
                public sealed class DashboardStyle : Akbura.Akcss.AkcssClass
                {
                    public override void Update(object control) { }
                }
            }
            """;

        var dashboardTree = AkburaSyntaxTree.ParseText(dashboardCode, "Pages/Dashboard.akbura");
        var taskCardTree = AkburaSyntaxTree.ParseText(taskCardCode, "Components/TaskCard.akbura");
        var csharpCompilation = CreateCSharpCompilation().AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(csharpCode));
        var compilation = new AkburaCompilation(
            csharpCompilation,
            [dashboardTree, taskCardTree],
            rootNamespace: "Demo",
            projectDirectory: Environment.CurrentDirectory);
        var model = compilation.GetSemanticModel(dashboardTree);
        var root = dashboardTree.GetRoot();
        var inlineAkcss = root.Members.OfType<InlineAkcssBlockSyntax>().Single();
        var cardStyle = inlineAkcss.Members.OfType<AkcssStyleRuleSyntax>().First();
        var managedStyle = inlineAkcss.Members.OfType<AkcssStyleRuleSyntax>().Skip(1).Single();
        var utilities = inlineAkcss.Members.OfType<AkcssUtilitiesSectionSyntax>().Single();
        var markup = root.Members.OfType<MarkupRootSyntax>().Single();
        var stackPanel = markup.Element;
        var children = ChildElements(stackPanel).ToArray();
        var textBlock = children.Single(element => ElementName(element) == "TextBlock");
        var button = children.Single(element => ElementName(element) == "Button");
        var taskCard = children.Single(element => ElementName(element) == "TaskCard");

        AssertSymbol<IAkburaComponentSymbol>(root);
        AssertSemanticBound<BoundComponentDeclaration>(root);
        AssertSymbol<IInjectSymbol>(root.Members.OfType<InjectDeclarationSyntax>().Single());
        AssertSemanticBound<BoundInjectDeclaration>(root.Members.OfType<InjectDeclarationSyntax>().Single());
        AssertSymbol<IParamSymbol>(root.Members.OfType<ParamDeclarationSyntax>().Single());
        AssertSemanticBound<BoundParamDeclaration>(root.Members.OfType<ParamDeclarationSyntax>().Single());
        AssertSymbol<IStateSymbol>(root.Members.OfType<StateDeclarationSyntax>().Single());
        AssertSemanticBound<BoundStateDeclaration>(root.Members.OfType<StateDeclarationSyntax>().Single());
        AssertSymbol<ICommandSymbol>(root.Members.OfType<CommandDeclarationSyntax>().Single());
        AssertSemanticBound<BoundCommandDeclaration>(root.Members.OfType<CommandDeclarationSyntax>().Single());
        var useEffect = root.Members.OfType<CSharpStatementSyntax>().Single();
        AssertSymbol<IUseHookSymbol>(useEffect);
        AssertSemanticBound<BoundUseHookStatement>(useEffect);
        AssertOperation<BoundUseHookStatement, IUseHookOperation>(useEffect);

        AssertSymbol<IAkcssModuleSymbol>(inlineAkcss);
        AssertSemanticBound<BoundAkcssModule>(inlineAkcss);
        AssertSymbol<IAkcssSymbol>(cardStyle);
        AssertSemanticBound<BoundAkcssStyle>(cardStyle);
        AssertSymbol<ITailwindUtilitySymbol>(utilities.Utilities[0]);
        AssertSemanticBound<BoundAkcssUtility>(utilities.Utilities[0]);

        AssertSymbol<IMarkupComponentSymbol>(stackPanel);
        AssertSemanticBound<BoundMarkupRoot>(markup);
        AssertSemanticBound<BoundMarkupComponent>(stackPanel);
        AssertSymbol<IMarkupComponentSymbol>(textBlock);
        AssertSymbol<IMarkupComponentSymbol>(button);
        AssertSymbol<IMarkupComponentSymbol>(taskCard);

        var textAttribute = Attribute(textBlock, "Text");
        var widthAttribute = Attribute(textBlock, "w");
        var hiddenAttribute = Attribute(textBlock, "hidden");
        var clickAttribute = Attribute(button, "Click");
        var contentAttribute = Attribute(button, "Content");
        var toggleAttribute = Attribute(taskCard, "Toggle");
        var backgroundAssignment = cardStyle.Members.OfType<AkcssAssignmentSyntax>().Single();
        var ifDirective = cardStyle.Members.OfType<AkcssIfDirectiveSyntax>().Single();
        var applyDirective = cardStyle.Members.OfType<AkcssApplyDirectiveSyntax>().Single();
        var interceptDirective = managedStyle.Members.OfType<AkcssInterceptDirectiveSyntax>().Single();

        AssertSymbol<AkburaPropertySymbol>(textAttribute);
        AssertOperation<BoundMarkupPropertySetter, IMarkupPropertySetterOperation>(textAttribute);
        AssertOperation<BoundTailwindUtilityAttribute, ITailwindUtilityAttributeOperation>(widthAttribute);
        AssertOperation<BoundTailwindUtilityAttribute, ITailwindUtilityAttributeOperation>(hiddenAttribute);
        AssertSymbol<IRoutedEventSymbol>(clickAttribute);
        AssertOperation<BoundMarkupRoutedEventBinding, IMarkupRoutedEventBindingOperation>(clickAttribute);
        AssertSymbol<AkburaPropertySymbol>(contentAttribute);
        AssertOperation<BoundMarkupPropertySetter, IMarkupPropertySetterOperation>(contentAttribute);
        var toggleProperty = AssertSymbol<AkburaPropertySymbol>(toggleAttribute);
        Assert.NotNull(toggleProperty.Command);
        AssertOperation<BoundMarkupCommandBinding, IMarkupCommandBindingOperation>(toggleAttribute);

        AssertOperation<BoundAkcssPropertySetter, IAkcssPropertySetterOperation>(backgroundAssignment);
        AssertOperation<BoundAkcssIf, IAkcssIfOperation>(ifDirective);
        AssertOperation<BoundAkcssApply, IAkcssApplyOperation>(applyDirective);
        AssertOperation<BoundAkcssIntercept, IAkcssInterceptOperation>(interceptDirective);

        TSymbol AssertSymbol<TSymbol>(AkburaSyntax syntax)
            where TSymbol : class, AkburaSymbol
        {
            var symbol = Assert.IsAssignableFrom<TSymbol>(model.GetSymbolInfo(syntax).Symbol);
            Assert.Same(symbol, model.GetSymbolInfo(syntax).Symbol);
            return symbol;
        }

        void AssertSemanticBound<TBound>(AkburaSyntax syntax)
            where TBound : BoundNode
        {
            Assert.IsType<TBound>(model.BindingSession.BindSemanticSyntax(syntax));
            Assert.Same(
                model.BindingSession.BindSemanticSyntax(syntax),
                model.BindingSession.BindSemanticSyntax(syntax));
        }

        void AssertOperation<TBound, TOperation>(AkburaSyntax syntax)
            where TBound : BoundNode
            where TOperation : class, AkburaOperation
        {
            Assert.IsType<TBound>(model.BindingSession.BindOperationSyntax(syntax));
            var operation = Assert.IsAssignableFrom<TOperation>(model.GetOperation(syntax));
            Assert.Same(operation, model.GetOperation(syntax));
            Assert.Same(syntax, operation.Syntax);
        }

        static IEnumerable<MarkupElementSyntax> ChildElements(MarkupElementSyntax element)
        {
            foreach (var content in element.Body)
            {
                if (content.Kind == AkburaSyntaxKind.MarkupElementContentSyntax)
                {
                    yield return ((MarkupElementContentSyntax)content).Element;
                }
            }
        }

        static string ElementName(MarkupElementSyntax element)
        {
            return element.StartTag?.Name.ToFullString().Trim() ?? string.Empty;
        }

        static MarkupAttributeSyntax Attribute(MarkupElementSyntax element, string name)
        {
            return element.StartTag!.Attributes.Single(attribute => AttributeName(attribute) == name);
        }

        static string AttributeName(MarkupAttributeSyntax attribute)
        {
            return attribute.Kind switch
            {
                AkburaSyntaxKind.MarkupPlainAttributeSyntax =>
                    ((MarkupPlainAttributeSyntax)attribute).Name.Identifier.ValueText,
                AkburaSyntaxKind.MarkupPrefixedAttributeSyntax =>
                    ((MarkupPrefixedAttributeSyntax)attribute).Name.Identifier.ValueText,
                AkburaSyntaxKind.TailwindFlagAttributeSyntax =>
                    ((TailwindFlagAttributeSyntax)attribute).Name.Identifier.ValueText,
                AkburaSyntaxKind.TailwindFullAttributeSyntax =>
                    ((TailwindFullAttributeSyntax)attribute).Name.Identifier.ValueText,
                _ => string.Empty,
            };
        }
    }

}
