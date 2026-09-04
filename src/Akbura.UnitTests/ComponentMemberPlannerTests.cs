using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;

namespace Akbura.UnitTests;

public sealed class ComponentMemberPlannerTests
{
    [Fact]
    public void Parameters_PreserveDenseIdsBindingsDefaultsAndContent()
    {
        var plan = CreatePlan(
            """
            using System.Collections.Generic;

            param string Title = "Default";
            param bind int Value = 1;
            param out bool Changed;
            param IList<string?> Content;
            """);

        Assert.Collection(
            plan.Parameters,
            title =>
            {
                Assert.Equal(0, title.Id);
                Assert.Equal("Title", title.Name);
                Assert.Equal(SpecialType.System_String, title.Type.SpecialType);
                Assert.Equal(ParamBindingKind.Default, title.BindingKind);
                Assert.Equal(ComponentParameterKind.Value, title.Kind);
                Assert.True(title.HasDefaultValue);
                Assert.True(title.ReceivesValue);
                Assert.False(title.SendsValue);
                Assert.Equal("\"Default\"", title.DefaultValue!.ToFullString());
            },
            value =>
            {
                Assert.Equal(1, value.Id);
                Assert.Equal(ParamBindingKind.Bind, value.BindingKind);
                Assert.True(value.ReceivesValue);
                Assert.True(value.SendsValue);
            },
            changed =>
            {
                Assert.Equal(2, changed.Id);
                Assert.Equal(ParamBindingKind.Out, changed.BindingKind);
                Assert.False(changed.ReceivesValue);
                Assert.True(changed.SendsValue);
            },
            content =>
            {
                Assert.Equal(3, content.Id);
                Assert.True(content.IsContent);
                Assert.Equal(ComponentParameterKind.Collection, content.Kind);
                Assert.Equal("IList", content.Collection.PropertyType.Name);
                Assert.Equal("String", content.Collection.ElementType.Name);
                Assert.Equal(
                    NullableAnnotation.Annotated,
                    content.Collection.ElementType.NullableAnnotation);
                Assert.Equal("ObservableCollection", content.Collection.BackingType.Name);
                Assert.True(content.Collection.ObservesChanges);
            });
    }

    [Fact]
    public void CollectionParameters_PreserveSemanticBackingShape()
    {
        var plan = CreatePlan(
            """
            using System.Collections;
            using System.Collections.Generic;
            using System.Collections.ObjectModel;

            param IList Content;
            param ICollection<int> Items;
            param ObservableCollection<string> ObservableItems;
            param List<double> Values;
            param bind IList<int> BoundItems;
            """);

        Assert.Collection(
            plan.Parameters,
            content =>
            {
                Assert.Equal(ComponentParameterKind.Collection, content.Kind);
                Assert.Equal(SpecialType.System_Object, content.Collection.ElementType.SpecialType);
                Assert.Equal("ObservableCollection", content.Collection.BackingType.Name);
                Assert.True(content.Collection.ObservesChanges);
            },
            items =>
            {
                Assert.Equal(ComponentParameterKind.Collection, items.Kind);
                Assert.Equal("Int32", items.Collection.ElementType.Name);
                Assert.Equal("ObservableCollection", items.Collection.BackingType.Name);
                Assert.True(items.Collection.ObservesChanges);
            },
            observableItems =>
            {
                Assert.Equal(ComponentParameterKind.Collection, observableItems.Kind);
                Assert.True(SymbolEqualityComparer.Default.Equals(
                    observableItems.Type,
                    observableItems.Collection.BackingType));
                Assert.True(observableItems.Collection.ObservesChanges);
            },
            values =>
            {
                Assert.Equal(ComponentParameterKind.Collection, values.Kind);
                Assert.True(SymbolEqualityComparer.Default.Equals(
                    values.Type,
                    values.Collection.BackingType));
                Assert.False(values.Collection.ObservesChanges);
            },
            boundItems =>
            {
                Assert.Equal(ComponentParameterKind.Value, boundItems.Kind);
                Assert.Null(boundItems.Collection.PropertyType);
            });
    }

