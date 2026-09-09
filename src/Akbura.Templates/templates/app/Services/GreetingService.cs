namespace AkburaTemplateNamespace.Services;

public sealed class GreetingService : IGreetingService
{
    public string GetMessage() =>
        "Dependency injection is connected. This message comes from IGreetingService.";
}
