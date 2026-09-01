using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;

namespace Akbura.UnitTests;

public sealed class AkcssActivatorPlannerTests
{
    [Fact]
    public void Create_PreservesOrderRangesAndResolvedStyleKinds()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Akbura.Styles.akcss;
            using Styles.akcss;

            <StackPanel>
                <Border Name="first" class="card" utility p-4 missing />
                <Button class="card" utility />
            </StackPanel>
            """;
        const string akcss =
            """
            @using Avalonia.Controls;

            .card { Opacity: 0.5; }
            Button.card { Padding: 8; }

            @utilities {
                .utility { Height: 10; }
                Border.utility { Width: 20; }
            }
            """;
        var fixture = CreateFixture(component, externalAkcss: akcss);
        var elements = fixture.GetChildElements();
        var firstElementSymbol = fixture.GetElementSymbol(elements[0]);
        var metadataOperation = Assert.Single(
            firstElementSymbol.AttributeOperations
                .OfType<ITailwindUtilityAttributeOperation>(),
            static operation => operation.Utilities.Any(
                static utility => utility is IMetadataAkcssSymbol));
        var metadataUtility = Assert.IsAssignableFrom<IMetadataAkcssSymbol>(
            Assert.Single(metadataOperation.Utilities));
        var moduleTypeNames = new Dictionary<AkburaSyntax, string>
        {
            [fixture.ExternalAkcssTree!.GetRoot()] =
                "global::Demo.GeneratedStyles",
        };
        var plan = AkcssActivatorPlanner.Create(
            fixture.SemanticModel,
            ImmutableArray.Create(
                new AkcssActivatorElementInput(
                    10,
                    firstElementSymbol,
                    fixture.GetElementType(elements[0]),
                    requiresLocalMarkupExtensionContext: false),
                new AkcssActivatorElementInput(
                    20,
                    fixture.GetElementSymbol(elements[1]),
                    fixture.GetElementType(elements[1]),
                    requiresLocalMarkupExtensionContext: false)),
            moduleTypeNames);

        Assert.Collection(
            plan.Elements,
            element =>
            {
                Assert.Equal(10, element.ElementId);
                Assert.Equal(0, element.Activators.Start);
                Assert.Equal(3, element.Activators.Length);
                AssertRange(element.MarkupExtensionSlots, 0, 0);
            },
            element =>
            {
                Assert.Equal(20, element.ElementId);
                Assert.Equal(3, element.Activators.Start);
                Assert.Equal(3, element.Activators.Length);
                AssertRange(element.MarkupExtensionSlots, 0, 0);
            });
        Assert.Equal(
            [
                AkcssActivatorKind.Class,
                AkcssActivatorKind.UtilityCandidate,
                AkcssActivatorKind.UtilityCandidate,
                AkcssActivatorKind.Class,
                AkcssActivatorKind.Class,
                AkcssActivatorKind.UtilityCandidate,
            ],
            plan.Activators.Select(static activator => activator.Kind));
        Assert.Equal(
            [2, 3, 1],
            plan.Candidates.Select(static candidate => candidate.SourceOrder));
        Assert.Collection(
            plan.ApplicationCaches,
            cache => AssertRange(cache.Applications, 0, 2),
            cache => AssertRange(cache.Applications, 2, 1),
            cache => AssertRange(cache.Applications, 3, 1));
        Assert.DoesNotContain(
            plan.Candidates,
            static candidate => candidate.ConflictKey == "missing");
        Assert.Equal(
            [0, 1],
            plan.ClassCaches.Select(static cache => cache.Style.StyleIndex));
        Assert.All(
            plan.ClassCaches,
            static cache => AssertGeneratedReference(cache.Style));
        var generatedApplications = plan.Applications
            .Where(static application =>
                application.Reference.Kind == AkcssStyleReferenceKind.GeneratedModule)
            .ToArray();
        Assert.Equal(
            [2, 3, 2],
            generatedApplications.Select(static application => application.Reference.StyleIndex));
        Assert.All(
            generatedApplications,
            static application => AssertGeneratedReference(application.Reference));
        var metadataApplication = Assert.Single(
            plan.Applications,
            static application =>
                application.Reference.Kind == AkcssStyleReferenceKind.MetadataModule);
        Assert.Equal(
            metadataUtility.RuntimeStyleIndex,
            metadataApplication.Reference.StyleIndex);
        Assert.True(
            SymbolEqualityComparer.Default.Equals(
                metadataUtility.MetadataModule.RuntimeModuleType,
                metadataApplication.Reference.RuntimeModuleType));
    }

    [Fact]
    public void Create_ClassifiesValueSourcesAndChoosesFactoryByContext()
    {
        var fixture = CreateRichMarkupExtensionFixture();
        var element = fixture.GetRootElement();
        var symbol = fixture.GetElementSymbol(element);
        Assert.Equal(5, symbol.AttributeOperations.Length);
        Assert.All(
            symbol.AttributeOperations,
            static operation => Assert.False(
                operation.HasErrors,
                operation.Syntax.ToFullString()));
        var inlineAkcss = Assert.Single(
            fixture.ComponentTree.GetRoot().Members
                .OfType<InlineAkcssBlockSyntax>());
        var plan = AkcssActivatorPlanner.Create(
            fixture.SemanticModel,
            ImmutableArray.Create(
                new AkcssActivatorElementInput(
                    7,
                    symbol,
                    fixture.GetElementType(element),
                    requiresLocalMarkupExtensionContext: false),
                new AkcssActivatorElementInput(
                    8,
                    symbol,
                    fixture.GetElementType(element),
                    requiresLocalMarkupExtensionContext: true)),
            new Dictionary<AkburaSyntax, string>
            {
                [inlineAkcss] = "global::Demo.RichStyles",
            });

        var expectedKinds = new[]
        {
            AkcssUtilityValueSourceKind.Direct,
            AkcssUtilityValueSourceKind.Object,
            AkcssUtilityValueSourceKind.Observable,
            AkcssUtilityValueSourceKind.ObservableObject,
            AkcssUtilityValueSourceKind.Binding,
        };

        Assert.Collection(
            plan.Elements,
            element =>
            {
                Assert.Equal(7, element.ElementId);
                AssertRange(element.MarkupExtensionSlots, 0, 5);
            },
            element =>
            {
                Assert.Equal(8, element.ElementId);
                AssertRange(element.MarkupExtensionSlots, 5, 5);
            });
        Assert.Equal(10, plan.ValueSources.Length);
        Assert.Equal(
            expectedKinds,
            plan.ValueSources.Take(5).Select(static source => source.Kind));
        Assert.Equal(
            expectedKinds,
            plan.ValueSources.Skip(5).Select(static source => source.Kind));
        Assert.Equal(
            [
                SpecialType.System_Double,
                SpecialType.System_Object,
                SpecialType.System_Double,
                SpecialType.System_Double,
                SpecialType.System_Double,
                SpecialType.System_Double,
                SpecialType.System_Object,
                SpecialType.System_Double,
                SpecialType.System_Double,
                SpecialType.System_Double,
            ],
            plan.ValueSources.Select(static source => source.ExpectedType.SpecialType));
        Assert.All(
            plan.ValueSources.Take(5),
            static source => Assert.True(source.UseFactoryMethod));
        Assert.All(
            plan.ValueSources.Skip(5),
            static source => Assert.False(source.UseFactoryMethod));
        Assert.All(
            plan.MarkupExtensionSlots.Take(5),
            static slot => Assert.True(slot.NeedsFactoryMethod));
        Assert.All(
            plan.MarkupExtensionSlots.Skip(5),
            static slot => Assert.False(slot.NeedsFactoryMethod));
        Assert.All(
            plan.MarkupExtensionSlots,
            static slot => Assert.True(slot.NeedsTargetProperty));
        Assert.True(plan.ValueSources[0].RecreateOnRefresh);
        Assert.All(
            plan.ValueSources.Skip(1).Take(4),
            static source => Assert.False(source.RecreateOnRefresh));
        Assert.Equal(
            SpecialType.System_Double,
            plan.ValueSources[2].ObservableElementType?.SpecialType);
        Assert.Equal(
            SpecialType.System_Object,
            plan.ValueSources[3].ObservableElementType?.SpecialType);
        Assert.Equal(
            [
                "double",
                "object?",
                "System.IObservable<double>?",
                "System.IObservable<object?>?",
                "Avalonia.Data.BindingBase",
            ],
            plan.MarkupExtensionSlots.Take(5).Select(
                static slot => slot.FactoryValueType.ToDisplayString()));
        Assert.True(
            SymbolEqualityComparer.Default.Equals(
                plan.ValueSources[0].ExpectedType,
                plan.MarkupExtensionSlots[0].FactoryValueType));
        Assert.All(
            plan.MarkupExtensionSlots,
            static slot => Assert.True(slot.IsControlTarget));
    }

    [Fact]
    public void Create_UsesParameterTypeFromFirstResolvedUtility()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Demo.Extensions;

            @akcss {
                @using Avalonia.Controls;

                @utilities {
                    .mixed-(double value) { Width: value; }
                    Border.mixed-(int value) { Height: value; }
                }
            }

            <Border mixed-4 />
            """;
        var fixture = CreateFixture(component, ExtensionSource);
        var element = fixture.GetRootElement();
        var symbol = fixture.GetElementSymbol(element);
        var operation = Assert.IsAssignableFrom<
            ITailwindUtilityAttributeOperation>(
                Assert.Single(symbol.AttributeOperations));
        var inlineAkcss = Assert.Single(
            fixture.ComponentTree.GetRoot().Members
                .OfType<InlineAkcssBlockSyntax>());
        var plan = AkcssActivatorPlanner.Create(
            fixture.SemanticModel,
            ImmutableArray.Create(
                new AkcssActivatorElementInput(
                    0,
                    symbol,
                    fixture.GetElementType(element),
                    requiresLocalMarkupExtensionContext: false)),
            new Dictionary<AkburaSyntax, string>
            {
                [inlineAkcss] = "global::Demo.MixedStyles",
            });

