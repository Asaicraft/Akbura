using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;

namespace Akbura.UnitTests;

public sealed class ComponentHotReloadIdentityTests
{
    [Fact]
    public void GeneratedNames_RemainStableWhenEarlierMembersAreInserted()
    {
        const string types =
            """
            namespace Demo;

            public interface IService
            {
            }

            public interface IOtherService
            {
            }
            """;
        var original = CreatePlan(
            """
            using Demo;
            using System.Collections.Generic;

            param IList<string> Items;
            inject IService service;
            command int Execute(string value);
            state int count = 1;
            """,
            types);
        var updated = CreatePlan(
            """
            using Demo;
            using System.Collections.Generic;

            param int Prefix;
            param IList<string> Items;
            inject IOtherService otherService;
            inject IService service;
            command void Reset();
            command int Execute(string value);
            state bool enabled = true;
            state int count = 2;
            """,
            types);

        try
        {
            Assert.Equal(
                original.Parameters.ItemRef(0).GeneratedName,
                updated.Parameters.ItemRef(1).GeneratedName);
            Assert.Equal(
                original.Services.ItemRef(0).GeneratedName,
                updated.Services.ItemRef(1).GeneratedName);
            Assert.Equal(
                original.Commands.ItemRef(0).GeneratedName,
                updated.Commands.ItemRef(1).GeneratedName);
            Assert.Equal(
                original.States.ItemRef(0).GeneratedName,
                updated.States.ItemRef(1).GeneratedName);
        }
        finally
        {
            original.ReturnToPool();
            updated.ReturnToPool();
        }
    }

    [Fact]
    public void ParameterIdentity_DistinguishesContentHandlerCompatibility()
    {
        var plan = CreatePlan("param string Value;");

        try
        {
            var type = plan.Parameters.ItemRef(0).Type;
            var normal = ComponentHotReloadIdentity.CreateParameterKey(
                "Content",
                type,
                ComponentParameterKind.Value,
                ComponentParameterFlags.None);
            var content = ComponentHotReloadIdentity.CreateParameterKey(
                "Content",
                type,
                ComponentParameterKind.Value,
                ComponentParameterFlags.IsContent);

            Assert.EndsWith(":styled:normal", normal, StringComparison.Ordinal);
            Assert.EndsWith(":styled:content", content, StringComparison.Ordinal);
            Assert.NotEqual(normal, content);
        }
        finally
        {
            plan.ReturnToPool();
        }
    }

    [Fact]
    public void Fingerprints_IgnoreWhitespaceAndStateInitializerBodies()
    {
        var compact = CreatePlan(
            """
            param string Title = string.Concat("A", "B");
            state int count = 1;
            state string name = "first";
            """);
        var formatted = CreatePlan(
            """
            param string Title =
                string.Concat(
                    "A",
                    "B");

            state int count = 200;
            state string name = "second";
            """);
        var changedDescriptor = CreatePlan(
            """
            param string Title = string.Concat("A", "C");
            state int count = 1;
            state string name = "first";
            """);
        var reorderedStates = CreatePlan(
            """
            param string Title = string.Concat("A", "B");
            state string name = "first";
            state int count = 1;
            """);

        try
        {
            var compactHotReload = ComponentHotReloadPlan.Create(compact);
            var formattedHotReload = ComponentHotReloadPlan.Create(formatted);
            var changedDescriptorHotReload =
                ComponentHotReloadPlan.Create(changedDescriptor);
            var reorderedStatesHotReload =
                ComponentHotReloadPlan.Create(reorderedStates);

            Assert.Equal(
                compactHotReload.DescriptorShape,
                formattedHotReload.DescriptorShape);
            Assert.NotEqual(
                compactHotReload.DescriptorShape,
                changedDescriptorHotReload.DescriptorShape);
            Assert.Equal(
                compact.Parameters.ItemRef(0).HotReloadKey,
                changedDescriptor.Parameters.ItemRef(0).HotReloadKey);
            Assert.Equal(
                compactHotReload.StateShape,
                formattedHotReload.StateShape);
            Assert.NotEqual(
                compactHotReload.StateShape,
                reorderedStatesHotReload.StateShape);
        }
        finally
        {
            compact.ReturnToPool();
            formatted.ReturnToPool();
            changedDescriptor.ReturnToPool();
            reorderedStates.ReturnToPool();
        }
    }

    private static ComponentMemberPlan CreatePlan(
        string componentSource,
        string? csharpSource = null)
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(
            componentSource,
            csharpSource);
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            fixture.SemanticModel.GetSymbolInfo(
                fixture.ComponentTree.GetRoot()).Symbol);

        return ComponentMemberPlanner.Create(
            component,
            fixture.SemanticModel);
    }
}
