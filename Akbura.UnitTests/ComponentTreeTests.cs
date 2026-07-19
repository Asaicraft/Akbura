using Akbura.ComponentTree;
using Akbura.Engine;
using Avalonia.Controls;
using Avalonia.Headless;
using System.Collections.Immutable;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ComponentTreeTests
{
    [Fact]
    public async Task AkburaControl_MaintainsComponentTreeAcrossVisualAttachment()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var engine = new AkburaEngineExtensions.AkburaEngineBuilder().Build();
                var firstChild = new TestComponent(engine, static () => new Border());
                var secondChild = new TestComponent(engine, static () => new Border());
                var parent = new TestComponent(
                    engine,
                    () => new StackPanel
                    {
                        Children =
                        {
                            new Border { Child = firstChild },
                            secondChild,
                        },
                    });
                var window = new Window { Content = parent };
                window.Show();

                var parentTree = (IComponentTree)parent;
                var firstChildTree = (IComponentTree)firstChild;
                var secondChildTree = (IComponentTree)secondChild;

                Assert.Null(parentTree.ComponentParent);
                Assert.Collection(
                    parentTree.ComponentChildren,
                    child => Assert.Same(firstChildTree, child),
                    child => Assert.Same(secondChildTree, child));
                Assert.Same(parentTree, firstChildTree.ComponentParent);
                Assert.Same(parentTree, secondChildTree.ComponentParent);

                window.Content = null;

                Assert.Empty(parentTree.ComponentChildren);
                Assert.Null(firstChildTree.ComponentParent);
                Assert.Null(secondChildTree.ComponentParent);

                window.Close();
            },
            CancellationToken.None);
    }

    private sealed class TestComponent : AkburaControl
    {
        private static readonly ImmutableArray<Parameter> s_parameters = [];
        private static readonly ImmutableArray<Avalonia.AvaloniaProperty<IAkburaCommand>> s_commands = [];
        private static readonly ImmutableArray<InjectService> s_services = [];

        private readonly Func<Control> _update;

        public TestComponent(AkburaEngine engine, Func<Control> update)
            : base(engine)
        {
            _update = update;
        }

        protected override Control Update()
        {
            return _update();
        }

        protected override Control FirstUpdate()
        {
            return new Border();
        }

        protected override ImmutableArray<Parameter> GetParameters()
        {
            return s_parameters;
        }

        protected override ImmutableArray<Avalonia.AvaloniaProperty<IAkburaCommand>> GetCommands()
        {
            return s_commands;
        }

        protected override ImmutableArray<InjectService> GetServices()
        {
            return s_services;
        }
    }
}