        Assert.Equal(2, operation.Utilities.Length);
        Assert.False(
            SymbolEqualityComparer.Default.Equals(
                operation.Utilities[0].Parameters[0].Type.Symbol,
                operation.Utilities[1].Parameters[0].Type.Symbol));
        var firstParameterType = Assert.IsAssignableFrom<ITypeSymbol>(
            Assert.Single(operation.Utilities[0].Parameters).Type.Symbol);
        var source = Assert.Single(plan.ValueSources);

        Assert.True(
            SymbolEqualityComparer.Default.Equals(
                firstParameterType,
                source.ExpectedType));
        Assert.Equal(2, Assert.Single(plan.ApplicationCaches).Applications.Length);
    }

    internal static PlannerFixture CreateRichMarkupExtensionFixture()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Demo.Extensions;

            @akcss {
                @using Avalonia.Controls;

                @utilities {
                    Control.direct-(double value) { Width: value; }
                    Control.late-(object value) { DataContext: value; }
                    Control.observable-(double value) { Width: value; }
                    Control.object-observable-(double value) { Width: value; }
                    Control.binding-(double value) { Width: value; }
                }
            }

            state double spacing = 4;

            <Border
                direct-${DirectPadding {spacing + 1}}
                late-${ObjectPadding}
                observable-${ObservablePadding}
                object-observable-${ObjectObservablePadding}
                binding-${BindingPadding} />
            """;

        return CreateFixture(component, ExtensionSource);
    }

    private static void AssertGeneratedReference(AkcssStyleReferencePlan reference)
    {
        Assert.Equal(AkcssStyleReferenceKind.GeneratedModule, reference.Kind);
        Assert.Equal("global::Demo.GeneratedStyles", reference.GeneratedModuleTypeName);
        Assert.Null(reference.RuntimeModuleType);
    }

    private static void AssertRange(AkcssPlanRange range, int start, int length)
    {
        Assert.Equal(start, range.Start);
        Assert.Equal(length, range.Length);
    }

    internal static PlannerFixture CreateFixture(
        string component,
        string? additionalCSharp = null,
        string? externalAkcss = null)
    {
        const string hostSource =
            """
            namespace Demo;

            public partial class PlannerView
            {
            }
            """;
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(
            LanguageVersion.Preview);
        var syntaxTrees = additionalCSharp == null
            ? new[] { CSharpSyntaxTree.ParseText(hostSource, parseOptions) }
            : new[]
            {
                CSharpSyntaxTree.ParseText(hostSource, parseOptions),
                CSharpSyntaxTree.ParseText(additionalCSharp, parseOptions),
            };
        var csharpCompilation = CSharpCompilation.Create(
            assemblyName: "AkcssActivatorPlannerTests",
            syntaxTrees,
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var componentTree = AkburaSyntaxTree.ParseText(
            component,
            "PlannerView.akbura");
        var akcssTree = externalAkcss == null
            ? null
            : AkcssSyntaxTree.ParseText(
                externalAkcss,
                "Styles.akcss",
                "Styles.akcss");
        var compilation = akcssTree == null
            ? new AkburaCompilation(
                csharpCompilation,
                [componentTree],
                rootNamespace: "Demo")
            : new AkburaCompilation(
                csharpCompilation,
                [componentTree],
                [akcssTree],
                rootNamespace: "Demo");

        return new PlannerFixture(
            csharpCompilation,
            componentTree,
            akcssTree,
            compilation.GetSemanticModel(componentTree));
    }

    internal const string ExtensionSource =
        """
        namespace Demo.Extensions;

        public sealed class DirectPaddingExtension
        {
            public DirectPaddingExtension(double value)
            {
            }

            public double ProvideValue(System.IServiceProvider services) => 0;
        }

        public sealed class ObjectPaddingExtension
        {
            public dynamic ProvideValue(System.IServiceProvider services) => new object();
        }

        public sealed class ObservablePaddingExtension
        {
            public System.IObservable<double> ProvideValue(
                System.IServiceProvider services) => null!;
        }

        public sealed class ObjectObservablePaddingExtension
        {
            public System.IObservable<object> ProvideValue(
                System.IServiceProvider services) => null!;
        }

        public sealed class BindingPaddingExtension
        {
            public Avalonia.Data.BindingBase ProvideValue(
                System.IServiceProvider services) => null!;
        }
        """;

    internal sealed class PlannerFixture
    {
        public PlannerFixture(
            CSharpCompilation csharpCompilation,
            AkburaSyntaxTree componentTree,
            AkcssSyntaxTree? externalAkcssTree,
            AkburaSemanticModel semanticModel)
        {
            CSharpCompilation = csharpCompilation;
            ComponentTree = componentTree;
            ExternalAkcssTree = externalAkcssTree;
            SemanticModel = semanticModel;
        }

        public CSharpCompilation CSharpCompilation { get; }

        public AkburaSyntaxTree ComponentTree { get; }

        public AkcssSyntaxTree? ExternalAkcssTree { get; }

        public AkburaSemanticModel SemanticModel { get; }

        public MarkupElementSyntax GetRootElement()
        {
            return Assert.Single(
                ComponentTree.GetRoot().Members
                    .OfType<MarkupRootSyntax>()).Element;
        }

        public MarkupElementSyntax[] GetChildElements()
        {
            return GetRootElement().Body
                .OfType<MarkupElementContentSyntax>()
                .Select(static content => content.Element)
                .ToArray();
        }

        public IMarkupComponentSymbol GetElementSymbol(
            MarkupElementSyntax element)
        {
            return Assert.IsAssignableFrom<IMarkupComponentSymbol>(
                SemanticModel.GetSymbolInfo(element).Symbol);
        }

        public ITypeSymbol GetElementType(MarkupElementSyntax element)
        {
            Assert.True(SemanticModel.TryGetMarkupElementReferenceType(element, out var type));
            return Assert.IsAssignableFrom<ITypeSymbol>(type.Symbol);
        }

        public BindingWriterEnvironment CreateBindingEnvironment()
        {
            var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
                SemanticModel.GetSymbolInfo(
                    ComponentTree.GetRoot()).Symbol);
            return BindingWriterEnvironment.Create(
                SemanticModel,
                component);
        }
    }
}
