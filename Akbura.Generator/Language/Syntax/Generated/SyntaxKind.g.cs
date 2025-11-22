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
        CommentTrivia = 1002,

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
        CommandKeyword = 109,

        NewKeyword = 110,
        ReactListKeyword = 111,

        IfKeyword = 112,
        ElseKeyword = 113,
        ReturnKeyword = 114,
        ForKeyword = 115,

        TrueKeyword = 116,
        FalseKeyword = 117,
        NullKeyword = 118,

        PlusToken = 119,
        MinusToken = 120,
        AsteriskToken = 121,
        SlashToken = 122,
        PercentToken = 123,
        CaretToken = 124,
        BarToken = 125,
        AmpersandToken = 126,
        QuestionToken = 127,
        ColonToken = 128,
        SemicolonToken = 129,
        CommaToken = 130,
        DotToken = 131,
        DoubleDotToken = 132,
        EqualsToken = 133,
        BangToken = 134,
        EqualsEqualsToken = 135,
        BangEqualsToken = 136,
        GreaterThanToken = 137,
        LessThanToken = 138,
        GreaterEqualsToken = 139,
        LessEqualsToken = 140,
        ArrowToken = 141,
        HashToken = 142,

        OpenBraceToken = 143,
        CloseBraceToken = 144,
        OpenBracketToken = 145,
        CloseBracketToken = 146,
        OpenParenToken = 147,
        CloseParenToken = 148,

        LessSlashToken = 149,
        SlashGreaterToken = 150,

        SingleQuoteToken = 151,
        DoubleQuoteToken = 152,

        AtToken = 153,
        BindToken = 154,
        InToken = 155,
        OutToken = 156,

        LastTokenWithWellKnownText = 156,

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

        CommandDeclarationSyntax = 515,

        FunctionDeclarationSyntax = 516,
        ParameterSyntax = 517,

        MarkupSyntaxNode = 518,
        MarkupRootSyntax = 519,
        MarkupNodeSyntax = 520,
        MarkupElementSyntax = 521,
        MarkupStartTagSyntax = 522,
        MarkupEndTagSyntax = 523,

        MarkupContentSyntax = 524,
        MarkupTextLiteralSyntax = 525,
        MarkupElementContentSyntax = 526,
        MarkupInlineExpressionSyntax = 527,

        MarkupAttributeSyntax = 528,
        MarkupPlainAttributeSyntax = 529,
        MarkupPrefixedAttributeSyntax = 530,

        MarkupAttributeValueSyntax = 531,
        MarkupLiteralAttributeValueSyntax = 532,
        MarkupDynamicAttributeValueSyntax = 533,

        TailwindAttributeSyntax = 534,
        TailwindSegmentSyntax = 535,
        TailwindIdentifierSegmentSyntax = 536,
        TailwindNumericSegmentSyntax = 537,
        TailwindExpressionSegmentSyntax = 538,
        TailwindFlagAttributeSyntax = 539,

        TailwindPrefixSegmentSyntax = 540,
        SimpleConditionalPrefixSyntax = 541,
        ExpressionConditionalPrefixSyntax = 542,

        TailwindFullAttributeSyntax = 543,

        AkcssDocumentSyntax = 544,
        AkcssTopLevelMember = 545,

        AkcssBodyMemberSyntax = 546,
        AkcssAssignmentSyntax = 547,
        AkcssIfDirectiveSyntax = 548,
        AkcssAdditionalPseudoStateSyntax = 549,
        AkcssPseudoSelectorSyntax = 550,
        AkcssPseudoBlockSyntax = 551,

        AkcssStyleRuleSyntax = 552,
        AkcssStyleSelectorSyntax = 553,

        AkcssUtilitiesSectionSyntax = 554,
        AkcssUtilityParameterSyntax = 555,
        AkcssUtilitySelectorSyntax = 556,
        AkcssUtilityDeclarationSyntax = 557,

        Type = 700,
        Name = 701,
        SimpleName = 702,
        IdentifierName = 703,
    }
}