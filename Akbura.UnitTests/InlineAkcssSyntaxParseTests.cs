using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using static Akbura.UnitTests.ParserHelper;

namespace Akbura.UnitTests;

public class InlineAkcssSyntaxParseTests
{
	[Fact]
	public void AkcssStyleRule_ParseSuccessfully()
	{
		const string code = ".btn { Background: \"Red\"; }";

		var parser = MakeParser(code);

		var syntax = parser.ParseAkcssStyleRuleSyntax();

		Assert.Null(syntax.Selector.TargetType);
		Assert.Equal("btn", syntax.Selector.Name.Identifier.ValueText);
		Assert.Equal(1, syntax.Members.Count);

		var assignment = Assert.IsType<GreenAkcssAssignmentSyntax>(syntax.Members[0]);
		Assert.Equal("Background", assignment.PropertyName.Identifier.ValueText);
		Assert.False(assignment.Colon.IsMissing);
		Assert.NotNull(assignment.Semicolon);
		Assert.Equal(code, syntax.ToFullString());
	}

	[Fact]
	public void AkcssStyleRule_WithIfDirective_ParseSuccessfully()
	{
		const string code = "Button.btn { @if(IsHovered) { Background: \"Blue\"; } }";

		var parser = MakeParser(code);

		var syntax = parser.ParseAkcssStyleRuleSyntax();

		Assert.NotNull(syntax.Selector.TargetType);
		Assert.Equal("Button", syntax.Selector.TargetType!.Identifier.ValueText);
		Assert.Equal("btn", syntax.Selector.Name.Identifier.ValueText);

		var ifDirective = Assert.IsType<GreenAkcssIfDirectiveSyntax>(syntax.Members[0]);
		Assert.Equal("IsHovered", ifDirective.Condition.ToFullString());
		Assert.Equal(1, ifDirective.Members.Count);
		Assert.IsType<GreenAkcssAssignmentSyntax>(ifDirective.Members[0]);
		Assert.Equal(code, syntax.ToFullString());
	}

	[Fact]
	public void AkcssUtilitiesSection_ParseSuccessfully()
	{
		const string code = "@utilities { .w-(double width) { Width: width * Spacing; } }";

		var parser = MakeParser(code);

		var syntax = parser.ParseAkcssUtilitiesSectionSyntax();

		Assert.Equal(1, syntax.Utilities.Count);
		var utility = syntax.Utilities[0]!;
		Assert.Equal("w", utility.Selector.Name.Identifier.ValueText);
		Assert.Equal(1, utility.Selector.Parameters.Count);
		Assert.Equal(1, utility.Members.Count);
		Assert.Equal(code, syntax.ToFullString());
	}

	[Fact]
	public void InlineAkcssBlock_ParseSuccessfully()
	{
		const string code = "@akcss { .btn { Background: \"Red\"; } }";

		var parser = MakeParser(code);

		var syntax = parser.ParseInlineAkcssBlockSyntax();

		Assert.Equal(SyntaxKind.AtToken, syntax.AtToken.Kind);
		Assert.Equal(SyntaxKind.AkcssKeyword, syntax.AkcssKeyword.Kind);
		Assert.Equal(1, syntax.Members.Count);
		Assert.IsType<GreenAkcssStyleRuleSyntax>(syntax.Members[0]);
		Assert.Equal(code, syntax.ToFullString());
	}

	[Fact]
	public void CompilationUnit_WithInlineAkcssStateAndMarkup_RemainsFlat()
	{
		const string code =
			"@akcss {\n" +
			"    .btn { Background: \"Red\"; }\n" +
			"}\n" +
			"\n" +
			"state count = 0;\n" +
			"\n" +
			"<Button Click={count++} class=\"btn\">\n" +
			"\t{count}\n" +
			"</Button>";

		var parser = MakeParser(code);

		var syntax = parser.ParseCompilationUnit();

		Assert.Equal(3, syntax.Members.Count);
		var akcss = Assert.IsType<GreenInlineAkcssBlockSyntax>(syntax.Members[0]);
		Assert.IsType<GreenAkcssStyleRuleSyntax>(akcss.Members[0]);
		Assert.IsType<GreenStateDeclarationSyntax>(syntax.Members[1]);

		var markup = Assert.IsType<GreenMarkupRootSyntax>(syntax.Members[2]);
		Assert.Equal("Button", markup.Element.StartTag!.Name.ToString());
		Assert.Equal(2, markup.Element.StartTag.Attributes.Count);
		Assert.IsType<GreenMarkupPlainAttributeSyntax>(markup.Element.StartTag.Attributes[1]);
		Assert.Equal(code, syntax.ToFullString());
	}

	[Fact]
	public void CompilationUnit_VerbatimAkcssIdentifier_StaysCSharpStatement()
	{
		const string code = "@akcss = 1;";

		var parser = MakeParser(code);

		var syntax = parser.ParseCompilationUnit();

		Assert.Equal(1, syntax.Members.Count);
		Assert.IsType<GreenCSharpStatementSyntax>(syntax.Members[0]);
		Assert.Equal(code, syntax.ToFullString());
	}

	[Fact]
	public void InlineAkcssBlock_MissingOpenBrace_Recovers()
	{
		const string code = "@akcss .btn { Background: \"Red\"; }";

		var parser = MakeParser(code);

		var syntax = parser.ParseInlineAkcssBlockSyntax();

		Assert.True(syntax.OpenBrace.IsMissing);
		Assert.True(syntax.CloseBrace.IsMissing);
		Assert.Equal(1, syntax.Members.Count);
		Assert.Equal(code, syntax.ToFullString());
	}

	[Fact]
	public void InlineAkcssBlock_MissingCloseBrace_Recovers()
	{
		const string code = "@akcss { .btn { Background: \"Red\"; }";

		var parser = MakeParser(code);

		var syntax = parser.ParseInlineAkcssBlockSyntax();

		Assert.False(syntax.OpenBrace.IsMissing);
		Assert.True(syntax.CloseBrace.IsMissing);
		Assert.Equal(1, syntax.Members.Count);
		Assert.Equal(code, syntax.ToFullString());
	}

	[Fact]
	public void InlineAkcssBlock_MalformedChildMember_Recovers()
	{
		const string code = "@akcss { .btn { Background \"Red\"; } }";

		var parser = MakeParser(code);

		var syntax = parser.ParseInlineAkcssBlockSyntax();

		var rule = Assert.IsType<GreenAkcssStyleRuleSyntax>(syntax.Members[0]);
		var assignment = Assert.IsType<GreenAkcssAssignmentSyntax>(rule.Members[0]);
		Assert.True(assignment.Colon.IsMissing);
		Assert.NotNull(assignment.Semicolon);
		Assert.Equal(code, syntax.ToFullString());
	}
}
