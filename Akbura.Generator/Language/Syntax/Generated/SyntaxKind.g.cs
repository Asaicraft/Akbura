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
		SuppressKeyword = 103,
		FinallyKeyword = 104,
		AsyncKeyword = 105,
		VoidKeyword = 106,
		CommandKeyword = 107,

		NewKeyword = 108,
		ReactListKeyword = 109,

		IfKeyword = 110,
		ElseKeyword = 111,
		ReturnKeyword = 112,
		ForKeyword = 113,

		TrueKeyword = 114,
		FalseKeyword = 115,
		NullKeyword = 116,

		PlusToken = 117,
		MinusToken = 118,
		AsteriskToken = 119,
		SlashToken = 120,
		PercentToken = 121,
		CaretToken = 122,
		BarToken = 123,
		AmpersandToken = 124,
		QuestionToken = 125,
		ColonToken = 126,
		SemicolonToken = 127,
		CommaToken = 128,
		DotToken = 129,
		DoubleDotToken = 130,
		EqualsToken = 131,
		BangToken = 132,
		EqualsEqualsToken = 133,
		BangEqualsToken = 134,
		GreaterThanToken = 135,
		LessThanToken = 136,
		GreaterEqualsToken = 137,
		LessEqualsToken = 138,
		ArrowToken = 139,
		HashToken = 140,

		OpenBraceToken = 141,
		CloseBraceToken = 142,
		OpenBracketToken = 143,
		CloseBracketToken = 144,
		OpenParenToken = 145,
		CloseParenToken = 146,
		DoubleColonToken = 147,

		LessSlashToken = 148,
		SlashGreaterToken = 149,

		SingleQuoteToken = 150,
		DoubleQuoteToken = 151,

		AtToken = 152,
		BindToken = 153,
		InToken = 154,
		OutToken = 155,
		UsingKeyword = 156,
		NamespaceKeyword = 157,
		GlobalKeyword = 158,
		StaticKeyword = 159,
		UnsafeKeyword = 160,

		UtilitiesKeyword = 161,
		AkcssKeyword = 162,
		ApplyKeyword = 163,
		InterceptKeyword = 164,
		DollarToken = 165,

		LastTokenWithWellKnownText = DollarToken,

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

		CommandDeclarationSyntax = 517,

		// Function Declarations
		//FunctionDeclarationSyntax = 518,
		CSharpParameterListSyntax = 518,
		CSharpArgumentListSyntax = 519,

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
		MarkupAttachedPropertyAttributeSyntax = 532,
		MarkupPrefixedAttributeSyntax = 533,
		MarkupAttributeValueSyntax = 534,
		MarkupLiteralAttributeValueSyntax = 535,
		MarkupDynamicAttributeValueSyntax = 536,
		MarkupExtensionAttributeValueSyntax = 537,
		MarkupExtensionSyntax = 538,
		MarkupExtensionTypeSyntax = 539,
		MarkupExtensionArgumentSyntax = 540,
		MarkupExtensionPositionalArgumentSyntax = 541,
		MarkupExtensionPropertyArgumentSyntax = 542,
		MarkupExtensionValueSyntax = 543,
		MarkupExtensionLiteralValueSyntax = 544,
		MarkupExtensionExpressionValueSyntax = 545,
		MarkupExtensionNestedValueSyntax = 546,

		// Tailwind attributes
		TailwindAttributeSyntax = 547,
		TailwindSegmentSyntax = 548,
		TailwindIdentifierSegmentSyntax = 549,
		TailwindNumericSegmentSyntax = 550,
		TailwindExpressionSegmentSyntax = 551,
		TailwindFlagAttributeSyntax = 552,
		TailwindPrefixSegmentSyntax = 553,
		SimpleConditionalPrefixSyntax = 554,
		ExpressionConditionalPrefixSyntax = 555,
		TailwindFullAttributeSyntax = 556,

		// AKCSS root
		AkcssDocumentSyntax = 557,
		AkcssTopLevelMember = 558,
		AkcssUsingDirectiveSyntax = 559,

		// Shared body members
		AkcssBodyMemberSyntax = 560,
		AkcssAssignmentSyntax = 561,
		AkcssApplyDirectiveSyntax = 562,
		AkcssInterceptDirectiveSyntax = 563,
		AkcssIfDirectiveSyntax = 564,
		AkcssAdditionalPseudoStateSyntax = 565,
		AkcssPseudoSelectorSyntax = 566,
		AkcssPseudoBlockSyntax = 567,

		// AKCSS style rules
		AkcssStyleRuleSyntax = 568,
		AkcssStyleSelectorSyntax = 569,

		// AKCSS utilities
		AkcssUtilitiesSectionSyntax = 570,
		AkcssUtilityParameterSyntax = 571,
		AkcssUtilitySelectorSyntax = 572,
		AkcssUtilityDeclarationSyntax = 573,

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
