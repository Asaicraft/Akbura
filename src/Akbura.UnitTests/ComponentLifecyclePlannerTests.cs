using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;

namespace Akbura.UnitTests;

public sealed class ComponentLifecyclePlannerTests
{
    [Fact]
    public void SingleControlRoot_SelectsDenseComponentElement()
    {
        var plan = CreatePlan(
            """
            using Avalonia.Controls;

            <Border />
            """);

        Assert.True(plan.Lifecycle.HasRootElement);
        Assert.False(plan.Lifecycle.UsesFallbackRoot);
        Assert.Equal(0, plan.Lifecycle.RootElementId);
        Assert.Equal(0, plan.Elements[plan.Lifecycle.RootElementId].ScopeId);
        Assert.True(plan.Elements[plan.Lifecycle.RootElementId].IsControl);
    }

    [Theory]
    [InlineData("using Avalonia.Controls;")]
    [InlineData("using Avalonia.Controls;\r\n\r\n<Border />\r\n<Button />")]
    [InlineData("using Avalonia.Markup.Xaml.Templates;\r\n\r\n<DataTemplate />")]
    public void InvalidRootShape_UsesFallback(string component)
    {
        var plan = CreatePlan(component);

        Assert.False(plan.Lifecycle.HasRootElement);
        Assert.True(plan.Lifecycle.UsesFallbackRoot);
        Assert.Equal(-1, plan.Lifecycle.RootElementId);
    }

    [Fact]
    public void LocalTemplateRoot_DoesNotReplaceComponentRoot()
    {
        var plan = CreatePlan(
            """
            using Avalonia.Controls;

            <ItemsControl>
                <ItemsControl.ItemTemplate>
                    <Border />
                </ItemsControl.ItemTemplate>
            </ItemsControl>
            """);
        ref readonly var root = ref plan.Elements.ItemRef(
            plan.Lifecycle.RootElementId);
        var template = Assert.Single(plan.Templates);
        ref readonly var templateScope = ref plan.Scopes.ItemRef(template.ScopeId);
        var localRootId = plan.ScopeRootElementIds[templateScope.Roots.Start];

        Assert.Equal("ItemsControl", root.Syntax.StartTag!.Name.ToFullString().Trim());
        Assert.Equal(0, root.ScopeId);
        Assert.NotEqual(root.Id, localRootId);
        Assert.True(plan.Elements[localRootId].IsLocal);
    }

    [Fact]
    public void SimpleStaticTree_DoesNotRequireBaseUri()
    {
        var plan = CreatePlan(
            """
            using Avalonia.Controls;

            <Border Width="42" />
            """);

        Assert.False(plan.Lifecycle.RequiresBaseUri);
    }

    [Fact]
    public void DeferredContent_RequiresBaseUri()
    {
        var plan = CreatePlan(
            """
            using Avalonia.Controls;
            using Avalonia.Markup.Xaml.Templates;

            <DataTemplate>
                <Border />
            </DataTemplate>
            """);

        Assert.Single(plan.DeferredContents);
        Assert.True(plan.Lifecycle.RequiresBaseUri);
    }

    [Fact]
    public void ServiceProviderMarkupExtension_RequiresBaseUri()
    {
        var plan = CreatePlan(
            """
            using Avalonia.Controls;
            using Demo.Extensions;

            <Border Width=${DirectPadding 4} />
            """,
            AkcssActivatorPlannerTests.ExtensionSource);

        Assert.Single(plan.MarkupExtensions);
        Assert.True(plan.Lifecycle.RequiresBaseUri);
    }

    [Fact]
    public void NestedServiceProviderMarkupExtension_RequiresBaseUri()
    {
        var plan = CreatePlan(
            """
            using Avalonia.Controls;
            using Demo.Extensions;

            <Border Width=${PassThrough ${PassThrough ${NeedsServices}}} />
            """,
            NestedBaseUriExtensionSource);
        var outer = Assert.Single(plan.MarkupExtensions).Extension;
        var middle = Assert.IsType<MarkupExtensionValue>(
            Assert.Single(outer.Arguments).NestedValue);
        var inner = Assert.IsType<MarkupExtensionValue>(
            Assert.Single(middle.Arguments).NestedValue);

        Assert.Equal("NeedsServices", inner.Name);
        Assert.True(plan.Lifecycle.RequiresBaseUri);
    }

    [Fact]
    public void NestedBindingServiceProviderMarkupExtension_RequiresBaseUri()
    {
        var plainPlan = CreatePlan(
            """
            using Avalonia.Controls;

            <TextBlock Text=${Binding .} />
            """);
        var plan = CreatePlan(
            """
            using Avalonia.Controls;
            using Demo.Extensions;

            <TextBlock Text=${Binding ., ConverterParameter=${NeedsServices}} />
            """,
            NestedBaseUriExtensionSource);
        var binding = Assert.Single(plan.Bindings);
        var property = Assert.Single(binding.Extension.Properties);

        Assert.False(plainPlan.Lifecycle.RequiresBaseUri);
        Assert.Null(binding.Extension.ProvideValueMethod.Symbol);
        Assert.NotNull(property.NestedValue);
        Assert.True(plan.Lifecycle.RequiresBaseUri);
    }

    [Fact]
    public void NestedAkcssServiceProviderMarkupExtension_RequiresBaseUri()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Demo.Extensions;

