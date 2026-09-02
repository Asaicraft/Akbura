using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akbura.UnitTests;

public sealed class PropertySubscriptionWriterTests
{
    [Fact]
    public void AvaloniaProperty_UsesNamedHandlerAndWritesMappedCast()
    {
        var fixture = CreateAvaloniaFixture(isLocal: false);

        var output = Write(fixture, writeHandler: true, out var finalIndent);

        Assert.Contains("private void __OnPropertyBindingChanged0(", output, StringComparison.Ordinal);
        Assert.Contains(
            "global::Avalonia.AvaloniaPropertyChangedEventArgs __change0)",
            output,
            StringComparison.Ordinal);
        Assert.Contains("__change0.Property != global::Avalonia.Controls.TextBox.TextProperty", output);
        Assert.Contains("result = (", output, StringComparison.Ordinal);
        Assert.Contains(")__change0.NewValue!;", output, StringComparison.Ordinal);
        Assert.Contains(
            "((global::Avalonia.AvaloniaObject)input).PropertyChanged += __OnPropertyBindingChanged0;",
            output,
            StringComparison.Ordinal);
        AssertSourceMapping(output);
        Assert.Equal(6, finalIndent);
    }

    [Fact]
    public void GeneratedParameter_StreamsDescriptorAndEscapesKeyword()
    {
        var fixture = CreateAvaloniaFixture(isLocal: false);
        var source = fixture.Subscription;
        var subscription = new ComponentPropertySubscriptionPlan(
            id: 37,
            source.ElementId,
            source.SourceOrder,
            source.Kind,
            PropertyObservationPlan.CreateGeneratedParameter(fixture.Element.Type, "class"),
            source.TargetOperation,
            source.ValueType,
            source.Syntax);

        var output = Write(fixture, subscription, writeHandler: true, out _);

        Assert.Contains("__OnPropertyBindingChanged37", output, StringComparison.Ordinal);
        Assert.Contains(
            "global::Avalonia.Controls.TextBox.@classProperty.AvaloniaProperty",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TargetOperation_PreservesEscapedIdentifier()
    {
        var fixture = CreateAvaloniaFixture(isLocal: false);
        var source = fixture.Subscription;
        var subscription = new ComponentPropertySubscriptionPlan(
            source.Id,
            source.ElementId,
            source.SourceOrder,
            source.Kind,
            source.Observation,
            CreateEscapedTargetOperation(fixture.CSharpCompilation),
            source.ValueType,
            source.Syntax);

        var output = Write(fixture, subscription, writeHandler: true, out _);

        Assert.Contains("@class = (", output, StringComparison.Ordinal);
    }

    [Fact]
    public void NotifyPropertyChanged_WritesRuntimeCheckEmptyNameFilterAndClrRead()
    {
        var fixture = CreateNotifyFixture(isLocal: false);

        var output = Write(fixture, writeHandler: true, out var finalIndent);

        Assert.Equal(PropertyObservationKind.NotifyPropertyChanged, fixture.Subscription.Observation.Kind);
        Assert.Contains(
            "if (source is global::System.ComponentModel.INotifyPropertyChanged __notifier0)",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "__notifier0.PropertyChanged += __OnPropertyBindingChanged0;",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "!global::System.String.IsNullOrEmpty(__event0.PropertyName) &&",
            output,
            StringComparison.Ordinal);
        Assert.Contains("__event0.PropertyName != \"Value\"", output, StringComparison.Ordinal);
        Assert.Contains(
            "result = ((global::Demo.NotifyControl)__sender!).Value;",
            output,
            StringComparison.Ordinal);
        AssertSourceMapping(output);
        Assert.Equal(6, finalIndent);
    }

    [Fact]
    public void LocalAvaloniaProperty_UsesInlineLambdaWithoutClassHandler()
    {
        var fixture = CreateAvaloniaFixture(isLocal: true);

        var output = Write(fixture, writeHandler: false, out var finalIndent);

        Assert.True(fixture.Element.IsLocal);
        Assert.Contains(
            "((global::Avalonia.AvaloniaObject)" + fixture.Element.Identifier +
                ").PropertyChanged += (_, __change0) =>",
            output,
            StringComparison.Ordinal);
        Assert.Contains("result = (", output, StringComparison.Ordinal);
        Assert.Contains(")__change0.NewValue!;", output, StringComparison.Ordinal);
        Assert.DoesNotContain("__OnPropertyBindingChanged", output, StringComparison.Ordinal);
        AssertSourceMapping(output);
        Assert.Equal(6, finalIndent);
    }

    [Fact]
    public void LocalNotifyPropertyChanged_UsesInlineLambdaAndStreamedNotifierName()
    {
        var fixture = CreateNotifyFixture(isLocal: true);

        var output = Write(fixture, writeHandler: false, out _);

        Assert.True(fixture.Element.IsLocal);
        Assert.Contains("__notifier0.PropertyChanged += (__sender, __event0) =>", output);
        Assert.Contains("result = ((global::Demo.NotifyControl)__sender!).Value;", output);
        Assert.DoesNotContain("__OnPropertyBindingChanged", output, StringComparison.Ordinal);
    }

    [Fact]
    public void AvaloniaProperty_GeneratedHandlerAndRegistrationCompile()
    {
        var fixture = CreateAvaloniaFixture(isLocal: false);

        AssertGeneratedCodeCompiles(fixture);
    }

    [Fact]
    public void GeneratedParameter_GeneratedHandlerAndRegistrationCompile()
    {
        var fixture = CreateGeneratedParameterFixture();

        Assert.Equal(PropertyObservationKind.GeneratedParameter, fixture.Subscription.Observation.Kind);
        AssertGeneratedCodeCompiles(fixture);
    }

    [Fact]
    public void NotifyPropertyChanged_GeneratedHandlerAndRegistrationCompile()
    {
        var fixture = CreateNotifyFixture(isLocal: false);

        AssertGeneratedCodeCompiles(fixture);
    }

    [Fact]
    public void LocalAvaloniaProperty_InlineRegistrationCompiles()
    {
        var fixture = CreateAvaloniaFixture(isLocal: true);

        AssertGeneratedCodeCompiles(fixture);
    }

    [Fact]
    public void LocalNotifyPropertyChanged_InlineRegistrationCompiles()
    {
        var fixture = CreateNotifyFixture(isLocal: true);

        AssertGeneratedCodeCompiles(fixture);
    }

    private static WriterFixture CreateAvaloniaFixture(bool isLocal)
    {
        var body = isLocal
            ? """
              <ItemsControl>
                  <ItemsControl.ItemTemplate>
                      <DataTemplate>
                          <TextBox x.Name="input" out:Text={result} />
                      </DataTemplate>
                  </ItemsControl.ItemTemplate>
              </ItemsControl>
              """
            : """<TextBox x.Name="input" out:Text={result} />""";
        var component =
            "using Avalonia.Controls;\n" +
            "using Avalonia.Markup.Xaml.Templates;\n\n" +
            "state string result = \"\";\n" +
            "\n" +
            body;

        return CreateFixture(component);
    }

    private static WriterFixture CreateNotifyFixture(bool isLocal)
    {
        const string csharp =
            """
            namespace Demo;

            public sealed class NotifyControl : Avalonia.Controls.Control,
                System.ComponentModel.INotifyPropertyChanged
            {
                public string Value { get; set; } = "";

                public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
            }
            """;
        var body = isLocal
            ? """
              <ItemsControl>
                  <ItemsControl.ItemTemplate>
                      <DataTemplate>
                          <NotifyControl x.Name="source" out:Value={result} />
                      </DataTemplate>
                  </ItemsControl.ItemTemplate>
              </ItemsControl>
              """
            : """<NotifyControl x.Name="source" out:Value={result} />""";
        var component =
            "using Avalonia.Controls;\n" +
            "using Avalonia.Markup.Xaml.Templates;\n" +
            "using Demo;\n\n" +
            "state string result = \"\";\n\n" +
            body;

        return CreateFixture(component, csharp);
    }

    private static WriterFixture CreateGeneratedParameterFixture()
    {
        const string component =
            "state string result = \"\";\n\n" +
            "<Child x.Name=\"input\" bind:Value={result} />";
        const string childComponent =
            "param bind string Value = \"\";";
        const string generatedChild =
            """
            namespace Demo;

            public sealed class Child : global::Akbura.AkburaControl
            {
                public static readonly global::Akbura.ComponentTree.Parameter<Child, string> ValueProperty =
                    global::Akbura.ComponentTree.Parameter.Create<Child, string>(
                        nameof(Value),
                        parameterBinding: global::Akbura.ComponentTree.ParameterBinding.Bind);

                public string Value
                {
                    get => GetValue(ValueProperty.AvaloniaProperty);
                    set => SetValue(ValueProperty.AvaloniaProperty, value);
                }

                protected override global::Avalonia.Controls.Control Update() => new();

                protected override global::Avalonia.Controls.Control FirstUpdate() => new();

                protected override global::System.Collections.Immutable.ImmutableArray<
                    global::Akbura.ComponentTree.Parameter> GetParameters() => [ValueProperty];

                protected override global::System.Collections.Immutable.ImmutableArray<
                    global::Avalonia.AvaloniaProperty<global::Akbura.IAkburaCommand>> GetCommands() => [];

                protected override global::System.Collections.Immutable.ImmutableArray<
                    global::Akbura.ComponentTree.InjectService> GetServices() => [];

                protected override global::System.Collections.Immutable.ImmutableArray<
                    global::Akbura.ComponentTree.State> GetStates() => [];
            }
            """;
        var baseFixture = AkcssActivatorPlannerTests.CreateFixture(component);
        var childTree = AkburaSyntaxTree.ParseText(childComponent, "Child.akbura");
        var compilation = new AkburaCompilation(
            baseFixture.CSharpCompilation,
            [baseFixture.ComponentTree, childTree],
            rootNamespace: "Demo");
        var semanticModel = compilation.GetSemanticModel(baseFixture.ComponentTree);
        var componentSymbol = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            semanticModel.GetSymbolInfo(baseFixture.ComponentTree.GetRoot()).Symbol);
        var plan = ComponentPlanner.Create(
            componentSymbol,
            semanticModel,
            new Dictionary<AkburaSyntax, string>());
        var subscription = Assert.Single(plan.PropertySubscriptions);
        var element = Assert.Single(plan.Elements);
        var generatedChildTree = CSharpSyntaxTree.ParseText(
            generatedChild,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

        return new WriterFixture(
            baseFixture.CSharpCompilation.AddSyntaxTrees(generatedChildTree),
            Assert.IsType<ComponentSyntaxTree>(baseFixture.ComponentTree),
            element,
            subscription);
    }

    private static CSharpOperationDefinition CreateEscapedTargetOperation(
        CSharpCompilation compilation)
    {
        const string source =
            """
            namespace Demo;

            internal sealed class EscapedTarget
            {
                private string @class = "";

                private void Assign()
                {
                    @class = "";
                }
            }
            """;
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        var targetCompilation = compilation.AddSyntaxTrees(syntaxTree);
        var semanticModel = targetCompilation.GetSemanticModel(syntaxTree);
        var assignment = Assert.Single(
            syntaxTree.GetRoot().DescendantNodes().OfType<AssignmentExpressionSyntax>());
        var operation = semanticModel.GetOperation(assignment.Left);

        Assert.NotNull(operation);
        return new CSharpOperationDefinition(operation!);
    }

    private static WriterFixture CreateFixture(
        string componentSource,
        string? additionalCSharp = null)
    {
        var semanticFixture = AkcssActivatorPlannerTests.CreateFixture(componentSource, additionalCSharp);
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            semanticFixture.SemanticModel.GetSymbolInfo(semanticFixture.ComponentTree.GetRoot()).Symbol);
        var plan = ComponentPlanner.Create(
            component,
            semanticFixture.SemanticModel,
            new Dictionary<AkburaSyntax, string>());
        var subscription = Assert.Single(plan.PropertySubscriptions);
        var element = plan.Elements[subscription.ElementId];

        return new WriterFixture(
            semanticFixture.CSharpCompilation,
            Assert.IsType<ComponentSyntaxTree>(semanticFixture.ComponentTree),
            element,
            subscription);
    }

    private static string Write(
        WriterFixture fixture,
        bool writeHandler,
        out int finalIndent)
    {
        return Write(fixture, fixture.Subscription, writeHandler, out finalIndent);
    }

    private static string Write(
        WriterFixture fixture,
        ComponentPropertySubscriptionPlan subscription,
        bool writeHandler,
        out int finalIndent)
    {
        using var codeWriter = new CodeWriter("\n")
        {
            CurrentIndent = 6,
        };
        var writer = new PropertySubscriptionWriter(
            codeWriter,
            new ComponentGenerationSourceMap(fixture.SyntaxTree));

        if (writeHandler)
        {
            writer.WriteHandler(subscription);
        }

        writer.WriteRegistration(fixture.Element, subscription);
        finalIndent = codeWriter.CurrentIndent;
        return codeWriter.GetText().ToString();
    }

    private static void AssertGeneratedCodeCompiles(WriterFixture fixture)
    {
        using var codeWriter = new CodeWriter("\n");
        codeWriter.WriteLine("#nullable enable");
        codeWriter.WriteLine("namespace Generated;");
        codeWriter.WriteLine("public sealed class GeneratedSubscriptionComponent");

        using (codeWriter.BuildScope())
        {
            var valueWriter = new CSharpValueWriter(codeWriter);
            if (!fixture.Element.IsLocal)
            {
                codeWriter.Write("private readonly ");
                valueWriter.WriteTypeName(fixture.Element.Type);
                codeWriter.Write(" ");
                valueWriter.WriteIdentifier(fixture.Element.Identifier);
                codeWriter.WriteLine(" = new();");
            }

            codeWriter.WriteLine("private string result = \"\";");
            codeWriter.WriteLine();
            var writer = new PropertySubscriptionWriter(
                codeWriter,
                new ComponentGenerationSourceMap(fixture.SyntaxTree));
            if (!fixture.Element.IsLocal)
            {
                writer.WriteHandler(fixture.Subscription);
                codeWriter.WriteLine();
            }

            codeWriter.WriteLine("public void Register()");
            using (codeWriter.BuildScope())
            {
                if (fixture.Element.IsLocal)
                {
                    codeWriter.Write("var ");
                    valueWriter.WriteIdentifier(fixture.Element.Identifier);
                    codeWriter.Write(" = new ");
                    valueWriter.WriteTypeName(fixture.Element.Type);
                    codeWriter.WriteLine("();");
                }

                writer.WriteRegistration(fixture.Element, fixture.Subscription);
            }
        }

        var source = codeWriter.GetText().ToString();
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        var errors = fixture.CSharpCompilation
            .AddSyntaxTrees(syntaxTree)
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            errors.Length == 0,
            string.Join(Environment.NewLine, errors.Select(static error => error.ToString())) +
            Environment.NewLine + source);
    }

    private static void AssertSourceMapping(string output)
    {
        Assert.Contains("#line (", output, StringComparison.Ordinal);
        Assert.Contains("\"PlannerView.akbura\"", output, StringComparison.Ordinal);
        Assert.Contains("#line default", output, StringComparison.Ordinal);
        Assert.Contains("#line hidden", output, StringComparison.Ordinal);
    }

    private sealed class WriterFixture(
        CSharpCompilation csharpCompilation,
        ComponentSyntaxTree syntaxTree,
        ComponentElementPlan element,
        ComponentPropertySubscriptionPlan subscription)
    {
        public CSharpCompilation CSharpCompilation { get; } = csharpCompilation;

        public ComponentSyntaxTree SyntaxTree { get; } = syntaxTree;

        public ComponentElementPlan Element { get; } = element;

        public ComponentPropertySubscriptionPlan Subscription { get; } = subscription;
    }
}
