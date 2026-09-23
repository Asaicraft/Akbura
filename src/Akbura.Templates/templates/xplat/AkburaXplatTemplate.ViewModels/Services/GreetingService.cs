namespace AkburaTemplateNamespace.Services;

public sealed class GreetingService : IGreetingService
{
    public string CreateGreeting(string? userName)
    {
        var name = userName?.Trim();
        return string.IsNullOrWhiteSpace(name)
            ? "Hello, friend!"
            : $"Hello, {name}!";
    }
}
