using Akbura.Akcss;
using Akbura.CompilerAnotations;
using Akbura.Markup;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using System.Collections.Immutable;
using System.Reflection;

namespace Akbura.UnitTests;

public sealed class AkcssRuntimeTests
{
    [Fact]
    public void ClassesAndUtilities_RunAsOneOrderedReactiveCascade()
    {
        var control = new Border();
        var events = new List<string>();
        var classSignal = new TestSignal<object?>();
        var style = new LoggingClass(events, classSignal);
        var utility = new LoggingUtility(events);
        var utilityActivator = new LoggingUtilityActivator(utility, 42);

        AkburaControl.SetAkcssStyles(
            control,
            [new AkcssClassActivator(style), utilityActivator]);

        Assert.Equal(
            ["utility:reset", "class:reset", "class:update", "utility:update:42"],
            events);

        events.Clear();
        classSignal.Emit(AvaloniaProperty.UnsetValue);

        Assert.Equal(
            ["utility:reset", "class:reset", "class:update", "utility:update:42"],
            events);

        events.Clear();
        control.ClearValue(AkburaControl.AkcssStylesProperty);

        Assert.Equal(["utility:reset", "class:reset"], events);

        events.Clear();
        classSignal.Emit(AvaloniaProperty.UnsetValue);
        Assert.Empty(events);
    }

    [Fact]
    public void ConditionalUtility_ResetsWhenConditionBecomesFalse()
    {
        var control = new Border();
        var condition = new TestSignal<bool>();
        var utility = new CountingUtility();
        var activator = new ConditionalUtilityActivator(utility, condition);

        AkburaControl.SetAkcssStyles(control, [activator]);

        Assert.Equal(1, utility.ResetCount);
        Assert.Equal(0, utility.UpdateCount);

        condition.Emit(true);

        Assert.Equal(2, utility.ResetCount);
        Assert.Equal(1, utility.UpdateCount);

        condition.Emit(false);

        Assert.Equal(3, utility.ResetCount);
        Assert.Equal(1, utility.UpdateCount);
    }

    [Fact]
    public void ObservesPropertyAttribute_ReappliesAkcssClass()
    {
        var control = new Border();
        var style = new WidthObservingClass();

        AkburaControl.SetAkcssStyles(
            control,
            [new AkcssClassActivator(style)]);

        Assert.Equal(1, style.UpdateCount);

        control.Width = 120;

        Assert.Equal(2, style.UpdateCount);
        Assert.Equal(2, style.ResetCount);

        control.Width = 120;
        Assert.Equal(2, style.UpdateCount);
    }

    [Fact]
    public void UtilityActivator_ValidatesArgumentCountAndTypes()
    {
        var utility = new ArgumentUtility();

        Assert.Throws<ArgumentException>(
            () => new RawUtilityActivator(utility, []));
        Assert.Throws<ArgumentException>(
            () => new RawUtilityActivator(utility, ["wrong"]));

        _ = new RawUtilityActivator(utility, [42]);
    }

    [Fact]
    public void AkcssStyles_CanOnlyBeSetOnceWithoutClearing()
    {
        var control = new Border();
        var styles = ImmutableArray.Create<AkcssStyleActivator>(
            new AkcssClassActivator(new WidthObservingClass()));

        AkburaControl.SetAkcssStyles(control, styles);

        Assert.Equal(styles, AkburaControl.GetAkcssStyles(control));
        Assert.Throws<InvalidOperationException>(
            () => AkburaControl.SetAkcssStyles(control, styles));
    }

