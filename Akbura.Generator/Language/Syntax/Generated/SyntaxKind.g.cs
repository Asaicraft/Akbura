using Akbura.Language.Syntax.Green;

namespace Akbura.Language.Syntax
{
    public enum SyntaxKind : ushort
    {
        None = 0,
        List = GreenNode.ListKind, // list pseudo-kind

        // Trivia kinds (predefined)
        EndOfLineTrivia = 1000,
        WhitespaceTrivia = 1001,

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
        BindToken = 153,
        InToken = 154,
        OutToken = 155,

        LastTokenWithWellKnownText = 155,

        // Literal tokens (predefined)
        StringLiteralToken = 2000,
        CharLiteralToken = 2001,
        NumericLiteralToken = 2002,

        // Identifier token (predefined)
        IdentifierToken = 3000,
        BadToken = 3001,

        // Literals
        AkTextLiteral = 201,

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

        StateInitializer = 509,
        SimpleStateInitializer = 510,
        BindableStateInitializer = 511,

        UseEffectDeclarationSyntax = 512,
        EffectCancelBlockSyntax = 513,
        EffectFinallyBlockSyntax = 514,

        FunctionDeclarationSyntax = 515,
        ParameterSyntax = 516,

        MarkupSyntaxNode = 517,
        MarkupRootSyntax = 518,
        MarkupNodeSyntax = 519,
        MarkupElementSyntax = 520,
        MarkupStartTagSyntax = 521,
        MarkupEndTagSyntax = 522,

        MarkupContentSyntax = 523,
        MarkupTextLiteralSyntax = 524,
        MarkupElementContentSyntax = 525,
        MarkupInlineExpressionSyntax = 526,

        MarkupAttributeSyntax = 527,
        MarkupPlainAttributeSyntax = 528,
        MarkupPrefixedAttributeSyntax = 529,

        MarkupAttributeValueSyntax = 530,
        MarkupLiteralAttributeValueSyntax = 531,
        MarkupDynamicAttributeValueSyntax = 532,

        TailwindAttributeSyntax = 533,

        TailwindSegmentSyntax = 534,
        TailwindIdentifierSegmentSyntax = 535,
        TailwindNumericSegmentSyntax = 536,
        TailwindExpressionSegmentSyntax = 537,

        TailwindFlagAttributeSyntax = 538,

        TailwindPrefixSegmentSyntax = 539,
        SimpleConditionalPrefixSyntax = 540,
        ExpressionConditionalPrefixSyntax = 541,

        TailwindFullAttributeSyntax = 542,

        AkcssDocumentSyntax = 543,
        AkcssTopLevelMember = 544,

        AkcssBodyMemberSyntax = 545,
        AkcssAssignmentSyntax = 546,
        AkcssIfDirectiveSyntax = 547,
        AkcssAdditionalPseudoStateSyntax = 548,
        AkcssPseudoSelectorSyntax = 549,
        AkcssPseudoBlockSyntax = 550,

        AkcssStyleRuleSyntax = 551,
        AkcssStyleSelectorSyntax = 552,

        AkcssUtilitiesSectionSyntax = 553,
        AkcssUtilityParameterSyntax = 554,
        AkcssUtilitySelectorSyntax = 555,
        AkcssUtilityDeclarationSyntax = 556,

        Type = 700,
        Name = 701,
        SimpleName = 702,
        IdentifierName = 703,
    }
}