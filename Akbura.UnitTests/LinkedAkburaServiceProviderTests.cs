using Akbura.ComponentTree;
using Akbura.Engine;
using System.Collections.Immutable;
using Avalonia.Controls;

namespace Akbura.UnitTests;

public sealed class LinkedAkburaServiceProviderTests
{
    [Fact]
    public void GetService_PreservesContextAndProviderOrder()
    {
        var calls = new List<string>();
        var service = new TestService();
        var first = new RecordingProvider("first", calls, service: null, forward: true);
        var second = new RecordingProvider("second", calls, service, forward: false);
        var builder = new AkburaEngineExtensions.AkburaEngineBuilder()
            .WithServiceProvider(first)
            .WithServiceProvider(second);
        var engine = builder.Build();
        var control = new TestControl(engine);

        var result = engine.GetService(
            control,
            typeof(TestService),
            optional: false,
            fieldName: "service");

        Assert.Same(service, result);
        Assert.Equal(["first", "second"], calls);
        Assert.NotNull(first.LastInjectionInfo);
        Assert.NotNull(second.LastInjectionInfo);
        Assert.Same(control, first.LastInjectionInfo.Value.TargetControl);
        Assert.Same(control, second.LastInjectionInfo.Value.TargetControl);
        Assert.Equal(typeof(TestService), second.LastInjectionInfo.Value.RequestedService);
        Assert.Equal(false, second.LastInjectionInfo.Value.IsOptional);
        Assert.Equal("service", second.LastInjectionInfo.Value.FieldName);
        Assert.NotNull(first.LastInjectionInfo.Value.NextProvider);
        Assert.Null(second.LastInjectionInfo.Value.NextProvider);
    }

    [Fact]
    public void GetService_StopsWhenProviderDoesNotForward()
    {
        var calls = new List<string>();
        var service = new TestService();
        var first = new RecordingProvider("first", calls, service, forward: false);
        var second = new RecordingProvider("second", calls, new TestService(), forward: false);
        var engine = new AkburaEngineExtensions.AkburaEngineBuilder()
            .WithServiceProvider(first)
            .WithServiceProvider(second)
            .Build();

        var result = engine.GetService(new TestControl(engine), typeof(TestService));

        Assert.Same(service, result);
        Assert.Equal(["first"], calls);
        Assert.Equal(0, second.CallCount);
    }

    [Fact]
    public void GetService_StandardProvidersContinueAfterNull()
    {
        var service = new TestService();
        var engine = new AkburaEngineExtensions.AkburaEngineBuilder()
            .WithServiceProvider(new DictionaryServiceProvider())
            .WithServiceProvider(new DictionaryServiceProvider(service))
            .Build();

        var result = engine.GetService(new TestControl(engine), typeof(TestService));

        Assert.Same(service, result);
    }

    [Fact]
    public void Build_EmptyChainReturnsNull()
    {
        var engine = new AkburaEngineExtensions.AkburaEngineBuilder().Build();

        var result = engine.GetService(new TestControl(engine), typeof(TestService));

        Assert.Null(result);
    }

    [Fact]
    public void Build_CapturesCurrentProviders()
    {
        var calls = new List<string>();
        var first = new RecordingProvider("first", calls, service: null, forward: true);
        var service = new TestService();
        var builder = new AkburaEngineExtensions.AkburaEngineBuilder()
            .WithServiceProvider(first);
        var firstEngine = builder.Build();

        builder.WithServiceProvider(
            new RecordingProvider("second", calls, service, forward: false));
        var secondEngine = builder.Build();

        Assert.Null(firstEngine.GetService(new TestControl(firstEngine), typeof(TestService)));
        Assert.Same(
            service,
            secondEngine.GetService(new TestControl(secondEngine), typeof(TestService)));
    }

    [Fact]
    public void Build_RejectsNullProvider()
    {
        var builder = new AkburaEngineExtensions.AkburaEngineBuilder()
            .WithServiceProvider((IAkburaServiceProvider)null!);

        Assert.Throws<ArgumentException>(() => builder.Build());
    }

    private sealed class RecordingProvider : IAkburaServiceProvider
    {
        private readonly string _name;
        private readonly List<string> _calls;
        private readonly object? _service;
        private readonly bool _forward;

        public RecordingProvider(
            string name,
            List<string> calls,
            object? service,
            bool forward)
        {
            _name = name;
            _calls = calls;
            _service = service;
            _forward = forward;
        }

        public int CallCount { get; private set; }

        public InjectionInfo? LastInjectionInfo { get; private set; }

        public object? GetService(ref readonly InjectionInfo injectionInfo)
        {
            CallCount++;
            LastInjectionInfo = injectionInfo;
            _calls.Add(_name);

            if (_service != null || !_forward)
            {
                return _service;
            }

            var nextProvider = injectionInfo.NextProvider;
            return nextProvider == null
                ? null
                : nextProvider.GetService(in injectionInfo);
        }
    }

    private sealed class DictionaryServiceProvider : IServiceProvider
    {
        private readonly TestService? _service;

        public DictionaryServiceProvider(TestService? service = null)
        {
            _service = service;
        }

        public object? GetService(Type serviceType)
        {
            return serviceType == typeof(TestService)
                ? _service
                : null;
        }
    }

    private sealed class TestControl : AkburaControl
    {
        private static readonly ImmutableArray<Parameter> s_parameters = [];
        private static readonly ImmutableArray<Avalonia.AvaloniaProperty<IAkburaCommand>> s_commands = [];
        private static readonly ImmutableArray<InjectService> s_services = [];
        private static readonly ImmutableArray<State> s_states = [];

        public TestControl(AkburaEngine engine)
            : base(engine)
        {
        }

        protected override Control Update()
        {
            return new Border();
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

        protected override ImmutableArray<State> GetStates()
        {
            return s_states;
        }
    }

    private sealed class TestService
    {
    }
}
