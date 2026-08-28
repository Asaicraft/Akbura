[assembly: HostingStartup(typeof(AkburaDocs.AppHost))]

namespace AkburaDocs;

public class AppHost() : AppHostBase("AkburaDocs"), IHostingStartup
{
    public void Configure(IWebHostBuilder builder) => builder
        .ConfigureServices(services => {
            // Configure ASP.NET Core IOC Dependencies
        });

    public override void Configure()
    {
    }
}