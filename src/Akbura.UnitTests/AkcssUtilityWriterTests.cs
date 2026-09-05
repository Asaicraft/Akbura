using Akbura.Akcss;
using Akbura.CompilerAnotations;
using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.IO;
using System.Reflection;

namespace Akbura.UnitTests;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class AkcssUtilityWriterTests
{
    [Fact]
    public void UtilityOperations_UpdateResetAndApplyPropertiesIndependently()
    {
        const string source =
            """
            @using Avalonia.Controls;

            @utilities {
                Border.dimensions-(double value) {
                    Width: value;
                    Height: value * 2;
                    Grid.Row: 3;
                }
            }
            """;

        var utility = Assert.Single(CompileUtilities(source));
        var target = new Border();
        object[] arguments = [40d];

        Assert.Collection(
            utility.Operations,
            operation => Assert.Equal("property:Width", operation.ConflictKey),
            operation => Assert.Equal("property:Height", operation.ConflictKey),
            operation => Assert.Equal("property:Grid.Row", operation.ConflictKey));

        for (var i = 0; i < utility.Operations.Length; i++)
        {
            var operation = utility.Operations[i];

            Assert.Same(utility, operation.Utility);
            Assert.Equal(i, operation.Order);
            Assert.Equal(AkcssOperationPriority.Style, operation.Priority);
            Assert.True(operation.IsActive(target, arguments));
            Assert.False(operation.IsActive(new object(), arguments));
        }

        var width = utility.Operations[0];
        var height = utility.Operations[1];
        var row = utility.Operations[2];

        width.Update(target, arguments);
        Assert.Equal(40d, target.Width);
        Assert.True(double.IsNaN(target.Height));

        height.Update(target, arguments);
        width.Reset(target);
        Assert.True(double.IsNaN(target.Width));
        Assert.Equal(80d, target.Height);

        utility.Reset(target);
        Assert.True(double.IsNaN(target.Height));

        using var baseWidth = target.SetValue(Border.WidthProperty, 10d, BindingPriority.Style);
        var widthContribution = width.Apply(target, arguments, BindingPriority.StyleTrigger);
        var heightContribution = height.Apply(target, arguments, BindingPriority.Style);
        var rowContribution = row.Apply(target, arguments, BindingPriority.Style);

        Assert.Equal(40d, target.Width);
        Assert.Equal(80d, target.Height);
        Assert.Equal(3, Grid.GetRow(target));

        widthContribution.Dispose();
        Assert.Equal(10d, target.Width);
        Assert.Equal(80d, target.Height);

        rowContribution.Dispose();
        Assert.Equal(0, Grid.GetRow(target));

        heightContribution.Dispose();
        Assert.True(double.IsNaN(target.Height));
    }

    [Fact]
    public void UtilityOperations_PreserveNestedConditionsAcrossApplyExpansion()
    {
        const string source =
            """
            @using Avalonia.Controls;

            Border.inner {
                @if(IsVisible) {
                    Height: 80;
                }
            }

            @utilities {
                Border.conditional-(double value) {
                    Width: value;
                    @if(IsEnabled) {
                        @apply inner;
                        Grid.Row: 2;
                    }
                }
            }
            """;

        var utility = Assert.Single(CompileUtilities(source));
        var target = new Border();
        object[] arguments = [40d];

        Assert.True(utility.IsConditional);
        Assert.Equal(3, utility.Operations.Length);

        var width = utility.Operations[0];
        var height = utility.Operations[1];
        var row = utility.Operations[2];

        Assert.Equal("property:Width", width.ConflictKey);
        Assert.Equal("property:Height", height.ConflictKey);
        Assert.Equal("property:Grid.Row", row.ConflictKey);
        Assert.Equal(AkcssOperationPriority.Style, width.Priority);
        Assert.Equal(AkcssOperationPriority.StyleTrigger, height.Priority);
        Assert.Equal(AkcssOperationPriority.StyleTrigger, row.Priority);

        Assert.True(width.IsActive(target, arguments));
        Assert.True(height.IsActive(target, arguments));
        Assert.True(row.IsActive(target, arguments));
        Assert.False(height.IsActive(new object(), arguments));

        target.IsVisible = false;
        Assert.False(height.IsActive(target, arguments));
        Assert.True(row.IsActive(target, arguments));

        target.IsVisible = true;
        target.IsEnabled = false;
        Assert.True(width.IsActive(target, arguments));
        Assert.False(height.IsActive(target, arguments));
        Assert.False(row.IsActive(target, arguments));

        utility.Update(target, arguments);
        Assert.Equal(40d, target.Width);
        Assert.True(double.IsNaN(target.Height));
        Assert.Equal(0, Grid.GetRow(target));

        target.IsEnabled = true;
        utility.Update(target, arguments);
        Assert.Equal(80d, target.Height);
        Assert.Equal(2, Grid.GetRow(target));
    }

