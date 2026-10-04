using Akbura.Commands;
using Akbura.ComponentTree;
using Akbura.Engine;
using Avalonia;
using Avalonia.Controls;
using System.Collections.Immutable;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class CommandPropertyTests
{
    [Fact]
    public async Task CommandChanges_RespectInitializationAndEffectiveValueChanges()
    {
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = new TestComponent();
            var first = AkburaCommandFactory.CreateAction<object>(static () => { });
            var second = AkburaCommandFactory.CreateAction<object>(static () => { });
            owner.SetValue(TestComponent.RunProperty, first);
            owner.SetValue(TestComponent.RunProperty, second);

            Assert.Null(owner.Child);
            Assert.Equal(0, owner.FirstUpdateCount);
            Assert.Equal(0, owner.UpdateCount);

            owner.InitializeForTest();
            var button = Assert.IsType<Button>(owner.Child);
            Assert.Same(second, button.Command);
            Assert.Equal(1, owner.FirstUpdateCount);
            Assert.Equal(1, owner.UpdateCount);

            owner.SetValue(TestComponent.RunProperty, first);
            Assert.Same(button, owner.Child);
            Assert.Same(first, button.Command);
            Assert.Equal(2, owner.UpdateCount);

            owner.SetValue(TestComponent.RunProperty, first);
            Assert.Equal(2, owner.UpdateCount);

            owner.ClearValue(TestComponent.RunProperty);
            Assert.Null(button.Command);
            Assert.Equal(3, owner.UpdateCount);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CommandChanges_AreBatchedByExistingUpdateSuppression()
    {
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = new TestComponent();
            owner.InitializeForTest();
            var button = Assert.IsType<Button>(owner.Child);
            var first = AkburaCommandFactory.CreateAction<object>(static () => { });
            var second = AkburaCommandFactory.CreateAction<object>(static () => { });

            using (owner.SuppressForTest())
            {
                owner.SetValue(TestComponent.RunProperty, first);
                owner.SetValue(TestComponent.RunProperty, second);
                Assert.Equal(1, owner.UpdateCount);
                Assert.Null(button.Command);
            }

            Assert.Same(button, owner.Child);
            Assert.Same(second, button.Command);
            Assert.Equal(2, owner.UpdateCount);
        }, CancellationToken.None);
    }

    private sealed class TestComponent : AkburaControl
    {
        public static readonly StyledProperty<IAkburaCommand> RunProperty =
            CommandProperty.Create<TestComponent>("Run");

        private static readonly ImmutableArray<AvaloniaProperty<IAkburaCommand>> s_commands = [RunProperty];
        private readonly Button _button = new();

        public TestComponent() : base(AkburaEngine.Empty) { }

        public int FirstUpdateCount { get; private set; }

        public int UpdateCount { get; private set; }

        public void InitializeForTest() => base.OnInitialized();

        public IDisposable SuppressForTest() => SuppressUpdates();

        protected override Control FirstUpdate()
        {
            FirstUpdateCount++;
            _button.Command = GetValue(RunProperty);
            return _button;
        }

        protected override Control Update()
        {
            UpdateCount++;
            _button.Command = GetValue(RunProperty);
            return _button;
        }

        protected override ImmutableArray<Parameter> GetParameters() => [];

        protected override ImmutableArray<InjectService> GetServices() => [];

        protected override ImmutableArray<State> GetStates() => [];

        protected override ImmutableArray<AvaloniaProperty<IAkburaCommand>> GetCommands() => s_commands;
    }
}