            @akcss {
                @using Avalonia.Controls;

                @utilities {
                    Control.probe-(double value) { Width: value; }
                }
            }

            <Border probe-${PassThrough ${NeedsServices}} />
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(
            component,
            NestedBaseUriExtensionSource);
        var inlineAkcss = Assert.Single(
            fixture.ComponentTree.GetRoot().Members.OfType<InlineAkcssBlockSyntax>());
        var plan = CreatePlan(
            fixture,
            new Dictionary<AkburaSyntax, string>
            {
                [inlineAkcss] = "global::Demo.NestedBaseUriStyles",
            });
        var slot = Assert.Single(plan.Akcss.MarkupExtensionSlots);

        Assert.NotNull(Assert.Single(slot.Extension.Arguments).NestedValue);
        Assert.True(plan.Lifecycle.RequiresBaseUri);
    }

    [Fact]
    public void RenderStatements_PreserveOrderAndFullBlockWhileExcludingLocalFunction()
    {
        const string component =
            """
            using Avalonia.Controls;

            RenderFirst();

            if (ShouldRender)
            {
                RenderSecond();
            }

            void LocalHelper()
            {
                RenderSecond();
            }

            <Border />
            """;
        const string csharp =
            """
            namespace Demo;

            public partial class PlannerView
            {
                private bool ShouldRender => true;

                private void RenderFirst()
                {
                }

                private void RenderSecond()
                {
                }
            }
            """;
        var plan = CreatePlan(component, csharp);

        Assert.Collection(
            plan.RenderStatements,
            first =>
            {
                Assert.Equal(ComponentRenderStatementKind.Statement, first.Kind);
                Assert.StartsWith("RenderFirst();", first.Node.ToFullString().Trim());
            },
            second =>
            {
                Assert.Equal(ComponentRenderStatementKind.Statement, second.Kind);
                var text = second.Node.ToFullString();
                Assert.Contains("if (ShouldRender)", text, StringComparison.Ordinal);
                Assert.Contains("RenderSecond();", text, StringComparison.Ordinal);
            });
    }

    [Fact]
    public void SemanticallyInvalidStatement_IsSkipped()
    {
        const string component =
            """
            using Avalonia.Controls;

            MissingMethod();
            ValidMethod();

            <Border />
            """;
        const string csharp =
            """
            namespace Demo;

            public partial class PlannerView
            {
                private void ValidMethod()
                {
                }
            }
            """;
        var statement = Assert.Single(CreatePlan(component, csharp).RenderStatements);

        Assert.Contains("ValidMethod();", statement.Node.ToFullString(), StringComparison.Ordinal);
    }

    [Fact]
    public void UseHook_StoresEffectiveInvocation()
    {
        const string component =
            """
            using Avalonia.Controls;
            using Hooks;

            useControlValue(Width);

            <Border />
            """;
        const string csharp =
            """
            using Akbura.CompilerAnotations;
            using Avalonia;
            using Avalonia.Controls;

            namespace Demo
            {
                public partial class PlannerView : Control
                {
                }
            }

            namespace Hooks
            {
                public static class ControlHooks
                {
                    [UseHook]
                    public static void useControlValue<T>(
                        [Self] T control,
                        AvaloniaProperty<double> property)
                        where T : Control
                    {
                    }
                }
            }
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(component, csharp);
        var syntax = Assert.Single(
            fixture.ComponentTree.GetRoot().Members.OfType<CSharpStatementSyntax>());
        var hook = Assert.IsAssignableFrom<IUseHookOperation>(
            fixture.SemanticModel.GetOperation(syntax));
        var plan = CreatePlan(fixture);
        var statement = Assert.Single(plan.RenderStatements);

        Assert.Equal(ComponentRenderStatementKind.UseHookInvocation, statement.Kind);
        Assert.Same(syntax, statement.Syntax);
        Assert.Equal(
            hook.EffectiveInvocation.ToFullString(),
            statement.Node.ToFullString());
        Assert.Contains("this", statement.Node.ToFullString(), StringComparison.Ordinal);
        Assert.Contains("WidthProperty", statement.Node.ToFullString(), StringComparison.Ordinal);
    }

    private const string NestedBaseUriExtensionSource =
        """
        namespace Demo.Extensions;

        public sealed class PassThroughExtension
        {
            public PassThroughExtension(double value)
            {
                Value = value;
            }

            public double Value { get; }

            public double ProvideValue() => Value;
        }

        public sealed class NeedsServicesExtension
        {
            public double ProvideValue(System.IServiceProvider services) => 4d;
        }
        """;

    private static ComponentPlan CreatePlan(
        string component,
        string? additionalCSharp = null)
    {
        return CreatePlan(
            AkcssActivatorPlannerTests.CreateFixture(
                component,
                additionalCSharp));
    }

    private static ComponentPlan CreatePlan(
        AkcssActivatorPlannerTests.PlannerFixture fixture)
    {
        return CreatePlan(
            fixture,
            new Dictionary<AkburaSyntax, string>());
    }

    private static ComponentPlan CreatePlan(
        AkcssActivatorPlannerTests.PlannerFixture fixture,
        IReadOnlyDictionary<AkburaSyntax, string> akcssModuleTypeNames)
    {
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            fixture.SemanticModel.GetSymbolInfo(
                fixture.ComponentTree.GetRoot()).Symbol);

        return ComponentPlanner.Create(
            component,
            fixture.SemanticModel,
            akcssModuleTypeNames);
    }
}
