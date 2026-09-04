using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;

namespace Akbura.UnitTests;

public sealed class DescriptorArrayWriterTests
{
    [Fact]
    public void Write_UsesStaticDescriptorArraysAndStableGetterFields()
    {
        const string csharp =
            """
            namespace Demo;

            public interface IUserService
            {
            }
            """;
        var plan = CreatePlan(
            """
            using Demo;

            param string Title = "Default";
            inject IUserService userService;
            command void Refresh();
            state int count = 0;
            """,
            csharp);
        using var codeWriter = new CodeWriter("\r\n")
        {
            CurrentIndent = 4,
        };
        var writer = new DescriptorArrayWriter(codeWriter);

        writer.Write(plan);

        var output = codeWriter.GetText().ToString();
        Assert.Contains(
            "private static readonly global::System.Collections.Immutable.ImmutableArray<" +
            "global::Akbura.ComponentTree.Parameter> s_parameters =",
            output,
            StringComparison.Ordinal);
        Assert.Contains("TitleProperty,", output, StringComparison.Ordinal);
        Assert.Contains(
            "private static readonly global::System.Collections.Immutable.ImmutableArray<" +
            "global::Avalonia.AvaloniaProperty<global::Akbura.IAkburaCommand>> " +
            "s_commands =",
            output,
            StringComparison.Ordinal);
        Assert.Contains("RefreshProperty,", output, StringComparison.Ordinal);
        Assert.Contains(
            "private static readonly global::System.Collections.Immutable.ImmutableArray<" +
            "global::Akbura.ComponentTree.InjectService> s_services =",
            output,
            StringComparison.Ordinal);
        Assert.Contains("userServiceProperty,", output, StringComparison.Ordinal);

        AssertGetterReturns(output, "GetParameters", "s_parameters");
        AssertGetterReturns(output, "GetCommands", "s_commands");
        AssertGetterReturns(output, "GetServices", "s_services");
        Assert.DoesNotContain("GetParameters() =>\r\n        [", output, StringComparison.Ordinal);
        Assert.DoesNotContain("GetCommands() =>\r\n        [", output, StringComparison.Ordinal);
        Assert.DoesNotContain("GetServices() =>\r\n        [", output, StringComparison.Ordinal);
        Assert.Equal(4, codeWriter.CurrentIndent);
    }

    [Fact]
    public void Write_WithStatesUsesOneLazyInstanceArray()
    {
        var plan = CreatePlan(
            """
            state int first = 1;
            state string second = "two";
            """);
        using var codeWriter = new CodeWriter("\r\n")
        {
            CurrentIndent = 4,
        };
        var writer = new DescriptorArrayWriter(codeWriter);

        writer.Write(plan);

        var output = codeWriter.GetText().ToString();
        Assert.Contains(
            "private global::System.Collections.Immutable.ImmutableArray<" +
            "global::Akbura.ComponentTree.State> __states;",
            output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "private static readonly global::System.Collections.Immutable.ImmutableArray<" +
            "global::Akbura.ComponentTree.State> s_states",
            output,
            StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(output, "__states =\r\n"));
        Assert.Contains(
            "if (__states.IsDefault)\r\n" +
            "        {\r\n" +
            "            __states =\r\n" +
            "            [\r\n" +
            "                __State0,\r\n" +
            "                __State1,\r\n" +
            "            ];\r\n" +
            "        }",
            output,
            StringComparison.Ordinal);
        Assert.Contains("return __states;", output, StringComparison.Ordinal);
        AssertGetterReturns(output, "GetStates", "__GetStates()");
        Assert.Equal(1, CountOccurrences(output, "__GetStates();"));
        Assert.Equal(4, codeWriter.CurrentIndent);
    }

    [Fact]
    public void Write_WithoutStatesUsesStaticEmptyStateArray()
    {
        var plan = CreatePlan("param int Value;");
        using var codeWriter = new CodeWriter("\r\n");
        var writer = new DescriptorArrayWriter(codeWriter);

        writer.Write(plan);

        var output = codeWriter.GetText().ToString();
        Assert.Contains(
            "private static readonly global::System.Collections.Immutable.ImmutableArray<" +
            "global::Akbura.ComponentTree.State> s_states =\r\n" +
            "[\r\n" +
            "];",
            output,
            StringComparison.Ordinal);
        Assert.DoesNotContain("__states", output, StringComparison.Ordinal);
        Assert.DoesNotContain("__GetStates", output, StringComparison.Ordinal);
        AssertGetterReturns(output, "GetStates", "s_states");
        Assert.DoesNotContain("GetStates() =>\r\n    [", output, StringComparison.Ordinal);
    }

    private static ComponentMemberPlan CreatePlan(
        string component,
        string? additionalCSharp = null)
    {
        var fixture = AkcssActivatorPlannerTests.CreateFixture(
            component,
            additionalCSharp);
        var componentSymbol = Assert.IsAssignableFrom<IAkburaComponentSymbol>(
            fixture.SemanticModel.GetSymbolInfo(
                fixture.ComponentTree.GetRoot()).Symbol);

        return ComponentMemberPlanner.Create(
            componentSymbol,
            fixture.SemanticModel);
    }

    private static void AssertGetterReturns(
        string output,
        string methodName,
        string result)
    {
        var signature = methodName + "() =>";
        var start = output.IndexOf(signature, StringComparison.Ordinal);

        Assert.True(start >= 0, output);
        var end = output.IndexOf(';', start);
        Assert.True(end > start, output);
        Assert.Contains(result, output[start..end], StringComparison.Ordinal);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var start = 0;

        while ((start = text.IndexOf(value, start, StringComparison.Ordinal)) >= 0)
        {
            count++;
            start += value.Length;
        }

        return count;
    }
}
