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
        .WithLeadingTrivia([.. indent4])   // "    Background..."
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
        .WithLeadingTrivia([.. indent4]);

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

    [Fact]
    public void Build_Syntax_For_Full_Akbura_Document()
    {
        var nl = new SyntaxTriviaList(LineFeed);
        var doubleNl = new SyntaxTriviaList(LineFeed, LineFeed);
        var space = new SyntaxTriviaList(Space);                              // single " "
        var indent4 = new SyntaxTriviaList(Whitespace("    "));               // 4 spaces
        var indent8 = new SyntaxTriviaList(Whitespace("        "));           // 8 spaces
        var nlIndent6 = new SyntaxTriviaList(LineFeed, Whitespace("      ")); // 6 spaces and newline

        //
        // inject ILogger<ProfileWithTasks> log;
        //
        var injectLog = InjectDeclarationSyntax(
            injectKeyword: TokenWithTrailingSpace(SyntaxKind.InjectKeyword),
            type: CSharpTypeSyntax(
                TokenList(
                    CSharpRawToken("ILogger<ProfileWithTasks>").WithTrailingTrivia(space)
                )
            ),
            name: IdentifierName("log"),
            semicolon: Token(SyntaxKind.SemicolonToken)
        ).WithTrailingTrivia(nl);

        //
        // inject IViewModel viewModel;
        //
        var iViewModelToken = Identifier("IViewModel").WithTrailingTrivia(space);

        var injectViewModel = InjectDeclarationSyntax(
            injectKeyword: Token(SyntaxKind.InjectKeyword)
                .WithTrailingTrivia(space),
            type: CSharpTypeSyntax(
                TokenList(iViewModelToken)
            ),
            name: IdentifierName("viewModel"),
            semicolon: Token(SyntaxKind.SemicolonToken)
        ).WithTrailingTrivia(doubleNl);

        //
        // param bind int UserId = 1;
        //
        var intToken = Identifier("int").WithTrailingTrivia(space);

        var paramBindUserId = ParamDeclarationSyntax(
            paramKeyword: Token(SyntaxKind.ParamKeyword)
                .WithTrailingTrivia(space),
            bindingKeyword: Token(SyntaxKind.BindToken)
                .WithTrailingTrivia(space),
            type: CSharpTypeSyntax(
                TokenList(intToken)
            ),
            name: IdentifierName("UserId").WithTrailingTrivia(space),
            equalsToken: Token(SyntaxKind.EqualsToken)
                .WithTrailingTrivia(space),
            defaultValue: CSharpExpressionSyntax(
                TokenList(
                    NumericLiteralToken("1", 1)
                )
            ),
            semicolon: Token(SyntaxKind.SemicolonToken)
        ).WithTrailingTrivia(nl);

        //
        // param out Result = "hello";
        //
        var paramOutResult = ParamDeclarationSyntax(
            paramKeyword: Token(SyntaxKind.ParamKeyword)
                .WithTrailingTrivia(space),
            bindingKeyword: Token(SyntaxKind.OutToken)
                .WithTrailingTrivia(space),
            type: null,
            name: IdentifierName("Result").WithTrailingTrivia(space),
            equalsToken: TokenWithTrailingSpace(SyntaxKind.EqualsToken),
            defaultValue: CSharpExpressionSyntax(
                 [CSharpRawToken("\"hello\"")]    
            ),
            semicolon: Token(SyntaxKind.SemicolonToken)
        ).WithTrailingTrivia(doubleNl);

        //
        // state count = 0;
        //
        var stateCount = StateDeclarationSyntax(
            stateKeyword: Token(SyntaxKind.StateKeyword)
                .WithTrailingTrivia(space),
            type: null,
            name: IdentifierName("count")
                .WithTrailingTrivia(space),
            equalsToken: Token(SyntaxKind.EqualsToken)
                .WithTrailingTrivia(space),
            initializer: SimpleStateInitializerSyntax(
                CSharpExpressionSyntax(
                    TokenList(
                        NumericLiteralToken("0", 0)
                    )
                )
            ),
            semicolon: Token(SyntaxKind.SemicolonToken)
        ).WithTrailingTrivia(nl);

        //
        // state ReactList tasks = bind viewModel.Tasks;
        //
        var reactListToken = Identifier("ReactList").WithTrailingTrivia(space);

        var stateTasks = StateDeclarationSyntax(
            stateKeyword: Token(SyntaxKind.StateKeyword)
                .WithTrailingTrivia(space),
            type: CSharpTypeSyntax(
                TokenList(reactListToken)
            ),
            name: IdentifierName("tasks")
                .WithTrailingTrivia(space),
            equalsToken: Token(SyntaxKind.EqualsToken)
                .WithTrailingTrivia(space),
            initializer: BindableStateInitializerSyntax(
                bindingKeyword: Token(SyntaxKind.BindToken)
                    .WithTrailingTrivia(space),
                expression: CSharpExpressionSyntax(
                    TokenList(
                        CSharpRawToken("viewModel.Tasks")
                    )
                )
            ),
            semicolon: Token(SyntaxKind.SemicolonToken)
        ).WithTrailingTrivia(doubleNl);

        //
        // useEffect(UserId, tasks) { }
        // cancel { }
        // finally { }
        //
        var useEffectBody = CSharpBlockSyntax(
            openBrace: Token(SyntaxKind.OpenBraceToken)
                .WithTrailingTrivia(nl),
            tokens: List<AkTopLevelMemberSyntax>(),
            closeBrace: Token(SyntaxKind.CloseBraceToken)
                .WithTrailingTrivia(nl)
        );

        var cancelBody = CSharpBlockSyntax(
            openBrace: Token(SyntaxKind.OpenBraceToken)
                .WithTrailingTrivia(nl),
            tokens: List<AkTopLevelMemberSyntax>(),
            closeBrace: Token(SyntaxKind.CloseBraceToken)
                .WithTrailingTrivia(nl)
        );

        var finallyBody = CSharpBlockSyntax(
            openBrace: Token(SyntaxKind.OpenBraceToken)
                .WithTrailingTrivia(nl),
            tokens: List<AkTopLevelMemberSyntax>(),
            closeBrace: Token(SyntaxKind.CloseBraceToken)
                .WithTrailingTrivia(nl)
        );

        var useEffect = UseEffectDeclarationSyntax(
            useEffectKeyword: Token(SyntaxKind.UseEffectKeyword),
            arguments: "(UserId, tasks)",
            body: useEffectBody,
            EffectCancelBlockSyntax(
                cancelKeyword: Token(SyntaxKind.CancelKeyword),
                body: cancelBody
            ),
            EffectFinallyBlockSyntax(
                finallyKeyword: Token(SyntaxKind.FinallyKeyword),
                body: finallyBody
            )
        ).WithTrailingTrivia(doubleNl);

        //
        // command int CustomClick(int a);
        //

        var command = CommandDeclarationSyntax(
            commandKeyword: Token(SyntaxKind.CommandKeyword)
                .WithTrailingTrivia(space),
            returnType: CSharpTypeSyntax(
                TokenList(
                    intToken
                )
            ),
            name: IdentifierName("CustomClick"),
            parameters: SyntaxFactory.CSharpParameterListSyntax("(int a)"),
            semicolon: Token(SyntaxKind.SemicolonToken)
        ).WithTrailingTrivia(doubleNl);

        //
        // onMounted(UserId, tasks) { }
        //
        var hookBody = CSharpBlockSyntax(
            openBrace: Token(SyntaxKind.OpenBraceToken)
                .WithTrailingTrivia(nl),
            tokens: List<AkTopLevelMemberSyntax>(),
            closeBrace: Token(SyntaxKind.CloseBraceToken)
                .WithTrailingTrivia(nl)
        );

        var hook = UserHook(
            name: IdentifierName("onMounted"),
            openParen: Token(SyntaxKind.OpenParenToken),
            parameters: SeparatedList(
                [
                    CSharpExpressionSyntax(
                        TokenList(
                            Identifier("UserId")
                        )
                    ),
                    CSharpExpressionSyntax(
                        TokenList(
                            Identifier("tasks")
                        )
                    )
                ]
            ),
            closeParen: Token(SyntaxKind.CloseParenToken),
            body: hookBody
        ).WithTrailingTrivia(doubleNl);

        //
        // Embedded C# statement as AkTopLevelMember:
        // Console.WriteLine("Hello from Akbura");
        //
        var csharpStatement = CSharpStatementSyntax(
            tokens: TokenList(
                CSharpRawToken("Console.WriteLine(\"Hello from Akbura\");")
            ),
            body: null
        ).WithTrailingTrivia(doubleNl);

        //
        // MARKUP:
        //
        // <Grid Title="Dashboard"
        //       bind:Search={search}
        //       flex
        //       md:w-40
        //       {isMobile}:h-15>
        //     <Button Click={count++} out:Result={CustomClick}>
        //         {count}
        //     </Button>
        // </Grid>
        //

        var less = Token(SyntaxKind.LessThanToken);
        var greater = Token(SyntaxKind.GreaterThanToken);
        var lessSlash = Token(SyntaxKind.LessSlashToken);

        var gridName = IdentifierName("Grid");
        var buttonName = IdentifierName("Button");

        // Title="Dashboard"
        var titleLiteral = AkTextLiteral("\"Dashboard\"", "Dashboard");
        var titleText = MarkupTextLiteralSyntax(
            textTokens: TokenList(titleLiteral)
        );
        var titleValue = MarkupLiteralAttributeValueSyntax(
            prefix: null,
            value: titleText
        );
        var titleAttribute = MarkupPlainAttributeSyntax(
            name: IdentifierName("Title"),
            equalsToken: Token(SyntaxKind.EqualsToken),
            value: titleValue
        );

        // bind:Search={search}
        var searchExpression = InlineExpressionSyntax(
            openBrace: Token(SyntaxKind.OpenBraceToken),
            expression: CSharpExpressionSyntax(
                TokenList(
                    Identifier("search")
                )
            ),
            closeBrace: Token(SyntaxKind.CloseBraceToken)
        );

        var bindSearchAttribute = MarkupPrefixedAttributeSyntax(
            prefix: Token(SyntaxKind.BindToken),
            colon: Token(SyntaxKind.ColonToken),
            name: IdentifierName("Search"),
            equalsToken: Token(SyntaxKind.EqualsToken),
            value: MarkupDynamicAttributeValueSyntax(
                prefix: null,
                expression: searchExpression
            )
        );

        // flex (TailwindFlagAttributeSyntax)
        var flexAttribute = TailwindFlagAttributeSyntax(
            name: IdentifierName("flex")
        );

        // md:w-40 (TailwindFullAttributeSyntax with simple conditional prefix)
        var mdPrefix = SimpleConditionalPrefixSyntax(
            name: IdentifierName("md"),
            colon: Token(SyntaxKind.ColonToken)
        );

        var mdWidthSegment = TailwindNumericSegmentSyntax(
            number: NumericLiteralToken("40", 40)
        );

        var mdWidthAttribute = TailwindFullAttributeSyntax(
            prefix: mdPrefix,
            name: IdentifierName("w"),
            minus: Token(SyntaxKind.MinusToken),
            segments: SingletonSeparatedList<TailwindSegmentSyntax>(
                mdWidthSegment
            )
        );

        // {isMobile}:h-15 (TailwindFullAttributeSyntax with expression-based prefix)
        var isMobileInline = InlineExpressionSyntax(
            openBrace: Token(SyntaxKind.OpenBraceToken),
            expression: CSharpExpressionSyntax(
                TokenList(
                    Identifier("isMobile")
                )
            ),
            closeBrace: Token(SyntaxKind.CloseBraceToken)
        );

        var mobilePrefix = ExpressionConditionalPrefixSyntax(
            expression: isMobileInline,
            colon: Token(SyntaxKind.ColonToken)
        );

        var mobileHeightSegment = TailwindNumericSegmentSyntax(
            number: NumericLiteralToken("15", 15)
        );

        var mobileHeightAttr = TailwindFullAttributeSyntax(
            prefix: mobilePrefix,
            name: IdentifierName("h"),
            minus: Token(SyntaxKind.MinusToken),
            segments: SingletonSeparatedList<TailwindSegmentSyntax>(
                mobileHeightSegment
            )
        );

        // <Grid ...>
        var gridStartTag = MarkupStartTagSyntax(
            lessToken: less,
            name: gridName.WithTrailingTrivia(space),
            attributes: List<MarkupAttributeSyntax>(
                [
                    titleAttribute.WithTrailingTrivia(nlIndent6),
                    bindSearchAttribute.WithTrailingTrivia(nlIndent6),
                    flexAttribute.WithTrailingTrivia(nlIndent6),
                    mdWidthAttribute.WithTrailingTrivia(nlIndent6),
                    mobileHeightAttr
                ]
            ),
            closeToken: greater.WithTrailingTrivia(nl)
        );

        var gridEndTag = MarkupEndTagSyntax(
            lessSlashToken: lessSlash,
            name: gridName,
            greaterToken: greater
        );

        //
        // <Button Click={count++} out:Result={Console.WriteLine(@value)}>
        //     {count}
        // </Button>
        //

        // Click={count++}
        var countPlusTokens = TokenList(
            CSharpRawToken("count++")
        );

        var clickInline = InlineExpressionSyntax(
            openBrace: Token(SyntaxKind.OpenBraceToken),
            expression: CSharpExpressionSyntax(countPlusTokens),
            closeBrace: Token(SyntaxKind.CloseBraceToken)
        );

        var clickAttribute = MarkupPlainAttributeSyntax(
            name: IdentifierName("Click"),
            equalsToken: Token(SyntaxKind.EqualsToken),
            value: MarkupDynamicAttributeValueSyntax(
                prefix: null,
                expression: clickInline
            )
        );

        // out:Result={Console.WriteLine(@value)}
        var customClickInline = InlineExpressionSyntax(
            openBrace: Token(SyntaxKind.OpenBraceToken),
            expression: CSharpExpressionSyntax(
                TokenList(
                    Identifier("Console.WriteLine(@value)")
                )
            ),
            closeBrace: Token(SyntaxKind.CloseBraceToken)
        );

        var outResultAttribute = MarkupPrefixedAttributeSyntax(
            prefix: Token(SyntaxKind.OutToken),
            colon: Token(SyntaxKind.ColonToken),
            name: IdentifierName("Result"),
            equalsToken: Token(SyntaxKind.EqualsToken),
            value: MarkupDynamicAttributeValueSyntax(
                prefix: null,
                expression: customClickInline
            )
        );

        var buttonStartTag = MarkupStartTagSyntax(
            lessToken: less,
            name: buttonName.WithTrailingTrivia(space),
            attributes: List<MarkupAttributeSyntax>(
                [
                    clickAttribute.WithTrailingTrivia(space),
                    outResultAttribute
                ]
            ),
            closeToken: greater.WithTrailingTrivia(nl)
        ).WithLeadingTrivia(indent4);

        var buttonEndTag = MarkupEndTagSyntax(
            lessSlashToken: lessSlash,
            name: buttonName,
            greaterToken: greater
        ).WithLeadingTrivia(indent4)
         .WithTrailingTrivia(nl);

        // {count}
        var countInline = InlineExpressionSyntax(
            openBrace: Token(SyntaxKind.OpenBraceToken),
            expression: CSharpExpressionSyntax(
                TokenList(
                    Identifier("count")
                )
            ),
            closeBrace: Token(SyntaxKind.CloseBraceToken)
        );

        var buttonBody = MarkupInlineExpressionSyntax(
            expression: countInline
        ).WithLeadingTrivia(indent8)
         .WithTrailingTrivia(nl);

        var buttonElement = MarkupElementSyntax(
            startTag: buttonStartTag,
            body: SingletonList<MarkupContentSyntax>(
                buttonBody
            ),
            endTag: buttonEndTag
        );

        var gridBodyContent = MarkupElementContentSyntax(
            element: buttonElement
        );

        var gridElement = MarkupElementSyntax(
            startTag: gridStartTag,
            body: SingletonList<MarkupContentSyntax>(
                gridBodyContent
            ),
            endTag: gridEndTag
        );

        var markupRoot = MarkupRootSyntax(
            element: gridElement
        );

        //
        // Final AkburaDocumentSyntax
        //
        var document = AkburaDocumentSyntax(
            members: List<AkTopLevelMemberSyntax>(
                [
                    injectLog,
                    injectViewModel,
                    paramBindUserId,
                    paramOutResult,
                    stateCount,
                    stateTasks,
                    useEffect,
                    command,
                    hook,
                    csharpStatement,
                    markupRoot
                ]
            ),
            endOfFile: EndOfFileToken()
        );

        var actual = document.ToFullString();

        const string expected = """
        inject ILogger<ProfileWithTasks> log;
        inject IViewModel viewModel;

        param bind int UserId = 1;
        param out Result = "hello";

        state count = 0;
        state ReactList tasks = bind viewModel.Tasks;

        useEffect(UserId, tasks) {
        }
        cancel{
        }
        finally{
        }

        command int CustomClick(int a);

        onMounted(UserId, tasks){
        }

        Console.WriteLine("Hello from Akbura");

        <Grid Title="Dashboard"
              bind:Search={search}
              flex
              md:w-40
              {isMobile}:h-15>
            <Button Click={count++} out:Result={Console.WriteLine(@value)}>
                {count}
            </Button>
        </Grid>
        """;

        Assert.Equal(expected.ReplaceLineEndings("\n"), actual);
    }
}
