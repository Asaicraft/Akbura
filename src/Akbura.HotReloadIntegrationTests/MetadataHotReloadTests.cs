using Akbura.ComponentTree;
using Akbura.HotReload;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using System.Collections.Immutable;
using System.Reflection;

namespace Akbura.HotReloadIntegrationTests;

public sealed class MetadataHotReloadTests
{
    [Fact]
    [Trait("Category", "ClrHotReload")]
    public async Task StateInfoFactories_UseUpdatedInitializersForNewOwnersWithoutStaticFields()
    {
        var testName = typeof(MetadataHotReloadTests).FullName + "." +
            nameof(StateInfoFactories_UseUpdatedInitializersForNewOwnersWithoutStaticFields);
        if (!HotReloadChildProcess.IsChild(testName))
        {
            await HotReloadChildProcess.RunAsync(testName);
            return;
        }

        var compilation = new AkburaCompilationSession(CreateSource(1), OwnerSource);
        using var reload = new ClrHotReloadSession(compilation.Compilation, "Demo.Counter");
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(MetadataHotReloadTests).Assembly);
        await session.Dispatch(() =>
        {
            var owner = CreateOwner(reload);
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var count = Assert.IsType<State<int>>(Assert.Single(GetStates(owner)));
                var info = count.Info;
                var text = Assert.IsType<TextBlock>(owner.Child);
                count.Value = 42;
                var metadata = AkburaHotReloadRuntime.GetComponentMetadata(reload.ComponentType);
                AssertNoMetadataFields(reload.ComponentType);

                for (var revision = 2; revision <= 5; revision++)
                {
                    reload.ApplyUpdate(compilation.Update(CreateSource(revision)));
                    reload.DispatchMetadataUpdateHandlers();
                    Assert.Same(count, Assert.Single(GetStates(owner)));
                    Assert.Same(info, count.Info);
                    Assert.Equal("42", text.Text);
                    Assert.Same(text, owner.Child);
                    Assert.Same(metadata, AkburaHotReloadRuntime.GetComponentMetadata(reload.ComponentType));

                    var newOwner = CreateOwner(reload);
                    var newWindow = new Window { Content = newOwner };
                    try
                    {
                        newWindow.Show();
                        var newCount = Assert.IsType<State<int>>(Assert.Single(GetStates(newOwner)));
                        Assert.Equal(revision, newCount.Value);
                        Assert.Equal("count", newCount.Info!.Name);
                        Assert.NotSame(info, newCount.Info);
                        Assert.Equal(revision.ToString(), Assert.IsType<TextBlock>(newOwner.Child).Text);
                    }
                    finally
                    {
                        newWindow.Close();
                    }

                    AssertNoMetadataFields(reload.ComponentType);
                }
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);

        static string CreateSource(int initialValue) =>
            "using Avalonia.Controls;\nnamespace Demo;\nstate int count = " + initialValue +
            ";\n<TextBlock Text={count.ToString()} />";
    }