    [Fact]
    public async Task UtilityOperations_ApplyStaticAndDynamicResourcesWithArguments()
    {
        const string source =
            """
            @using Akbura;
            @using Avalonia.Controls;

            @utilities {
                Border.resource-(double value) {
                    Width: Amx.StaticResource<double>("static-size") * value;
                    Height: Amx.DynamicResource<double>("dynamic-size") * value;
                }
            }
            """;

        var utility = Assert.Single(CompileUtilities(source));

        using var session = HeadlessUnitTestSession.StartNew(typeof(AvaloniaTestAppBuilder));

        await session.Dispatch(
            () =>
            {
                var target = new Border();
                target.Resources["static-size"] = 3d;
                target.Resources["dynamic-size"] = 4d;

                object[] arguments = [10d];
                Assert.Equal(2, utility.Operations.Length);

                var width = utility.Operations[0];
                var height = utility.Operations[1];

                Assert.True(width.IsActive(target, arguments));
                Assert.True(height.IsActive(target, arguments));

                var widthContribution = width.Apply(target, arguments, BindingPriority.Style);
                var heightContribution = height.Apply(target, arguments, BindingPriority.Style);

                Assert.Equal(30d, target.Width);
                Assert.Equal(40d, target.Height);

                target.Resources["static-size"] = 6d;
                target.Resources["dynamic-size"] = 5d;

                Assert.Equal(30d, target.Width);
                Assert.Equal(50d, target.Height);

                widthContribution.Dispose();
                heightContribution.Dispose();

                Assert.True(double.IsNaN(target.Width));
                Assert.True(double.IsNaN(target.Height));

                target.Resources["dynamic-size"] = 7d;
                Assert.True(double.IsNaN(target.Height));
            },
            CancellationToken.None);
    }

    [Fact]
    public async Task UtilityWriter_MapsMoreThanSixteenArgumentsIncludingResourceKeys()
    {
        var parameters = string.Concat(
            Enumerable.Range(0, 16).Select(static index => "-(double p" + index + ")"));

        var source =
            "@using Akbura;\r\n" +
            "@using Avalonia.Controls;\r\n" +
            "@utilities {\r\n" +
            "    Border.many" + parameters + "-(string key) {\r\n" +
            "        Width: p0 + p15 + Amx.StaticResource<double>(key);\r\n" +
            "        Height: p0 * Amx.DynamicResource<double>(key);\r\n" +
            "    }\r\n" +
            "}\r\n";

        var utility = Assert.Single(CompileUtilities(source));
        var arguments = Enumerable.Range(1, 16)
            .Select(static value => (object)(double)value)
            .Append("size")
            .ToArray();

        Assert.Equal(17, utility.Parameters.Length);

        using var session = HeadlessUnitTestSession.StartNew(typeof(AvaloniaTestAppBuilder));

        await session.Dispatch(
            () =>
            {
                var target = new Border();
                target.Resources["size"] = 3d;

                utility.Update(target, arguments);
                Assert.Equal(20d, target.Width);
                Assert.Equal(3d, target.Height);

                utility.Reset(target);
                Assert.True(double.IsNaN(target.Width));
                Assert.True(double.IsNaN(target.Height));

                Assert.Equal(2, utility.Operations.Length);
                var width = utility.Operations[0];
                var height = utility.Operations[1];

                arguments[15] = 34d;
                width.Update(target, arguments);
                height.Update(target, arguments);
                Assert.Equal(38d, target.Width);
                Assert.Equal(3d, target.Height);

                height.Reset(target);
                var contribution = height.Apply(target, arguments, BindingPriority.Style);

                Assert.Equal(3d, target.Height);
                target.Resources["size"] = 5d;
                Assert.Equal(5d, target.Height);
                Assert.Equal(38d, target.Width);

                contribution.Dispose();
                Assert.True(double.IsNaN(target.Height));
            },
            CancellationToken.None);
    }