    [Fact]
    public void UtilityCandidates_ResolveBreakpointGroupAndFallback()
    {
        var control = new Border();
        var events = new List<string>();
        var smSignal = new TestSignal<bool>();
        var mdSignal = new TestSignal<bool>();
        var fallback = CreateCandidate(
            "p",
            sourceOrder: 3,
            "fallback",
            events);
        var sm = CreateCandidate(
            "p",
            sourceOrder: 0,
            "sm",
            events,
            CreateVariantSource(smSignal),
            order: 1d,
            conflictGroup: "Breakpoints",
            UnprefixedUtilityPrecedence.Above);
        var md = CreateCandidate(
            "p",
            sourceOrder: 1,
            "md",
            events,
            CreateVariantSource(mdSignal),
            order: 10d,
            conflictGroup: "Breakpoints",
            UnprefixedUtilityPrecedence.Above);

        AkburaControl.SetAkcssStyles(
            control,
            [sm, md, fallback]);

        Assert.Equal("fallback:update", events[^1]);

        events.Clear();
        smSignal.Emit(true);
        Assert.Equal("sm:update", events[^1]);

        events.Clear();
        mdSignal.Emit(true);
        Assert.Equal("md:update", events[^1]);

        events.Clear();
        mdSignal.Emit(false);
        Assert.Equal("sm:update", events[^1]);

        events.Clear();
        smSignal.Emit(false);
        Assert.Equal("fallback:update", events[^1]);
    }

    [Fact]
    public void UtilityCandidateChange_RecalculatesOnlyItsConflictKey()
    {
        var control = new Border();
        var events = new List<string>();
        var classSignal = new TestSignal<object?>();
        var style = new LoggingClass(events, classSignal);
        var variantSignal = new TestSignal<bool>();
        var padding = CreateCandidate(
            "p",
            sourceOrder: 0,
            "padding",
            events,
            CreateVariantSource(variantSignal));
        var background = CreateCandidate(
            "bg",
            sourceOrder: 1,
            "background",
            events);

        AkburaControl.SetAkcssStyles(
            control,
            [
                new AkcssClassActivator(style),
                padding,
                background,
            ]);

        events.Clear();
        variantSignal.Emit(true);

        Assert.Contains("padding:update", events);
        Assert.DoesNotContain("class:reset", events);
        Assert.DoesNotContain("class:update", events);
        Assert.DoesNotContain("background:reset", events);
        Assert.DoesNotContain("background:update", events);
    }

    [Fact]
    public void UtilityCandidates_ResolveEachPropertyOperationIndependently()
    {
        var control = new Border();
        var events = new List<string>();
        var first = CreateOperationCandidate(
            "my-w",
            sourceOrder: 0,
            events,
            ("property:Width", "first-width"),
            ("property:Background", "first-background"),
            ("property:Padding", "first-padding"));
        var second = CreateOperationCandidate(
            "square",
            sourceOrder: 1,
            events,
            ("property:Width", "second-width"),
            ("property:Height", "second-height"));

        AkburaControl.SetAkcssStyles(
            control,
            [first, second]);

        Assert.Equal(
            [
                "first-background:update",
                "first-padding:update",
                "second-width:update",
                "second-height:update",
            ],
            events.Where(static item =>
                item.EndsWith(
                    ":update",
                    StringComparison.Ordinal)));
    }

    [Fact]
    public void UtilityVariants_CompareDifferentUtilityNamesByPropertyOperation()
    {
        var control = new Border();
        var events = new List<string>();
        var lgSignal = new TestSignal<bool>();
        var mdSignal = new TestSignal<bool>();
        var lg = CreateOperationCandidate(
            "lg-w",
            sourceOrder: 0,
            events,
            [("property:Width", "lg")],
            CreateVariantSource(lgSignal),
            order: 20d,
            conflictGroup: "Breakpoints",
            UnprefixedUtilityPrecedence.Above);
        var fallback = CreateOperationCandidate(
            "w",
            sourceOrder: 1,
            events,
            ("property:Width", "fallback"));
        var md = CreateOperationCandidate(
            "md-w",
            sourceOrder: 2,
            events,
            [("property:Width", "md")],
            CreateVariantSource(mdSignal),
            order: 10d,
            conflictGroup: "Breakpoints",
            UnprefixedUtilityPrecedence.Above);

        AkburaControl.SetAkcssStyles(
            control,
            [lg, fallback, md]);
        Assert.Equal("fallback:update", events[^1]);

        mdSignal.Emit(true);
        Assert.Equal("md:update", events[^1]);

        lgSignal.Emit(true);
        Assert.Equal("lg:update", events[^1]);

        lgSignal.Emit(false);
        Assert.Equal("md:update", events[^1]);
    }

