using Akbura.Language.Syntax;
using System.Net.WebSockets;

namespace Akbura.UnitTests;

public class AkburaSyntaxFactoryTests
{
    [Fact]
    public void Build_Syntax_For_ButtonClick()
    {
        //
        // state count = 0;
        //

        var stateKeyword = SyntaxFactory.TokenWithTrailingSpace(SyntaxKind.StateKeyword);

        var identifierCount = SyntaxFactory.IdentifierWithTrailingSpace("count");

        var equalsToken = SyntaxFactory.TokenWithTrailingSpace(SyntaxKind.EqualsToken);

        var tokens = SyntaxFactory.TokenList(
            SyntaxFactory.NumericLiteralToken("0", 0)
        );

        var zeroExpression = SyntaxFactory.CSharpExpressionSyntax(
            tokens: tokens
        );

        var stateDeclaration = SyntaxFactory.StateDeclarationSyntax(
            stateKeyword: stateKeyword,
            type: null,
            name: SyntaxFactory.IdentifierName(identifierCount),
            equalsToken: equalsToken,
            initializer: SyntaxFactory.SimpleStateInitializerSyntax(zeroExpression),
            semicolon: SyntaxFactory.Token(SyntaxKind.SemicolonToken)
        );

        var stateDeclarationText = stateDeclaration.ToFullString();

        const string expectedStateDeclarationText = "state count = 0;";

        Assert.Equal(expectedStateDeclarationText, stateDeclarationText);

        //
        // <Button Click={count++}>
        //     {count}
        // </Button>
        //

        var lessToken = SyntaxFactory.Token(SyntaxKind.LessThanToken);
        var greaterToken = SyntaxFactory.Token(SyntaxKind.GreaterThanToken);
        var lessSlashToken = SyntaxFactory.Token(SyntaxKind.LessSlashToken);

        // Element name
        var buttonName = SyntaxFactory.IdentifierName("Button");

        // Attribute: Click={count++}

        var countIncrementTokens = SyntaxFactory.TokenList(
            SyntaxFactory.CSharpRawToken("count++")
        );

        var clickInlineExpression = SyntaxFactory.InlineExpressionSyntax(
            openBrace: SyntaxFactory.Token(SyntaxKind.OpenBraceToken),
            expression: SyntaxFactory.CSharpExpressionSyntax(countIncrementTokens),
            closeBrace: SyntaxFactory.Token(SyntaxKind.CloseBraceToken)
        );

        var clickAttribute = SyntaxFactory.MarkupPlainAttributeSyntax(
            name: SyntaxFactory.IdentifierName("Click"),
            equalsToken: SyntaxFactory.Token(SyntaxKind.EqualsToken),
            value: SyntaxFactory.MarkupDynamicAttributeValueSyntax(
                prefix: null,
                expression: clickInlineExpression
            )
        );

        // START TAG
        var startTag = SyntaxFactory.MarkupStartTagSyntax(
            lessToken,
            buttonName.WithTrailingTrivia(new(SyntaxFactory.Space)),
            SyntaxFactory.SingletonList<MarkupAttributeSyntax>(clickAttribute),
            greaterToken.WithTrailingTrivia(
                SyntaxFactory.TriviaList(
                    SyntaxFactory.LineFeed    // "\n"
                )
            )
        );

        // BODY: {count}

        var bodyExpressionTokens = SyntaxFactory.TokenList(
            SyntaxFactory.Identifier("count")
        );

        var bodyInlineExpression = SyntaxFactory.MarkupInlineExpressionSyntax(
            SyntaxFactory.InlineExpressionSyntax(
                openBrace: SyntaxFactory.Token(SyntaxKind.OpenBraceToken)
                    .WithLeadingTrivia(SyntaxFactory.Whitespace("    ")), // indent "    "
                expression: SyntaxFactory.CSharpExpressionSyntax(bodyExpressionTokens),
                closeBrace: SyntaxFactory.Token(SyntaxKind.CloseBraceToken)
            )
        ).WithTrailingTrivia(
            SyntaxFactory.TriviaList(
                SyntaxFactory.LineFeed     // "\n"
            )
        );

        // END TAG
        var endTag = SyntaxFactory.MarkupEndTagSyntax(
            lessSlashToken,
            name: buttonName,
            greaterToken: greaterToken
        );

        // ELEMENT
        var buttonElement = SyntaxFactory.MarkupElementSyntax(
            startTag,
            SyntaxFactory.SingletonList<MarkupContentSyntax>(bodyInlineExpression),
            endTag
        );

        var buttonText = buttonElement.ToFullString();

        const string expectedButtonText =
            "<Button Click={count++}>\n" +
            "    {count}\n" +
            "</Button>";

        Assert.Equal(expectedButtonText, buttonText);

        //
        // Now we assemble the full document:
        //
        // state count = 0;
        //
        // <Button Click={count++}>
        //     {count}
        // </Button>
        //

        // Add two blank lines after the state declaration
        var stateWithBlankLine = stateDeclaration.WithTrailingTrivia(
            SyntaxFactory.TriviaList(
                SyntaxFactory.LineFeed,   // "\n"
                SyntaxFactory.LineFeed    // extra blank line
            )
        );

        // Markup root with the Button element
        var markupRoot = SyntaxFactory.MarkupRootSyntax(
            element: buttonElement
        );

        // Compose the full Akbura document
        var document = SyntaxFactory.AkburaDocumentSyntax(
            members: SyntaxFactory.List<AkTopLevelMemberSyntax>(
                [
                    stateWithBlankLine,
                    markupRoot
                ]),
            endOfFile: SyntaxFactory.EndOfFileToken()
        );

        var documentText = document.ToFullString();

        // Expected result in multi-line form
        const string expectedDocumentText =
            "state count = 0;\n" +
            "\n" +
            "<Button Click={count++}>\n" +
            "    {count}\n" +
            "</Button>";

        Assert.Equal(expectedDocumentText, documentText);
    }
}
