using Akbura.ComponentTree;
using Akbura.Engine;
using Avalonia;
using Avalonia.Controls;
using System.Collections.Immutable;

namespace Akbura.UnitTests;

public sealed class InjectServiceTests
{
    [Fact]
    public void OnInitialized_InjectsRequiredServiceBeforeFirstUpdate()
    {
        var service = new TestService();
        var provider = new RecordingServiceProvider(service);
        var control = new TestComponent(
            CreateEngine(provider),
            useOptionalService: false,
            returnNewArray: false);

        control.InitializeForTest();

        Assert.Same(service, control.Service);
        Assert.True(control.ServiceWasAvailableDuringFirstUpdate);
        Assert.True(TestComponent.RequiredService.IsInjected(control));
        Assert.False(TestComponent.RequiredService.IsOptional);
        Assert.NotNull(provider.LastInjectionInfo);
        Assert.Same(control, provider.LastInjectionInfo.Value.TargetControl);
        Assert.Equal(false, provider.LastInjectionInfo.Value.IsOptional);
        Assert.Equal(nameof(TestComponent.Service), provider.LastInjectionInfo.Value.FieldName);
        Assert.Equal(typeof(ITestService), provider.LastInjectionInfo.Value.RequestedService);
        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public void OnInitialized_DoesNotOverwriteServiceSetBeforeInitialization()
    {
        var externalService = new TestService();
        var provider = new RecordingServiceProvider(new TestService());
        var control = new TestComponent(
            CreateEngine(provider),
            useOptionalService: false,
            returnNewArray: false);
        control.SetValue(
            TestComponent.RequiredService.AvaloniaProperty,
            externalService);

        control.InitializeForTest();

        Assert.Same(externalService, control.Service);
        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public void OnInitialized_AllowsMissingOptionalService()
    {
        var provider = new RecordingServiceProvider(service: null);
        var control = new TestComponent(
            CreateEngine(provider),
            useOptionalService: true,
            returnNewArray: false);

        control.InitializeForTest();

        Assert.Null(control.OptionalService);
        Assert.True(TestComponent.OptionalServiceInjection.IsOptional);
        Assert.NotNull(provider.LastInjectionInfo);
        Assert.Equal(true, provider.LastInjectionInfo.Value.IsOptional);
        Assert.Equal(nameof(TestComponent.OptionalService), provider.LastInjectionInfo.Value.FieldName);
        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public void OnInitialized_ThrowsWhenRequiredServiceIsMissing()
    {
        var provider = new RecordingServiceProvider(service: null);
        var control = new TestComponent(
            CreateEngine(provider),
            useOptionalService: false,
            returnNewArray: false);

        var exception = Assert.Throws<AkburaServiceNotFoundException>(
            control.InitializeForTest);

        Assert.Same(control, exception.AkburaControl);
        Assert.Same(TestComponent.RequiredService, exception.Service);
        Assert.Contains(typeof(ITestService).FullName!, exception.Message);
        Assert.Contains(nameof(TestComponent.Service), exception.Message);
    }

    [Fact]
    public void OnInitialized_ThrowsWhenGetServicesReturnsDifferentArray()
    {
        var provider = new RecordingServiceProvider(new TestService());
        var control = new TestComponent(
            CreateEngine(provider),
            useOptionalService: false,
            returnNewArray: true);

        var exception = Assert.Throws<AkburaServicesArrayChangedException>(
            control.InitializeForTest);

        Assert.Same(control, exception.AkburaControl);
        Assert.Contains("GetServices()", exception.Message);
        Assert.Contains("same immutable array instance", exception.Message);
        Assert.Equal(0, provider.CallCount);
    }

    private static AkburaEngine CreateEngine(IAkburaServiceProvider provider)
    {
        return new AkburaEngineExtensions.AkburaEngineBuilder()
            .WithServiceProvider(provider)
            .Build();
    }

    private interface ITestService
    {
    }

    private sealed class TestService : ITestService
    {
    }

    private sealed class RecordingServiceProvider : IAkburaServiceProvider
    {
        private readonly object? _service;

        public RecordingServiceProvider(object? service)
        {
            _service = service;
        }

        public int CallCount { get; private set; }

        public InjectionInfo? LastInjectionInfo { get; private set; }

        public object? GetService(ref readonly InjectionInfo injectionInfo)
        {
            CallCount++;
            LastInjectionInfo = injectionInfo;
            return injectionInfo.RequestedService == typeof(ITestService)
                ? _service
                : null;
        }
    }

    private sealed class TestComponent : AkburaControl
    {
        public static readonly InjectService<TestComponent, ITestService> RequiredService =
            InjectService.Create<TestComponent, ITestService>(
                nameof(Service),
                static control => control.Service,
                static (control, value) => control.Service = value);

        public static readonly InjectService<TestComponent, ITestService> OptionalServiceInjection =
            InjectService.Create<TestComponent, ITestService>(
                nameof(OptionalService),
                static control => control.OptionalService,
                static (control, value) => control.OptionalService = value,
                isOptional: true);

        private static readonly ImmutableArray<Parameter> s_parameters = [];
        private static readonly ImmutableArray<AvaloniaProperty<IAkburaCommand>> s_commands = [];
        private static readonly ImmutableArray<State> s_states = [];
        private static readonly ImmutableArray<InjectService> s_requiredServices =
            [RequiredService];
        private static readonly ImmutableArray<InjectService> s_optionalServices =
            [OptionalServiceInjection];

        private readonly ImmutableArray<InjectService> _services;
        private readonly bool _returnNewArray;
        private readonly bool _useOptionalService;
        private ITestService? _service;
        private ITestService? _optionalService;

        public TestComponent(
            AkburaEngine engine,
            bool useOptionalService,
            bool returnNewArray)
            : base(engine)
        {
            _services = useOptionalService
                ? s_optionalServices
                : s_requiredServices;
            _useOptionalService = useOptionalService;
            _returnNewArray = returnNewArray;
        }

        public ITestService? Service
        {
            get => _service;
            set => SetAndRaise(RequiredService.AvaloniaProperty, ref _service, value);
        }

        public ITestService? OptionalService
        {
            get => _optionalService;
            set => SetAndRaise(
                OptionalServiceInjection.AvaloniaProperty,
                ref _optionalService,
                value);
        }

        public bool ServiceWasAvailableDuringFirstUpdate
        {
            get; private set;
        }

        public void InitializeForTest()
        {
            base.OnInitialized();
        }

        protected override Control Update()
        {
            return new Border();
        }

        protected override Control FirstUpdate()
        {
            ServiceWasAvailableDuringFirstUpdate =
                (_useOptionalService ? OptionalService : Service) != null;
            return new Border();
        }

        protected override ImmutableArray<Parameter> GetParameters()
        {
            return s_parameters;
        }

        protected override ImmutableArray<AvaloniaProperty<IAkburaCommand>> GetCommands()
        {
            return s_commands;
        }

        protected override ImmutableArray<InjectService> GetServices()
        {
            return _returnNewArray
                ? ImmutableArray.Create(_services[0])
                : _services;
        }

        protected override ImmutableArray<State> GetStates()
        {
            return s_states;
        }
    }
}
