using Akbura.Language.Syntax.Green;
using static Akbura.UnitTests.ParserHelper;

namespace Akbura.UnitTests;

public class FullComponentSyntaxParseTests
{
	[Fact]
	public void Readme_GettingStartedCounter_ParseSuccessfully()
	{
		const string code =
			"// Counter.akbura\n" +
			"\n" +
			"state int count = 0;\n" +
			"\n" +
			"<Stack w-full h-full items-center>\n" +
			"\t<Text FontSize=\"24\">Count: {count}</Text>\n" +
			"\t<Button Click={count++}>Increment</Button>\n" +
			"</Stack>";

		var syntax = ParseCompilationUnitAndRoundTrip(code);

		Assert.Equal(2, syntax.Members.Count);
		Assert.IsType<GreenStateDeclarationSyntax>(syntax.Members[0]);
		var markup = Assert.IsType<GreenMarkupRootSyntax>(syntax.Members[1]);
		Assert.Equal("Stack", markup.Element.StartTag!.Name.ToFullString().Trim());
		Assert.Equal(3, markup.Element.StartTag.Attributes.Count);
	}

	[Fact]
	public void Readme_ConditionalRenderingExample_ParseSuccessfully()
	{
		const string code =
			"state isOpen = false;\n" +
			"\n" +
			"if(isOpen)\n" +
			"{\n" +
			"\tConsole.WriteLine(\"Panel is open\");\n" +
			"\n" +
			"\t<TextBlock Text=\"Opened!\"/>\n" +
			"}\n" +
			"\n" +
			"<Button OnClick={isOpen = true}>Open</Button>";

		var syntax = ParseCompilationUnitAndRoundTrip(code);

		Assert.Equal(3, syntax.Members.Count);
		Assert.IsType<GreenStateDeclarationSyntax>(syntax.Members[0]);
		var conditional = Assert.IsType<GreenCSharpStatementSyntax>(syntax.Members[1]);
		Assert.NotNull(conditional.Body);
		Assert.Equal(2, conditional.Body!.Tokens.Count);
		Assert.IsType<GreenCSharpStatementSyntax>(conditional.Body.Tokens[0]);
		Assert.IsType<GreenMarkupRootSyntax>(conditional.Body.Tokens[1]);
		Assert.IsType<GreenMarkupRootSyntax>(syntax.Members[2]);
	}

	[Fact]
	public void Readme_VisibilityPatternExample_ParseSuccessfully()
	{
		const string code =
			"state isBox1Visible = true;\n" +
			"state isBox2Visible = false;\n" +
			"\n" +
			"<StackPanel>\n" +
			"\t<Border class=\"box\" IsVisible={isBox1Visible}/>\n" +
			"\t<Border class=\"box\" {!isBox2Visible}:hidden/>\n" +
			"</StackPanel>";

		var syntax = ParseCompilationUnitAndRoundTrip(code);

		Assert.Equal(3, syntax.Members.Count);
		Assert.IsType<GreenStateDeclarationSyntax>(syntax.Members[0]);
		Assert.IsType<GreenStateDeclarationSyntax>(syntax.Members[1]);
		var markup = Assert.IsType<GreenMarkupRootSyntax>(syntax.Members[2]);
		Assert.Equal("StackPanel", markup.Element.StartTag!.Name.ToFullString().Trim());
		Assert.Equal(2, markup.Element.Body.Count);
	}

	[Fact]
	public void Readme_DependencyInjectionExample_ParseSuccessfully()
	{
		const string code =
			"inject ILogger<MyComponent> logger;\n" +
			"\n" +
			"useEffect() {\n" +
			"\tlogger.LogInformation(\"MyComponent mounted\");\n" +
			"}\n" +
			"\n" +
			"<Text>\n" +
			"\tHello world!\n" +
			"\tILogger hashcode: {logger.GetHashCode()}\n" +
			"</Text>";

		var syntax = ParseCompilationUnitAndRoundTrip(code);

		Assert.Equal(3, syntax.Members.Count);
		Assert.IsType<GreenInjectDeclarationSyntax>(syntax.Members[0]);
		Assert.IsType<GreenUseEffectDeclarationSyntax>(syntax.Members[1]);
		Assert.IsType<GreenMarkupRootSyntax>(syntax.Members[2]);
	}

	[Fact]
	public void Readme_CommandDeclarationExample_ParseSuccessfully()
	{
		const string code =
			"command int CustomClick(int a);\n" +
			"\n" +
			"state int clicked = 0;\n" +
			"\n" +
			"useEffect(CustomClick.IsExecuting) {\n" +
			"\tConsole.WriteLine(\"Command is executing\");\n" +
			"}\n" +
			"\n" +
			"<Block p-4 {CustomClick.IsExecuting}:disabled>\n" +
			"\t<Button Click={() => {\n" +
			"\t\tvar result = await CustomClick.Execute(clicked++);\n" +
			"\t\tConsole.WriteLine($\"Result is {result}\");\n" +
			"\t}}/>\n" +
			"</Block>";

		var parser = MakeParser(code);

		var syntax = parser.ParseCompilationUnit();

		Assert.Equal(4, syntax.Members.Count);
		Assert.IsType<GreenCommandDeclarationSyntax>(syntax.Members[0]);
		Assert.IsType<GreenStateDeclarationSyntax>(syntax.Members[1]);
		Assert.IsType<GreenUseEffectDeclarationSyntax>(syntax.Members[2]);
		Assert.IsType<GreenMarkupRootSyntax>(syntax.Members[3]);
		Assert.Equal(code.Length, syntax.FullWidth);
		Assert.Equal(code, syntax.ToFullString());
	}

