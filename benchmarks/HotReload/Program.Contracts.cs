internal static partial class Program
{
    private static string RealMemberSource(string scenario, int revision)
    {
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
        "real-service" => "global::ContractFixture.Current.Service.Format(1)",
        "real-property" => "global::ContractFixture.Current.Vm.Name",
        _ => "global::ContractFixture.Current.Vm.GetName().ToString()",
    };
}
