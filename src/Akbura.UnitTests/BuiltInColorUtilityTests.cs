using System.Collections.Immutable;
using System.Reflection;
using System.Xml.Linq;
using Akbura.Akcss;
using Akbura.CompilerAnotations;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class BuiltInColorUtilityTests
{
    private static readonly Lazy<Dictionary<(Type Target, string Name, int ParameterCount), Type>> s_catalog = new(CreateCatalog);

    [Theory]
    [InlineData(typeof(Panel), "bg", "Background")]
    [InlineData(typeof(Border), "bg", "Background")]
    [InlineData(typeof(TemplatedControl), "bg", "Background")]
    [InlineData(typeof(TextBlock), "text", "Foreground")]
    [InlineData(typeof(Border), "border", "BorderBrush")]
    public void NamedColors_ExportEveryPaletteKeyAsParameterlessUtility(Type targetType, string prefix, string property)
    {
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Akbura.slnx")))
        {
            directory = directory.Parent;
        }

        var root = Assert.IsType<DirectoryInfo>(directory).FullName;
        var palette = XDocument.Load(Path.Combine(root, "src", "Akbura", "Styles.axaml"));
        var keys = palette.Root!.Elements()
            .Select(element => (string?)element.Attribute(xaml + "Key"))
            .OfType<string>()
            .Where(key => key.StartsWith("--color-", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(keys);

        foreach (var key in keys)
        {
            var name = prefix + "-" + key["--color-".Length..];
            Assert.True(s_catalog.Value.TryGetValue((targetType, name, 0), out var carrier),
                $"Missing built-in utility: {targetType.Name}.{name}");
            var symbol = carrier!.GetCustomAttribute<AkcssSymbolAttribute>()!;
            Assert.False(symbol.HasErrors);
            Assert.True(symbol.RuntimeStyleIndex >= 0);
            var operation = Assert.Single(carrier.GetCustomAttributes<AkcssOperationAttribute>());
            Assert.Equal(AkcssOperationKind.Set, operation.Kind);
            Assert.Equal(property, operation.Property);
            Assert.Contains("DynamicResource", operation.Expression, StringComparison.Ordinal);
            Assert.Contains("\"" + key + "\"", operation.Expression, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(typeof(Panel), "bg", 1)]
    [InlineData(typeof(Panel), "bg", 2)]
    [InlineData(typeof(Border), "bg", 1)]
    [InlineData(typeof(Border), "bg", 2)]
    [InlineData(typeof(TemplatedControl), "bg", 1)]
    [InlineData(typeof(TemplatedControl), "bg", 2)]
    [InlineData(typeof(TextBlock), "text", 1)]
    [InlineData(typeof(TextBlock), "text", 2)]
    [InlineData(typeof(Border), "border", 2)]
    public void ParameterizedColors_RemainAvailableForCustomResources(Type targetType, string name, int parameterCount)
    {
        Assert.True(s_catalog.Value.TryGetValue((targetType, name, parameterCount), out var carrier));
        var parameters = carrier!.GetCustomAttributes<AkcssUtilityParameterAttribute>()
            .OrderBy(parameter => parameter.Ordinal).ToArray();
        Assert.Equal(typeof(string), parameters[0].Type);
        if (parameterCount == 2)
        {
            Assert.Equal(typeof(int), parameters[1].Type);
        }
    }

    [Theory]
    [InlineData(typeof(Panel), typeof(StackPanel), "bg", "Background")]
    [InlineData(typeof(Border), typeof(Border), "bg", "Background")]
    [InlineData(typeof(TemplatedControl), typeof(Button), "bg", "Background")]
    [InlineData(typeof(TextBlock), typeof(TextBlock), "text", "Foreground")]
    [InlineData(typeof(Border), typeof(Border), "border", "BorderBrush")]
    public async Task NamedAndParameterizedColors_ObserveResourceOverrides(Type targetType, Type controlType, string prefix, string property)
    {
        await AvaloniaHeadlessTestSession.GetSession().Dispatch(() =>
        {
            var control = Assert.IsAssignableFrom<Control>(Activator.CreateInstance(controlType));
            var initial = new SolidColorBrush(Colors.Teal);
            var replacement = new SolidColorBrush(Colors.Coral);
            var custom = new SolidColorBrush(Colors.Gold);
            control.Resources["--color-teal-300"] = initial;
            control.Resources["--color-brand-123"] = custom;
            var window = new Window { Content = control };
            try
            {
                window.Show();
                var getter = controlType.GetProperty(property)!;
                var named = GetRuntimeUtility(targetType, prefix + "-teal-300", 0);
                named.Update(control, []);
                Assert.Same(initial, getter.GetValue(control));

                control.Resources["--color-teal-300"] = replacement;
                Assert.Same(replacement, getter.GetValue(control));

                named.Reset(control);
                var parameterized = GetRuntimeUtility(targetType, prefix, 2);
                parameterized.Update(control, ["brand", 123]);
                Assert.Same(custom, getter.GetValue(control));

                control.Resources["--color-brand-123"] = replacement;
                Assert.Same(replacement, getter.GetValue(control));
                parameterized.Reset(control);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static Type GetModuleType()
    {
        var reference = Assert.Single(typeof(AkburaControl).Assembly.GetCustomAttributes<AkcssModuleReferenceAttribute>(),
            attribute => attribute.ModuleType.GetCustomAttribute<AkcssModuleAttribute>()?.MetadataName == "Akbura.Styles.akcss");
        return reference.ModuleType;
    }

    private static Dictionary<(Type Target, string Name, int ParameterCount), Type> CreateCatalog()
    {
        var catalog = new Dictionary<(Type Target, string Name, int ParameterCount), Type>();
        foreach (var type in GetModuleType().GetNestedTypes(BindingFlags.Public))
        {
            var symbol = type.GetCustomAttribute<AkcssSymbolAttribute>();
            if (symbol?.Kind != AkcssSymbolKind.Utility || symbol.TargetType == null)
            {
                continue;
            }

            var parameterCount = type.GetCustomAttributes<AkcssUtilityParameterAttribute>().Count();
            catalog.Add((symbol.TargetType, symbol.Name, parameterCount), type);
        }

        return catalog;
    }

    private static AkcssUtility GetRuntimeUtility(Type targetType, string name, int parameterCount)
    {
        var carrier = s_catalog.Value[(targetType, name, parameterCount)];
        var symbol = carrier.GetCustomAttribute<AkcssSymbolAttribute>()!;
        var styles = Assert.IsType<ImmutableArray<AkcssStyle>>(
            GetModuleType().GetField("Styles", BindingFlags.Public | BindingFlags.Static)!.GetValue(null));
        return Assert.IsAssignableFrom<AkcssUtility>(styles[symbol.RuntimeStyleIndex]);
    }
}
