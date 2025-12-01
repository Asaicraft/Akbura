using Akbura.Language.Syntax.Green;

namespace Akbura.Language.Syntax
{
    public enum SyntaxKind : ushort
    {
        None = 0,
        List = GreenNode.ListKind,

        // Trivia
        EndOfLineTrivia = 1000,
        WhitespaceTrivia = 1001,

        // Built-in literal tokens
        StringLiteralToken = 2000,
        CharLiteralToken = 2001,
        NumericLiteralToken = 2002,

        IdentifierToken = 3000,
        BadToken = 3001,
        EndOfFileToken = 3002,

        // ─────────────────────────────────────────────
        // TOKENS WITH WELL-KNOWN TEXT (contiguous)
        // ─────────────────────────────────────────────
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

        UtilitiesKeyword = 157,

        LastTokenWithWellKnownText = 157,

        // DSL-specific literal
        AkTextLiteral = 201,

        CSharpRawToken = 202,

        // ─────────────────────────────────────────────
        // SYNTAX NODES
        // ─────────────────────────────────────────────

        // Base node abstractions
        AkTopLevelMember = 500,

        // Embedded C# syntax nodes
        CSharpTypeSyntax = 501,
        CSharpExpressionSyntax = 502,
        CSharpBlockSyntax = 503,
        CSharpStatementSyntax = 504,
        InlineExpressionSyntax = 505,

        // Document root
        AkburaDocumentSyntax = 506,

        // Inject / Param / State / Command Declarations
        InjectDeclarationSyntax = 507,
        ParamDeclarationSyntax = 508,
        StateDeclarationSyntax = 509,
        StateInitializer = 510,
        SimpleStateInitializer = 511,
        BindableStateInitializer = 512,

        // useEffect Declaration (+ cancel / finally blocks)
        UseEffectDeclarationSyntax = 513,
        EffectCancelBlockSyntax = 514,
        EffectFinallyBlockSyntax = 515,
        CommandDeclarationSyntax = 516,

        // Function Declarations
        FunctionDeclarationSyntax = 517,
        ParameterSyntax = 518,

        // User hooks
        UserHook = 519,

        // Markup tree
        MarkupSyntaxNode = 520,
        MarkupRootSyntax = 521,
        MarkupNodeSyntax = 522,
        MarkupElementSyntax = 523,
        MarkupStartTagSyntax = 524,
        MarkupEndTagSyntax = 525,

        // Markup content nodes
        MarkupContentSyntax = 526,
        MarkupTextLiteralSyntax = 527,
        MarkupElementContentSyntax = 528,
        MarkupInlineExpressionSyntax = 529,

        // Markup attributes
        MarkupAttributeSyntax = 530,
        MarkupPlainAttributeSyntax = 531,
        MarkupPrefixedAttributeSyntax = 532,
        MarkupAttributeValueSyntax = 533,
        MarkupLiteralAttributeValueSyntax = 534,
        MarkupDynamicAttributeValueSyntax = 535,

        // Tailwind attributes
        TailwindAttributeSyntax = 536,
        TailwindSegmentSyntax = 537,
        TailwindIdentifierSegmentSyntax = 538,
        TailwindNumericSegmentSyntax = 539,
        TailwindExpressionSegmentSyntax = 540,
        TailwindFlagAttributeSyntax = 541,
        TailwindPrefixSegmentSyntax = 542,
        SimpleConditionalPrefixSyntax = 543,
        ExpressionConditionalPrefixSyntax = 544,
        TailwindFullAttributeSyntax = 545,

        // AKCSS root
        AkcssDocumentSyntax = 546,
        AkcssTopLevelMember = 547,

        // Shared body members
        AkcssBodyMemberSyntax = 548,
        AkcssAssignmentSyntax = 549,
        AkcssIfDirectiveSyntax = 550,
        AkcssAdditionalPseudoStateSyntax = 551,
        AkcssPseudoSelectorSyntax = 552,
        AkcssPseudoBlockSyntax = 553,

        // AKCSS style rules
        AkcssStyleRuleSyntax = 554,
        AkcssStyleSelectorSyntax = 555,

        // AKCSS utilities
        AkcssUtilitiesSectionSyntax = 556,
        AkcssUtilityParameterSyntax = 557,
        AkcssUtilitySelectorSyntax = 558,
        AkcssUtilityDeclarationSyntax = 559,

        // Identifiers and types
        Type = 700,
        Name = 701,
        SimpleName = 702,
        IdentifierName = 703,

        // Trivia
        SingleLineCommentTrivia = 800,
        MultiLineCommentTrivia = 801,
    }
}