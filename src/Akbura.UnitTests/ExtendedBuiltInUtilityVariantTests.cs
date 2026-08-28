using Akbura.Language;
using Akbura.Language.Operations;
using Akbura.Language.Syntax;
using Akbura.Markup;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ExtendedBuiltInUtilityVariantTests
{
    private static readonly VariantMetadataCase[] VariantCases =
    [
        new(typeof(focusWithinExtension), "focusWithin", 5d, "Akbura.Tailwind.Interaction"),
        new(typeof(focusVisibleExtension), "focusVisible", 30d, "Akbura.Tailwind.Interaction"),
        new(typeof(activeExtension), "active", 40d, "Akbura.Tailwind.Interaction"),
        new(typeof(enabledExtension), "enabled", 10d, "Akbura.Tailwind.Availability"),
        new(typeof(disabledExtension), "disabled", 20d, "Akbura.Tailwind.Availability"),
        new(typeof(visitedExtension), "visited", 10d, "Akbura.Tailwind.LinkState"),
        new(typeof(openExtension), "open", 10d, "Akbura.Tailwind.Disclosure"),
        new(typeof(checkedExtension), "checked", 10d, "Akbura.Tailwind.ToggleState"),
        new(typeof(indeterminateExtension), "indeterminate", 20d, "Akbura.Tailwind.ToggleState"),
        new(typeof(selectedExtension), "selected", 10d, "Akbura.Tailwind.Selection"),
        new(typeof(optionalExtension), "optional", 10d, "Akbura.Tailwind.Requirement"),
        new(typeof(requiredExtension), "required", 20d, "Akbura.Tailwind.Requirement"),
        new(typeof(validExtension), "valid", 10d, "Akbura.Tailwind.Validation"),
        new(typeof(invalidExtension), "invalid", 20d, "Akbura.Tailwind.Validation"),
        new(typeof(inRangeExtension), "inRange", 10d, "Akbura.Tailwind.RangeState"),
        new(typeof(outOfRangeExtension), "outOfRange", 20d, "Akbura.Tailwind.RangeState"),
        new(typeof(readOnlyExtension), "readOnly", 10d, "Akbura.Tailwind.Editability"),
        new(typeof(placeholderShownExtension), "placeholderShown", 10d, "Akbura.Tailwind.Placeholder"),
        new(typeof(defaultExtension), "default", 10d, "Akbura.Tailwind.DefaultState"),
        new(typeof(firstExtension), "first", 10d, "Akbura.Tailwind.Structure"),
        new(typeof(lastExtension), "last", 20d, "Akbura.Tailwind.Structure"),
        new(typeof(onlyExtension), "only", 30d, "Akbura.Tailwind.Structure"),
        new(typeof(oddExtension), "odd", 40d, "Akbura.Tailwind.Structure"),
        new(typeof(evenExtension), "even", 50d, "Akbura.Tailwind.Structure"),
        new(typeof(firstOfTypeExtension), "firstOfType", 60d, "Akbura.Tailwind.Structure"),
        new(typeof(lastOfTypeExtension), "lastOfType", 70d, "Akbura.Tailwind.Structure"),
        new(typeof(onlyOfTypeExtension), "onlyOfType", 80d, "Akbura.Tailwind.Structure"),
        new(typeof(emptyExtension), "empty", 130d, "Akbura.Tailwind.Structure"),
        new(typeof(nthExtension), "nth Step=2, Offset=1", 0d, null),
        new(typeof(nthLastExtension), "nthLast Offset=2", 0d, null),
        new(typeof(nthOfTypeExtension), "nthOfType Offset=2", 0d, null),
        new(typeof(nthLastOfTypeExtension), "nthLastOfType Offset=2", 0d, null),
        new(typeof(ltrExtension), "ltr", 10d, "Akbura.Tailwind.Direction"),
        new(typeof(rtlExtension), "rtl", 20d, "Akbura.Tailwind.Direction"),
        new(typeof(portraitExtension), "portrait", 10d, "Akbura.Tailwind.Orientation"),
        new(typeof(landscapeExtension), "landscape", 20d, "Akbura.Tailwind.Orientation"),
        new(typeof(contrastMoreExtension), "contrastMore", 10d, "Akbura.Tailwind.Contrast"),
        new(typeof(minExtension), "min Width=900", 0d, null),
        new(typeof(maxExtension), "max Width=900", 0d, null),
        new(typeof(maxSmExtension), "maxSm", -10d, "BreakpointsGroup"),
        new(typeof(maxMdExtension), "maxMd", -20d, "BreakpointsGroup"),
        new(typeof(maxLgExtension), "maxLg", -30d, "BreakpointsGroup"),
        new(typeof(maxXlExtension), "maxXl", -40d, "BreakpointsGroup"),
        new(typeof(maxXxlExtension), "maxXxl", -50d, "BreakpointsGroup"),
    ];

    [Fact]
    public void ExtendedVariants_ExposeExpectedMetadataAndStyleTriggerPriority()
    {
        foreach (var testCase in VariantCases)
        {
            var variants = testCase.ExtensionType
                .GetCustomAttributes<UtilityVariantAttribute>(inherit: false)
                .ToArray();
            var priorities = testCase.ExtensionType
                .GetCustomAttributes<UtilityBindingPriorityAttribute>(
                    inherit: true)
                .ToArray();

            var variant = Assert.Single(variants);
            var priority = Assert.Single(priorities);

            Assert.Equal(testCase.Order, variant.Order);
            Assert.Equal(testCase.ConflictGroup, variant.ConflictGroup);
            Assert.Equal(
                UnprefixedUtilityPrecedence.Above,
                variant.UnprefixedPrecedence);
            Assert.Equal(BindingPriority.StyleTrigger, priority.Priority);
            Assert.Null(priority.PriorityMember);
        }
    }

    [Fact]
    public void SemanticModel_AllExtendedPrefixesBindVariantAndPriorityMetadata()
    {
        var attributesText = string.Join(
            Environment.NewLine,
            VariantCases.Select(
                (testCase, index) =>
                    $"    ${{{testCase.Prefix}}}:p-{index + 1}"));
        var code = $$"""
            using Avalonia.Controls;
            using Akbura.Markup;

            @akcss {
                @using Avalonia.Controls;

                @utilities {
                    Control.p-(double value) { Width: value; }
                }
            }

            <Border
            {{attributesText}} />
            """;

        var syntaxTree = AkburaSyntaxTree.ParseText(code);
        var compilation = new AkburaCompilation(
            CreateCSharpCompilation(),
            [syntaxTree]);
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var attributes = GetOnlyMarkupElement(syntaxTree)
            .StartTag!
            .Attributes
            .OfType<TailwindFullAttributeSyntax>()
            .ToArray();

        Assert.Equal(VariantCases.Length, attributes.Length);

        for (var index = 0; index < VariantCases.Length; index++)
        {
            var testCase = VariantCases[index];
            var attribute = attributes[index];
            var diagnostics = semanticModel.GetSemanticDiagnostics(attribute);
            var operation = Assert.IsAssignableFrom<
                ITailwindUtilityAttributeOperation>(
                    semanticModel.GetOperation(attribute));

            Assert.True(
                diagnostics.IsEmpty,
                $"{testCase.ExtensionType.Name}: " +
                string.Join(
                    " | ",
                    diagnostics.Select(static diagnostic =>
                        diagnostic.Message)));
            Assert.False(operation.HasErrors);
            Assert.NotNull(operation.ConditionMarkupExtension);
            Assert.True(operation.Variant.IsPrefixed);
            Assert.Equal(testCase.Order, operation.Variant.Order);
            Assert.Equal(
                testCase.ConflictGroup,
                operation.Variant.ConflictGroup);
            Assert.Equal(
                TailwindUtilityUnprefixedPrecedence.Above,
                operation.Variant.UnprefixedPrecedence);
            Assert.Equal(
                TailwindUtilityBindingPrioritySource.Constant,
                operation.BindingPriority.Source);
            Assert.Equal(
                (int)BindingPriority.StyleTrigger,
                operation.BindingPriority.ConstantValue);
        }
    }

    [Fact]
    public async Task AvailabilityVariants_ObserveEffectiveEnabledStateFromParent()
    {
        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));

        await session.Dispatch(
            () =>
            {
                var target = new Button();
                var parent = new StackPanel
                {
                    Children = { target },
                };
                var window = new Window { Content = parent };
                window.Show();

                try
                {
                    var enabled = Observe(new enabledExtension(), target);
                    var disabled = Observe(new disabledExtension(), target);
                    var enabledObserver = new RecordingObserver<bool>();
                    var disabledObserver = new RecordingObserver<bool>();

                    using var enabledSubscription =
                        enabled.Subscribe(enabledObserver);
                    using var disabledSubscription =
                        disabled.Subscribe(disabledObserver);

                    Assert.Equal([true], enabledObserver.Values);
                    Assert.Equal([false], disabledObserver.Values);

                    parent.IsEnabled = false;
                    Assert.Equal([true, false], enabledObserver.Values);
                    Assert.Equal([false, true], disabledObserver.Values);

                    parent.IsEnabled = true;
                    Assert.Equal([true, false, true], enabledObserver.Values);
                    Assert.Equal([false, true, false], disabledObserver.Values);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task FocusVariants_DistinguishWithinAndKeyboardVisibleFocus()
    {
        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));

        await session.Dispatch(
            () =>
            {
                var child = new Button();
                var outside = new Button();
                var container = new Border { Child = child };
                var window = new Window
                {
                    Content = new StackPanel
                    {
                        Children =
                        {
                            container,
                            outside,
                        },
                    },
                };
                window.Show();

                try
                {
                    var focusWithin = Observe(
                        new focusWithinExtension(),
                        container);
                    var focusVisible = Observe(
                        new focusVisibleExtension(),
                        child);
                    var withinObserver = new RecordingObserver<bool>();
                    var visibleObserver = new RecordingObserver<bool>();

                    using var withinSubscription =
                        focusWithin.Subscribe(withinObserver);
                    using var visibleSubscription =
                        focusVisible.Subscribe(visibleObserver);

                    Assert.Equal([false], withinObserver.Values);
                    Assert.Equal([false], visibleObserver.Values);

                    Assert.True(child.Focus(
                        NavigationMethod.Tab,
                        KeyModifiers.None));
                    Assert.Equal([false, true], withinObserver.Values);
                    Assert.Equal([false, true], visibleObserver.Values);

                    Assert.True(outside.Focus(
                        NavigationMethod.Tab,
                        KeyModifiers.None));
                    Assert.Equal([false, true, false], withinObserver.Values);
                    Assert.Equal([false, true, false], visibleObserver.Values);

                    Assert.True(child.Focus(
                        NavigationMethod.Pointer,
                        KeyModifiers.None));
                    Assert.Equal(
                        [false, true, false, true],
                        withinObserver.Values);
                    Assert.Equal([false, true, false], visibleObserver.Values);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task PseudoClassVariants_TrackRealToggleSelectionAndDisclosureControls()
    {
        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));

        await session.Dispatch(
            () =>
            {
                var checkBox = new CheckBox { IsThreeState = true };
                var firstItem = new ListBoxItem { Content = "first" };
                var secondItem = new ListBoxItem { Content = "second" };
                var listBox = new ListBox();
                listBox.Items.Add(firstItem);
                listBox.Items.Add(secondItem);
                var expander = new Expander
                {
                    Header = "details",
                    Content = new Border(),
                };
                var window = new Window
                {
                    Content = new StackPanel
                    {
                        Children =
                        {
                            checkBox,
                            listBox,
                            expander,
                        },
                    },
                };
                window.Show();
                window.UpdateLayout();

                try
                {
                    var checkedObserver = Subscribe(
                        new checkedExtension(),
                        checkBox,
                        out var checkedSubscription);
                    var indeterminateObserver = Subscribe(
                        new indeterminateExtension(),
                        checkBox,
                        out var indeterminateSubscription);
                    var firstSelectedObserver = Subscribe(
                        new selectedExtension(),
                        firstItem,
                        out var firstSelectedSubscription);
                    var secondSelectedObserver = Subscribe(
                        new selectedExtension(),
                        secondItem,
                        out var secondSelectedSubscription);
                    var openObserver = Subscribe(
                        new openExtension(),
                        expander,
                        out var openSubscription);

                    using (checkedSubscription)
                    using (indeterminateSubscription)
                    using (firstSelectedSubscription)
                    using (secondSelectedSubscription)
                    using (openSubscription)
                    {
                        AssertCurrent(checkedObserver, false);
                        AssertCurrent(indeterminateObserver, false);
                        AssertCurrent(firstSelectedObserver, false);
                        AssertCurrent(secondSelectedObserver, false);
                        AssertCurrent(openObserver, false);

                        checkBox.IsChecked = true;
                        AssertCurrent(checkedObserver, true);
                        AssertCurrent(indeterminateObserver, false);

                        checkBox.IsChecked = null;
                        AssertCurrent(checkedObserver, false);
                        AssertCurrent(indeterminateObserver, true);

                        checkBox.IsChecked = false;
                        AssertCurrent(indeterminateObserver, false);

                        firstItem.IsSelected = true;
                        AssertCurrent(firstSelectedObserver, true);
                        AssertCurrent(secondSelectedObserver, false);

                        firstItem.IsSelected = false;
                        secondItem.IsSelected = true;
                        AssertCurrent(firstSelectedObserver, false);
                        AssertCurrent(secondSelectedObserver, true);

                        secondItem.IsSelected = false;
                        AssertCurrent(secondSelectedObserver, false);

                        expander.IsExpanded = true;
                        AssertCurrent(openObserver, true);
                        expander.IsExpanded = false;
                        AssertCurrent(openObserver, false);
                    }
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public void ActiveVariant_ObservesPressedPseudoClass()
    {
        var target = new PseudoClassButton();
        var observer = Subscribe(
            new activeExtension(),
            target,
            out var subscription);

        using (subscription)
        {
            Assert.Equal([false], observer.Values);

            target.SetPseudoClass(":pressed", true);
            Assert.Equal([false, true], observer.Values);

            target.SetPseudoClass(":pressed", false);
            Assert.Equal([false, true, false], observer.Values);
        }
    }

    [Fact]
    public void RequirementAndValidationVariants_TrackAttachedProperties()
    {
        var target = new TextBox();
        var requiredObserver = Subscribe(
            new requiredExtension(),
            target,
            out var requiredSubscription);
        var optionalObserver = Subscribe(
            new optionalExtension(),
            target,
            out var optionalSubscription);
        var validObserver = Subscribe(
            new validExtension(),
            target,
            out var validSubscription);
        var invalidObserver = Subscribe(
            new invalidExtension(),
            target,
            out var invalidSubscription);

        using (requiredSubscription)
        using (optionalSubscription)
        using (validSubscription)
        using (invalidSubscription)
        {
            Assert.Equal([false], requiredObserver.Values);
            Assert.Equal([true], optionalObserver.Values);
            Assert.Equal([true], validObserver.Values);
            Assert.Equal([false], invalidObserver.Values);

            AutomationProperties.SetIsRequiredForForm(target, true);
            Assert.Equal([false, true], requiredObserver.Values);
            Assert.Equal([true, false], optionalObserver.Values);

            AutomationProperties.SetIsRequiredForForm(target, false);
            Assert.Equal([false, true, false], requiredObserver.Values);
            Assert.Equal([true, false, true], optionalObserver.Values);

            target.SetValue(DataValidationErrors.HasErrorsProperty, true);
            Assert.Equal([true, false], validObserver.Values);
            Assert.Equal([false, true], invalidObserver.Values);

            target.SetValue(DataValidationErrors.HasErrorsProperty, false);
            Assert.Equal([true, false, true], validObserver.Values);
            Assert.Equal([false, true, false], invalidObserver.Values);
        }
    }

    [Fact]
    public void RangeVariants_TrackValueAndBounds()
    {
        var numeric = new NumericUpDown
        {
            Minimum = 0m,
            Maximum = 10m,
            Value = 5m,
        };
        var inRangeObserver = Subscribe(
            new inRangeExtension(),
            numeric,
            out var inRangeSubscription);
        var outOfRangeObserver = Subscribe(
            new outOfRangeExtension(),
            numeric,
            out var outOfRangeSubscription);

        using (inRangeSubscription)
        using (outOfRangeSubscription)
        {
            Assert.Equal([true], inRangeObserver.Values);
            Assert.Equal([false], outOfRangeObserver.Values);

            numeric.Value = 11m;
            Assert.Equal([true, false], inRangeObserver.Values);
            Assert.Equal([false, true], outOfRangeObserver.Values);

            numeric.Value = null;
            Assert.Equal([true, false], inRangeObserver.Values);
            Assert.Equal([false, true, false], outOfRangeObserver.Values);

            numeric.Value = 0m;
            Assert.Equal([true, false, true], inRangeObserver.Values);
            Assert.Equal([false, true, false], outOfRangeObserver.Values);
        }

        Assert.Null(new inRangeExtension().ProvideValue(
            CreateServiceProvider(new TextBox())));
        Assert.Null(new outOfRangeExtension().ProvideValue(
            CreateServiceProvider(new TextBox())));
    }

    [Fact]
    public void ReadOnlyVariant_SupportsTextAndNumericEditors()
    {
        var textBox = new TextBox();
        var numeric = new NumericUpDown();
        var textObserver = Subscribe(
            new readOnlyExtension(),
            textBox,
            out var textSubscription);
        var numericObserver = Subscribe(
            new readOnlyExtension(),
            numeric,
            out var numericSubscription);

        using (textSubscription)
        using (numericSubscription)
        {
            Assert.Equal([false], textObserver.Values);
            Assert.Equal([false], numericObserver.Values);

            textBox.IsReadOnly = true;
            numeric.IsReadOnly = true;
            Assert.Equal([false, true], textObserver.Values);
            Assert.Equal([false, true], numericObserver.Values);

            textBox.IsReadOnly = false;
            numeric.IsReadOnly = false;
            Assert.Equal([false, true, false], textObserver.Values);
            Assert.Equal([false, true, false], numericObserver.Values);
        }

        Assert.Null(new readOnlyExtension().ProvideValue(
            CreateServiceProvider(new Border())));
    }

    [Fact]
    public void PlaceholderShownVariant_RequiresEmptyTextAndPlaceholder()
    {
        var target = new TextBox { PlaceholderText = "hint" };
        var observer = Subscribe(
            new placeholderShownExtension(),
            target,
            out var subscription);

        using (subscription)
        {
            Assert.Equal([true], observer.Values);

            target.Text = "value";
            Assert.Equal([true, false], observer.Values);

            target.Text = string.Empty;
            Assert.Equal([true, false, true], observer.Values);

            target.PlaceholderText = null;
            Assert.Equal([true, false, true, false], observer.Values);
        }

        Assert.Null(new placeholderShownExtension().ProvideValue(
            CreateServiceProvider(new Border())));
    }

    [Fact]
    public void StructuralVariants_ReactToSiblingInsertionAndReordering()
    {
        var target = new Button();
        var parent = new StackPanel { Children = { target } };
        var firstObserver = Subscribe(
            new firstExtension(), target, out var firstSubscription);
        var lastObserver = Subscribe(
            new lastExtension(), target, out var lastSubscription);
        var onlyObserver = Subscribe(
            new onlyExtension(), target, out var onlySubscription);
        var oddObserver = Subscribe(
            new oddExtension(), target, out var oddSubscription);
        var evenObserver = Subscribe(
            new evenExtension(), target, out var evenSubscription);
        var firstOfTypeObserver = Subscribe(
            new firstOfTypeExtension(),
            target,
            out var firstOfTypeSubscription);
        var lastOfTypeObserver = Subscribe(
            new lastOfTypeExtension(),
            target,
            out var lastOfTypeSubscription);
        var onlyOfTypeObserver = Subscribe(
            new onlyOfTypeExtension(),
            target,
            out var onlyOfTypeSubscription);

        using (firstSubscription)
        using (lastSubscription)
        using (onlySubscription)
        using (oddSubscription)
        using (evenSubscription)
        using (firstOfTypeSubscription)
        using (lastOfTypeSubscription)
        using (onlyOfTypeSubscription)
        {
            AssertStructuralState(
                firstObserver,
                lastObserver,
                onlyObserver,
                oddObserver,
                evenObserver,
                firstOfTypeObserver,
                lastOfTypeObserver,
                onlyOfTypeObserver,
                first: true,
                last: true,
                only: true,
                odd: true,
                even: false,
                firstOfType: true,
                lastOfType: true,
                onlyOfType: true);

            parent.Children.Insert(0, new Border());
            AssertStructuralState(
                firstObserver,
                lastObserver,
                onlyObserver,
                oddObserver,
                evenObserver,
                firstOfTypeObserver,
                lastOfTypeObserver,
                onlyOfTypeObserver,
                first: false,
                last: true,
                only: false,
                odd: false,
                even: true,
                firstOfType: true,
                lastOfType: true,
                onlyOfType: true);

            parent.Children.Add(new Button());
            AssertStructuralState(
                firstObserver,
                lastObserver,
                onlyObserver,
                oddObserver,
                evenObserver,
                firstOfTypeObserver,
                lastOfTypeObserver,
                onlyOfTypeObserver,
                first: false,
                last: false,
                only: false,
                odd: false,
                even: true,
                firstOfType: true,
                lastOfType: false,
                onlyOfType: false);

            parent.Children.Remove(target);
            parent.Children.Add(target);
            AssertStructuralState(
                firstObserver,
                lastObserver,
                onlyObserver,
                oddObserver,
                evenObserver,
                firstOfTypeObserver,
                lastOfTypeObserver,
                onlyOfTypeObserver,
                first: false,
                last: true,
                only: false,
                odd: true,
                even: false,
                firstOfType: false,
                lastOfType: true,
                onlyOfType: false);
        }
    }

    [Fact]
    public void EmptyVariant_TracksLogicalChildren()
    {
        var target = new StackPanel();
        var observer = Subscribe(
            new emptyExtension(),
            target,
            out var subscription);

        using (subscription)
        {
            Assert.Equal([true], observer.Values);

            var child = new Border();
            target.Children.Add(child);
            Assert.Equal([true, false], observer.Values);

            target.Children.Remove(child);
            Assert.Equal([true, false, true], observer.Values);
        }
    }

    [Fact]
    public void ParameterizedNthVariants_UseOneBasedAndOfTypePositions()
    {
        var firstButton = new Button();
        var target = new Button();
        var thirdButton = new Button();
        var parent = new StackPanel
        {
            Children =
            {
                firstButton,
                new Border(),
                target,
                thirdButton,
                new Border(),
            },
        };
        var nthObserver = Subscribe(
            new nthExtension { Step = 2, Offset = 1 },
            target,
            out var nthSubscription);
        var nthLastObserver = Subscribe(
            new nthLastExtension { Offset = 3 },
            target,
            out var nthLastSubscription);
        var nthOfTypeObserver = Subscribe(
            new nthOfTypeExtension { Offset = 2 },
            target,
            out var nthOfTypeSubscription);
        var nthLastOfTypeObserver = Subscribe(
            new nthLastOfTypeExtension { Offset = 2 },
            target,
            out var nthLastOfTypeSubscription);

        using (nthSubscription)
        using (nthLastSubscription)
        using (nthOfTypeSubscription)
        using (nthLastOfTypeSubscription)
        {
            AssertCurrent(nthObserver, true);
            AssertCurrent(nthLastObserver, true);
            AssertCurrent(nthOfTypeObserver, true);
            AssertCurrent(nthLastOfTypeObserver, true);

            parent.Children.Remove(target);
            parent.Children.Insert(3, target);

            AssertCurrent(nthObserver, false);
            AssertCurrent(nthLastObserver, false);
            AssertCurrent(nthOfTypeObserver, false);
            AssertCurrent(nthLastOfTypeObserver, false);
        }
    }

    [Fact]
    public void DirectionVariants_TrackFlowDirection()
    {
        var target = new Border
        {
            FlowDirection = FlowDirection.LeftToRight,
        };
        var ltrObserver = Subscribe(
            new ltrExtension(), target, out var ltrSubscription);
        var rtlObserver = Subscribe(
            new rtlExtension(), target, out var rtlSubscription);

        using (ltrSubscription)
        using (rtlSubscription)
        {
            Assert.Equal([true], ltrObserver.Values);
            Assert.Equal([false], rtlObserver.Values);

            target.FlowDirection = FlowDirection.RightToLeft;
            Assert.Equal([true, false], ltrObserver.Values);
            Assert.Equal([false, true], rtlObserver.Values);

            target.FlowDirection = FlowDirection.LeftToRight;
            Assert.Equal([true, false, true], ltrObserver.Values);
            Assert.Equal([false, true, false], rtlObserver.Values);
        }
    }

    [Fact]
    public async Task ViewportOrientationAndContrastVariants_ResolveFromTopLevel()
    {
        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));

        await session.Dispatch(
            () =>
            {
                var target = new Border();
                var window = new Window
                {
                    Width = 300d,
                    Height = 500d,
                    Content = target,
                };
                window.Show();
                window.UpdateLayout();

                try
                {
                    var portraitObserver = Subscribe(
                        new portraitExtension(),
                        target,
                        out var portraitSubscription);
                    var landscapeObserver = Subscribe(
                        new landscapeExtension(),
                        target,
                        out var landscapeSubscription);
                    var contrastObserver = Subscribe(
                        new contrastMoreExtension(),
                        target,
                        out var contrastSubscription);

                    using (portraitSubscription)
                    using (landscapeSubscription)
                    using (contrastSubscription)
                    {
                        AssertCurrent(portraitObserver, true);
                        AssertCurrent(landscapeObserver, false);
                        Assert.Single(contrastObserver.Values);

                        SetClientSize(window, new Size(600d, 300d));
                        window.UpdateLayout();

                        AssertCurrent(portraitObserver, false);
                        AssertCurrent(landscapeObserver, true);
                    }
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Theory]
    [InlineData(typeof(maxSmExtension), 640d)]
    [InlineData(typeof(maxMdExtension), 768d)]
    [InlineData(typeof(maxLgExtension), 1024d)]
    [InlineData(typeof(maxXlExtension), 1280d)]
    [InlineData(typeof(maxXxlExtension), 1536d)]
    public void NamedMaxBreakpoints_UseExclusiveThreshold(
        Type extensionType,
        double threshold)
    {
        Assert.NotNull(Activator.CreateInstance(extensionType));
        var method = Assert.IsAssignableFrom<MethodInfo>(
            extensionType.GetMethod(
                "IsActivated",
                BindingFlags.NonPublic | BindingFlags.Static));

        Assert.True(Assert.IsType<bool>(
            method.Invoke(null, [threshold - 0.01d])));
        Assert.False(Assert.IsType<bool>(
            method.Invoke(null, [threshold])));
    }

    [Fact]
    public void ParameterizedViewportVariants_UseInclusiveMinAndExclusiveMax()
    {
        var min = new minExtension { Width = 900d };
        var max = new maxExtension { Width = 900d };

        Assert.False(InvokeViewportPredicate(min, new Size(899.99d, 500d)));
        Assert.True(InvokeViewportPredicate(max, new Size(899.99d, 500d)));
        Assert.True(InvokeViewportPredicate(min, new Size(900d, 500d)));
        Assert.False(InvokeViewportPredicate(max, new Size(900d, 500d)));
    }

    private static void SetClientSize(TopLevel topLevel, Size size)
    {
        var property = Assert.IsAssignableFrom<PropertyInfo>(
            typeof(TopLevel).GetProperty(nameof(TopLevel.ClientSize)));
        var setter = Assert.IsAssignableFrom<MethodInfo>(
            property.GetSetMethod(nonPublic: true));

        setter.Invoke(topLevel, [size]);
    }

    private static bool InvokeViewportPredicate(
        object extension,
        Size size)
    {
        var method = Assert.IsAssignableFrom<MethodInfo>(
            extension.GetType().GetMethod(
                "IsActive",
                BindingFlags.Instance | BindingFlags.NonPublic));

        return Assert.IsType<bool>(method.Invoke(extension, [size]));
    }

    private static IObservable<bool> Observe(
        object extension,
        StyledElement target)
    {
        var provideValue = Assert.IsAssignableFrom<MethodInfo>(
            extension.GetType().GetMethod(
                "ProvideValue",
                BindingFlags.Instance | BindingFlags.Public));

        return Assert.IsAssignableFrom<IObservable<bool>>(
            provideValue.Invoke(
                extension,
                [CreateServiceProvider(target)]));
    }

    private static RecordingObserver<bool> Subscribe(
        object extension,
        StyledElement target,
        out IDisposable subscription)
    {
        var observer = new RecordingObserver<bool>();
        subscription = Observe(extension, target).Subscribe(observer);
        return observer;
    }

    private static AkburaMarkupServiceProvider CreateServiceProvider(
        StyledElement target)
    {
        return new AkburaMarkupServiceProvider(
            target,
            StyledElement.DataContextProperty,
            target,
            target,
            new Uri("avares://Akbura.UnitTests/"),
            [target]);
    }

    private static void AssertCurrent(
        RecordingObserver<bool> observer,
        bool expected)
    {
        Assert.NotEmpty(observer.Values);
        Assert.Equal(expected, observer.Values[^1]);
    }

    private static void AssertStructuralState(
        RecordingObserver<bool> firstObserver,
        RecordingObserver<bool> lastObserver,
        RecordingObserver<bool> onlyObserver,
        RecordingObserver<bool> oddObserver,
        RecordingObserver<bool> evenObserver,
        RecordingObserver<bool> firstOfTypeObserver,
        RecordingObserver<bool> lastOfTypeObserver,
        RecordingObserver<bool> onlyOfTypeObserver,
        bool first,
        bool last,
        bool only,
        bool odd,
        bool even,
        bool firstOfType,
        bool lastOfType,
        bool onlyOfType)
    {
        AssertCurrent(firstObserver, first);
        AssertCurrent(lastObserver, last);
        AssertCurrent(onlyObserver, only);
        AssertCurrent(oddObserver, odd);
        AssertCurrent(evenObserver, even);
        AssertCurrent(firstOfTypeObserver, firstOfType);
        AssertCurrent(lastOfTypeObserver, lastOfType);
        AssertCurrent(onlyOfTypeObserver, onlyOfType);
    }

    private static MarkupElementSyntax GetOnlyMarkupElement(
        AkburaSyntaxTree syntaxTree)
    {
        var root = syntaxTree.GetRoot();
        var markupRoot = Assert.IsType<MarkupRootSyntax>(
            root.Members.Single(member => member is MarkupRootSyntax));
        return markupRoot.Element;
    }

    private static CSharpCompilation CreateCSharpCompilation()
    {
        return CSharpCompilation.Create(
            assemblyName: "ExtendedBuiltInUtilityVariantTests",
            references: SymbolTests.CreateAvaloniaReferences(),
            syntaxTrees: []);
    }

    private sealed record VariantMetadataCase(
        Type ExtensionType,
        string Prefix,
        double Order,
        string? ConflictGroup);

    private sealed class RecordingObserver<T> : IObserver<T>
    {
        public List<T> Values { get; } = [];

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
            throw error;
        }

        public void OnNext(T value)
        {
            Values.Add(value);
        }
    }

    private sealed class PseudoClassButton : Button
    {
        public void SetPseudoClass(string pseudoClass, bool value)
        {
            PseudoClasses.Set(pseudoClass, value);
        }
    }
}