    [Fact]
    public void UtilityCandidates_UseOrderOnlyWithinSameConflictGroup()
    {
        var control = new Border();
        var events = new List<string>();
        var firstSignal = new TestSignal<bool>();
        var secondSignal = new TestSignal<bool>();
        var highOrderEarlier = CreateCandidate(
            "p",
            sourceOrder: 0,
            "high-order",
            events,
            CreateVariantSource(firstSignal),
            order: 100d,
            conflictGroup: "First");
        var lowOrderLater = CreateCandidate(
            "p",
            sourceOrder: 1,
            "later-group",
            events,
            CreateVariantSource(secondSignal),
            order: 1d,
            conflictGroup: "Second");

        AkburaControl.SetAkcssStyles(
            control,
            [highOrderEarlier, lowOrderLater]);
        firstSignal.Emit(true);
        events.Clear();
        secondSignal.Emit(true);

        Assert.Equal("later-group:update", events[^1]);
    }

    [Theory]
    [InlineData(
        UnprefixedUtilityPrecedence.Below,
        10,
        0,
        "unprefixed:update")]
    [InlineData(
        UnprefixedUtilityPrecedence.Above,
        0,
        10,
        "prefixed:update")]
    [InlineData(
        UnprefixedUtilityPrecedence.SourceOrder,
        0,
        10,
        "unprefixed:update")]
    [InlineData(
        UnprefixedUtilityPrecedence.SourceOrder,
        10,
        0,
        "prefixed:update")]
    public void UtilityCandidates_RespectUnprefixedPrecedence(
        UnprefixedUtilityPrecedence precedence,
        int prefixedSourceOrder,
        int unprefixedSourceOrder,
        string expected)
    {
        var control = new Border();
        var events = new List<string>();
        var prefixed = CreateCandidate(
            "p",
            prefixedSourceOrder,
            "prefixed",
            events,
            AkcssUtilityValueSource.Create<bool>(
                static _ => true),
            precedence: precedence);
        var unprefixed = CreateCandidate(
            "p",
            unprefixedSourceOrder,
            "unprefixed",
            events);

        AkburaControl.SetAkcssStyles(
            control,
            [prefixed, unprefixed]);

        Assert.Equal(expected, events[^1]);
    }

    [Fact]
    public void UtilityCandidates_WithEqualOrder_UseLaterSourceOrder()
    {
        var control = new Border();
        var events = new List<string>();
        var first = CreateCandidate(
            "p",
            sourceOrder: 0,
            "first",
            events,
            AkcssUtilityValueSource.Create<bool>(
                static _ => true),
            order: 10d,
            conflictGroup: "Group");
        var second = CreateCandidate(
            "p",
            sourceOrder: 1,
            "second",
            events,
            AkcssUtilityValueSource.Create<bool>(
                static _ => true),
            order: 10d,
            conflictGroup: "Group");

        AkburaControl.SetAkcssStyles(
            control,
            [first, second]);

        Assert.Equal("second:update", events[^1]);
    }

    [Fact]
    public void UtilityCandidates_WithoutConflictGroup_IgnoreOrder()
    {
        var control = new Border();
        var events = new List<string>();
        var highOrder = CreateCandidate(
            "p",
            sourceOrder: 0,
            "high-order",
            events,
            AkcssUtilityValueSource.Create<bool>(
                static _ => true),
            order: 100d);
        var later = CreateCandidate(
            "p",
            sourceOrder: 1,
            "later",
            events,
            AkcssUtilityValueSource.Create<bool>(
                static _ => true),
            order: 1d);

        AkburaControl.SetAkcssStyles(
            control,
            [highOrder, later]);

        Assert.Equal("later:update", events[^1]);
    }

