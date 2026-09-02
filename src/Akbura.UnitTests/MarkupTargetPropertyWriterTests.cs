using Akbura.Language.CodeGeneration;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Akbura.UnitTests;

public sealed class MarkupTargetPropertyWriterTests
{
    [Fact]
    public void Write_Expression_WritesExpressionWithoutIntermediateStringGeneration()
    {
        using var codeWriter = new CodeWriter();
        var writer = new MarkupTargetPropertyWriter(codeWriter);
        var plan = MarkupTargetPropertyPlan.CreateExpression("__targetProperty");

        writer.Write(plan);

        Assert.Equal("__targetProperty", codeWriter.GetText().ToString());
    }

    [Fact]
    public void Write_DefaultPlan_WritesNullTargetProperty()
    {
        using var codeWriter = new CodeWriter();
        var writer = new MarkupTargetPropertyWriter(codeWriter);

        writer.Write(default);

        Assert.Equal("null!", codeWriter.GetText().ToString());
    }

    [Fact]
    public void Write_StaticMember_WritesQualifiedMemberReference()
    {
        var fixture = CreateFixture();
        var targetType = Assert.IsAssignableFrom<INamedTypeSymbol>(
            fixture.CSharpCompilation.GetTypeByMetadataName("Demo.Target"));
        var member = Assert.Single(targetType.GetMembers("ValueProperty").OfType<IFieldSymbol>());
        var plan = MarkupTargetPropertyPlan.CreateStaticMember(member);
        using var codeWriter = new CodeWriter();
        var writer = new MarkupTargetPropertyWriter(codeWriter);

        writer.Write(plan);

        Assert.Equal("global::Demo.Target.ValueProperty", codeWriter.GetText().ToString());
    }

    [Fact]
    public void Write_ClrProperty_WritesPropertyInfoLookup()
    {
        var fixture = CreateFixture();
        var targetType = Assert.IsAssignableFrom<INamedTypeSymbol>(
            fixture.CSharpCompilation.GetTypeByMetadataName("Demo.Target"));
        var property = Assert.Single(targetType.GetMembers("Value").OfType<IPropertySymbol>());
        var plan = MarkupTargetPropertyPlan.CreateClrProperty(property);
        using var codeWriter = new CodeWriter();
        var writer = new MarkupTargetPropertyWriter(codeWriter);

        writer.Write(plan);

        Assert.Equal(
            "typeof(global::Demo.Target).GetProperty(\"Value\")!",
            codeWriter.GetText().ToString());
    }

    [Fact]
    public void Write_AttachedSetter_WritesMethodInfoLookup()
    {
        var fixture = CreateFixture();
        var attachedType = Assert.IsAssignableFrom<INamedTypeSymbol>(
            fixture.CSharpCompilation.GetTypeByMetadataName("Demo.Attached"));
        var setter = Assert.Single(attachedType.GetMembers("SetValue").OfType<IMethodSymbol>());
        var plan = MarkupTargetPropertyPlan.CreateAttachedSetter(setter);
        using var codeWriter = new CodeWriter();
        var writer = new MarkupTargetPropertyWriter(codeWriter);

        writer.Write(plan);

        Assert.Equal(
            "typeof(global::Demo.Attached).GetMethod(\"SetValue\")!",
            codeWriter.GetText().ToString());
    }

    [Fact]
    public void Write_GeneratedParameter_EscapesKeywordWithoutBuildingIdentifierString()
    {
        const string component =
            """
            using Avalonia.Controls;

            <Border />
            """;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(component);
        var targetType = fixture.CSharpCompilation.GetSpecialType(SpecialType.System_Object);
        var plan = MarkupTargetPropertyPlan.CreateGeneratedParameter(targetType, "class");
        using var codeWriter = new CodeWriter();
        var writer = new MarkupTargetPropertyWriter(codeWriter);

        writer.Write(plan);

        Assert.EndsWith(".@classProperty.AvaloniaProperty", codeWriter.GetText().ToString());
    }

    private static AkcssActivatorPlannerTests.PlannerFixture CreateFixture()
    {
        const string component =
            """
            using Avalonia.Controls;

            <Border />
            """;
        const string csharp =
            """
            namespace Demo;

            public sealed class Target
            {
                public static readonly object ValueProperty = new();

                public string Value { get; set; } = "";
            }

            public static class Attached
            {
                public static void SetValue(Target target, string value)
                {
                }
            }
            """;

        return AkcssActivatorPlannerTests.CreateFixture(component, csharp);
    }
}
