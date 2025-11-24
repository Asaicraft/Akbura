using Akbura.Language.Syntax;
using static Akbura.Language.Syntax.SyntaxFactory;

namespace Akbura.UnitTests;

public class AkburaSyntaxFactoryTests
{
    [Fact]
    public void Build_Syntax_For_ButtonClick()
    {
        // state count = 0;
        //
        // <Button Click={count++}>
        //     {count}
        // </Button>


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

    [Fact]
    public void Build_Syntax_For_Akcss_Document()
    {
        // Common trivia helpers
        var nl = new SyntaxTriviaList(LineFeed);
        var doubleNl = new SyntaxTriviaList(LineFeed, LineFeed);
        var space = new SyntaxTriviaList(Space);                         // single " "
        var indent4 = new SyntaxTriviaList(Whitespace("    "));          // 4 spaces
        var indent8 = new SyntaxTriviaList(Whitespace("        "));      // 8 spaces
        var indent12 = new SyntaxTriviaList(Whitespace("            ")); // 12 spaces

        //
        // .myclass {
        //     Background: "Red";
        //     @hover {
        //         Background: "Blue";
        //     }
        // }
        //

        // .myclass
        var myClassSelector = AkcssStyleSelectorSyntax(
            targetType: null,
            dotToken: Token(SyntaxKind.DotToken),
            name: IdentifierName("myclass")
        );

        // Background: "Red";
        var redExpression = CSharpExpressionSyntax(
            TokenList(
                CSharpRawToken("\"Red\"")
            )
        );

        var backgroundRedAssignment = AkcssAssignmentSyntax(
            propertyName: IdentifierName("Background"),
            colon: TokenWithTrailingSpace(SyntaxKind.ColonToken),
            expression: redExpression,
            semicolon: Token(SyntaxKind.SemicolonToken)
        )
        .WithLeadingTrivia(new(indent4))   // "    Background..."
        .WithTrailingTrivia(nl);      // end of line

        // @hover { Background: "Blue"; }

        var blueExpression = CSharpExpressionSyntax(
            TokenList(
                CSharpRawToken("\"Blue\"")
            )
        );

        var backgroundBlueAssignment = AkcssAssignmentSyntax(
            propertyName: IdentifierName("Background"),
            colon: TokenWithTrailingSpace(SyntaxKind.ColonToken),
            expression: blueExpression,
            semicolon: Token(SyntaxKind.SemicolonToken)
        )
        .WithLeadingTrivia(indent8)   // "        Background..."
        .WithTrailingTrivia(nl);      // end of line

        var hoverSelector = AkcssPseudoSelectorSyntax(
            atToken: Token(SyntaxKind.AtToken),
            firstState: IdentifierName("hover"),
            additional: List<AkcssAdditionalPseudoStateSyntax>()
        );

        // "@hover {" line + its body
        var hoverBlock = AkcssPseudoBlockSyntax(
            selector: hoverSelector,
            openBrace: Token(SyntaxKind.OpenBraceToken)
                .WithLeadingTrivia(space)      // "@hover {"
                .WithTrailingTrivia(nl),       // newline after "{"
            members: List<AkcssBodyMemberSyntax>(
                [
                    backgroundBlueAssignment
                ]),
            closeBrace: Token(SyntaxKind.CloseBraceToken)
                .WithLeadingTrivia(indent4)    // "    }"
                .WithTrailingTrivia(nl)        // newline after inner '}'
        )
        .WithLeadingTrivia(indent4);           // "    @hover..."

        // Whole .myclass rule
        var myClassRule = AkcssStyleRuleSyntax(
            selector: myClassSelector,
            openBrace: Token(SyntaxKind.OpenBraceToken)
                .WithLeadingTrivia(space)      // ".myclass {"
                .WithTrailingTrivia(nl),       // newline after "{"
            members: List<AkcssBodyMemberSyntax>(
                [
                    backgroundRedAssignment,
                    hoverBlock
                ]),
            closeBrace: Token(SyntaxKind.CloseBraceToken)
                .WithTrailingTrivia(doubleNl)    // "}\n\n" (blank line after rule)
        );

        //
        // @utilities {
        //     .rounded {
        //         CornerRadius: 4;
        //     }
        //
        //     .w-(double width) {
        //         Width: width * Spacing;
        //     }
        //
        //     .space-(int x)-(int y) {
        //         MarginLeft: x * Spacing;
        //         MarginTop:  y * Spacing;
        //
        //         @if(x > y) {
        //             BorderThickness: x - y;
        //         }
        //     }
        // }
        //

        // ---- .rounded { CornerRadius: 4; } ----

        var roundedSelector = AkcssUtilitySelectorSyntax(
            targetType: null,
            dotToken: Token(SyntaxKind.DotToken),
            name: IdentifierName("rounded"),
            parameters: List<AkcssUtilityParameterSyntax>()
        );

        var fourExpression = CSharpExpressionSyntax(
            TokenList(
                NumericLiteralToken("4", 4)
            )
        );

        var cornerRadiusAssignment = AkcssAssignmentSyntax(
            propertyName: IdentifierName("CornerRadius"),
            colon: TokenWithTrailingSpace(SyntaxKind.ColonToken),
            expression: fourExpression,
            semicolon: Token(SyntaxKind.SemicolonToken)
        )
        .WithLeadingTrivia(indent8)   // "        CornerRadius..."
        .WithTrailingTrivia(nl);

        var roundedUtility = AkcssUtilityDeclarationSyntax(
            selector: roundedSelector,
            openBrace: Token(SyntaxKind.OpenBraceToken)
                .WithLeadingTrivia(space)      // ".rounded {"
                .WithTrailingTrivia(nl),
            members: List<AkcssBodyMemberSyntax>(
                [
                    cornerRadiusAssignment
                ]),
            closeBrace: Token(SyntaxKind.CloseBraceToken)
                .WithLeadingTrivia(indent4)    // "    }"
                .WithTrailingTrivia(doubleNl)    // blank line after rounded
        )
        .WithLeadingTrivia(indent4);           // "    .rounded..."

        // ---- .w-(double width) { Width: width * Spacing; } ----

        // "double " token + type
        var doubleToken = Identifier("double")
            .WithTrailingTrivia(space);

        var widthType = CSharpTypeSyntax(
            TokenList(
                doubleToken
            )
        );

        var widthParam = AkcssUtilityParameterSyntax(
            minus: Token(SyntaxKind.MinusToken),
            openParen: Token(SyntaxKind.OpenParenToken),
            type: widthType,
            paramName: IdentifierName("width"),
            closeParen: Token(SyntaxKind.CloseParenToken)
        );

        var wSelector = AkcssUtilitySelectorSyntax(
            targetType: null,
            dotToken: Token(SyntaxKind.DotToken),
            name: IdentifierName("w"),
            parameters: List(
                [widthParam]
            )
        );

        var widthTimesSpacingExpression = CSharpExpressionSyntax(
            TokenList(
                CSharpRawToken("width * Spacing")
            )
        );

        var widthAssignment = AkcssAssignmentSyntax(
            propertyName: IdentifierName("Width"),
            colon: TokenWithTrailingSpace(SyntaxKind.ColonToken),
            expression: widthTimesSpacingExpression,
            semicolon: Token(SyntaxKind.SemicolonToken)
        )
        .WithLeadingTrivia(indent8)
        .WithTrailingTrivia(nl);

        var wUtility = AkcssUtilityDeclarationSyntax(
            selector: wSelector,
            openBrace: Token(SyntaxKind.OpenBraceToken)
                .WithLeadingTrivia(space)      // ".w-(double width) {"
                .WithTrailingTrivia(nl),
            members: List<AkcssBodyMemberSyntax>(
                [
                    widthAssignment
                ]),
            closeBrace: Token(SyntaxKind.CloseBraceToken)
                .WithLeadingTrivia(indent4)
                .WithTrailingTrivia(doubleNl)    // blank line after w-utility
        )
        .WithLeadingTrivia(indent4);

        // ---- .space-(int x)-(int y) { ... } ----

        var intToken = Identifier("int")
            .WithTrailingTrivia(space);

        var intType = CSharpTypeSyntax(
            TokenList(
                intToken
            )
        );

        var xParam = AkcssUtilityParameterSyntax(
            minus: Token(SyntaxKind.MinusToken),
            openParen: Token(SyntaxKind.OpenParenToken),
            type: intType,
            paramName: IdentifierName("x"),
            closeParen: Token(SyntaxKind.CloseParenToken)
        );

        var yParam = AkcssUtilityParameterSyntax(
            minus: Token(SyntaxKind.MinusToken),
            openParen: Token(SyntaxKind.OpenParenToken),
            type: intType,
            paramName: IdentifierName("y"),
            closeParen: Token(SyntaxKind.CloseParenToken)
        );

        var spaceSelector = AkcssUtilitySelectorSyntax(
            targetType: null,
            dotToken: Token(SyntaxKind.DotToken),
            name: IdentifierName("space"),
            parameters: List(
                [xParam, yParam]
            )
        );

        // MarginLeft: x * Spacing;
        var marginLeftExpr = CSharpExpressionSyntax(
            TokenList(
                CSharpRawToken("x * Spacing")
            )
        );

        var marginLeftAssignment = AkcssAssignmentSyntax(
            propertyName: IdentifierName("MarginLeft"),
            colon: TokenWithTrailingSpace(SyntaxKind.ColonToken),
            expression: marginLeftExpr,
            semicolon: Token(SyntaxKind.SemicolonToken)
        )
        .WithLeadingTrivia(indent8)
        .WithTrailingTrivia(nl);

        // MarginTop: y * Spacing;
        var marginTopExpr = CSharpExpressionSyntax(
            TokenList(
                CSharpRawToken("y * Spacing")
            )
        );

        var marginTopAssignment = AkcssAssignmentSyntax(
            propertyName: IdentifierName("MarginTop"),
            colon: TokenWithTrailingSpace(SyntaxKind.ColonToken),
            expression: marginTopExpr,
            semicolon: Token(SyntaxKind.SemicolonToken)
        )
        .WithLeadingTrivia(indent8)
        .WithTrailingTrivia(doubleNl); // blank line before @if

        // @if(x > y) { BorderThickness: x - y; }

        var conditionExpr = CSharpExpressionSyntax(
            TokenList(
                CSharpRawToken("x > y")
            )
        );

        var borderThicknessExpr = CSharpExpressionSyntax(
            TokenList(
                CSharpRawToken("x - y")
            )
        );

        var borderThicknessAssignment = AkcssAssignmentSyntax(
            propertyName: IdentifierName("BorderThickness"),
            colon: TokenWithTrailingSpace(SyntaxKind.ColonToken),
            expression: borderThicknessExpr,
            semicolon: Token(SyntaxKind.SemicolonToken)
        )
        .WithLeadingTrivia(indent12)
        .WithTrailingTrivia(nl);

        var spaceIfBlock = AkcssIfDirectiveSyntax(
            atToken: Token(SyntaxKind.AtToken)
                .WithLeadingTrivia(indent8),   // "        @if..."
            ifKeyword: Token(SyntaxKind.IfKeyword),
            openParen: Token(SyntaxKind.OpenParenToken),
            condition: conditionExpr,
            closeParen: TokenWithTrailingSpace(SyntaxKind.CloseParenToken),
            openBrace: Token(SyntaxKind.OpenBraceToken)
                .WithTrailingTrivia(nl),
            members: List<AkcssBodyMemberSyntax>(
                [
                    borderThicknessAssignment
                ]),
            closeBrace: Token(SyntaxKind.CloseBraceToken)
                .WithLeadingTrivia(indent8)
                .WithTrailingTrivia(nl)
        );

        var spaceUtility = AkcssUtilityDeclarationSyntax(
            selector: spaceSelector,
            openBrace: Token(SyntaxKind.OpenBraceToken)
                .WithLeadingTrivia(space)      // ".space-(...) {"
                .WithTrailingTrivia(nl),
            members: List<AkcssBodyMemberSyntax>(
                [
                    marginLeftAssignment,
                    marginTopAssignment,
                    spaceIfBlock
                ]),
            closeBrace: Token(SyntaxKind.CloseBraceToken)
                .WithLeadingTrivia(indent4)
                .WithTrailingTrivia(nl)
        )
        .WithLeadingTrivia(new(indent4));

        //
        // @utilities { ... }
        //

        var utilitiesSection = AkcssUtilitiesSectionSyntax(
            atToken: Token(SyntaxKind.AtToken),
            utilitiesToken: Token(SyntaxKind.UtilitiesKeyword),
            openBrace: Token(SyntaxKind.OpenBraceToken)
                .WithLeadingTrivia(space)      // "@utilities {"
                .WithTrailingTrivia(nl),
            utilities: List(
                [
                    roundedUtility,
                    wUtility,
                    spaceUtility
                ]),
            closeBrace: Token(SyntaxKind.CloseBraceToken)
                .WithTrailingTrivia(nl)        // final newline
        );

        //
        // Final AkcssDocumentSyntax
        //

        var akcssDocument = AkcssDocumentSyntax(
            members: List<AkcssTopLevelMemberSyntax>(
                [
                    myClassRule,
                    utilitiesSection
                ]),
            endOfFile: Token(SyntaxKind.EndOfFileToken)
        );

        var text = akcssDocument.ToFullString();

        const string expected =
            ".myclass {\n" +
            "    Background: \"Red\";\n" +
            "    @hover {\n" +
            "        Background: \"Blue\";\n" +
            "    }\n" +
            "}\n" +
            "\n" +
            "@utilities {\n" +
            "    .rounded {\n" +
            "        CornerRadius: 4;\n" +
            "    }\n" +
            "\n" +
            "    .w-(double width) {\n" +
            "        Width: width * Spacing;\n" +
            "    }\n" +
            "\n" +
            "    .space-(int x)-(int y) {\n" +
            "        MarginLeft: x * Spacing;\n" +
            "        MarginTop: y * Spacing;\n" +
            "\n" +
            "        @if(x > y) {\n" +
            "            BorderThickness: x - y;\n" +
            "        }\n" +
            "    }\n" +
            "}\n";

        Assert.Equal(expected, text);
    }
}