    [Fact]
    public void UpdateDependentUtilityValue_IsRecreatedOnRefresh()
    {
        var control = new Border();
        var values = new List<int>();
        var created = 0;
        var source = AkcssUtilityValueSource.Create<int>(
            _ => ++created,
            recreateOnRefresh: true);
        var utility = new ValueUtility(values);
        var candidate = new AkcssUtilityCandidateActivator(
            "p",
            sourceOrder: 0,
            applications:
            [
                new AkcssUtilityApplication(
                    utility,
                    (_, arguments) =>
                        utility.Update(
                            control,
                            (int)arguments[0]!)),
            ],
            arguments: [source]);

        AkburaControl.SetAkcssStyles(control, [candidate]);
        AkburaControl.ExecuteAkcssStyles(control);

        Assert.Equal([1, 2], values);
        Assert.Equal(2, created);
    }

    [Fact]
    public void ObservableUtilityValue_RetainsLastValueAfterCompletion()
    {
        var control = new Border();
        var values = new List<int>();
        var signal = new TestSignal<int>();
        var source =
            AkcssUtilityValueSource.CreateObservable<int, int>(
                _ => signal,
                static value => value);
        var utility = new ValueUtility(values);
        var candidate = new AkcssUtilityCandidateActivator(
            "p",
            sourceOrder: 0,
            applications:
            [
                new AkcssUtilityApplication(
                    utility,
                    (_, arguments) =>
                        utility.Update(
                            control,
                            (int)arguments[0]!)),
            ],
            arguments: [source]);

        AkburaControl.SetAkcssStyles(control, [candidate]);
        signal.Emit(7);
        signal.Complete();
        AkburaControl.ExecuteAkcssStyles(control);

        Assert.Equal([7, 7], values);
    }

    [Fact]
    public void ObservableUtilityValue_ErrorIsFailFast()
    {
        var control = new Border();
        var signal = new TestSignal<int>();
        var source =
            AkcssUtilityValueSource.CreateObservable<int, int>(
                _ => signal,
                static value => value);
        var utility = new ValueUtility([]);
        var candidate = new AkcssUtilityCandidateActivator(
            "p",
            sourceOrder: 0,
            applications:
            [
                new AkcssUtilityApplication(
                    utility,
                    (_, arguments) =>
                        utility.Update(
                            control,
                            (int)arguments[0]!)),
            ],
            arguments: [source]);

        AkburaControl.SetAkcssStyles(control, [candidate]);
        var error = new InvalidOperationException("Observable failed.");

        Assert.Same(
            error,
            Assert.Throws<InvalidOperationException>(
                () => signal.Error(error)));
    }

    [Fact]
    public void UtilityBindingPriority_DisposesOnlyItsContributionAndRestoresLocalValue()
    {
        var control = new Border { Width = 10d };
        var signal = new TestSignal<bool>();
        var factoryCalls = 0;
        var variant = AkcssUtilityValueSource
            .CreateObservableWithPriority<bool, bool>(
                _ =>
                {
                    factoryCalls++;
                    return new AkcssUtilityPrefixInvocation<IObservable<bool>?>(
                        signal,
                        BindingPriority.Animation);
                },
                static value => value,
                recreateOnRefresh: true);
        var utility = new WidthPriorityUtility(42d);
        var candidate = new AkcssUtilityCandidateActivator(
            "property:Width",
            sourceOrder: 0,
            applications:
            [
                new AkcssUtilityApplication(
                    utility,
                    static (_, _) => throw new InvalidOperationException(
                        "The legacy utility path must not be used.")),
            ],
            variant: variant);

        AkburaControl.SetAkcssStyles(control, [candidate]);
        Assert.Equal(10d, control.Width);

        signal.Emit(true);
        Assert.Equal(42d, control.Width);

        signal.Emit(false);
        Assert.Equal(10d, control.Width);
        Assert.Equal(0, utility.Operation.ResetCount);

        signal.Emit(true);
        Assert.Equal(42d, control.Width);
        AkburaControl.ExecuteAkcssStyles(control);
        Assert.Equal(10d, control.Width);
        Assert.Equal(2, factoryCalls);

        signal.Emit(true);
        Assert.Equal(42d, control.Width);
        control.ClearValue(AkburaControl.AkcssStylesProperty);
        Assert.Equal(10d, control.Width);
        Assert.Equal(0, utility.Operation.ResetCount);
    }

