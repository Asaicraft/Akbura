using Akbura.Language.Syntax.Green;
using static Akbura.UnitTests.ParserHelper;

namespace Akbura.UnitTests;

public class FullComponentSyntaxParseTests
{
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
		Assert.Equal(code, syntax.ToFullString());
	}
}