	[Fact]
	public void Readme_AkcssBasicExample_ParseSuccessfully()
	{
		const string code =
			".myclass {\n" +
			"\tBackground: \"Red\";\n" +
			"\n" +
			"\t@if(IsHovered) {\n" +
			"\t\tBackground: \"Blue\";\n" +
			"\t}\n" +
			"\n" +
			"\t@if(this == Button) {\n" +
			"\t\tPadding: 10;\n" +
			"\t}\n" +
			"\n" +
			"\t@if(this is Button) {\n" +
			"\t\tPadding: 5;\n" +
			"\t}\n" +
			"}";

		var syntax = ParseAkcssDocumentAndRoundTrip(code);

		Assert.Equal(1, syntax.Members.Count);
		var rule = Assert.IsType<GreenAkcssStyleRuleSyntax>(syntax.Members[0]);
		Assert.Equal(4, rule.Members.Count);
		Assert.IsType<GreenAkcssAssignmentSyntax>(rule.Members[0]);
		Assert.IsType<GreenAkcssIfDirectiveSyntax>(rule.Members[1]);
		Assert.IsType<GreenAkcssIfDirectiveSyntax>(rule.Members[2]);
		Assert.IsType<GreenAkcssIfDirectiveSyntax>(rule.Members[3]);
	}

	[Fact]
	public void Readme_AkcssUtilitiesExample_ParseSuccessfully()
	{
		const string code =
			"@utilities {\n" +
			"\t.rounded {\n" +
			"\t\tCornerRadius: 4;\n" +
			"\t}\n" +
			"\n" +
			"\t.w-(double width) {\n" +
			"\t\tWidth: width * MyNamespace.MyStaticClass.Spacing;\n" +
			"\t}\n" +
			"\n" +
			"\t.space-(int x)-(int y) {\n" +
			"\t\tMarginLeft: x * MyNamespace.MyStaticClass.Spacing;\n" +
			"\t\tMarginTop:  y * MyNamespace.MyStaticClass.Spacing;\n" +
			"\n" +
			"\t\t@if(x > y) {\n" +
			"\t\t\tBorderThickness: x - y;\n" +
			"\t\t}\n" +
			"\t}\n" +
			"}";

		var syntax = ParseAkcssDocumentAndRoundTrip(code);

		Assert.Equal(1, syntax.Members.Count);
		var utilities = Assert.IsType<GreenAkcssUtilitiesSectionSyntax>(syntax.Members[0]);
		Assert.Equal(3, utilities.Utilities.Count);
	}

	[Fact]
	public void Readme_AkcssUsageExamples_ParseSuccessfully()
	{
		const string code =
			"<Button class=\"myclass\" />\n" +
			"<Block w-30 space-2-4 rounded />\n" +
			"state bool isMobile = false;\n" +
			"\n" +
			"<Box w-30 {isMobile}:w-20>";

		var syntax = ParseCompilationUnitAndRoundTrip(code);

		Assert.Equal(4, syntax.Members.Count);
		Assert.IsType<GreenMarkupRootSyntax>(syntax.Members[0]);
		Assert.IsType<GreenMarkupRootSyntax>(syntax.Members[1]);
		Assert.IsType<GreenStateDeclarationSyntax>(syntax.Members[2]);
		Assert.IsType<GreenMarkupRootSyntax>(syntax.Members[3]);
	}

