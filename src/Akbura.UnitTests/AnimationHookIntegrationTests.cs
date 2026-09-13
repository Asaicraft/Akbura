using System.Reflection;
using System.Runtime.ExceptionServices;
using Akbura.Hooks;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Avalonia;
using Avalonia.Controls;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class AnimationHookIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnimationHooks_CompileFromShortDslCalls(bool debugStructural)
    {
        const string source =
            """
            using System;
            using Avalonia;
            using Avalonia.Animation;
            using Avalonia.Controls;
            using Avalonia.Layout;
            using Akbura.Hooks;

            state bool enabled = true;
            state int replay = 0;
            state bool expanded = false;
            state AnimationController animator = useAnimate(enabled);

            useTransition(panel, Visual.OpacityProperty, 250, enabled: enabled);
            useTransition(() => panel, Layoutable.MarginProperty,
                TimeSpan.FromMilliseconds(300), enabled: enabled);
            useTransitions(panel, CreateTransitions, [replay], enabled: enabled);
            useTransitions(() => panel, CreateTransitions, [replay], enabled: enabled);
            useAnimation(panel, CreateAnimation, [replay], enabled: enabled);
            useAnimation(() => panel, CreateAnimation, [replay], enabled: enabled);

            <Border x.Name="panel" Opacity={enabled ? 1d : 0.25d}>
                <Button Click={async () =>
                {
                    await animator.RunAsync(panel, Demo.AnimationScenarios.CreateAnimation());
                }}>Animate</Button>
            </Border>
            """;

        _ = Compile(source, AnimationOwner, debugStructural);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NamedTargets_AreCreatedBeforeEffectsAndRerendersDoNotReplay(bool debugStructural)
    {
        const string source =
            """
            using Avalonia;
            using Avalonia.Controls;
            using Akbura.Hooks;

            state bool enabled = true;
            state int replay = 0;
            state bool expanded = false;
            state AnimationController animator = useAnimate(enabled);

            RenderCount++;
            useTransition(panel, Visual.OpacityProperty, 250, enabled: enabled);
            useAnimation(panel, CreateAnimation, [replay], enabled: enabled);

            <Border x.Name="panel" Opacity={expanded ? 1d : 0.25d}>
                <TextBlock Text={replay.ToString()} />
            </Border>
            """;
        var ownerType = Compile(source, AnimationOwner, debugStructural);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(ownerType));
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var panel = Assert.IsType<Border>(owner.Child);
                var transitions = panel.Transitions;
                Assert.Single(Assert.IsType<Avalonia.Animation.Transitions>(transitions));
                Assert.Equal(1, Read<int>(owner, "AnimationCount"));
                Assert.Equal("0", Assert.IsType<TextBlock>(panel.Child).Text);
                var states = owner.GetDiagnosticStates();

                Invoke(owner, "Expand");

                Assert.Same(panel, owner.Child);
                Assert.Same(transitions, panel.Transitions);
                Assert.Equal(1d, panel.GetBaseValue(Visual.OpacityProperty).Value);
                Assert.Equal(1, Read<int>(owner, "AnimationCount"));
                Assert.True(states == owner.GetDiagnosticStates());

                Invoke(owner, "Replay");

                Assert.Equal(2, Read<int>(owner, "AnimationCount"));
                Assert.Equal("1", Assert.IsType<TextBlock>(panel.Child).Text);
                Assert.Same(transitions, panel.Transitions);

                Invoke(owner, "Disable");

                Assert.Equal(2, Read<int>(owner, "AnimationCount"));
                Assert.Null(panel.Transitions);
                Assert.Equal(1d, panel.Opacity);
                Invoke(owner, "Replay");
                Assert.Equal(2, Read<int>(owner, "AnimationCount"));

                Invoke(owner, "Enable");

                Assert.Equal(3, Read<int>(owner, "AnimationCount"));
                Assert.Single(Assert.IsType<Avalonia.Animation.Transitions>(panel.Transitions));
                window.Content = null;
                Assert.Null(panel.Transitions);
                window.Content = owner;
                Assert.Equal(4, Read<int>(owner, "AnimationCount"));
                Assert.Single(Assert.IsType<Avalonia.Animation.Transitions>(panel.Transitions));
                Assert.True(states == owner.GetDiagnosticStates());
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolvedTargets_FollowCommittedContentReplacementAndAbsence(bool debugStructural)
    {
        const string source =
            """
            using Avalonia;
            using Avalonia.Animation;
            using Avalonia.Controls;
            using Akbura.Hooks;

            state bool show = true;
            state bool alternate = false;
            state int unrelated = 0;

            useTransition(() => host.Child as Animatable, Visual.OpacityProperty, 250);
            useAnimation(() => host.Child as Animatable, CreateAnimation, []);

            <Border x.Name="host"
                Child={show ? (alternate ? SecondPanel : FirstPanel) : null} />
            """;
        var ownerType = Compile(source, ResolvedOwner, debugStructural);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(() =>
        {
            var owner = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(ownerType));
            var first = Read<Border>(owner, "FirstPanel");
            var second = Read<Border>(owner, "SecondPanel");
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var host = Assert.IsType<Border>(owner.Child);
                var targets = Read<List<Avalonia.Animation.Animatable>>(owner, "AnimationTargets");
                var states = owner.GetDiagnosticStates();
                Assert.Same(first, host.Child);
                Assert.Same(first, Assert.Single(targets));
                var initialTransitions = Assert.IsType<Avalonia.Animation.Transitions>(first.Transitions);
                Assert.Single(initialTransitions);
                Assert.Null(second.Transitions);

                Invoke(owner, "Rerender");
                Assert.Same(first, Assert.Single(targets));
                Assert.Same(initialTransitions, first.Transitions);

                Invoke(owner, "ToggleTarget");
                Assert.Same(second, host.Child);
                Assert.Equal(new Avalonia.Animation.Animatable[] { first, second }, targets);
                Assert.Null(first.Transitions);
                Assert.Single(Assert.IsType<Avalonia.Animation.Transitions>(second.Transitions));

                Invoke(owner, "HideTarget");
                Assert.Null(host.Child);
                Assert.Null(first.Transitions);
                Assert.Null(second.Transitions);
                Assert.Equal(2, targets.Count);
                Invoke(owner, "Rerender");
                Assert.Equal(2, targets.Count);

                Invoke(owner, "ShowTarget");
                Assert.Same(second, host.Child);
                Assert.Equal(new Avalonia.Animation.Animatable[] { first, second, second }, targets);
                Assert.Single(Assert.IsType<Avalonia.Animation.Transitions>(second.Transitions));
                Assert.True(states == owner.GetDiagnosticStates());
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImperativeController_StopsOnDetachWithoutFrameStateUpdates(bool debugStructural)
    {
        const string source =
            """
            using Avalonia.Controls;
            using Akbura.Hooks;

            state bool enabled = true;
            state int replay = 0;
            state bool expanded = false;
            state AnimationController animator = useAnimate(enabled);

            RenderCount++;

            <Border x.Name="panel">
                <TextBlock Text="Animation target" />
            </Border>
            """;
        var ownerType = Compile(source, AnimationOwner, debugStructural);
        var session = AvaloniaHeadlessTestSession.GetSession();
        await session.Dispatch(async () =>
        {
            var owner = Assert.IsAssignableFrom<AkburaControl>(Activator.CreateInstance(ownerType));
            var window = new Window { Content = owner };
            try
            {
                window.Show();
                var animator = Read<AnimationController>(owner, "AnimatorForTest");
                var states = owner.GetDiagnosticStates();
                var renderCount = Read<int>(owner, "RenderCount");
                var first = (Task)Invoke(owner, "Animate")!;
                Assert.False(first.IsCompleted);
                animator.Stop();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => first.WaitAsync(TimeSpan.FromSeconds(10)));
                Assert.Equal(renderCount, Read<int>(owner, "RenderCount"));

                var second = (Task)Invoke(owner, "Animate")!;
                window.Content = null;
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => second.WaitAsync(TimeSpan.FromSeconds(10)));
                Assert.Equal(renderCount, Read<int>(owner, "RenderCount"));
                window.Content = owner;
                Assert.Same(animator, Read<AnimationController>(owner, "AnimatorForTest"));
                Assert.True(states == owner.GetDiagnosticStates());

                Invoke(owner, "Disable");
                var disabledRenderCount = Read<int>(owner, "RenderCount");
                await ((Task)Invoke(owner, "Animate")!).WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(disabledRenderCount, Read<int>(owner, "RenderCount"));
                Assert.Same(animator, Read<AnimationController>(owner, "AnimatorForTest"));
                Assert.True(states == owner.GetDiagnosticStates());
            }
            finally
            {
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    private static Type Compile(string source, string ownerSource, bool debugStructural)
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(source, ownerSource);
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            fixture.SemanticModel.GetSymbolInfo(fixture.ComponentTree.GetRoot()).Symbol);
        var errors = fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot())
            .Where(diagnostic => diagnostic.Severity == AkburaDiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(
            diagnostic => diagnostic.Code + ": " + diagnostic.Message)));

        var generated = ComponentDocumentWriter.Generate(
            component, fixture.SemanticModel, "Views/PlannerView.akbura",
            new Dictionary<AkburaSyntax, string>(),
            mode: debugStructural ? ComponentGenerationMode.DebugStructural : ComponentGenerationMode.ReleaseDirect);
        var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        if (debugStructural)
        {
            options = options.WithPreprocessorSymbols("DEBUG");
        }

        var tree = CSharpSyntaxTree.ParseText(generated, options, "PlannerView.AnimationHooks.g.cs");
        var compilation = fixture.CSharpCompilation.AddSyntaxTrees(tree)
            .WithAssemblyName("AnimationHooks_" + Guid.NewGuid().ToString("N"));
        var diagnostics = compilation.GetDiagnostics().Where(
            diagnostic => diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error).ToArray();
        Assert.True(diagnostics.Length == 0,
            string.Join(Environment.NewLine, diagnostics.AsEnumerable()) + Environment.NewLine + generated);
        using var output = new MemoryStream();
        var emitted = compilation.Emit(output);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        return Assert.IsAssignableFrom<Type>(Assembly.Load(output.ToArray()).GetType("Demo.PlannerView"));
    }

    private static T Read<T>(AkburaControl owner, string property) =>
        (T)owner.GetType().GetProperty(property)!.GetValue(owner)!;

    private static object? Invoke(AkburaControl owner, string method)
    {
        try
        {
            return owner.GetType().GetMethod(method)!.Invoke(owner, null);
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private const string AnimationOwner =
        """
        using System;
        using System.Threading.Tasks;
        using Akbura;
        using Akbura.Engine;
        using Akbura.Hooks;
        using Avalonia;
        using Avalonia.Animation;
        using Avalonia.Controls;
        using Avalonia.Layout;
        using Avalonia.Styling;

        namespace Demo;

        public partial class PlannerView : AkburaControl
        {
            public PlannerView() : base(AkburaEngine.Empty) { }
            public int RenderCount { get; set; }
            public int AnimationCount { get; private set; }
            public AnimationController AnimatorForTest => animator;
            public void Expand() => expanded = true;
            public void Replay() => replay++;
            public void Disable() => enabled = false;
            public void Enable() => enabled = true;
            public Task Animate() => animator.RunAsync(panel, CreateAnimation());

            public Transitions CreateTransitions() => new()
            {
                new ThicknessTransition
                {
                    Property = Layoutable.MarginProperty,
                    Duration = TimeSpan.FromMilliseconds(300)
                }
            };

            public Animation CreateAnimation()
            {
                if (panel == null || panel.Child == null)
                {
                    throw new InvalidOperationException("The named target is not initialized.");
                }

                AnimationCount++;
                return AnimationScenarios.CreateAnimation();
            }
        }

        public static class AnimationScenarios
        {
            public static Animation CreateAnimation()
            {
                return new Animation
                {
                    Duration = TimeSpan.FromHours(1),
                    FillMode = FillMode.None,
                    Children =
                    {
                        new KeyFrame
                        {
                            Cue = new Cue(0d),
                            Setters = { new Setter(Layoutable.WidthProperty, 40d) }
                        },
                        new KeyFrame
                        {
                            Cue = new Cue(1d),
                            Setters = { new Setter(Layoutable.WidthProperty, 80d) }
                        }
                    }
                };
            }
        }
        """;

    private const string ResolvedOwner =
        """
        using System;
        using System.Collections.Generic;
        using Akbura;
        using Akbura.Engine;
        using Avalonia.Animation;
        using Avalonia.Controls;
        using Avalonia.Layout;
        using Avalonia.Styling;

        namespace Demo;

        public partial class PlannerView : AkburaControl
        {
            public PlannerView() : base(AkburaEngine.Empty) { }
            public Border FirstPanel { get; } = new() { Child = new TextBlock { Text = "First" } };
            public Border SecondPanel { get; } = new() { Child = new TextBlock { Text = "Second" } };
            public List<Animatable> AnimationTargets { get; } = new();
            public void Rerender() => unrelated++;
            public void ToggleTarget() => alternate = !alternate;
            public void HideTarget() => show = false;
            public void ShowTarget() => show = true;

            public Animation CreateAnimation()
            {
                if (host.Child is not Animatable target)
                {
                    throw new InvalidOperationException("The target is not present after render.");
                }

                AnimationTargets.Add(target);
                return new Animation
                {
                    Duration = TimeSpan.FromHours(1),
                    FillMode = FillMode.None,
                    Children =
                    {
                        new KeyFrame
                        {
                            Cue = new Cue(0d),
                            Setters = { new Setter(Layoutable.WidthProperty, 40d) }
                        },
                        new KeyFrame
                        {
                            Cue = new Cue(1d),
                            Setters = { new Setter(Layoutable.WidthProperty, 80d) }
                        }
                    }
                };
            }
        }
        """;
}