    [Fact]
    public void UtilityBindingPriority_DoesNotChangeAkcssConflictWinner()
    {
        var control = new Border();
        var first = new RecordingPriorityUtility();
        var second = new RecordingPriorityUtility();
        var firstCandidate = CreatePriorityCandidate(
            first,
            sourceOrder: 0,
            BindingPriority.Animation);
        var secondCandidate = CreatePriorityCandidate(
            second,
            sourceOrder: 1,
            BindingPriority.Style);

        AkburaControl.SetAkcssStyles(
            control,
            [firstCandidate, secondCandidate]);

        Assert.Equal(0, first.Operation.ApplyCount);
        Assert.Equal(1, second.Operation.ApplyCount);
        Assert.Equal(BindingPriority.Style, second.Operation.LastPriority);
    }

    [Theory]
    [InlineData(BindingPriority.Animation)]
    [InlineData(BindingPriority.StyleTrigger)]
    [InlineData(BindingPriority.Template)]
    [InlineData(BindingPriority.Style)]
    public void UtilityBindingPriority_AcceptsEveryReversiblePriority(
        BindingPriority priority)
    {
        var control = new Border();
        var utility = new RecordingPriorityUtility();
        var candidate = CreatePriorityCandidate(
            utility,
            sourceOrder: 0,
            priority);

        AkburaControl.SetAkcssStyles(control, [candidate]);

        Assert.Equal(1, utility.Operation.ApplyCount);
        Assert.Equal(priority, utility.Operation.LastPriority);
    }

    [Theory]
    [InlineData(BindingPriority.LocalValue)]
    [InlineData(BindingPriority.Inherited)]
    [InlineData(BindingPriority.Unset)]
    [InlineData((BindingPriority)123)]
    public void UtilityBindingPriorityMember_RejectsNonReversibleRuntimeValueBeforeWrite(
        BindingPriority priority)
    {
        var control = new Border();
        var variant = AkcssUtilityValueSource.CreateWithPriority<bool>(
            _ => new AkcssUtilityPrefixInvocation<bool>(
                true,
                priority));
        var utility = new RecordingPriorityUtility();
        var candidate = new AkcssUtilityCandidateActivator(
            "property:Width",
            sourceOrder: 0,
            applications:
            [
                new AkcssUtilityApplication(
                    utility,
                    static (_, _) => throw new InvalidOperationException()),
            ],
            variant: variant);

        Assert.Throws<InvalidOperationException>(
            () => AkburaControl.SetAkcssStyles(control, [candidate]));
        Assert.Equal(0, utility.Operation.ApplyCount);
    }

