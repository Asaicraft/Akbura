using Akbura.Language.Syntax.Green;
using static Akbura.UnitTests.ParserHelper;

namespace Akbura.UnitTests;

public class CSharpStatementSyntaxParseTests
{
	[Fact]
	public void ExpressionStatement_ParseSuccessfully()
	{
		const string code = "a = 5;";

		var parser = MakeParser(code);

		var syntax = parser.ParseCSharpStatementSyntax();

		Assert.Null(syntax.Body);
		Assert.Equal(code, syntax.ToFullString());
	}

	[Fact]
	public void IfStatementWithBlock_ParseSuccessfully()
	{
		const string code =
			"if(a == 10)\n" +
			"{\n" +
			"    a = 5;\n" +
			"}\n";

		var parser = MakeParser(code);

		var syntax = parser.ParseCSharpStatementSyntax();

		Assert.NotNull(syntax.Body);
		Assert.Equal(1, syntax.Body!.Tokens.Count);
		Assert.IsType<GreenCSharpStatementSyntax>(syntax.Body.Tokens[0]);
		Assert.Equal(code, syntax.ToFullString());
	}

	[Fact]
	public void StatementWithObjectInitializer_DoesNotTreatInitializerAsBody()
	{
		const string code = "var item = new Item { Count = 1 };";

		var parser = MakeParser(code);

		var syntax = parser.ParseCSharpStatementSyntax();

		Assert.Null(syntax.Body);
		Assert.Equal(code, syntax.ToFullString());
	}

	[Fact]
	public void CompilationUnit_WithCSharpStatementAndMarkup_ParseSuccessfully()
	{
		const string code =
			"state int a = 10;\n" +
			"\n" +
			"if(a == 10)\n" +
			"{\n" +
			"    a = 5;\n" +
			"}\n" +
			"\n" +
			"<Button OnClick={a++}>\n" +
			"   Hello a is {a}\n" +
			"</Button>";

		var parser = MakeParser(code);

		var syntax = parser.ParseCompilationUnit();

		Assert.Equal(3, syntax.Members.Count);
		Assert.IsType<GreenStateDeclarationSyntax>(syntax.Members[0]);
		var statement = Assert.IsType<GreenCSharpStatementSyntax>(syntax.Members[1]);
		Assert.NotNull(statement.Body);
		Assert.IsType<GreenMarkupRootSyntax>(syntax.Members[2]);
		Assert.Equal(code, syntax.ToFullString());
	}

	[Fact]
	public void CompilationUnit_CounterComponent_ParseSuccessfully()
	{
		const string code =
			"using System;\n" +
			"namespace Demo.Components;\n" +
			"\n" +
			"state int count = 0;\n" +
			"\n" +
			"if(count < 0)\n" +
			"{\n" +
			"    count = 0;\n" +
			"}\n" +
			"\n" +
			"<Stack gap-4>\n" +
			"    <Text>Count: {count}</Text>\n" +
			"    <Button OnClick={count++}>Increment</Button>\n" +
			"</Stack>";

		var parser = MakeParser(code);

		var syntax = parser.ParseCompilationUnit();

		Assert.Equal(5, syntax.Members.Count);
		Assert.IsType<GreenUsingDirectiveSyntax>(syntax.Members[0]);
		Assert.IsType<GreenNamespaceDeclarationSyntax>(syntax.Members[1]);
		Assert.IsType<GreenStateDeclarationSyntax>(syntax.Members[2]);

		var guardStatement = Assert.IsType<GreenCSharpStatementSyntax>(syntax.Members[3]);
		Assert.Equal("\nif(count < 0)\n", guardStatement.Tokens.ToFullString());
		Assert.NotNull(guardStatement.Body);
		Assert.Equal(1, guardStatement.Body!.Tokens.Count);
		Assert.IsType<GreenCSharpStatementSyntax>(guardStatement.Body.Tokens[0]);

		var markup = Assert.IsType<GreenMarkupRootSyntax>(syntax.Members[4]);
		Assert.Equal("Stack", markup.Element.StartTag!.Name.ToString());
		Assert.Equal(1, markup.Element.StartTag.Attributes.Count);
		Assert.IsType<GreenTailwindFullAttributeSyntax>(markup.Element.StartTag.Attributes[0]);
		Assert.Equal(code, syntax.ToFullString());
	}
}
