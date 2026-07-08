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

public sealed class OperationArchitectureTests : SemanticArchitectureTestBase
{
    [Fact]
    public void OperationWalker_VisitsAkcssOperationTree()
    {
        const string code =
            "@akcss {\n" +
            "    Button.card {\n" +
            "        @if(true) {\n" +
            "            Background: White;\n" +
            "        }\n" +
            "    }\n" +
            "}";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var compilation = CreateCompilation(tree);
        var model = compilation.GetSemanticModel(tree);
        var block = Assert.IsType<InlineAkcssBlockSyntax>(tree.GetRoot().Members[0]);
        var rule = Assert.IsType<AkcssStyleRuleSyntax>(block.Members[0]);
        var ifDirective = Assert.IsType<AkcssIfDirectiveSyntax>(rule.Members[0]);

        var operation = Assert.IsAssignableFrom<IAkcssIfOperation>(model.GetOperation(ifDirective));
        var walker = new RecordingOperationWalker();

        walker.Visit(operation);

        Assert.Contains(AkburaOperationKind.AkcssIf, walker.Kinds);
        Assert.Contains(AkburaOperationKind.CSharpExpression, walker.Kinds);
        Assert.Contains(AkburaOperationKind.AkcssAssignment, walker.Kinds);
    }


    [Fact]
    public void CSharpProbeBinder_DoesNotExposeAkburaCSharpOperationTree()
    {
        var csharpOperationType = typeof(ICSharpOperation);
        var binderType = typeof(CSharpProbeBinder);
        var source = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Binder",
            "CSharpProbeBinder.cs");

