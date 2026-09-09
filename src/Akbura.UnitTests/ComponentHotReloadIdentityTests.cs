using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;

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

    [Fact]
    public void RenderSyntaxIdentity_IsCompactStableAndContentSensitive()
    {
        var compact = CreateStructuralPlan(
            """
            using Avalonia.Controls;

            <Button Content="Stable identity text" Width="42" />
            """);
        var formatted = CreateStructuralPlan(
            """
            using Avalonia.Controls;

            <Button
                Content = "Stable identity text"
                Width = "42" />
            """);
        var changed = CreateStructuralPlan(
            """
            using Avalonia.Controls;

            <Button Content="Changed identity text" Width="42" />
            """);

        try
        {
            var compactIdentity =
                ComponentHotReloadIdentity.CreateRenderSyntaxIdentity(
                    compact.Elements.ItemRef(0).Syntax);
            var formattedIdentity =
                ComponentHotReloadIdentity.CreateRenderSyntaxIdentity(
                    formatted.Elements.ItemRef(0).Syntax);
            var changedIdentity =
                ComponentHotReloadIdentity.CreateRenderSyntaxIdentity(
                    changed.Elements.ItemRef(0).Syntax);

            Assert.Equal(64, compactIdentity.Length);
            Assert.Matches("^[0-9A-F]{64}$", compactIdentity);
            Assert.Equal(compactIdentity, formattedIdentity);
            Assert.NotEqual(compactIdentity, changedIdentity);
            Assert.DoesNotContain(
                "Stable identity text",
                compactIdentity,
                StringComparison.Ordinal);
        }
        finally
        {
            compact.ReturnToPool();
            formatted.ReturnToPool();
            changed.ReturnToPool();
        }
    }

    [Fact]
    public void ContentSyntaxIdentity_IgnoresOwnerAttributesAndTracksContent()
    {
        var original = CreateStructuralPlan(
            """
            using Avalonia.Controls;

            <Button Width="100">Stable content</Button>
            """);
        var changedAttribute = CreateStructuralPlan(
            """
            using Avalonia.Controls;

            <Button Width="200">Stable content</Button>
            """);
        var changedContent = CreateStructuralPlan(
            """
            using Avalonia.Controls;

            <Button Width="200">Changed content</Button>
            """);

        try
        {
            var originalIdentity =
                ComponentHotReloadIdentity.CreateContentSyntaxIdentity(
                    Assert.Single(original.PropertyContents).Syntax);
            var changedAttributeIdentity =
                ComponentHotReloadIdentity.CreateContentSyntaxIdentity(
                    Assert.Single(changedAttribute.PropertyContents).Syntax);
            var changedContentIdentity =
                ComponentHotReloadIdentity.CreateContentSyntaxIdentity(
                    Assert.Single(changedContent.PropertyContents).Syntax);

            Assert.Equal(originalIdentity, changedAttributeIdentity);
            Assert.NotEqual(originalIdentity, changedContentIdentity);
        }
        finally
        {
            original.ReturnToPool();
            changedAttribute.ReturnToPool();
            changedContent.ReturnToPool();
        }
    }

    [Fact]
    public void RenderFingerprint_IncludesSemanticContentSlot()
    {
        const string types =
            """
            namespace Demo;

            public sealed class SlotHost : Avalonia.Controls.Control
            {
                public Avalonia.Controls.Control? First { get; set; }

                public Avalonia.Controls.Control? Second { get; set; }
            }
            """;
        var first = CreateStructuralPlan(
            """
            using Avalonia.Controls;
            using Demo;

            <SlotHost>
                <SlotHost.First>
                    <TextBlock Text="Same child" />
                </SlotHost.First>
            </SlotHost>
            """,
            types);
        var second = CreateStructuralPlan(
            """
            using Avalonia.Controls;
            using Demo;

            <SlotHost>
                <SlotHost.Second>
                    <TextBlock Text="Same child" />
                </SlotHost.Second>
            </SlotHost>
            """,
            types);

        try
        {
            Assert.Equal(2, first.Elements.Length);
            Assert.Equal(2, second.Elements.Length);

            ref readonly var firstChild = ref first.Elements.ItemRef(1);
            ref readonly var secondChild = ref second.Elements.ItemRef(1);
            var firstSlot = ComponentStructuralHotReloadWriter.GetElementSlot(
                first,
                firstChild);
            var secondSlot = ComponentStructuralHotReloadWriter.GetElementSlot(
                second,
                secondChild);

            Assert.Equal(
                ComponentHotReloadIdentity.CreateRenderSyntaxIdentity(
                    first.Elements.ItemRef(0).Syntax),
                ComponentHotReloadIdentity.CreateRenderSyntaxIdentity(
                    second.Elements.ItemRef(0).Syntax));
            Assert.Equal(
                ComponentHotReloadIdentity.CreateRenderSyntaxIdentity(
                    firstChild.Syntax),
                ComponentHotReloadIdentity.CreateRenderSyntaxIdentity(
                    secondChild.Syntax));
            Assert.NotEqual(firstSlot, secondSlot);
            Assert.NotEqual(
                ComponentHotReloadIdentity.CreateRenderFingerprint(first),
                ComponentHotReloadIdentity.CreateRenderFingerprint(second));
        }
        finally
        {
            first.ReturnToPool();
            second.ReturnToPool();
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

    private static ComponentPlan CreateStructuralPlan(
        string componentSource,
        string? csharpSource = null)
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(
            componentSource,
            csharpSource);
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            fixture.SemanticModel.GetSymbolInfo(
                fixture.ComponentTree.GetRoot()).Symbol);

        return ComponentPlanner.Create(
            component,
            fixture.SemanticModel,
            new Dictionary<AkburaSyntax, string>(),
            ComponentGenerationMode.DebugStructural);
    }
}
