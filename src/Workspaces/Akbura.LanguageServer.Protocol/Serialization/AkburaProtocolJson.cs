using System.Text.Json;
using System.Text.Json.Serialization;

namespace Akbura.LanguageServer.Protocol.Serialization;

public static class AkburaProtocolJson
{
    public static JsonSerializerOptions CreateOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition =
                JsonIgnoreCondition.WhenWritingNull,
            NumberHandling =
                JsonNumberHandling.AllowReadingFromString,
        };
    }
}