        Assert.DoesNotContain(binderType.GetMethods(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic),
            method =>
                csharpOperationType.IsAssignableFrom(method.ReturnType) ||
                method.GetParameters().Any(parameter =>
                    csharpOperationType.IsAssignableFrom(parameter.ParameterType)));
        Assert.DoesNotContain(nameof(CSharpOperationTreeBuilder), source);
    }


    [Fact]
    public void OperationFactory_DoesNotReferenceSemanticModelFacade()
    {
        var source = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Operations",
            "AkburaOperationFactory.cs");

        Assert.DoesNotContain(nameof(AkburaSemanticModel), source);
        Assert.DoesNotContain("_semanticModel", source);
        Assert.DoesNotContain(".GetSymbolInfo(", source);
        Assert.DoesNotContain(".GetOperation(", source);
        Assert.DoesNotContain(".GetSemanticDiagnostics(", source);
        Assert.DoesNotContain(nameof(BindingSession), source);
        Assert.Contains("CSharpOperationTreeBuilder.Create", source);
    }


    [Fact]
    public void AkcssOperationMaterializer_OwnsAkcssOperationBatchMaterialization()
    {
        var boundFactorySource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Compilation",
            "AkcssBoundNodeFactory.cs");
        var materializerSource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Compilation",
            "AkcssOperationMaterializer.cs");
        var semanticModelSource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Compilation",
            "AkburaSemanticModel.cs");

        Assert.Contains("internal sealed class AkcssBoundNodeFactory", boundFactorySource);
        Assert.Contains("CreateSyntax(", boundFactorySource);
        Assert.Contains("CreateBoundOperations(", boundFactorySource);
        Assert.Contains("CreatePropertySetter(", boundFactorySource);
        Assert.Contains("CreateIf(", boundFactorySource);
        Assert.Contains("new BoundAkcss", boundFactorySource);
        Assert.Contains("internal sealed class AkcssOperationMaterializer", materializerSource);
        Assert.Contains("CreateOperations(", materializerSource);
        Assert.Contains("boundNodes.CreatePropertySetter", materializerSource);
        Assert.Contains("boundNodes.CreateIf", materializerSource);
        Assert.Contains("boundNodes.CreateApply", materializerSource);
        Assert.Contains("boundNodes.CreateIntercept", materializerSource);
        Assert.DoesNotContain("new BoundAkcss", materializerSource);
        Assert.DoesNotContain("private ImmutableArray<BoundAkcssOperation> CreateBoundOperations", materializerSource);
        Assert.Contains("new AkcssBoundNodeFactory(this)", semanticModelSource);
        Assert.Contains("internal AkcssBoundNodeFactory AkcssBoundNodes", semanticModelSource);
        Assert.Contains("_akcssOperationMaterializer.CreateOperations", semanticModelSource);
        Assert.DoesNotContain("_akcssOperationMaterializer.CreateBoundOperations", semanticModelSource);
        Assert.DoesNotContain("new BoundAkcss", semanticModelSource);
        Assert.DoesNotContain("CreateBoundAkcss", semanticModelSource);
        Assert.DoesNotContain("CreateAkcssBoundOperations", semanticModelSource);
        Assert.DoesNotContain("interceptOperationsBuilder", semanticModelSource);
        Assert.DoesNotContain("GetOrCreateAkcssOperation", semanticModelSource);
    }


    [Fact]
    public void MarkupBoundNodeFactory_OwnsMarkupSemanticBoundNodeConstruction()
    {
        var boundFactorySource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Compilation",
            "MarkupBoundNodeFactory.cs");
        var binderSource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Binder",
            "MarkupBinder.cs");
        var semanticModelSource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Compilation",
            "AkburaSemanticModel.cs");
        var markupOperationsSource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Compilation",
            "AkburaSemanticModel.MarkupOperations.cs");

        Assert.Contains("internal sealed class MarkupBoundNodeFactory", boundFactorySource);
        Assert.Contains("CreateSyntax(", boundFactorySource);
        Assert.Contains("new BoundMarkupRoot", boundFactorySource);
        Assert.Contains("new BoundMarkupComponent", boundFactorySource);
        Assert.Contains("new BoundMarkupContentSetter", boundFactorySource);
        Assert.Contains("new BoundMarkupContent", boundFactorySource);
        Assert.Contains("SemanticModel.MarkupBoundNodes.CreateSyntax", binderSource);
        Assert.DoesNotContain("SemanticModel.CreateBoundMarkupSyntax", binderSource);
        Assert.Contains("new MarkupBoundNodeFactory(this)", semanticModelSource);
        Assert.Contains("internal MarkupBoundNodeFactory MarkupBoundNodes", semanticModelSource);
        Assert.DoesNotContain("new BoundMarkup", semanticModelSource);
        Assert.DoesNotContain("CreateBoundMarkup", semanticModelSource);
        Assert.DoesNotContain("new Bound", markupOperationsSource);
        Assert.DoesNotContain("BoundTailwindUtilityArgument", markupOperationsSource);
    }


    [Fact]
    public void BindingSession_DelegatesOperationBindingToBinderChain()
    {
        var source = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Binder",
            "BindingSession.cs");
        var semanticModelSource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Compilation",
            "AkburaSemanticModel.cs");
        var markupSemanticModelSource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Compilation",
            "AkburaSemanticModel.MarkupOperations.cs");

        Assert.Contains("GetMemberSemanticModel(syntax).BindOperationSyntax(syntax)", source);
        Assert.DoesNotContain("CreateBoundMarkupAttribute", source);
        Assert.DoesNotContain("CreateBoundAkcss", source);
        Assert.DoesNotContain("BindMarkupAttributeOperation", semanticModelSource + markupSemanticModelSource);
        Assert.DoesNotContain("BindAkcssPropertySetterOperation", semanticModelSource + markupSemanticModelSource);
        Assert.DoesNotContain("ResolveMarkupAttributeOperation", semanticModelSource + markupSemanticModelSource);
        Assert.DoesNotContain("ResolveAkcssPropertySetterOperation", semanticModelSource + markupSemanticModelSource);
    }


    [Fact]
    public void OperationBearingBinders_OwnMarkupAndAkcssOperationEntrypoints()
    {
        var semanticModelSource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Compilation",
            "AkburaSemanticModel.cs");
        var markupSemanticModelSource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Compilation",
            "AkburaSemanticModel.MarkupOperations.cs");
        var markupBinderSource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Binder",
            "MarkupBinder.cs");
        var tailwindBinderSource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Binder",
            "MarkupBinder.Tailwind.cs");
        var akcssStyleBinderSource = ReadRepositoryFile(
            "Akbura.Generator",
            "Language",
            "Binder",
            "AkcssStyleBinder.cs");

        Assert.Contains("CreateBoundTailwindUtilityAttribute", markupBinderSource + tailwindBinderSource);
        Assert.DoesNotContain("CreateBoundTailwindUtilityAttribute", markupSemanticModelSource);

        Assert.Contains("BindAkcssPropertySetter", akcssStyleBinderSource);
        Assert.Contains("BindAkcssIf", akcssStyleBinderSource);
        Assert.Contains("BindAkcssApply", akcssStyleBinderSource);
        Assert.Contains("BindAkcssIntercept", akcssStyleBinderSource);
        Assert.DoesNotContain("CreateBoundAkcssPropertySetter(AkcssAssignmentSyntax", semanticModelSource);
        Assert.DoesNotContain("CreateBoundAkcssIf(AkcssIfDirectiveSyntax", semanticModelSource);
        Assert.DoesNotContain("CreateBoundAkcssApply(AkcssApplyDirectiveSyntax", semanticModelSource);
        Assert.DoesNotContain("CreateBoundAkcssIntercept(AkcssInterceptDirectiveSyntax", semanticModelSource);
    }


    [Fact]
    public void OperationFactory_CreatesOperationsFromBoundOperationNodes()
    {
        const string code =
            """
            using Avalonia.Controls;

            @akcss {
                Button.surface {
                    Width: 10;
                }

                Button.card {
                    @apply surface;
                    @if(true) {
                        Height: 20;
                    }
                }

                @utilities {
                    .w-(double value) {
                        Width: value;
                    }
                }
            }

            <TextBlock Text="Hello" w-30 />
            """;
        var tree = AkburaSyntaxTree.ParseText(code, "Dashboard.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var root = tree.GetRoot();
        var akcss = Assert.IsType<InlineAkcssBlockSyntax>(root.Members[1]);
        var surface = Assert.IsType<AkcssStyleRuleSyntax>(akcss.Members[0]);
        var card = Assert.IsType<AkcssStyleRuleSyntax>(akcss.Members[1]);
        var assignment = Assert.IsType<AkcssAssignmentSyntax>(surface.Members[0]);
        var apply = Assert.IsType<AkcssApplyDirectiveSyntax>(card.Members[0]);
        var ifDirective = Assert.IsType<AkcssIfDirectiveSyntax>(card.Members[1]);
        var markup = Assert.IsType<MarkupRootSyntax>(root.Members[2]);
        var textAttribute = Assert.IsType<MarkupPlainAttributeSyntax>(
            markup.Element.StartTag!.Attributes[0]);
        var tailwindAttribute = Assert.IsType<TailwindFullAttributeSyntax>(
            markup.Element.StartTag!.Attributes[1]);

        Assert.IsType<BoundMarkupPropertySetter>(
            model.BindingSession.BindOperationSyntax(textAttribute));
        Assert.IsAssignableFrom<IMarkupPropertySetterOperation>(
            model.GetOperation(textAttribute));

        Assert.IsType<BoundTailwindUtilityAttribute>(
            model.BindingSession.BindOperationSyntax(tailwindAttribute));
        Assert.IsAssignableFrom<ITailwindUtilityAttributeOperation>(
            model.GetOperation(tailwindAttribute));

        Assert.IsType<BoundAkcssPropertySetter>(
            model.BindingSession.BindOperationSyntax(assignment));
        Assert.IsAssignableFrom<IAkcssPropertySetterOperation>(
            model.GetOperation(assignment));

        Assert.IsType<BoundAkcssApply>(
            model.BindingSession.BindOperationSyntax(apply));
        Assert.IsAssignableFrom<IAkcssApplyOperation>(
            model.GetOperation(apply));

        Assert.IsType<BoundAkcssIf>(
            model.BindingSession.BindOperationSyntax(ifDirective));
        Assert.IsAssignableFrom<IAkcssIfOperation>(
            model.GetOperation(ifDirective));
    }


    [Fact]
    public void MarkupBinder_BindsPropertyAndEventAttributesToBoundNodes()
    {
        const string code =
            "using Avalonia.Controls;\n" +
            "state int count = 0;\n" +
            "<StackPanel><TextBlock Text={count.ToString()} /><Button Click={() => count++} /></StackPanel>";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var model = CreateCompilation(tree).GetSemanticModel(tree);
        var root = tree.GetRoot();
        var markup = Assert.IsType<MarkupRootSyntax>(root.Members[2]);
        var textBlockContent = Assert.IsType<MarkupElementContentSyntax>(markup.Element.Body[0]);
        var buttonContent = Assert.IsType<MarkupElementContentSyntax>(markup.Element.Body[1]);
        var textBlock = textBlockContent.Element;
        var button = buttonContent.Element;
        var textAttribute = Assert.IsType<MarkupPlainAttributeSyntax>(
            textBlock.StartTag!.Attributes[0]);
        var clickAttribute = Assert.IsType<MarkupPlainAttributeSyntax>(
            button.StartTag!.Attributes[0]);

        var propertyBoundNode = Assert.IsType<BoundMarkupPropertySetter>(
            model.BindingSession.BindOperationSyntax(textAttribute));
        var eventBoundNode = Assert.IsType<BoundMarkupRoutedEventBinding>(
            model.BindingSession.BindOperationSyntax(clickAttribute));

        Assert.IsType<MarkupBinder>(propertyBoundNode.Binder);
        Assert.IsType<MarkupBinder>(eventBoundNode.Binder);
        Assert.Equal("Text", propertyBoundNode.Property?.Name);
        Assert.Equal("Click", eventBoundNode.RoutedEvent.Name);
    }


    [Fact]
    public void SymbolVisitor_DispatchesConcreteAkburaSymbols()
    {
        const string code =
            "inject int service;\n" +
            "param int UserId = 1;\n" +
            "state int count = 0;\n" +
            "command int Refresh(int id);";
        var tree = AkburaSyntaxTree.ParseText(code, "Counter.akbura");
        var compilation = CreateCompilation(tree);
        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();
        var inject = Assert.IsType<InjectDeclarationSyntax>(root.Members[0]);
        var param = Assert.IsType<ParamDeclarationSyntax>(root.Members[1]);
        var state = Assert.IsType<StateDeclarationSyntax>(root.Members[2]);
        var command = Assert.IsType<CommandDeclarationSyntax>(root.Members[3]);
        var visitor = new RecordingSymbolVisitor();

        visitor.Visit(model.GetSymbolInfo(inject).Symbol);
        visitor.Visit(model.GetSymbolInfo(param).Symbol);
        visitor.Visit(model.GetSymbolInfo(state).Symbol);
        visitor.Visit(model.GetSymbolInfo(command).Symbol);

        Assert.Equal(
            ["inject", "param", "state", "command"],
            visitor.Visited);
    }


    private sealed class RecordingOperationWalker : OperationWalker
    {
        public List<AkburaOperationKind> Kinds { get; } = [];

        public override void DefaultVisit(AkburaOperation operation)
        {
            Kinds.Add(operation.Kind);
            base.DefaultVisit(operation);
        }
    }


    private sealed class RecordingSymbolVisitor : AkburaSymbolVisitor
    {
        public List<string> Visited { get; } = [];

        public override void VisitInject(IInjectSymbol symbol)
        {
            Visited.Add("inject");
        }

        public override void VisitParameter(IParamSymbol symbol)
        {
            Visited.Add("param");
        }

        public override void VisitState(IStateSymbol symbol)
        {
            Visited.Add("state");
        }

        public override void VisitCommand(ICommandSymbol symbol)
        {
            Visited.Add("command");
        }
    }

}
