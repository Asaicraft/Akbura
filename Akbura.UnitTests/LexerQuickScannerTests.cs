using Akbura.Language;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Text;

namespace Akbura.UnitTests;

#if STATS

public class LexerQuickScannerTests
{
	[Fact]
	public void TopLevelTokens_QuickScannerMatchesRegularLexer()
	{
		const string code =
			"// Counter\n" +
			"state int count = 0;\n" +
			"\n" +
			"param bind string Title = \"Hello\";\n" +
			"\n" +
			"<Stack w-full h-full items-center>\n" +
			"\t<Button Click={count++} class=\"primary\">Increment</Button>\n" +
			"\t<ui::Icon Name=\"save\" />\n" +
			"</Stack>";

		var regular = LexAll(code, Lexer.LexerMode.TopLevel, enableQuickScanner: false);
		var quick = LexAll(code, Lexer.LexerMode.TopLevel, enableQuickScanner: true);

		Assert.Equal(regular.Kinds, quick.Kinds);
		Assert.Equal(regular.Text, quick.Text);
		Assert.Equal(code, quick.Text);
		Assert.True(quick.Hits > 0);
		Assert.True(quick.Fallbacks > 0);
	}

	[Fact]
	public void AkcssTokens_QuickScannerMatchesRegularLexer()
	{
		const string code =
			"@utilities {\n" +
			"\t.w-(double width) {\n" +
			"\t\tWidth: width * Spacing;\n" +
			"\t}\n" +
			"}\n" +
			"\n" +
			".btn {\n" +
			"\t@if(IsHovered) {\n" +
			"\t\tBackground: \"Blue\";\n" +
			"\t}\n" +
			"}";

		var regular = LexAll(code, Lexer.LexerMode.InAkcss, enableQuickScanner: false);
		var quick = LexAll(code, Lexer.LexerMode.InAkcss, enableQuickScanner: true);

		Assert.Equal(regular.Kinds, quick.Kinds);
		Assert.Equal(regular.Text, quick.Text);
		Assert.Equal(code, quick.Text);
		Assert.True(quick.Hits > 0);
	}

	[Fact]
	public void ComplexTokens_FallBackToRegularLexerAndRoundTrip()
	{
		const string code =
			"@akcss { .\u043A\u043D\u043E\u043F\u043A\u0430 { Width: 1.5; } }\n" +
			"state @name = 0x10; // comment\n";

		var regular = LexAll(code, Lexer.LexerMode.TopLevel, enableQuickScanner: false);
		var quick = LexAll(code, Lexer.LexerMode.TopLevel, enableQuickScanner: true);

		Assert.Equal(regular.Kinds, quick.Kinds);
		Assert.Equal(regular.Text, quick.Text);
		Assert.Equal(code, quick.Text);
		Assert.True(quick.Hits > 0);
		Assert.True(quick.Fallbacks > 0);
	}

	[Fact]
	public void CSharpRawModes_DoNotUseQuickScanner()
	{
		using var lexer = new Lexer(
			SourceText.From("count++}"),
			enableQuickScanner: true,
			collectQuickScannerStats: true);

		var token = lexer.Lex(Lexer.LexerMode.InInlineExpression);

		Assert.Equal(SyntaxKind.CSharpRawToken, token.Kind);
		Assert.Equal(0, lexer.QuickScannerHitCount);
		Assert.Equal(0, lexer.QuickScannerFallbackCount);
	}

	[Fact]
	public void CompilationUnit_QuickScannerMatchesRegularParser()
	{
		const string code =
			"using System;\n" +
			"namespace Demo.App;\n" +
			"\n" +
			"@akcss {\n" +
			"\t.card { Padding: 12; }\n" +
			"\t@utilities { .w-(double width) { Width: width * Spacing; } }\n" +
			"}\n" +
			"\n" +
			"state bool isOpen = false;\n" +
			"state int count = 0;\n" +
			"\n" +
			"if(isOpen)\n" +
			"{\n" +
			"\tConsole.WriteLine(\"Opened\");\n" +
			"\t<TextBlock Text=\"Opened!\"/>\n" +
			"}\n" +
			"\n" +
			"<Button OnClick={count++} class=\"card\" w-30 {isOpen}:hidden>\n" +
			"\tCount: {count}\n" +
			"</Button>";

		var regular = ParseCompilationUnit(code, enableQuickScanner: false);
		var quick = ParseCompilationUnit(code, enableQuickScanner: true);

		Assert.Equal(regular.Text, quick.Text);
		Assert.Equal(code, quick.Text);
		Assert.True(quick.Hits > 0);
		Assert.True(quick.Fallbacks > 0);
	}

	private static (string Text, SyntaxKind[] Kinds, int Hits, int Fallbacks) LexAll(
		string code,
		Lexer.LexerMode mode,
		bool enableQuickScanner)
	{
		using var lexer = new Lexer(
			SourceText.From(code),
			enableQuickScanner,
			collectQuickScannerStats: true);

		var builder = new StringBuilder();
		var kinds = new List<SyntaxKind>();

		while (true)
		{
			var token = lexer.Lex(mode);
			kinds.Add(token.Kind);

			if (token.Kind == SyntaxKind.EndOfFileToken)
			{
				break;
			}

			builder.Append(token.ToFullString());
		}

		return (builder.ToString(), kinds.ToArray(), lexer.QuickScannerHitCount, lexer.QuickScannerFallbackCount);
	}

	private static (string Text, int Hits, int Fallbacks) ParseCompilationUnit(
		string code,
		bool enableQuickScanner)
	{
		using var lexer = new Lexer(
			SourceText.From(code),
			enableQuickScanner,
			collectQuickScannerStats: true);

		using var parser = new Parser(lexer, default);
		var syntax = parser.ParseCompilationUnit();

		return (syntax.ToFullString(), lexer.QuickScannerHitCount, lexer.QuickScannerFallbackCount);
	}
}
#endif