    [Theory]
    [InlineData(typeof(smExtension), 640d)]
    [InlineData(typeof(mdExtension), 768d)]
    [InlineData(typeof(lgExtension), 1024d)]
    [InlineData(typeof(xlExtension), 1280d)]
    [InlineData(typeof(xxlExtension), 1536d)]
    public void BreakpointVariants_UseExpectedThreshold(
        Type extensionType,
        double threshold)
    {
        Assert.NotNull(Activator.CreateInstance(extensionType));
        var method = extensionType.GetMethod(
            "IsActivated",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        Assert.False(
            Assert.IsType<bool>(
                method.Invoke(null, [threshold - 0.01d])));
        Assert.True(
            Assert.IsType<bool>(
                method.Invoke(null, [threshold])));
    }

    private static AkcssUtilityCandidateActivator CreateCandidate(
        string conflictKey,
        int sourceOrder,
        string name,
        List<string> events,
        AkcssUtilityValueSource? variant = null,
        double order = 0d,
        string? conflictGroup = null,
        UnprefixedUtilityPrecedence precedence =
            UnprefixedUtilityPrecedence.SourceOrder)
    {
        var utility = new NamedUtility(name, events);
        return new AkcssUtilityCandidateActivator(
            conflictKey,
            sourceOrder,
            [
                new AkcssUtilityApplication(
                    utility,
                    (target, _) => utility.Update(target)),
            ],
            variant: variant,
            order: order,
            conflictGroup: conflictGroup,
            unprefixedPrecedence: precedence);
    }

    private static AkcssUtilityCandidateActivator CreateOperationCandidate(
        string legacyConflictKey,
        int sourceOrder,
        List<string> events,
        params (string ConflictKey, string Name)[] operations)
    {
        return CreateOperationCandidate(
            legacyConflictKey,
            sourceOrder,
            events,
            operations,
            variant: null);
    }

    private static AkcssUtilityCandidateActivator CreateOperationCandidate(
        string legacyConflictKey,
        int sourceOrder,
        List<string> events,
        (string ConflictKey, string Name)[] operations,
        AkcssUtilityValueSource? variant,
        double order = 0d,
        string? conflictGroup = null,
        UnprefixedUtilityPrecedence precedence =
            UnprefixedUtilityPrecedence.SourceOrder)
    {
        var utility = new OperationUtility(events, operations);
        return new AkcssUtilityCandidateActivator(
            legacyConflictKey,
            sourceOrder,
            [
                new AkcssUtilityApplication(
                    utility,
                    (target, _) => utility.Update(target)),
            ],
            variant: variant,
            order: order,
            conflictGroup: conflictGroup,
            unprefixedPrecedence: precedence);
    }

    private static AkcssUtilityValueSource CreateVariantSource(
        IObservable<bool> signal)
    {
        return AkcssUtilityValueSource
            .CreateObservable<bool, bool>(
                _ => signal,
                static value => value);
    }

    private static AkcssUtilityCandidateActivator CreatePriorityCandidate(
        RecordingPriorityUtility utility,
        int sourceOrder,
        BindingPriority bindingPriority)
    {
        return new AkcssUtilityCandidateActivator(
            "property:Width",
            sourceOrder,
            [
                new AkcssUtilityApplication(
                    utility,
                    static (_, _) => throw new InvalidOperationException(
                        "The legacy utility path must not be used.")),
            ],
            bindingPriority: bindingPriority);
    }

    private sealed class LoggingClass : AkcssClass
    {
        private readonly List<string> _events;
        private readonly IObservable<object?> _signal;

        public LoggingClass(
            List<string> events,
            IObservable<object?> signal)
        {
            _events = events;
            _signal = signal;
        }

        public override void Update(object control)
        {
            _events.Add("class:update");
        }

        public override void Reset(object target)
        {
            _events.Add("class:reset");
        }

        public override IObservable<object?> Watch(object target)
        {
            return _signal;
        }
    }

    private sealed class LoggingUtility : AkcssUtility<int>
    {
        private readonly List<string> _events;

        public LoggingUtility(List<string> events)
        {
            _events = events;
        }

        public override void Update(object target, int value)
        {
            _events.Add($"utility:update:{value}");
        }

        public override void Reset(object target)
        {
            _events.Add("utility:reset");
        }
    }

    private sealed class LoggingUtilityActivator : TailwindUtilityActivator
    {
        public LoggingUtilityActivator(LoggingUtility utility, int value)
            : base(utility, isConditional: false, [value])
        {
        }

        public override bool Condition => true;

        public override void Execute(Control control)
        {
            ((LoggingUtility)Utility).Update(control, (int)Arguments[0]!);
        }
    }

    private sealed class CountingUtility : ZeroAkcssUtility
    {
        public int UpdateCount { get; private set; }

        public int ResetCount { get; private set; }

        public override void Update(object target)
        {
            UpdateCount++;
        }

        public override void Reset(object target)
        {
            ResetCount++;
        }
    }

    private sealed class NamedUtility : ZeroAkcssUtility
    {
        private readonly string _name;
        private readonly List<string> _events;

        public NamedUtility(
            string name,
            List<string> events)
        {
            _name = name;
            _events = events;
        }

        public override void Update(object target)
        {
            _events.Add(_name + ":update");
        }

        public override void Reset(object target)
        {
            _events.Add(_name + ":reset");
        }
    }

    private sealed class OperationUtility : ZeroAkcssUtility
    {
        private readonly ImmutableArray<AkcssUtilityOperation> _operations;

        public OperationUtility(
            List<string> events,
            IEnumerable<(string ConflictKey, string Name)> operations)
        {
            _operations =
            [
                .. operations.Select(
                    (operation, index) =>
                        (AkcssUtilityOperation)new LoggingUtilityOperation(
                            this,
                            operation.ConflictKey,
                            index,
                            operation.Name,
                            events)),
            ];
        }

        public override ImmutableArray<AkcssUtilityOperation> Operations =>
            _operations;

        public override void Update(object target)
        {
        }
    }

    private sealed class LoggingUtilityOperation
        : AkcssUtilityOperation
    {
        private readonly string _name;
        private readonly List<string> _events;

        public LoggingUtilityOperation(
            AkcssUtility utility,
            string conflictKey,
            int order,
            string name,
            List<string> events)
            : base(
                utility,
                conflictKey,
                AkcssOperationPriority.Style,
                order)
        {
            _name = name;
            _events = events;
        }

        public override bool IsActive(
            object target,
            IReadOnlyList<object?> arguments)
        {
            return true;
        }

        public override void Update(
            object target,
            IReadOnlyList<object?> arguments)
        {
            _events.Add(_name + ":update");
        }

        public override void Reset(object target)
        {
            _events.Add(_name + ":reset");
        }
    }

    private sealed class ValueUtility : AkcssUtility<int>
    {
        private readonly List<int> _values;

        public ValueUtility(List<int> values)
        {
            _values = values;
        }

        public override void Update(object target, int value)
        {
            _values.Add(value);
        }
    }

    private sealed class WidthPriorityUtility : ZeroAkcssUtility
    {
        private readonly ImmutableArray<AkcssUtilityOperation> _operations;

        public WidthPriorityUtility(double value)
        {
            Operation = new WidthPriorityOperation(this, value);
            _operations = [Operation];
        }

        public WidthPriorityOperation Operation { get; }

        public override ImmutableArray<AkcssUtilityOperation> Operations =>
            _operations;

        public override void Update(object target)
        {
        }
    }

    private sealed class WidthPriorityOperation : AkcssUtilityOperation
    {
        private readonly double _value;

        public WidthPriorityOperation(
            AkcssUtility utility,
            double value)
            : base(
                utility,
                "property:Width",
                AkcssOperationPriority.Style,
                order: 0)
        {
            _value = value;
        }

        public int ResetCount { get; private set; }

        public override bool IsActive(
            object target,
            IReadOnlyList<object?> arguments) => true;

        public override void Update(
            object target,
            IReadOnlyList<object?> arguments)
        {
            throw new InvalidOperationException(
                "Priority-aware candidates must call Apply.");
        }

        public override IDisposable Apply(
            object target,
            IReadOnlyList<object?> arguments,
            BindingPriority priority)
        {
            return ((Border)target).SetValue(
                Border.WidthProperty,
                _value,
                priority)!;
        }

        public override void Reset(object target)
        {
            ResetCount++;
            ((Border)target).ClearValue(Border.WidthProperty);
        }
    }

    private sealed class RecordingPriorityUtility : ZeroAkcssUtility
    {
        private readonly ImmutableArray<AkcssUtilityOperation> _operations;

        public RecordingPriorityUtility()
        {
            Operation = new RecordingPriorityOperation(this);
            _operations = [Operation];
        }

        public RecordingPriorityOperation Operation { get; }

        public override ImmutableArray<AkcssUtilityOperation> Operations =>
            _operations;

        public override void Update(object target)
        {
        }
    }

    private sealed class RecordingPriorityOperation : AkcssUtilityOperation
    {
        public RecordingPriorityOperation(AkcssUtility utility)
            : base(
                utility,
                "property:Width",
                AkcssOperationPriority.Style,
                order: 0)
        {
        }

        public int ApplyCount { get; private set; }

        public BindingPriority? LastPriority { get; private set; }

        public override bool IsActive(
            object target,
            IReadOnlyList<object?> arguments) => true;

        public override void Update(
            object target,
            IReadOnlyList<object?> arguments)
        {
            throw new InvalidOperationException();
        }

        public override IDisposable Apply(
            object target,
            IReadOnlyList<object?> arguments,
            BindingPriority priority)
        {
            ApplyCount++;
            LastPriority = priority;
            return new TestDisposable();
        }
    }

    private sealed class TestDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private sealed class ArgumentUtility : AkcssUtility<int>
    {
        public override void Update(object target, int value)
        {
        }
    }

    private sealed class ConditionalUtilityActivator : SimpleTailwindUtilityActivator
    {
        public ConditionalUtilityActivator(
            CountingUtility utility,
            IObservable<bool> condition)
            : base(utility, [], condition)
        {
        }

        public override void Execute(Control control)
        {
            ((CountingUtility)Utility).Update(control);
        }
    }

    private sealed class RawUtilityActivator : TailwindUtilityActivator
    {
        public RawUtilityActivator(
            AkcssUtility utility,
            ImmutableArray<object?> arguments)
            : base(utility, isConditional: false, arguments)
        {
        }

        public override bool Condition => true;

        public override void Execute(Control control)
        {
        }
    }

    [ObservesProperty(nameof(Control.Width))]
    private sealed class WidthObservingClass : AkcssClass
    {
        public int UpdateCount { get; private set; }

        public int ResetCount { get; private set; }

        public override void Update(object control)
        {
            UpdateCount++;
        }

        public override void Reset(object target)
        {
            ResetCount++;
        }
    }

    private sealed class TestSignal<T> : IObservable<T>
    {
        private readonly List<IObserver<T>> _observers = [];

        public IDisposable Subscribe(IObserver<T> observer)
        {
            ArgumentNullException.ThrowIfNull(observer);
            _observers.Add(observer);
            return new Subscription(_observers, observer);
        }

        public void Emit(T value)
        {
            foreach (var observer in _observers.ToArray())
            {
                observer.OnNext(value);
            }
        }

        public void Error(Exception error)
        {
            foreach (var observer in _observers.ToArray())
            {
                observer.OnError(error);
            }
        }

        public void Complete()
        {
            foreach (var observer in _observers.ToArray())
            {
                observer.OnCompleted();
            }
        }

        private sealed class Subscription : IDisposable
        {
            private List<IObserver<T>>? _observers;
            private IObserver<T>? _observer;

            public Subscription(
                List<IObserver<T>> observers,
                IObserver<T> observer)
            {
                _observers = observers;
                _observer = observer;
            }

            public void Dispose()
            {
                var observers = Interlocked.Exchange(ref _observers, null);
                var observer = Interlocked.Exchange(ref _observer, null);
                if (observers != null && observer != null)
                {
                    observers.Remove(observer);
                }
            }
        }
    }
}
