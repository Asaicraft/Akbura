namespace Akbura.Language.Syntax
{
    public enum SyntaxKind : ushort
    {
        None = 0,
        List = Green.GreenNode.ListKind, // list pseudo-kind

        // Trivia
        EndOfLineTrivia = 1000,
        WhitespaceTrivia = 1001,

        // Literal tokens
        StringLiteralToken = 2000,
        CharLiteralToken = 2001,
        NumericLiteralToken = 2002,

        // Identifier and bad token
        IdentifierToken = 3000,
        BadToken = 3001,

        // Tokens with well-known text (contiguous range)
        FirstTokenWithWellKnownText = 100,

        InjectKeyword = 100,
        ParamKeyword = 101,
        StateKeyword = 102,
        UseEffectKeyword = 103,
        SuppressKeyword = 104,
        CancelKeyword = 105,
        FinallyKeyword = 106,
        AsyncKeyword = 107,
        VoidKeyword = 108,

        NewKeyword = 109,
        ReactListKeyword = 110,

        IfKeyword = 111,
        ElseKeyword = 112,
        ReturnKeyword = 113,
        ForKeyword = 114,

        TrueKeyword = 115,
        FalseKeyword = 116,
        NullKeyword = 117,

        PlusToken = 118,
        MinusToken = 119,
        AsteriskToken = 120,
        SlashToken = 121,
        PercentToken = 122,
        CaretToken = 123,
        BarToken = 124,
        AmpersandToken = 125,
        QuestionToken = 126,
        ColonToken = 127,
        SemicolonToken = 128,
        CommaToken = 129,
        DotToken = 130,
        DoubleDotToken = 131,
        EqualsToken = 132,
        BangToken = 133,
        EqualsEqualsToken = 134,
        BangEqualsToken = 135,
        GreaterThanToken = 136,
        LessThanToken = 137,
        GreaterEqualsToken = 138,
        LessEqualsToken = 139,
        ArrowToken = 140,
        HashToken = 141,

        OpenBraceToken = 142,
        CloseBraceToken = 143,
        OpenBracketToken = 144,
        CloseBracketToken = 145,
        OpenParenToken = 146,
        CloseParenToken = 147,

        LessSlashToken = 148,
        SlashGreaterToken = 149,

        SingleQuoteToken = 150,
        DoubleQuoteToken = 151,

        AtToken = 152,

        LastTokenWithWellKnownText = 152,

        // Literals
        AkTextLiteral = 4000,

        // Nodes (starting from 500)
        AkTopLevelMember = 500,

        CSharpTypeSyntax = 501,
        CSharpExpressionSyntax = 502,
        CSharpBlockSyntax = 503,
        InlineExpressionSyntax = 504,

        AkburaDocumentSyntax = 505,

        InjectDeclarationSyntax = 506,
        ParamDeclarationSyntax = 507,
        StateDeclarationSyntax = 508,

        UseEffectDeclarationSyntax = 509,
        EffectCancelBlockSyntax = 510,
        EffectFinallyBlockSyntax = 511,

        FunctionDeclarationSyntax = 512,
        ParameterSyntax = 513,

        MarkupSyntaxNode = 514,
        MarkupRootSyntax = 515,
        MarkupNodeSyntax = 516,
        MarkupElementSyntax = 517,
        MarkupStartTagSyntax = 518,
        MarkupEndTagSyntax = 519,

        MarkupContentSyntax = 520,
        MarkupTextLiteralSyntax = 521,
        MarkupElementContentSyntax = 522,
        MarkupInlineExpressionSyntax = 523,

        MarkupAttributeSyntax = 524,
        MarkupPlainAttributeSyntax = 525,
        MarkupPrefixedAttributeSyntax = 526,

        MarkupAttributeValueSyntax = 527,
        MarkupLiteralAttributeValueSyntax = 528,
        MarkupDynamicAttributeValueSyntax = 529,

        Type = 530,
        Name = 531,
        SimpleName = 532,
        IdentifierName = 533,
    }
}