	[Fact]
	public void CompilationUnit_RealisticComponentWithInlineAkcssUtilitiesAndConditionalBlock_ParseSuccessfully()
	{
		const string code =
			"using System;\n" +
			"global using static System.Math;\n" +
			"namespace Demo.App;\n" +
			"\n" +
			"@akcss {\n" +
			"    .card {\n" +
			"        Padding: 12;\n" +
			"\n" +
			"        @if(IsHovered) {\n" +
			"            Background: \"AliceBlue\";\n" +
			"        }\n" +
			"    }\n" +
			"\n" +
			"    @utilities {\n" +
			"        .gap-(double value) {\n" +
			"            RowGap: value * Spacing;\n" +
			"        }\n" +
			"\n" +
			"        .accent {\n" +
			"            BorderBrush: \"DodgerBlue\";\n" +
			"        }\n" +
			"    }\n" +
			"}\n" +
			"\n" +
			"inject ILogger<DashboardPage> log;\n" +
			"inject DashboardViewModel viewModel;\n" +
			"\n" +
			"param int UserId = 1;\n" +
			"param bind string Search = \"\";\n" +
			"param out SelectedTask;\n" +
			"\n" +
			"state bool isOpen = false;\n" +
			"state bool isBusy = bind viewModel.IsBusy;\n" +
			"state ReactList tasks = bind viewModel.Tasks;\n" +
			"\n" +
			"useEffect(UserId, Search) {\n" +
			"    log.LogInformation(\"Loading user\");\n" +
			"\n" +
			"    if(UserId < 0) {\n" +
			"        return;\n" +
			"    }\n" +
			"}\n" +
			"cancel {\n" +
			"    log.LogInformation(\"Cancelled\");\n" +
			"}\n" +
			"finally {\n" +
			"    log.LogInformation(\"Done\");\n" +
			"}\n" +
			"\n" +
			"command Task Refresh(int userId);\n" +
			"\n" +
			"if(isOpen)\n" +
			"{\n" +
			"    Console.WriteLine(\"Panel opened\");\n" +
			"\n" +
			"    <TextBlock Text=\"Opened!\" class=\"status\" />\n" +
			"}\n" +
			"\n" +
			"<StackPanel class=\"card\" gap-4 p-4 {isBusy}:opacity-50>\n" +
			"    <TextBlock Text=\"Dashboard\" class=\"title\"/>\n" +
			"    <Input bind:Value={Search} Placeholder=\"Search tasks\"/>\n" +
			"    <Button OnClick={isOpen = true} class=\"primary\" w-30>\n" +
			"        Open\n" +
			"    </Button>\n" +
			"    <TaskList Items={tasks} out:Selected={SelectedTask}/>\n" +
			"    <Border class=\"box\" IsVisible={isOpen}/>\n" +
			"    <Border class=\"box\" {!isOpen}:hidden/>\n" +
			"</StackPanel>";

		var parser = MakeParser(code);

		var syntax = parser.ParseCompilationUnit();

		Assert.Equal(16, syntax.Members.Count);
		Assert.IsType<GreenUsingDirectiveSyntax>(syntax.Members[0]);
		Assert.IsType<GreenUsingDirectiveSyntax>(syntax.Members[1]);
		Assert.IsType<GreenNamespaceDeclarationSyntax>(syntax.Members[2]);

		var akcss = Assert.IsType<GreenInlineAkcssBlockSyntax>(syntax.Members[3]);
		Assert.Equal(2, akcss.Members.Count);
		Assert.IsType<GreenAkcssStyleRuleSyntax>(akcss.Members[0]);
		var utilities = Assert.IsType<GreenAkcssUtilitiesSectionSyntax>(akcss.Members[1]);
		Assert.Equal(2, utilities.Utilities.Count);

		Assert.IsType<GreenInjectDeclarationSyntax>(syntax.Members[4]);
		Assert.IsType<GreenInjectDeclarationSyntax>(syntax.Members[5]);
		Assert.IsType<GreenParamDeclarationSyntax>(syntax.Members[6]);
		Assert.IsType<GreenParamDeclarationSyntax>(syntax.Members[7]);
		Assert.IsType<GreenParamDeclarationSyntax>(syntax.Members[8]);
		Assert.IsType<GreenStateDeclarationSyntax>(syntax.Members[9]);
		Assert.IsType<GreenStateDeclarationSyntax>(syntax.Members[10]);
		Assert.IsType<GreenStateDeclarationSyntax>(syntax.Members[11]);
		Assert.IsType<GreenUseEffectDeclarationSyntax>(syntax.Members[12]);
		Assert.IsType<GreenCommandDeclarationSyntax>(syntax.Members[13]);

		var conditional = Assert.IsType<GreenCSharpStatementSyntax>(syntax.Members[14]);
		Assert.NotNull(conditional.Body);
		Assert.Equal(2, conditional.Body!.Tokens.Count);
		Assert.IsType<GreenCSharpStatementSyntax>(conditional.Body.Tokens[0]);
		Assert.IsType<GreenMarkupRootSyntax>(conditional.Body.Tokens[1]);

		var markup = Assert.IsType<GreenMarkupRootSyntax>(syntax.Members[15]);
		Assert.Equal("StackPanel", markup.Element.StartTag!.Name.ToFullString().Trim());
		Assert.Equal(4, markup.Element.StartTag.Attributes.Count);
		Assert.Equal(6, markup.Element.Body.Count);
		Assert.Equal(code.Length, syntax.FullWidth);
		Assert.Equal(code, syntax.ToFullString());
	}

	private static GreenAkburaDocumentSyntax ParseCompilationUnitAndRoundTrip(string code)
	{
		var parser = MakeParser(code);
		var syntax = parser.ParseCompilationUnit();

		Assert.Equal(code.Length, syntax.FullWidth);
		Assert.Equal(code, syntax.ToFullString());

		return syntax;
	}

	private static GreenAkcssDocumentSyntax ParseAkcssDocumentAndRoundTrip(string code)
	{
		var parser = MakeParser(code);
		var syntax = parser.ParseAkcssDocumentSyntax();

		Assert.Equal(code.Length, syntax.FullWidth);
		Assert.Equal(code, syntax.ToFullString());

		return syntax;
	}
}