    [Fact]
    public void Services_NormalizeOnlyTheOuterNullableAnnotation()
    {
        const string csharp =
            """
            namespace Demo;

            public interface IService<T>
            {
            }
            """;
        var plan = CreatePlan(
            """
            using Demo;

            inject IService<string> required;
            inject IService<string?>? optional;
            """,
            csharp);

        Assert.Collection(
            plan.Services,
            required =>
            {
                Assert.Equal(0, required.Id);
                Assert.Equal("required", required.Name);
                Assert.False(required.IsOptional);
                Assert.Equal(
                    NullableAnnotation.NotAnnotated,
                    required.ServiceType.NullableAnnotation);
            },
            optional =>
            {
                Assert.Equal(1, optional.Id);
                Assert.Equal("optional", optional.Name);
                Assert.True(optional.IsOptional);
                Assert.Equal(
                    NullableAnnotation.NotAnnotated,
                    optional.ServiceType.NullableAnnotation);
                var namedType = Assert.IsAssignableFrom<INamedTypeSymbol>(
                    optional.ServiceType);
                Assert.Equal(
                    NullableAnnotation.Annotated,
                    Assert.Single(namedType.TypeArguments).NullableAnnotation);
            });
    }

    [Fact]
    public void States_UseDenseIdsAndPreserveReadOnlyBinding()
    {
        const string csharp =
            """
            namespace Demo;

            public partial class PlannerView : Avalonia.Controls.Control
            {
            }
            """;
        var plan = CreatePlan(
            """
            using Avalonia.Controls;

            state int count = 1;
            state double width = out Width;
            """,
            csharp);

        Assert.Collection(
            plan.States,
            count =>
            {
                Assert.Equal(0, count.Id);
                Assert.Equal(ComponentStateFactoryKind.Value, count.FactoryKind);
                Assert.Equal(StateBindingKind.None, count.BindingKind);
                Assert.False(count.IsReadOnly);
                Assert.False(count.UsesHook);
                Assert.Equal("1", count.Initializer.ToFullString());
            },
            width =>
            {
                Assert.Equal(1, width.Id);
                Assert.Equal(ComponentStateFactoryKind.Value, width.FactoryKind);
                Assert.Equal(StateBindingKind.Out, width.BindingKind);
                Assert.True(width.IsReadOnly);
                Assert.False(width.UsesHook);
            });
    }

