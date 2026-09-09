using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akbura.UnitTests;

internal static class GeneratedCodeAssertions
{
    public static void AssertDoubleUnderscoreMethodsAreHidden(
        string source,
        bool isMemberFragment = false)
    {
        var syntaxSource = isMemberFragment
            ? "internal sealed class Generated\r\n{\r\n" + source + "\r\n}\r\n"
            : source;
        var syntaxTree = CSharpSyntaxTree.ParseText(
            syntaxSource,
            CSharpParseOptions.Default
                .WithLanguageVersion(LanguageVersion.Preview)
                .WithPreprocessorSymbols("DEBUG"));
        var methods = syntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(static method =>
                method.Identifier.ValueText.StartsWith(
                    "__",
                    StringComparison.Ordinal));

        foreach (var method in methods)
        {
            var attributes = method.AttributeLists
                .SelectMany(static list => list.Attributes)
                .ToArray();
            var methodName = method.Identifier.ValueText;

            Assert.True(
                attributes.Any(static attribute =>
                    IsAttribute(
                        attribute,
                        "EditorBrowsableAttribute",
                        "EditorBrowsableState.Never")),
                $"Generated method '{methodName}' is missing " +
                "EditorBrowsableAttribute(EditorBrowsableState.Never).\r\n" +
                method);
            Assert.True(
                attributes.Any(static attribute =>
                    IsAttribute(
                        attribute,
                        "BrowsableAttribute",
                        "false")),
                $"Generated method '{methodName}' is missing " +
                "BrowsableAttribute(false).\r\n" +
                method);
        }
    }

    private static bool IsAttribute(
        AttributeSyntax attribute,
        string name,
        string argumentSuffix)
    {
        return attribute.Name.ToString().EndsWith(
                name,
                StringComparison.Ordinal) &&
            attribute.ArgumentList is { Arguments.Count: 1 } arguments &&
            arguments.Arguments[0].Expression.ToString().EndsWith(
                argumentSuffix,
                StringComparison.Ordinal);
    }
}