    [Fact]
    [Trait("Category", "ClrHotReload")]
    public async Task DescriptorManifest_IsCapturedBeforeFirstOwnerAndTracksRepeatedReloads()
    {
        var testName = typeof(MetadataHotReloadTests).FullName + "." +
            nameof(DescriptorManifest_IsCapturedBeforeFirstOwnerAndTracksRepeatedReloads);
        if (!HotReloadChildProcess.IsChild(testName))
        {
            await HotReloadChildProcess.RunAsync(testName);
            return;
        }

        var compilation = new AkburaCompilationSession(CreateSource("param bool Removed = true;"), OwnerSource);
        using var reload = new ClrHotReloadSession(compilation.Compilation, "Demo.Counter");
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(MetadataHotReloadTests).Assembly);
        await session.Dispatch(() =>
        {
            // A static descriptor can register properties without any component instance.
            Assert.NotNull(reload.ComponentType.GetField("ValueProperty")!.GetValue(null));
            var metadata = AkburaHotReloadRuntime.GetComponentMetadata(reload.ComponentType);
            var originalShape = metadata.DescriptorShape;
            var valueProperty = GetProperty(metadata, "Value");
            var removedProperty = GetProperty(metadata, "Removed");
            var registry = AvaloniaPropertyRegistry.Instance;
            Assert.Same(removedProperty, registry.FindRegistered(reload.ComponentType, "Removed"));
            AssertNoMetadataFields(reload.ComponentType);

            reload.ApplyUpdate(compilation.Update(CreateSource("param string Added = \"new\";")));
            reload.DispatchMetadataUpdateHandlers();
            Assert.Same(metadata, AkburaHotReloadRuntime.GetComponentMetadata(reload.ComponentType));
            Assert.NotEqual(originalShape, metadata.DescriptorShape);
            Assert.Same(valueProperty, GetProperty(metadata, "Value"));
            Assert.Null(registry.FindRegistered(reload.ComponentType, "Removed"));
            var addedProperty = GetProperty(metadata, "Added");
            Assert.Same(addedProperty, registry.FindRegistered(reload.ComponentType, "Added"));

            var owner = CreateOwner(reload);
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                owner.SetValue(Assert.IsType<StyledProperty<int>>(valueProperty), 12);
                var text = Assert.IsType<TextBlock>(owner.Child);
                Assert.Equal("12", text.Text);

                reload.ApplyUpdate(compilation.Update(CreateSource(string.Empty)));
                reload.DispatchMetadataUpdateHandlers();
                Assert.Same(valueProperty, GetProperty(metadata, "Value"));
                Assert.Single(metadata.Properties);
                Assert.Null(registry.FindRegistered(reload.ComponentType, "Added"));
                Assert.Equal("12", text.Text);

                reload.ApplyUpdate(compilation.Update(CreateSource("param string Added = \"again\";")));
                reload.DispatchMetadataUpdateHandlers();
                var recreatedProperty = GetProperty(metadata, "Added");
                Assert.NotSame(addedProperty, recreatedProperty);
                Assert.Same(recreatedProperty, registry.FindRegistered(reload.ComponentType, "Added"));
                Assert.Same(valueProperty, GetProperty(metadata, "Value"));
                Assert.Same(owner, window.Content);
                Assert.Same(text, owner.Child);
                Assert.Equal("12", text.Text);
                AssertNoMetadataFields(reload.ComponentType);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);

        static string CreateSource(string declaration) =>
            "using Avalonia.Controls;\nnamespace Demo;\nparam int Value = 1;\n" + declaration +
            "\n<TextBlock Text={Value.ToString()} />";
    }

    private static AvaloniaProperty GetProperty(AkburaHotReloadMetadata metadata, string name) =>
        Assert.Single(metadata.Properties, registration => registration.Property.Name == name).Property;

    private static AkburaControl CreateOwner(ClrHotReloadSession reload) =>
        Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(reload.ComponentType));

    private static ImmutableArray<State> GetStates(AkburaControl owner) =>
        Assert.IsType<ImmutableArray<State>>(owner.GetType().GetProperty("StatesForTest")!.GetValue(owner));

    private static void AssertNoMetadataFields(Type type) => Assert.DoesNotContain(
        type.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly),
        field => field.Name.StartsWith("s_stateInfo_", StringComparison.Ordinal) ||
            field.Name is "s_akburaAppliedDescriptorShape" or "s_akburaAppliedStateShape" or "s_akburaHotReloadProperties");

    private const string OwnerSource =
        """
        using Akbura;
        using Akbura.ComponentTree;
        using Akbura.Engine;
        using System.Collections.Immutable;
        namespace Demo;
        public partial class Counter : AkburaControl
        {
            public Counter() : base(AkburaEngine.Empty) { }
            public ImmutableArray<State> StatesForTest => GetStates();
        }
        """;
}