    [Fact]
    public void HookState_StoresEffectiveInvocation()
    {
        const string csharp =
            """
            using Akbura.CompilerAnotations;
            using Akbura.ComponentTree;
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
                public static class PlannerHooks
                {
                    [UseHook]
                    public static State<double> useControlValue<T>(
                        [Self] T control,
                        AvaloniaProperty<double> property)
                        where T : Control => null!;
                }
            }
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(
            """
            using Hooks;

            state double width = useControlValue(Width);
            """,
            csharp);
        var stateSyntax = Assert.Single(
            fixture.ComponentTree.GetRoot().Members.OfType<StateDeclarationSyntax>());
        var operation = Assert.IsAssignableFrom<IUseHookOperation>(
            fixture.SemanticModel.GetOperation(stateSyntax.Initializer));
        var state = Assert.Single(CreatePlan(fixture).States);

        Assert.Equal(ComponentStateFactoryKind.State, state.FactoryKind);
        Assert.True(state.UsesHook);
        Assert.Equal(
            operation.EffectiveInvocation.ToFullString(),
            state.Initializer.ToFullString());
        Assert.Contains("this", state.Initializer.ToFullString(), StringComparison.Ordinal);
        Assert.Contains(
            "WidthProperty",
            state.Initializer.ToFullString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Commands_UseOneFlatContiguousParameterArray()
    {
        var plan = CreatePlan(
            """
            command void Reset();
            command int Sum(int left, string label);
            command bool IsPositive(double value);
            """);

        Assert.Collection(
            plan.Commands,
            reset =>
            {
                Assert.Equal(0, reset.Id);
                Assert.True(reset.Parameters.IsEmpty);
                Assert.Equal(0, reset.Parameters.Start);
            },
            sum =>
            {
                Assert.Equal(1, sum.Id);
                Assert.Equal(0, sum.Parameters.Start);
                Assert.Equal(2, sum.Parameters.Length);
                Assert.Equal(SpecialType.System_Int32, sum.ResultType.SpecialType);
            },
            positive =>
            {
                Assert.Equal(2, positive.Id);
                Assert.Equal(2, positive.Parameters.Start);
                Assert.Equal(1, positive.Parameters.Length);
                Assert.Equal(SpecialType.System_Boolean, positive.ResultType.SpecialType);
            });
        Assert.Collection(
            plan.CommandParameters,
            left =>
            {
                Assert.Equal(0, left.Ordinal);
                Assert.Equal("left", left.Name);
                Assert.Equal(SpecialType.System_Int32, left.Type.SpecialType);
            },
            label =>
            {
                Assert.Equal(1, label.Ordinal);
                Assert.Equal("label", label.Name);
                Assert.Equal(SpecialType.System_String, label.Type.SpecialType);
            },
            value =>
            {
                Assert.Equal(0, value.Ordinal);
                Assert.Equal("value", value.Name);
                Assert.Equal(SpecialType.System_Double, value.Type.SpecialType);
            });
    }

    [Fact]
    public void UserMembers_ContainOnlySemanticallyValidLocalFunctions()
    {
        const string csharp =
            """
            using Akbura.CompilerAnotations;

            namespace Demo
            {
                public partial class PlannerView
                {
                    private void Render()
                    {
                    }
                }
            }

            namespace Hooks
            {
                public static class PlannerHooks
                {
                    [UseHook]
                    public static void useRender([Self] Demo.PlannerView owner)
                    {
                    }
                }
            }
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(
            """
            using Hooks;

            Render();
            useRender();

            int Helper(int value)
            {
                return value + 1;
            }

            int Broken()
            {
                return MissingValue;
            }
            """,
            csharp);
        var plan = CreatePlan(fixture);
        var member = Assert.Single(plan.UserMembers);

        Assert.IsType<LocalFunctionStatementSyntax>(member.Member);
        Assert.Equal("Helper", member.Member.Identifier.ValueText);
        Assert.Same(
            fixture.ComponentTree.GetRoot().Members
                .OfType<CSharpStatementSyntax>()
                .Single(statement => statement.ToFullString().Contains(
                    "Helper",
                    StringComparison.Ordinal)),
            member.Syntax);
    }

    [Fact]
    public void DefaultMemberPlan_NormalizesAllArrays()
    {
        var plan = new ComponentMemberPlan(
            default,
            default,
            default,
            default,
            default,
            default);

        Assert.Equal(ImmutableArray<ComponentParameterPlan>.Empty, plan.Parameters);
        Assert.Equal(ImmutableArray<ComponentStatePlan>.Empty, plan.States);
        Assert.Equal(ImmutableArray<ComponentInjectServicePlan>.Empty, plan.Services);
        Assert.Equal(ImmutableArray<ComponentCommandPlan>.Empty, plan.Commands);
        Assert.Equal(
            ImmutableArray<ComponentCommandParameterPlan>.Empty,
            plan.CommandParameters);
        Assert.Equal(ImmutableArray<ComponentUserMemberPlan>.Empty, plan.UserMembers);
        Assert.True(plan.IsEmpty);
    }

    private static ComponentMemberPlan CreatePlan(
        string component,
        string? additionalCSharp = null)
    {
        return CreatePlan(AkcssActivatorPlannerTests.CreateFixture(
            component,
            additionalCSharp));
    }

    private static ComponentMemberPlan CreatePlan(
        AkcssActivatorPlannerTests.PlannerFixture fixture)
    {
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            fixture.SemanticModel.GetSymbolInfo(
                fixture.ComponentTree.GetRoot()).Symbol);

        return ComponentMemberPlanner.Create(
            component,
            fixture.SemanticModel);
    }
}
