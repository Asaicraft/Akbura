using Akbura.ComponentTree;
using Akbura.Engine;
using Akbura.HotReload;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using System.Collections.Immutable;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class AkburaHotReloadRuntimeTests
{
    [Fact]
    public void RecreateForHotReload_ReusesStyledPropertyAndItsSingleClassHandler()
    {
        var original =
            Parameter.RecreateForHotReload<ParameterComponent, int>(
                property: null,
                nameof(ParameterComponent.Value),
                new Optional<int>(1),
                ParameterBinding.In,
                static (owner, _) => owner.ParameterChangedCount++);
        var property = original.AvaloniaProperty;
        var component = new ParameterComponent();

        component.SetValue(property, 10);

        var recreated =
            Parameter.RecreateForHotReload<ParameterComponent, int>(
                property,
                nameof(ParameterComponent.Value),
                new Optional<int>(42),
                ParameterBinding.Bind,
                static (owner, _) => owner.ParameterChangedCount++);

        Assert.Same(property, recreated.AvaloniaProperty);
        Assert.NotSame(original, recreated);
        Assert.Equal(ParameterBinding.Bind, recreated.Binding);
        Assert.True(recreated.HasDefaultValue);
        Assert.Equal(42, recreated.DefaultValue);
        Assert.Equal(42, property.GetDefaultValue(typeof(ParameterComponent)));
        Assert.Equal(
            BindingMode.TwoWay,
            property.GetMetadata(typeof(ParameterComponent)).DefaultBindingMode);
        Assert.Equal(10, component.GetValue(property));
        Assert.Equal(1, component.ParameterChangedCount);

        component.SetValue(property, 11);

        var recreatedAgain =
            Parameter.RecreateForHotReload<ParameterComponent, int>(
                property,
                nameof(ParameterComponent.Value),
                new Optional<int>(84),
                ParameterBinding.Out,
                static (owner, _) => owner.ParameterChangedCount++);

        component.SetValue(property, 12);

        Assert.Same(property, recreatedAgain.AvaloniaProperty);
        Assert.Equal(3, component.ParameterChangedCount);
        Assert.Equal(
            BindingMode.OneWayToSource,
            property.GetMetadata(typeof(ParameterComponent)).DefaultBindingMode);
    }

    [Fact]
    public void RecreateReadOnlyForHotReload_ReusesDirectProperty()
    {
        var original =
            Parameter.RecreateReadOnlyForHotReload<ParameterComponent, int>(
                property: null,
                nameof(ParameterComponent.ReadOnlyValue),
                static owner => owner.ReadOnlyValue);
        var recreated =
            Parameter.RecreateReadOnlyForHotReload<ParameterComponent, int>(
                original.AvaloniaProperty,
                nameof(ParameterComponent.ReadOnlyValue),
                static owner => owner.ReadOnlyValue);
        var component = new ParameterComponent
        {
            ReadOnlyValue = 17,
        };

        Assert.Same(original.AvaloniaProperty, recreated.AvaloniaProperty);
        Assert.NotSame(original, recreated);
        Assert.Equal(17, component.GetValue(recreated.AvaloniaProperty));
    }

    [Fact]
    public void RecreateForHotReload_PreservesAnExistingBinding()
    {
        var original =
            Parameter.RecreateForHotReload<ParameterComponent, int>(
                property: null,
                nameof(ParameterComponent.Value));
        var property = original.AvaloniaProperty;
        var component = new ParameterComponent();
        var source = new TestObservable();
        using var binding = component.Bind(
            property,
            source,
            BindingPriority.Style);

        source.Publish(17);

        var recreated =
            Parameter.RecreateForHotReload<ParameterComponent, int>(
                property,
                nameof(ParameterComponent.Value),
                new Optional<int>(42));

        source.Publish(29);

        Assert.Same(property, recreated.AvaloniaProperty);
        Assert.Equal(29, component.GetValue(property));
    }

    [Fact]
    public void InjectService_RecreateForHotReload_ReusesDirectProperty()
    {
        var original =
            InjectService.RecreateForHotReload<
                DescriptorComponent,
                IRefreshService>(
                    property: null,
                    nameof(DescriptorComponent.Service),
                    static owner => owner.Service,
                    static (owner, value) => owner.Service = value);
        var recreated =
            InjectService.RecreateForHotReload<
                DescriptorComponent,
                IRefreshService>(
                    original.AvaloniaProperty,
                    nameof(DescriptorComponent.Service),
                    static owner => owner.Service,
                    static (owner, value) => owner.Service = value,
                    isOptional: true);

        Assert.Same(original.AvaloniaProperty, recreated.AvaloniaProperty);
        Assert.NotSame(original, recreated);
        Assert.True(recreated.IsOptional);
    }

    [Fact]
    public void FindProperty_ReturnsOnlyTheExpectedManifestPropertyType()
    {
        var manifest =
            ImmutableArray.Create(
                new AkburaHotReloadPropertyRegistration(
                    "param:value",
                    LookupComponent.ValueProperty));

        var result =
            AkburaHotReloadRuntime.FindProperty<StyledProperty<int>>(
                manifest,
                "param:value");
        var missing =
            AkburaHotReloadRuntime.FindProperty<StyledProperty<int>>(
                manifest,
                "param:missing");

        Assert.Same(LookupComponent.ValueProperty, result);
        Assert.Null(missing);
        Assert.Throws<InvalidOperationException>(
            () => AkburaHotReloadRuntime.FindProperty<
                DirectProperty<LookupComponent, int>>(
                    manifest,
                    "param:value"));
    }

    [Fact]
    public void FindProperty_RejectsDuplicateManifestKeys()
    {
        var manifest =
            ImmutableArray.Create(
                new AkburaHotReloadPropertyRegistration(
                    "param:value",
                    LookupComponent.ValueProperty),
                new AkburaHotReloadPropertyRegistration(
                    "param:value",
                    LookupComponent.OtherValueProperty));

        var exception = Assert.Throws<InvalidOperationException>(
            () => AkburaHotReloadRuntime.FindProperty<StyledProperty<int>>(
                manifest,
                "param:value"));

        Assert.Contains("more than once", exception.Message);
    }

    [Fact]
    public void BeginPropertyUpdate_RemovesOnlyObsoleteGeneratedProperties()
    {
#pragma warning disable AVP1001 // Test owns and removes these registrations explicitly
        var generatedStyled =
            AvaloniaProperty.Register<RegistryComponent, int>(
                "GeneratedStyled");
        var generatedDirect =
            AvaloniaProperty.RegisterDirect<RegistryComponent, int>(
                "GeneratedDirect",
                static owner => owner.DirectValue,
                static (owner, value) => owner.DirectValue = value);
        var generatedAttached =
            AvaloniaProperty.RegisterAttached<
                RegistryComponent,
                RegistryComponent,
                int>("GeneratedAttached");
        var userProperty =
            AvaloniaProperty.Register<RegistryComponent, int>(
                "UserProperty");
        var sharedProperty =
            AvaloniaProperty.Register<RegistryComponent, int>(
                "GeneratedShared");

        sharedProperty.AddOwner<OtherRegistryComponent>();
#pragma warning restore AVP1001

        var registry = AvaloniaPropertyRegistry.Instance;
        var previous =
            ImmutableArray.Create(
                new AkburaHotReloadPropertyRegistration(
                    "param:styled",
                    generatedStyled),
                new AkburaHotReloadPropertyRegistration(
                    "param:direct",
                    generatedDirect),
                new AkburaHotReloadPropertyRegistration(
                    "param:attached",
                    generatedAttached),
                new AkburaHotReloadPropertyRegistration(
                    "param:shared",
                    sharedProperty));

        _ = registry.GetRegistered(typeof(RegistryComponent));
        _ = registry.GetRegisteredDirect(typeof(RegistryComponent));
        _ = registry.GetRegisteredAttached(typeof(RegistryComponent));
        _ = registry.GetRegisteredInherited(typeof(RegistryComponent));

        using var update =
            AkburaHotReloadRuntime.BeginPropertyUpdate(
                typeof(RegistryComponent),
                previous,
                ["param:styled"]);

        Assert.True(
            registry.IsRegistered(
                typeof(RegistryComponent),
                generatedStyled));
        Assert.DoesNotContain(
            generatedDirect,
            registry.GetRegisteredDirect(typeof(RegistryComponent)));
        Assert.DoesNotContain(
            generatedAttached,
            registry.GetRegisteredAttached(typeof(RegistryComponent)));
        Assert.True(
            registry.IsRegistered(
                typeof(RegistryComponent),
                userProperty));
        Assert.True(
            registry.IsRegistered(
                typeof(OtherRegistryComponent),
                sharedProperty));
        Assert.Same(
            sharedProperty,
            registry.FindRegistered(
                typeof(OtherRegistryComponent),
                "GeneratedShared"));
        Assert.Same(
            generatedStyled,
            registry.FindRegistered(
                typeof(RegistryComponent),
                "GeneratedStyled"));
        Assert.Null(
            registry.FindRegistered(
                typeof(RegistryComponent),
                "GeneratedDirect"));
    }

    [Fact]
    public void BeginPropertyUpdate_PreservesAvaloniaGlobalPropertyIdTable()
    {
#pragma warning disable AVP1001 // Test owns and removes this registration explicitly
        var obsoleteProperty =
            AvaloniaProperty.Register<RegistryComponent, int>(
                "AppendOnlyObsolete");
        var laterProperty =
            AvaloniaProperty.Register<RegistryComponent, int>(
                "AppendOnlyLater");
#pragma warning restore AVP1001

        var previous =
            ImmutableArray.Create(
                new AkburaHotReloadPropertyRegistration(
                    "param:obsolete",
                    obsoleteProperty));

        using var update =
            AkburaHotReloadRuntime.BeginPropertyUpdate(
                typeof(RegistryComponent),
                previous,
                []);

        Assert.False(
            AvaloniaPropertyRegistry.Instance.IsRegistered(
                typeof(RegistryComponent),
                obsoleteProperty));
        Assert.True(
            AvaloniaPropertyRegistry.Instance.IsRegistered(
                typeof(RegistryComponent),
                laterProperty));
        Assert.Same(
            obsoleteProperty,
            FindGloballyRegistered(obsoleteProperty));
        Assert.Same(
            laterProperty,
            FindGloballyRegistered(laterProperty));
    }

    [Fact]
    public void BeginPropertyUpdate_AllowsIncompatiblePropertyReplacement()
    {
#pragma warning disable AVP1001 // Test owns and replaces this registration explicitly
        var previousProperty =
            AvaloniaProperty.Register<ReplacementRegistryComponent, string>(
                "Value");
        var previous =
            ImmutableArray.Create(
                new AkburaHotReloadPropertyRegistration(
                    "param:Value:System.String",
                    previousProperty));

        using var update =
            AkburaHotReloadRuntime.BeginPropertyUpdate(
                typeof(ReplacementRegistryComponent),
                previous,
                ["param:Value:System.Int32"]);

        var currentProperty =
            AvaloniaProperty.Register<ReplacementRegistryComponent, int>(
                "Value");
#pragma warning restore AVP1001

        Assert.False(
            AvaloniaPropertyRegistry.Instance.IsRegistered(
                typeof(ReplacementRegistryComponent),
                previousProperty));
        Assert.True(
            AvaloniaPropertyRegistry.Instance.IsRegistered(
                typeof(ReplacementRegistryComponent),
                currentProperty));
        Assert.Same(
            currentProperty,
            AvaloniaPropertyRegistry.Instance.FindRegistered(
                typeof(ReplacementRegistryComponent),
                "Value"));
    }

    [Fact]
    public async Task Refresh_PreparesAttachedComponentsAndDetachedComponentsCatchUpOnAttach()
    {
        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var service = new RefreshService();
                var provider = new RefreshServiceProvider(service);
                var engine = new AkburaEngineExtensions.AkburaEngineBuilder()
                    .WithServiceProvider(provider)
                    .Build();
                var component = new RefreshComponent(engine);
                var derived = new DerivedRefreshComponent(engine);
                var other = new OtherRefreshComponent(engine);
                var detached = new RefreshComponent(engine);
                var window = new Window
                {
                    Content = new StackPanel
                    {
                        Children =
                        {
                            component,
                            derived,
                            other,
                        },
                    },
                };

                RefreshComponent.SetServices([]);

                try
                {
                    window.Show();

                    var componentUpdates = component.UpdateCount;
                    var derivedUpdates = derived.UpdateCount;
                    var otherUpdates = other.UpdateCount;
                    var detachedUpdates = detached.UpdateCount;

                    RefreshComponent.SetServices(
                        [RefreshComponent.ServiceDescriptor]);

                    AkburaHotReloadRuntime.Refresh<RefreshComponent>(
                        static current =>
                        {
                            current.PrepareCount++;
                            current.IsPrepared = true;
                        });

                    Assert.Equal(1, component.PrepareCount);
                    Assert.Equal(1, derived.PrepareCount);
                    Assert.Equal(0, detached.PrepareCount);
                    Assert.Same(service, component.Service);
                    Assert.Same(service, derived.Service);
                    Assert.Null(detached.Service);
                    Assert.Equal(componentUpdates + 1, component.UpdateCount);
                    Assert.Equal(derivedUpdates + 1, derived.UpdateCount);
                    Assert.Equal(otherUpdates, other.UpdateCount);
                    Assert.True(component.UpdateObservedPrepare);
                    Assert.True(derived.UpdateObservedPrepare);
                    Assert.True(component.UpdateObservedService);
                    Assert.True(derived.UpdateObservedService);
                    Assert.Equal(2, provider.CallCount);

                    var panel = Assert.IsType<StackPanel>(window.Content);
                    panel.Children.Add(detached);

                    Assert.Equal(1, detached.PrepareCount);
                    Assert.Same(service, detached.Service);
                    Assert.Equal(detachedUpdates + 1, detached.UpdateCount);
                    Assert.True(detached.UpdateObservedPrepare);
                    Assert.True(detached.UpdateObservedService);
                    Assert.Equal(3, provider.CallCount);

                    AkburaHotReloadRuntime.Refresh(
                        typeof(OtherRefreshComponent));

                    Assert.Equal(otherUpdates + 1, other.UpdateCount);
                }
                finally
                {
                    window.Close();
                    RefreshComponent.SetServices([]);
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Refresh_ContinuesAttachedComponentsAfterOneFailure()
    {
        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var failing = new IsolatedRefreshComponent(
                    AkburaEngine.Empty)
                {
                    FailureMessage = "First component failed.",
                };
                var succeeding = new IsolatedRefreshComponent(
                    AkburaEngine.Empty);
                var window = new Window
                {
                    Content = new StackPanel
                    {
                        Children =
                        {
                            failing,
                            succeeding,
                        },
                    },
                };

                try
                {
                    window.Show();
                    var succeedingUpdates = succeeding.UpdateCount;

                    var exception = Assert.Throws<InvalidOperationException>(
                        () => AkburaHotReloadRuntime.Refresh<
                            IsolatedRefreshComponent>(
                                PrepareIsolatedRefresh));

                    Assert.Equal(
                        "First component failed.",
                        exception.Message);
                    Assert.Equal(1, failing.PrepareCount);
                    Assert.Equal(1, succeeding.PrepareCount);
                    Assert.Equal(
                        succeedingUpdates + 1,
                        succeeding.UpdateCount);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Refresh_AggregatesFailuresAfterTryingEveryAttachedComponent()
    {
        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var first = new IsolatedRefreshComponent(
                    AkburaEngine.Empty)
                {
                    FailureMessage = "First component failed.",
                };
                var succeeding = new IsolatedRefreshComponent(
                    AkburaEngine.Empty);
                var last = new IsolatedRefreshComponent(
                    AkburaEngine.Empty)
                {
                    FailureMessage = "Last component failed.",
                };
                var window = new Window
                {
                    Content = new StackPanel
                    {
                        Children =
                        {
                            first,
                            succeeding,
                            last,
                        },
                    },
                };

                try
                {
                    window.Show();
                    var succeedingUpdates = succeeding.UpdateCount;

                    var exception = Assert.Throws<AggregateException>(
                        () => AkburaHotReloadRuntime.Refresh<
                            IsolatedRefreshComponent>(
                                PrepareIsolatedRefresh));

                    Assert.Equal(2, exception.InnerExceptions.Count);
                    Assert.Contains(
                        exception.InnerExceptions,
                        static current =>
                            current.Message == "First component failed.");
                    Assert.Contains(
                        exception.InnerExceptions,
                        static current =>
                            current.Message == "Last component failed.");
                    Assert.Equal(1, first.PrepareCount);
                    Assert.Equal(1, succeeding.PrepareCount);
                    Assert.Equal(1, last.PrepareCount);
                    Assert.Equal(
                        succeedingUpdates + 1,
                        succeeding.UpdateCount);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task QueuedRefresh_IsAcknowledgedOnlyAfterSuccessfulUpdate()
    {
        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var component = new AcknowledgedRefreshComponent(
                    AkburaEngine.Empty);
                var window = new Window
                {
                    Content = component,
                };

                try
                {
                    window.Show();
                    var updateAttempts = component.UpdateAttemptCount;
                    var suppression =
                        component.SuppressUpdatesForTest();

                    AkburaHotReloadRuntime.Refresh<
                        AcknowledgedRefreshComponent>(
                            static current =>
                                current.PrepareCount++);

                    Assert.Equal(1, component.PrepareCount);
                    Assert.Equal(
                        updateAttempts,
                        component.UpdateAttemptCount);

                    component.ThrowOnUpdate = true;

                    var exception = Assert.Throws<InvalidOperationException>(
                        suppression.Dispose);

                    Assert.Equal(
                        "Queued Hot Reload update failed.",
                        exception.Message);
                    Assert.Equal(
                        updateAttempts + 1,
                        component.UpdateAttemptCount);

                    component.ThrowOnUpdate = false;

                    Assert.True(
                        AkburaHotReloadRuntime.ApplyPendingRefreshes(
                            component));
                    Assert.Equal(1, component.PrepareCount);
                    Assert.Equal(
                        updateAttempts + 2,
                        component.UpdateAttemptCount);
                    Assert.False(
                        AkburaHotReloadRuntime.ApplyPendingRefreshes(
                            component));
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task ReentrantRefresh_IsNotAcknowledgedByCurrentUpdate()
    {
        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var component =
                    new ReentrantAcknowledgedRefreshComponent(
                        AkburaEngine.Empty);
                var window = new Window
                {
                    Content = component,
                };

                try
                {
                    window.Show();
                    component.QueueRefreshDuringUpdate = true;

                    var exception = Assert.Throws<InvalidOperationException>(
                        component.InvalidState);

                    Assert.Equal(
                        "Reentrant Hot Reload update failed.",
                        exception.Message);
                    Assert.Equal(1, component.HotReloadPrepareCount);

                    Assert.True(
                        AkburaHotReloadRuntime.ApplyPendingRefreshes(
                            component));
                    Assert.Equal(1, component.HotReloadPrepareCount);
                    Assert.False(
                        AkburaHotReloadRuntime.ApplyPendingRefreshes(
                            component));
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Refresh_CoalescesMissedRevisionsAndSkipsNewInstances()
    {
        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var existing = new HistoricalRefreshComponent(
                    AkburaEngine.Empty);

                AkburaHotReloadRuntime.Refresh<HistoricalRefreshComponent>(
                    static component => component.FirstPrepareCount++);
                AkburaHotReloadRuntime.Refresh<HistoricalRefreshComponent>(
                    static component => component.SecondPrepareCount++);

                var createdAfterRevisions =
                    new HistoricalRefreshComponent(AkburaEngine.Empty);
                var window = new Window
                {
                    Content = new StackPanel
                    {
                        Children =
                        {
                            existing,
                            createdAfterRevisions,
                        },
                    },
                };

                try
                {
                    window.Show();

                    Assert.Equal(0, existing.FirstPrepareCount);
                    Assert.Equal(1, existing.SecondPrepareCount);
                    Assert.Equal(0, createdAfterRevisions.FirstPrepareCount);
                    Assert.Equal(0, createdAfterRevisions.SecondPrepareCount);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task DetachedCatchUp_PreparesEveryMissedRevisionBeforeLatestUpdate()
    {
        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var component = new BatchedCatchUpDerivedRefreshComponent(
                    AkburaEngine.Empty);

                AkburaHotReloadRuntime.Refresh<
                    BatchedCatchUpRefreshComponent>(
                        static current =>
                            current.FirstPrepareCount++);
                AkburaHotReloadRuntime.Refresh<
                    BatchedCatchUpDerivedRefreshComponent>(
                        static current =>
                        {
                            current.SecondPrepareCount++;
                            current.ShapeReady = true;
                        });

                component.RequireShapeReady = true;
                var window = new Window
                {
                    Content = component,
                };

                try
                {
                    window.Show();

                    Assert.Equal(1, component.FirstPrepareCount);
                    Assert.Equal(1, component.SecondPrepareCount);
                    Assert.True(component.ShapeReady);
                    Assert.Equal(1, component.ValidatedUpdateCount);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Refresh_ReentrantPendingCheckDoesNotApplyRevisionTwice()
    {
        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var component = new ReentrantRefreshComponent(
                    AkburaEngine.Empty);
                var window = new Window
                {
                    Content = component,
                };

                try
                {
                    window.Show();
                    var updateCount = component.UpdateCount;

                    AkburaHotReloadRuntime.Refresh<ReentrantRefreshComponent>(
                        static current =>
                        {
                            current.PrepareCount++;
                            current.ReentrantApplyResult =
                                AkburaHotReloadRuntime.ApplyPendingRefreshes(
                                    current);
                        });

                    Assert.Equal(1, component.PrepareCount);
                    Assert.False(component.ReentrantApplyResult);
                    Assert.Equal(updateCount + 1, component.UpdateCount);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task ApplyPendingRefreshes_RetriesFailedRevisionBeforeQueuedNewerRevision()
    {
        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var component = new ThrowingRefreshComponent(
                    AkburaEngine.Empty)
                {
                    ThrowOnPrepare = true,
                };

                AkburaHotReloadRuntime.Refresh<ThrowingRefreshComponent>(
                    static current =>
                    {
                        current.OlderPrepareCount++;
                        if (!current.NestedRefreshRegistered)
                        {
                            current.NestedRefreshRegistered = true;
                            AkburaHotReloadRuntime.Refresh<
                                ThrowingRefreshComponent>(
                                    static nested =>
                                        nested.NewerPrepareCount++);
                        }

                        if (current.ThrowOnPrepare)
                        {
                            throw new InvalidOperationException(
                                "Preparation failed.");
                        }
                    });

                var exception = Assert.Throws<InvalidOperationException>(
                    () => AkburaHotReloadRuntime.ApplyPendingRefreshes(
                        component));

                Assert.Equal("Preparation failed.", exception.Message);
                Assert.Equal(1, component.OlderPrepareCount);
                Assert.Equal(0, component.NewerPrepareCount);

                component.ThrowOnPrepare = false;

                Assert.True(
                    AkburaHotReloadRuntime.ApplyPendingRefreshes(component));
                Assert.Equal(1, component.OlderPrepareCount);
                Assert.Equal(1, component.NewerPrepareCount);
                Assert.True(
                    AkburaHotReloadRuntime.ApplyPendingRefreshes(component));
                Assert.Equal(1, component.OlderPrepareCount);
                Assert.Equal(1, component.NewerPrepareCount);

                var window = new Window
                {
                    Content = component,
                };

                try
                {
                    window.Show();

                    Assert.False(
                        AkburaHotReloadRuntime.ApplyPendingRefreshes(
                            component));
                    Assert.Equal(1, component.OlderPrepareCount);
                    Assert.Equal(1, component.NewerPrepareCount);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task Refresh_DoesNotEnterAnExcludedTopLevel()
    {
        using var session = HeadlessUnitTestSession.StartNew(
            typeof(AvaloniaTestAppBuilder));
        await session.Dispatch(
            () =>
            {
                var component = new ExcludedRefreshComponent(
                    AkburaEngine.Empty);
                var window = new Window
                {
                    Content = component,
                };

                try
                {
                    window.Show();
                    var updateCount = component.UpdateCount;
                    AkburaComponentRegistry.ExcludeTopLevel(window);

                    AkburaHotReloadRuntime.Refresh<ExcludedRefreshComponent>(
                        static current => current.PrepareCount++);

                    Assert.Equal(0, component.PrepareCount);
                    Assert.Equal(updateCount, component.UpdateCount);
                }
                finally
                {
                    window.Close();
                }
            },
            CancellationToken.None);
    }

    private static AvaloniaProperty? FindGloballyRegistered(
        AvaloniaProperty property)
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic;

        var idProperty =
            typeof(AvaloniaProperty).GetProperty("Id", flags) ??
            throw new InvalidOperationException(
                "AvaloniaProperty.Id was not found.");
        var findRegistered =
            typeof(AvaloniaPropertyRegistry).GetMethod(
                "FindRegistered",
                flags,
                binder: null,
                types: [typeof(int)],
                modifiers: null) ??
            throw new InvalidOperationException(
                "AvaloniaPropertyRegistry.FindRegistered(int) was not found.");
        var propertyId =
            (int)(idProperty.GetValue(property) ??
                throw new InvalidOperationException(
                    "AvaloniaProperty.Id returned null."));

        return (AvaloniaProperty?)findRegistered.Invoke(
            AvaloniaPropertyRegistry.Instance,
            [propertyId]);
    }

    private static void PrepareIsolatedRefresh(
        IsolatedRefreshComponent component)
    {
        component.PrepareCount++;
        if (component.FailureMessage != null)
        {
            throw new InvalidOperationException(
                component.FailureMessage);
        }
    }

    private interface IRefreshService
    {
    }

    private sealed class RefreshService : IRefreshService
    {
    }

    private sealed class RefreshServiceProvider : IAkburaServiceProvider
    {
        private readonly IRefreshService _service;

        public RefreshServiceProvider(IRefreshService service)
        {
            _service = service;
        }

        public int CallCount { get; private set; }

        public object? GetService(ref readonly InjectionInfo injectionInfo)
        {
            CallCount++;
            return injectionInfo.RequestedService == typeof(IRefreshService)
                ? _service
                : null;
        }
    }

    private abstract class EmptyComponent : AkburaControl
    {
        private static readonly ImmutableArray<Parameter> s_parameters = [];
        private static readonly ImmutableArray<AvaloniaProperty<IAkburaCommand>>
            s_commands = [];
        private static readonly ImmutableArray<InjectService> s_services = [];
        private static readonly ImmutableArray<State> s_states = [];

        private readonly Border _root = new();

        protected EmptyComponent()
            : base(AkburaEngine.Empty)
        {
        }

        protected EmptyComponent(AkburaEngine engine)
            : base(engine)
        {
        }

        protected override Control FirstUpdate()
        {
            return _root;
        }

        protected override Control Update()
        {
            return _root;
        }

        protected override ImmutableArray<Parameter> GetParameters()
        {
            return s_parameters;
        }

        protected override ImmutableArray<AvaloniaProperty<IAkburaCommand>>
            GetCommands()
        {
            return s_commands;
        }

        protected override ImmutableArray<InjectService> GetServices()
        {
            return s_services;
        }

        protected override ImmutableArray<State> GetStates()
        {
            return s_states;
        }
    }

    private sealed class ParameterComponent : EmptyComponent
    {
        public int ParameterChangedCount { get; set; }

        public int Value { get; set; }

        public int ReadOnlyValue { get; set; }
    }

    private sealed class DescriptorComponent : EmptyComponent
    {
        public IRefreshService? Service { get; set; }
    }

    private sealed class RegistryComponent : EmptyComponent
    {
        public int DirectValue { get; set; }
    }

    private sealed class OtherRegistryComponent : EmptyComponent
    {
    }

    private sealed class ReplacementRegistryComponent : EmptyComponent
    {
    }

    private sealed class TestObservable : IObservable<object>
    {
        private IObserver<object>? _observer;

        public IDisposable Subscribe(IObserver<object> observer)
        {
            _observer = observer;
            return new Subscription(this, observer);
        }

        public void Publish(object value)
        {
            _observer?.OnNext(value);
        }

        private sealed class Subscription : IDisposable
        {
            private TestObservable? _owner;
            private readonly IObserver<object> _observer;

            public Subscription(
                TestObservable owner,
                IObserver<object> observer)
            {
                _owner = owner;
                _observer = observer;
            }

            public void Dispose()
            {
                if (_owner?._observer == _observer)
                {
                    _owner._observer = null;
                }

                _owner = null;
            }
        }
    }

    private sealed class LookupComponent : AvaloniaObject
    {
        public static readonly StyledProperty<int> ValueProperty =
            AvaloniaProperty.Register<LookupComponent, int>("Value");

        public static readonly StyledProperty<int> OtherValueProperty =
            AvaloniaProperty.Register<LookupComponent, int>("OtherValue");
    }

    private class RefreshComponent : EmptyComponent
    {
        private static ImmutableArray<InjectService> s_services = [];
        private IRefreshService? _service;

        public static readonly InjectService<
            RefreshComponent,
            IRefreshService> ServiceDescriptor =
                InjectService.Create<RefreshComponent, IRefreshService>(
                    nameof(Service),
                    static owner => owner._service,
                    static (owner, value) => owner._service = value);

        public RefreshComponent(AkburaEngine engine)
            : base(engine)
        {
        }

        public IRefreshService? Service => _service;

        public int PrepareCount { get; set; }

        public int UpdateCount { get; private set; }

        public bool IsPrepared { get; set; }

        public bool UpdateObservedPrepare { get; private set; }

        public bool UpdateObservedService { get; private set; }

        public static void SetServices(
            ImmutableArray<InjectService> services)
        {
            s_services = services;
        }

        protected override Control Update()
        {
            UpdateCount++;
            UpdateObservedPrepare = IsPrepared;
            UpdateObservedService = Service != null;
            return base.Update();
        }

        protected override ImmutableArray<InjectService> GetServices()
        {
            return s_services;
        }
    }

    private sealed class DerivedRefreshComponent : RefreshComponent
    {
        public DerivedRefreshComponent(AkburaEngine engine)
            : base(engine)
        {
        }
    }

    private sealed class HistoricalRefreshComponent : RefreshComponent
    {
        public HistoricalRefreshComponent(AkburaEngine engine)
            : base(engine)
        {
        }

        public int FirstPrepareCount { get; set; }

        public int SecondPrepareCount { get; set; }
    }

    private class BatchedCatchUpRefreshComponent : RefreshComponent
    {
        public BatchedCatchUpRefreshComponent(AkburaEngine engine)
            : base(engine)
        {
        }

        public bool RequireShapeReady { get; set; }

        public bool ShapeReady { get; set; }

        public int FirstPrepareCount { get; set; }

        public int SecondPrepareCount { get; set; }

        public int ValidatedUpdateCount { get; private set; }

        protected override Control Update()
        {
            if (RequireShapeReady && !ShapeReady)
            {
                throw new InvalidOperationException(
                    "The latest shape was not prepared.");
            }

            if (RequireShapeReady)
            {
                ValidatedUpdateCount++;
            }

            return base.Update();
        }
    }

    private sealed class BatchedCatchUpDerivedRefreshComponent
        : BatchedCatchUpRefreshComponent
    {
        public BatchedCatchUpDerivedRefreshComponent(AkburaEngine engine)
            : base(engine)
        {
        }
    }

    private sealed class IsolatedRefreshComponent : RefreshComponent
    {
        public IsolatedRefreshComponent(AkburaEngine engine)
            : base(engine)
        {
        }

        public string? FailureMessage { get; set; }
    }

    private sealed class AcknowledgedRefreshComponent : RefreshComponent
    {
        public AcknowledgedRefreshComponent(AkburaEngine engine)
            : base(engine)
        {
        }

        public bool ThrowOnUpdate { get; set; }

        public int UpdateAttemptCount { get; private set; }

        public IDisposable SuppressUpdatesForTest()
        {
            return SuppressUpdates();
        }

        protected override Control Update()
        {
            UpdateAttemptCount++;
            if (ThrowOnUpdate)
            {
                throw new InvalidOperationException(
                    "Queued Hot Reload update failed.");
            }

            return base.Update();
        }
    }

    private sealed class ReentrantRefreshComponent : RefreshComponent
    {
        public ReentrantRefreshComponent(AkburaEngine engine)
            : base(engine)
        {
        }

        public bool ReentrantApplyResult { get; set; }
    }

    private sealed class ReentrantAcknowledgedRefreshComponent
        : RefreshComponent
    {
        private bool _refreshQueued;
        private bool _throwOnNextUpdate;

        public ReentrantAcknowledgedRefreshComponent(
            AkburaEngine engine)
            : base(engine)
        {
        }

        public bool QueueRefreshDuringUpdate { get; set; }

        public int HotReloadPrepareCount { get; private set; }

        protected override Control Update()
        {
            if (_throwOnNextUpdate)
            {
                _throwOnNextUpdate = false;
                throw new InvalidOperationException(
                    "Reentrant Hot Reload update failed.");
            }

            if (QueueRefreshDuringUpdate && !_refreshQueued)
            {
                _refreshQueued = true;
                _throwOnNextUpdate = true;
                AkburaHotReloadRuntime.Refresh<
                    ReentrantAcknowledgedRefreshComponent>(
                        static current =>
                            current.HotReloadPrepareCount++);
            }

            return base.Update();
        }
    }

    private sealed class ThrowingRefreshComponent : RefreshComponent
    {
        public ThrowingRefreshComponent(AkburaEngine engine)
            : base(engine)
        {
        }

        public bool ThrowOnPrepare { get; set; }

        public bool NestedRefreshRegistered { get; set; }

        public int OlderPrepareCount { get; set; }

        public int NewerPrepareCount { get; set; }
    }

    private sealed class ExcludedRefreshComponent : RefreshComponent
    {
        public ExcludedRefreshComponent(AkburaEngine engine)
            : base(engine)
        {
        }
    }

    private sealed class OtherRefreshComponent : EmptyComponent
    {
        public OtherRefreshComponent(AkburaEngine engine)
            : base(engine)
        {
        }

        public int UpdateCount { get; private set; }

        protected override Control Update()
        {
            UpdateCount++;
            return base.Update();
        }
    }
}