    [Fact]
    public void UtilityWriter_DistinguishesTargetFromParameterNamedTarget()
    {
        const string source =
            """
            @using Avalonia.Controls;

            @utilities {
                Border.reserved-(double __target) {
                    Width: __target;
                }
            }
            """;

        var utility = Assert.Single(CompileUtilities(source));
        var target = new Border();
        object[] arguments = [37d];

        utility.Update(target, arguments);
        Assert.Equal(37d, target.Width);

        var operation = Assert.Single(utility.Operations);
        arguments[0] = 72d;
        Assert.True(operation.IsActive(target, arguments));
        operation.Update(target, arguments);
        Assert.Equal(72d, target.Width);
    }

    private static AkcssUtility[] CompileUtilities(string source)
    {
        const string rootNamespace = "UtilityTests";
        var projectDirectory = Path.Combine(Path.GetTempPath(), "AkcssUtilityWriterTests");
        var sourcePath = Path.Combine(projectDirectory, "Styles.akcss");
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

        var syntaxTree = AkcssSyntaxTree.ParseText(
            SourceText.From(source),
            sourcePath,
            AkcssGeneratedModuleNames.GetMetadataName(rootNamespace, "Styles.akcss"));

        var compilation = CSharpCompilation.Create(
            "AkcssUtilityWriterTests_" + Guid.NewGuid().ToString("N"),
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var catalog = AkburaGenerationCatalogBuilder.Create(
            compilation,
            [syntaxTree],
            rootNamespace,
            projectDirectory);

        var input = Assert.Single(catalog.ExternalAkcssModules);
        var plan = AkcssModulePlanner.Create(input, rootNamespace);

        try
        {
            using var codeWriter = new CodeWriter("\r\n");

            var writer = new AkcssModuleWriter(codeWriter, catalog.AkcssSourceMap);

            codeWriter.WriteLine("// <auto-generated />");
            codeWriter.WriteLine("#nullable enable");
            codeWriter.WriteLine("public static class RuntimeUtilities");
            codeWriter.WriteLine("{");
            codeWriter.CurrentIndent += codeWriter.TabSize;

            Assert.True(writer.WriteUtilities(plan));

            codeWriter.WriteLine();
            codeWriter.WriteLine("public static global::Akbura.Akcss.AkcssUtility[] Create() =>");
            codeWriter.CurrentIndent += codeWriter.TabSize;
            codeWriter.WriteLine("[");
            codeWriter.CurrentIndent += codeWriter.TabSize;

            for (var i = 0; i < plan.Symbols.Length; i++)
            {
                ref readonly var symbol = ref plan.Symbols.ItemRef(i);

                if (symbol.Kind != AkcssSymbolGenerationKind.Utility || !symbol.EmitsRuntimeStyle)
                {
                    continue;
                }

                Assert.False(symbol.HasErrors);
                codeWriter.Write("new ");
                AkcssGeneratedNameWriter.WriteStyleTypeName(codeWriter, symbol.SymbolIndex);
                codeWriter.WriteLine("(),");
            }

            codeWriter.CurrentIndent -= codeWriter.TabSize;
            codeWriter.WriteLine("];");
            codeWriter.CurrentIndent -= codeWriter.TabSize * 2;
            codeWriter.WriteLine("}");

            var output = codeWriter.GetText().ToString();
            var generatedTree = CSharpSyntaxTree.ParseText(
                output,
                parseOptions,
                path: "AkcssUtilityWriter.g.cs");

            var generatedCompilation = catalog.Compilation.CSharpCompilation.AddSyntaxTrees(generatedTree);
            var diagnostics = generatedCompilation.GetDiagnostics()
                .Where(static diagnostic =>
                    diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
                .ToArray();

            Assert.True(
                diagnostics.Length == 0,
                string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => diagnostic.ToString())) +
                Environment.NewLine +
                output);

            using var assemblyStream = new MemoryStream();
            var result = generatedCompilation.Emit(assemblyStream);
            Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));

            var assembly = Assembly.Load(assemblyStream.ToArray());
            var factory = assembly.GetType("RuntimeUtilities")!.GetMethod("Create")!;

            return Assert.IsType<AkcssUtility[]>(factory.Invoke(null, null));
        }
        finally
        {
            plan.ReturnToPool();
        }
    }
}
