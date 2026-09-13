using System.Collections.Immutable;
using Akbura.ComponentTree;
using Akbura.Engine;
using Akbura.Hooks;
using Avalonia;
using Avalonia.Controls;

namespace Akbura.UnitTests;

public sealed class UsefulHookDerivedTests
{
    [Fact]
    public void Computed_InitializesOnceAndSettlesOnlyChangedDependencies()
    {
        var dependency = 2;
        var calls = 0;
        State<int>? result = null;
        var owner = new HookComponent(control =>
        {
            var snapshot = dependency;
            result = control.useComputed(() =>
            {
                calls++;
                return snapshot * 3;
            }, [snapshot]);
        });
        owner.InitializeForTest();
        var initial = result;
        Assert.Equal(6, result!.Value);
        Assert.Equal(1, calls);

        owner.InvalidState();
        Assert.Equal(1, calls);
        dependency = 4;
        owner.InvalidState();

        Assert.Same(initial, result);
        Assert.Equal(12, result.Value);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Computed_CopiesDependenciesAndDoesNotCommitAbortedRenders()
    {
        object?[] dependencies = [1];
        var value = 10;
        var calls = 0;
        State<int>? result = null;
        var owner = new HookComponent(control =>
        {
            var snapshot = value;
            result = control.useComputed(() =>
            {
                calls++;
                return snapshot;
            }, dependencies);
        });
        owner.InitializeForTest();
        dependencies[0] = 2;
        value = 20;
        owner.ThrowFromRender = true;

        Assert.Throws<RenderException>(owner.InvalidState);
        Assert.Equal(10, result!.Value);
        Assert.Equal(1, calls);

        owner.ThrowFromRender = false;
        owner.InvalidState();
        Assert.Equal(20, result.Value);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Computed_FailedCalculationCanRetryTheSameDependencies()
    {
        var dependency = 1;
        var failCompute = false;
        var calls = 0;
        State<int>? result = null;
        var owner = new HookComponent(control =>
        {
            result = control.useComputed(() =>
            {
                calls++;
                if (failCompute)
                {
                    throw new ComputeException();
                }

                return dependency;
            }, [dependency]);
        });
        owner.InitializeForTest();
        dependency = 2;
        failCompute = true;

        Assert.Throws<ComputeException>(owner.InvalidState);
        Assert.Equal(1, result!.Value);
        failCompute = false;
        owner.InvalidState();

        Assert.Equal(2, result.Value);
        Assert.Equal(3, calls);
    }

    [Fact]
    public void Select_UsesSourceIdentityAndValueWithoutDependingOnInlineSelectorIdentity()
    {
        var source = new State<int>(2);
        var calls = 0;
        State<string>? result = null;
        var owner = new HookComponent(control => result = control.useSelect(source, value =>
        {
            calls++;
            return (value * 2).ToString();
        }));
        owner.InitializeForTest();
        owner.InvalidState();
        Assert.Equal(1, calls);
        var initial = result;

        source = new State<int>(2);
        owner.InvalidState();
        Assert.Equal(2, calls);
        source.Value = 3;
        owner.InvalidState();

        Assert.Same(initial, result);
        Assert.Equal("6", result!.Value);
        Assert.Equal(3, calls);
    }

    [Fact]
    public void Distinct_SuppressesEquivalentValuesAndRespondsToComparerReplacement()
    {
        var source = new State<string>("hello");
        IEqualityComparer<string> comparer = StringComparer.OrdinalIgnoreCase;
        State<string>? result = null;
        var owner = new HookComponent(control => result = control.useDistinct(source, comparer));
        owner.InitializeForTest();
        source.Value = "HELLO";
        owner.InvalidState();
        Assert.Equal("hello", result!.Value);

        comparer = StringComparer.Ordinal;
        owner.InvalidState();
        Assert.Equal("HELLO", result.Value);
        source.Value = "world";
        owner.InvalidState();
        Assert.Equal("world", result.Value);
    }

    [Fact]
    public void Previous_PreservesHistoryThroughSettlingAndResetsForAnotherSource()
    {
        var source = new State<int>(10);
        State<int>? previous = null;
        var owner = new HookComponent(control => previous = control.usePrevious(source, -1));
        owner.InitializeForTest();
        Assert.Equal(-1, previous!.Value);

        source.Value = 20;
        owner.InvalidState();
        owner.InvalidState();
        Assert.Equal(10, previous.Value);
        source.Value = 30;
        owner.InvalidState();
        Assert.Equal(20, previous.Value);

        source = new State<int>(30);
        owner.InvalidState();
        Assert.Equal(-1, previous.Value);
        source.Value = 40;
        owner.InvalidState();
        Assert.Equal(30, previous.Value);
    }

    [Fact]
    public void PreviousAndOnChange_AbortedFrameDoesNotAdvanceHistory()
    {
        var source = new State<int>(1);
        var changes = new List<(int Before, int After)>();
        State<int>? previous = null;
        var owner = new HookComponent(control =>
        {
            previous = control.usePrevious(source);
            control.useOnChange(source, (before, after) => changes.Add((before, after)));
        });
        owner.InitializeForTest();
        Assert.Empty(changes);
        source.Value = 2;
        owner.ThrowFromRender = true;
        Assert.Throws<RenderException>(owner.InvalidState);
        Assert.Equal(0, previous!.Value);
        Assert.Empty(changes);

        source.Value = 3;
        owner.ThrowFromRender = false;
        owner.InvalidState();
        Assert.Equal(1, previous.Value);
        Assert.Equal([(1, 3)], changes);

        source = new State<int>(100);
        owner.InvalidState();
        Assert.Equal([(1, 3)], changes);
        source.Value = 101;
        owner.InvalidState();
        Assert.Equal([(1, 3), (100, 101)], changes);
    }

    [Fact]
    public void OnChange_CommitsHistoryBeforeCallbackInvalidatesTheSourceAgain()
    {
        var changes = new List<(int Before, int After)>();
        State<int>? source = null;
        var owner = new HookComponent(control =>
        {
            source = control.useHookState(1);
            control.useOnChange(source, (before, after) =>
            {
                changes.Add((before, after));
                if (after == 2)
                {
                    source.Value = 3;
                }
            });
        });
        owner.InitializeForTest();
        source!.Value = 2;

        Assert.Equal([(1, 2), (2, 3)], changes);
        Assert.Equal(3, source.Value);
    }

    [Fact]
    public void DerivedCallsAndOwners_DoNotShareResultStates()
    {
        var source = new State<int>(1);
        State<int>? first = null;
        State<int>? second = null;
        State<int>? other = null;
        var owner = new HookComponent(control =>
        {
            first = control.useSelect(source, value => value + 1);
            second = control.useSelect(source, value => value + 2);
        });
        var otherOwner = new HookComponent(control => other = control.useSelect(source, value => value + 3));
        owner.InitializeForTest();
        otherOwner.InitializeForTest();

        Assert.NotSame(first, second);
        Assert.NotSame(first, other);
        Assert.Equal(2, first!.Value);
        Assert.Equal(3, second!.Value);
        Assert.Equal(4, other!.Value);
    }

    private sealed class RenderException : Exception;
    private sealed class ComputeException : Exception;

    private sealed class HookComponent(Action<HookComponent> renderFrame) : AkburaControl(AkburaEngine.Empty)
    {
        public bool ThrowFromRender { get; set; }

        public void InitializeForTest() => base.OnInitialized();

        protected override Control FirstUpdate() => new Border();

        protected override Control Update()
        {
            renderFrame(this);
            if (ThrowFromRender)
            {
                throw new RenderException();
            }

            return new Border();
        }

        protected override ImmutableArray<Parameter> GetParameters() => [];
        protected override ImmutableArray<AvaloniaProperty<IAkburaCommand>> GetCommands() => [];
        protected override ImmutableArray<InjectService> GetServices() => [];
        protected override ImmutableArray<State> GetStates() => [];
    }
}
