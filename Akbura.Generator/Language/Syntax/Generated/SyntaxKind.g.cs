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
		SkippedTokensTrivia = 1002,

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
		DoubleColonToken = 149,

		LessSlashToken = 150,
		SlashGreaterToken = 151,

		SingleQuoteToken = 152,
		DoubleQuoteToken = 153,

		AtToken = 154,
		BindToken = 155,
		InToken = 156,
		OutToken = 157,
		UsingKeyword = 158,
		NamespaceKeyword = 159,
		GlobalKeyword = 160,
		StaticKeyword = 161,
		UnsafeKeyword = 162,

		UtilitiesKeyword = 163,
		AkcssKeyword = 164,
		ApplyKeyword = 165,
		InterceptKeyword = 166,

		LastTokenWithWellKnownText = InterceptKeyword,

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
		InlineAkcssBlockSyntax = 507,

		// Using and namespace directives
		UsingAliasSyntax = 508,
		UsingDirectiveSyntax = 509,
		NamespaceDeclarationSyntax = 510,

		// Inject / Param / State / Command Declarations
		InjectDeclarationSyntax = 511,
		ParamDeclarationSyntax = 512,
		StateDeclarationSyntax = 513,
		StateInitializer = 514,
		SimpleStateInitializer = 515,
		BindableStateInitializer = 516,

		// useEffect Declaration (+ cancel / finally blocks)
		UseEffectDeclarationSyntax = 517,
		UseEffectTailBlockSyntax = 518,
		EffectCancelBlockSyntax = 519,
		EffectFinallyBlockSyntax = 520,
		CommandDeclarationSyntax = 521,

		// Function Declarations
		//FunctionDeclarationSyntax = 522,
		CSharpParameterListSyntax = 522,
		CSharpArgumentListSyntax = 523,

		// User hooks
		UserHook = 524,

		// Markup tree
		MarkupSyntaxNode = 525,
		MarkupRootSyntax = 526,
		MarkupNodeSyntax = 527,
		MarkupElementSyntax = 528,
		MarkupStartTagSyntax = 529,
		MarkupEndTagSyntax = 530,

		// Markup content nodes
		MarkupContentSyntax = 531,
		MarkupTextLiteralSyntax = 532,
		MarkupElementContentSyntax = 533,
		MarkupInlineExpressionSyntax = 534,

		// Markup attributes
		MarkupAttributeSyntax = 535,
		MarkupPlainAttributeSyntax = 536,
		MarkupAttachedPropertyAttributeSyntax = 537,
		MarkupPrefixedAttributeSyntax = 538,
		MarkupAttributeValueSyntax = 539,
		MarkupLiteralAttributeValueSyntax = 540,
		MarkupDynamicAttributeValueSyntax = 541,

		// Tailwind attributes
		TailwindAttributeSyntax = 542,
		TailwindSegmentSyntax = 543,
		TailwindIdentifierSegmentSyntax = 544,
		TailwindNumericSegmentSyntax = 545,
		TailwindExpressionSegmentSyntax = 546,
		TailwindFlagAttributeSyntax = 547,
		TailwindPrefixSegmentSyntax = 548,
		SimpleConditionalPrefixSyntax = 549,
		ExpressionConditionalPrefixSyntax = 550,
		TailwindFullAttributeSyntax = 551,

		// AKCSS root
		AkcssDocumentSyntax = 552,
		AkcssTopLevelMember = 553,
		AkcssUsingDirectiveSyntax = 554,

		// Shared body members
		AkcssBodyMemberSyntax = 555,
		AkcssAssignmentSyntax = 556,
		AkcssApplyDirectiveSyntax = 557,
		AkcssInterceptDirectiveSyntax = 558,
		AkcssIfDirectiveSyntax = 559,
		AkcssAdditionalPseudoStateSyntax = 560,
		AkcssPseudoSelectorSyntax = 561,
		AkcssPseudoBlockSyntax = 562,

		// AKCSS style rules
		AkcssStyleRuleSyntax = 563,
		AkcssStyleSelectorSyntax = 564,

		// AKCSS utilities
		AkcssUtilitiesSectionSyntax = 565,
		AkcssUtilityParameterSyntax = 566,
		AkcssUtilitySelectorSyntax = 567,
		AkcssUtilityDeclarationSyntax = 568,

		// Identifiers and types
		Type = 700,
		Name = 701,
		SimpleName = 702,
		IdentifierName = 703,

		MarkupComponentNameSyntax = 704,
		MarkupSimpleComponentNameSyntax = 705,
		MarkupQualifiedNameSyntax = 706,
		MarkupAliasQualifierSyntax = 707,
		MarkupGenericArgumentListSyntax = 708,
		MarkupQualifiedComponentNameSyntax = 709,
		MarkupNameSegmentSyntax = 710,
		MarkupIdentifierNameSegmentSyntax = 711,
		MarkupGenericNameSegmentSyntax = 712,


		// Trivia
		SingleLineCommentTrivia = 800,
		MultiLineCommentTrivia = 801,
	}
}
