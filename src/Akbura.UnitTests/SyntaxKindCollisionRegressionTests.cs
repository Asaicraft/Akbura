using System;
using System.Linq;
using Akbura.Language.Syntax;
using Xunit;
using AkburaSyntaxKind = Akbura.Language.Syntax.SyntaxKind;

namespace Akbura.UnitTests;

public sealed class SyntaxKindCollisionRegressionTests
{
    [Fact]
    public void SyntaxKinds_AreUniqueExceptExplicitTokenRangeMarkers()
    {
        var collisions = Enum.GetNames(typeof(AkburaSyntaxKind))
            // Exclude only the two intentional alias names, not their values.
            // An unrelated kind using either value must still fail this test.
            .Where(static name =>
                name != nameof(AkburaSyntaxKind.FirstTokenWithWellKnownText) &&
                name != nameof(AkburaSyntaxKind.LastTokenWithWellKnownText))
            .Select(static name => new
            {
                Name = name,
                Value = (ushort)(AkburaSyntaxKind)Enum.Parse(
                    typeof(AkburaSyntaxKind), name),
            })
            .GroupBy(static entry => entry.Value)
            .Where(static group => group.Count() > 1)
            .OrderBy(static group => group.Key)
            .Select(static group =>
                $"{group.Key}: " +
                string.Join(", ", group.Select(static entry => entry.Name)))
            .ToArray();

        Assert.True(
            collisions.Length == 0,
            "SyntaxKind values must be unique:" + Environment.NewLine +
            string.Join(Environment.NewLine, collisions));
    }

    [Fact]
    public void IncompleteConditionalAttribute_DoesNotEnterIfBinder()
    {
        const string source =
            """
            using Avalonia.Controls;

            <Border {true}: />
            """;

        var fixture = AkcssActivatorPlannerTests.CreateFixture(source);
        var root = fixture.ComponentTree.GetRoot();
        var attribute = Assert.Single(
            root.DescendantNodes().OfType<IncompletePrefixedAttributeSyntax>());

        // This source is intentionally incomplete. Keep its syntax diagnostic;
        // semantic analysis must not mistake the recovery node for a $if node.
        var syntaxDiagnostics = root
            .DescendantNodesAndTokensAndSelf(descendIntoTrivia: true)
            .SelectMany(static node => node.GetDiagnostics())
            .ToArray();
        Assert.NotEmpty(syntaxDiagnostics);

        // Follow the diagnostic path from the reported InvalidCastException,
        // then exercise the public operation lookup on the recovery node.
        _ = fixture.SemanticModel.GetSemanticDiagnostics(root);
        _ = fixture.SemanticModel.GetOperation(attribute);
    }
}
