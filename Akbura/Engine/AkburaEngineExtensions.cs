using Avalonia;
using System.Runtime.InteropServices;

namespace Akbura.Engine;

public static class AkburaEngineExtensions
{
    extension(AppBuilder builder)
    {
        public AppBuilder UseAkbura() => UseAkbura(builder, _ => { });

        public AppBuilder UseAkbura(Action<AkburaEngineBuilder> withAkburaEngineBuilder)
        {
            return builder.AfterPlatformServicesSetup(platformBuilder =>
            {
                var akburaBuilder = new AkburaEngineBuilder();

                withAkburaEngineBuilder(akburaBuilder);

                AkburaEngine.Singletone = akburaBuilder.Build();
            });
        }
    }


    public sealed class AkburaEngineBuilder
    {
        private readonly List<IAkburaServiceProvider> _serviceProviders = [];

        public AkburaEngineBuilder WithServiceProvider(IServiceProvider serviceProvider)
        {
            _serviceProviders.Add(new AkburaServiceProviderAdapter(serviceProvider));

            return this;
        }

        public AkburaEngineBuilder WithServiceProvider(IAkburaServiceProvider serviceProvider)
        {
            _serviceProviders.Add(serviceProvider);

            return this;
        }

        public AkburaEngineBuilder WithServiceProviders<T>(T serviceProviders) where T: IEnumerable<IServiceProvider>
        {
            foreach (var serviceProvider in serviceProviders)
            {
                var adapter = serviceProvider is IAkburaServiceProvider akburaServiceProvider
                    ? akburaServiceProvider
                    : new AkburaServiceProviderAdapter(serviceProvider);

                _serviceProviders.Add(adapter);
            }

            return this;
        }

        public AkburaEngineBuilder WithServiceProviders(ReadOnlySpan<IServiceProvider> serviceProviders)
        {
            for (var i = 0; i < serviceProviders.Length; i++)
            {
                var serviceProvider = serviceProviders[i];

                var adapter = serviceProvider is IAkburaServiceProvider akburaServiceProvider
                    ? akburaServiceProvider
                    : new AkburaServiceProviderAdapter(serviceProvider);

                _serviceProviders.Add(adapter);
            }

            return this;
        }

        public AkburaEngineBuilder WithServiceProviders(ReadOnlySpan<IAkburaServiceProvider> serviceProviders)
        {
            for (var i = 0; i < serviceProviders.Length; i++)
            {
                var serviceProvider = serviceProviders[i];

                _serviceProviders.Add(serviceProvider);
            }

            return this;
        }

        public AkburaEngine Build()
        {
            var list = LinkedAkburaServiceProvider.Build(CollectionsMarshal.AsSpan(_serviceProviders));

            return new AkburaEngine(list);
        }
    }
}
