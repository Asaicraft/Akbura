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

        // Literals
        StringLiteralToken = 2000,
        CharLiteralToken = 2001,
        NumericLiteralToken = 2002,

        // Identifiers and special tokens
        IdentifierToken = 3000,
        BadToken = 3001,
        EndOfFileToken = 3002,

        // ─────────────────────────────────────────────
        // TOKENS WITH WELL-KNOWN TEXT (contiguous block)
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

        // ─────────────────────────────────────────────
        // DSL-specific literals and tokens
        // ─────────────────────────────────────────────
        AkTextLiteral = 200,
        CSharpRawToken = 201,

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

        // Markup tree
        MarkupSyntaxNode = 519,
        MarkupRootSyntax = 520,
        MarkupNodeSyntax = 521,
        MarkupElementSyntax = 522,
        MarkupStartTagSyntax = 523,
        MarkupEndTagSyntax = 524,

        // Markup content nodes
        MarkupContentSyntax = 525,
        MarkupTextLiteralSyntax = 526,
        MarkupElementContentSyntax = 527,
        MarkupInlineExpressionSyntax = 528,

        // Markup attributes
        MarkupAttributeSyntax = 529,
        MarkupPlainAttributeSyntax = 530,
        MarkupPrefixedAttributeSyntax = 531,
        MarkupAttributeValueSyntax = 532,
        MarkupLiteralAttributeValueSyntax = 533,
        MarkupDynamicAttributeValueSyntax = 534,

        // Tailwind attributes
        TailwindAttributeSyntax = 535,
        TailwindSegmentSyntax = 536,
        TailwindIdentifierSegmentSyntax = 537,
        TailwindNumericSegmentSyntax = 538,
        TailwindExpressionSegmentSyntax = 539,
        TailwindFlagAttributeSyntax = 540,
        TailwindPrefixSegmentSyntax = 541,
        SimpleConditionalPrefixSyntax = 542,
        ExpressionConditionalPrefixSyntax = 543,
        TailwindFullAttributeSyntax = 544,

        // AKCSS root
        AkcssDocumentSyntax = 545,
        AkcssTopLevelMember = 546,

        // Shared body members
        AkcssBodyMemberSyntax = 547,
        AkcssAssignmentSyntax = 548,
        AkcssIfDirectiveSyntax = 549,
        AkcssAdditionalPseudoStateSyntax = 550,
        AkcssPseudoSelectorSyntax = 551,
        AkcssPseudoBlockSyntax = 552,

        // AKCSS style rules
        AkcssStyleRuleSyntax = 553,
        AkcssStyleSelectorSyntax = 554,

        // AKCSS utilities
        AkcssUtilitiesSectionSyntax = 555,
        AkcssUtilityParameterSyntax = 556,
        AkcssUtilitySelectorSyntax = 557,
        AkcssUtilityDeclarationSyntax = 558,

        // Identifiers and types
        Type = 700,
        Name = 701,
        SimpleName = 702,
        IdentifierName = 703,
    }
}