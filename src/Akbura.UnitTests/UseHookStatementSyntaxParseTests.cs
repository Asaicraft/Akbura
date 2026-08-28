using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Microsoft.CodeAnalysis.Text;
using static Akbura.UnitTests.ParserHelper;

namespace Akbura.UnitTests;

public sealed class UseHookStatementSyntaxParseTests
{
    [Fact]
    public void UseEffectInvocation_ParsesAsOrdinaryCSharpStatement()
    {
        const string code = "useEffect(() => { Console.WriteLine(\"render\"); });";

        using var parser = MakeParser(code);
        var root = parser.ParseCompilationUnit();

        Assert.Equal(1, root.Members.Count);
        var statement = Assert.IsType<GreenCSharpStatementSyntax>(root.Members[0]);
        Assert.Equal(code, statement.ToFullString());
        Assert.Equal(code, root.ToFullString());
    }

    [Fact]
    public void StateHookInvocation_ParsesAsStateInitializerExpression()
    {
        const string code = "state width = useAvaloniaProperty(Width);";

        using var parser = MakeParser(code);
        var root = parser.ParseCompilationUnit();

        Assert.Equal(1, root.Members.Count);
        var state = Assert.IsType<GreenStateDeclarationSyntax>(root.Members[0]);
        var initializer = Assert.IsType<GreenSimpleStateInitializerSyntax>(state.Initializer);
        Assert.Equal("useAvaloniaProperty(Width)", initializer.Expression.ToString());
        Assert.Equal(code, root.ToFullString());
    }

    [Fact]
    public void CallbackEdit_ReusesUnaffectedNeighboringMembers()
    {
        const string oldCode =
            "state int count = 0;\n" +
            "useEffect(() => Console.WriteLine(count));\n" +
            "<TextBlock Text={count} />";
        const string newCode =
            "state int count = 0;\n" +
            "useEffect(() => Console.WriteLine(count + 1));\n" +
            "<TextBlock Text={count} />";

        using var oldParser = MakeParser(oldCode);
        var oldRoot = oldParser.ParseCompilationUnit();
        var insertion = oldCode.IndexOf("count));", StringComparison.Ordinal) + "count".Length;
        var change = new TextChangeRange(new TextSpan(insertion, 0), " + 1".Length);
        var oldRedRoot = (AkburaDocumentSyntax)oldRoot.CreateRed();

        using var parser = MakeIncrementalParser(newCode, oldRedRoot, [change]);
        var newRoot = parser.ParseCompilationUnit();

        Assert.Equal(newCode, newRoot.ToFullString());
        Assert.Same(oldRoot.Members[0], newRoot.Members[0]);
        Assert.NotSame(oldRoot.Members[1], newRoot.Members[1]);
        Assert.Same(oldRoot.Members[2], newRoot.Members[2]);
    }
}
