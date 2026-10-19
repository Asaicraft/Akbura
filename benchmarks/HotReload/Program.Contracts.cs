internal static partial class Program
{
    private static string RealMemberSource(string scenario, int revision)
    {
        if (scenario.StartsWith("semantic-", StringComparison.Ordinal))
        {
            return SemanticMemberSource(scenario, revision);
        }
        var source = """
            namespace ContractFixture;
            public sealed class UserViewModel
            {
                private string _name = "John";
                public int Age { get; set; }
                public string Name { get { return _name; } set { _name = value; } }
                public string GetName() { return _name + "0"; }
                public int GetAge() => 20;
                public bool IsActive() => true;
            }
            public sealed class UserService
            {
                private readonly string _prefix = "service";
                public string Format(int value) { return _prefix + value.ToString() + "0"; }
                public string GetStatus() => "ready";
            }
            public static class Current
            {
                public static UserViewModel Vm { get; } = new UserViewModel();
                public static UserService Service { get; } = new UserService();
            }
            """;
        if (revision == 0)
        {
            return source;
        }
        return scenario switch
        {
            "real-service" => source.Replace("value.ToString() + \"0\"", "value.ToString() + \"" + revision + "\"", StringComparison.Ordinal),
            "real-property" => source.Replace("return _name;", "return _name + \"" + revision + "\";", StringComparison.Ordinal),
            "real-method" => source.Replace("=> true", "=> " + (revision % 2 == 0 ? "true" : "false"), StringComparison.Ordinal),
            "real-signature" => revision % 2 == 0 ? source : source.Replace("string GetName()", "object GetName()", StringComparison.Ordinal),
            "real-overload" => revision % 2 == 0 ? source : source.Replace("public int GetAge()", "public string GetName(int value) => value.ToString(); public int GetAge()", StringComparison.Ordinal),
            "real-multiple" => source.Replace("_name + \"0\"", "_name + \"" + revision + "\"", StringComparison.Ordinal)
                .Replace("=> 20", "=> " + (20 + revision), StringComparison.Ordinal),
            _ => source.Replace("_name + \"0\"", "_name + \"" + revision + "\"", StringComparison.Ordinal),
        };
    }

    private static string RealMemberExpression(string scenario) => scenario switch
    {
        "semantic-property" => "global::SemanticFixture.ProfileService.Name.ToString()",
        "semantic-add-overload" or "semantic-remove-overload" => "global::SemanticFixture.ProfileService.Format(\"x\")",
        "semantic-negative" => "global::SemanticFixture.ProfileService.Missing()",
        _ when scenario.StartsWith("semantic-", StringComparison.Ordinal) => "global::SemanticFixture.ProfileService.GetName().ToString()",
        "real-service" => "global::ContractFixture.Current.Service.Format(1)",
        "real-property" => "global::ContractFixture.Current.Vm.Name",
        _ => "global::ContractFixture.Current.Vm.GetName().ToString()",
    };

    private static string SemanticMemberSource(string scenario, int revision)
    {
        var source = """
            namespace SemanticFixture;
            public static class ProfileService
            {
                public static string GetName() => "John";
                public static string Name { get => "John"; }
                public static string Format(object value) => "object";
            }
            public static class OrderService { public static string Read() => "orders"; }
            public static class SettingsService { public static string Read() => "settings"; }
            public static class UnrelatedService { public static string Read() => "unused"; }
            """;
        if (scenario == "semantic-remove-overload" && revision % 2 == 0 || scenario == "semantic-add-overload" && revision % 2 == 1)
        {
            source = source.Replace("public static string Name", "public static string Format(string value) => value; public static string Name", StringComparison.Ordinal);
        }
        if (revision % 2 == 0)
        {
            return source;
        }
        return scenario switch
        {
            "semantic-property" => source.Replace("string Name { get => \"John\"; }", "int Name { get => 42; }", StringComparison.Ordinal),
            "semantic-signature" or "semantic-multiple" or "semantic-mixed" => source.Replace("string GetName() => \"John\"", "int GetName() => 42", StringComparison.Ordinal),
            "semantic-negative" => source.Replace("public static string Name", "public static string Missing() => \"found\"; public static string Name", StringComparison.Ordinal),
            "semantic-unrelated" => source.Replace("string Read() => \"unused\"", "int Read() => 42", StringComparison.Ordinal),
            "semantic-global" => "global using System.Text;\n" + source,
            _ => source,
        };
    }

    private static Input[] SemanticFiles(Input[] files, string directory, string scenario)
    {
        // Preserve original project files. These controlled consumers make the
        // supported subset explicit rather than silently simplifying application UI.
        var result = files.Concat(new[]
        {
            new Input(Path.Combine(directory, "SemanticOrders.akbura"), "namespace SemanticFixture; using Avalonia.Controls; <TextBlock Text={global::SemanticFixture.OrderService.Read()} />"),
            new Input(Path.Combine(directory, "SemanticSettings.akbura"), "namespace SemanticFixture; using Avalonia.Controls; <TextBlock Text={global::SemanticFixture.SettingsService.Read()} />"),
        });
        if (scenario == "semantic-multiple")
        {
            result = result.Append(new Input(Path.Combine(directory, "SemanticProfile.akbura"),
                "namespace SemanticFixture; using Avalonia.Controls; <TextBlock Text={global::SemanticFixture.ProfileService.GetName().ToString()} />"));
        }
        return result.ToArray();
    }
}
