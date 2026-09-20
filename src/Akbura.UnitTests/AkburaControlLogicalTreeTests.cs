using Akbura.ComponentTree;
using Akbura.Engine;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using System.Collections.Immutable;
using System.Windows.Input;

namespace Akbura.UnitTests;

public sealed class AkburaControlLogicalTreeTests
{
    [Fact]
    public async Task RootChild_AttachesLogicallyAndUpdatesCommandEligibility()
    {
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(
            () =>
            {
                var command = new ToggleCommand();
                var dataContext = new object();
                var button = new Button { Command = command };
                var root = new StackPanel { Children = { button } };
                var component = new CommandComponent(root)
                {
                    DataContext = dataContext,
                };
                var auxiliary = new TextBlock();
                component.AddAuxiliaryLogicalChild(auxiliary);
                var window = new Window { Content = component };

                try
                {
                    window.Show();
                    Dispatcher.UIThread.RunJobs();

                    Assert.Same(root, component.Child);
                    Assert.Same(component, root.Parent);
                    Assert.True(((ILogical)root).IsAttachedToLogicalTree);
                    Assert.True(((ILogical)button).IsAttachedToLogicalTree);
                    Assert.True(((ILogical)auxiliary).IsAttachedToLogicalTree);
                    Assert.Same(dataContext, button.DataContext);
                    Assert.False(button.IsEffectivelyEnabled);

                    command.SetEnabled(true);
                    Dispatcher.UIThread.RunJobs();
                    Assert.True(button.IsEffectivelyEnabled);

                    var replacementButton = new Button { Command = command };
                    var replacement = new StackPanel
                    {
                        Children = { replacementButton },
                    };
                    component.ReplaceRoot(replacement);
                    Dispatcher.UIThread.RunJobs();

                    Assert.Null(root.Parent);
                    Assert.False(((ILogical)root).IsAttachedToLogicalTree);
                    Assert.False(((ILogical)button).IsAttachedToLogicalTree);
                    Assert.Same(replacement, component.Child);
                    Assert.Same(component, replacement.Parent);
                    Assert.True(((ILogical)replacementButton).IsAttachedToLogicalTree);
                    Assert.True(((ILogical)auxiliary).IsAttachedToLogicalTree);
                    Assert.Same(component, auxiliary.Parent);
                    Assert.Same(dataContext, replacementButton.DataContext);

                    command.SetEnabled(false);
                    Dispatcher.UIThread.RunJobs();
                    Assert.False(replacementButton.IsEffectivelyEnabled);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    private sealed class CommandComponent(Control root) : AkburaControl(AkburaEngine.Empty)
    {
        private static readonly ImmutableArray<Parameter> s_parameters = [];
        private static readonly ImmutableArray<AvaloniaProperty<IAkburaCommand>> s_commands = [];
        private static readonly ImmutableArray<InjectService> s_services = [];
        private static readonly ImmutableArray<State> s_states = [];

        private Control _root = root;

        public void AddAuxiliaryLogicalChild(Control child) =>
            LogicalChildren.Add(child);

        public void ReplaceRoot(Control root)
        {
            _root = root;
            InvalidState();
        }

        protected override Control FirstUpdate() => _root;

        protected override Control Update() => _root;

        protected override ImmutableArray<Parameter> GetParameters() => s_parameters;

        protected override ImmutableArray<AvaloniaProperty<IAkburaCommand>> GetCommands() => s_commands;

        protected override ImmutableArray<InjectService> GetServices() => s_services;

        protected override ImmutableArray<State> GetStates() => s_states;
    }

    private sealed class ToggleCommand : ICommand
    {
        private bool _enabled;

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => _enabled;

        public void Execute(object? parameter) { }

        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
