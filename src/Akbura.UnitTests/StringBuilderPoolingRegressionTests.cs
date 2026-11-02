using Akbura.Language;
using Akbura.Language.Binder;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Reflection;
using System.Globalization;
using System.Text.Json;

namespace Akbura.UnitTests;

[Collection(StringBuilderMeasurements.Name)]
public sealed class StringBuilderPoolingRegressionTests
{
    private static readonly string[] s_sources =
    [
        "using Alias = System.Collections.Generic.Dictionary<string, System.Tuple<int, string>>; <Border />",
        "<Border>$if (Call(values[2]) /* ) } */ && text != \"(\") { <TextBlock Text=\"hello\\n世界\" /> }</Border>",
        "<StackPanel>$foreach (var item in items.Where(x => x.Text != \"}\")) { <TextBlock Text={item.Text} /> }</StackPanel>",
        "<Border><TextBlock Text='a\\'b\\n世界' /> raw\r\ntext $for (var i = 0; i < 2; i++) { bad }</Border>",
        "<TextBlock Text=${local::Factory{System.Collections.Generic.List{int}}, Value=raw text, Other=${Nested}} />",
        "<Border 2xl:bg-red-500 p-4 />",
        "@using Avalonia.Controls; @utilities { Border.pad-4 { Padding: 4; @if(true) { Width: 12; } } }",
        "void Local(int value) {}\r\nCall(1);\r\n<Border />",
        "<TextBlock Text=\"unterminated\\\" />",
    ];

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void ParserAndIncrementalEdits_PreserveTextTokensAndDiagnostics(int index)
    {
        var source = s_sources[index];
        using var parser = ParserHelper.MakeParser(source);
        var original = parser.ParseCompilationUnit();
        Assert.Equal(source, original.ToFullString());
        var updated = source.Replace("Border", "Button", StringComparison.Ordinal);
        // Replace the complete text so malformed input is tested without assuming node reuse.
        using var incrementalParser = ParserHelper.MakeIncrementalParser(updated,
            (AkburaDocumentSyntax)original.CreateRed(), [new TextChangeRange(new TextSpan(0, source.Length), updated.Length)]);
        var incremental = incrementalParser.ParseCompilationUnit();
        using var fullParser = ParserHelper.MakeParser(updated);
        var full = fullParser.ParseCompilationUnit();
        Assert.Equal(updated, incremental.ToFullString());
        Assert.Equal(SerializeTree(full.CreateRed()), SerializeTree(incremental.CreateRed()));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0000123.4500")]
    [InlineData("0.000000125")]
    [InlineData("1.234567890123456789e100")]
    [InlineData("1e-400")]
    [InlineData("4.9406564584124654e-324")]
    [InlineData("1.7976931348623157e308")]
    public void FloatingPointMantissa_PreservesBits(string source)
    {
        Assert.True(RealParser.TryParseDouble(source, out var actual));
        Assert.Equal(BitConverter.DoubleToInt64Bits(double.Parse(source, CultureInfo.InvariantCulture)),
            BitConverter.DoubleToInt64Bits(actual));
    }

    [Fact]
    public void TailwindSignature_PreservesSeparatorsEmptyDisplaysAndUnicode()
    {
        var signature = typeof(MarkupBinder).GetMethod("GetTailwindCompletionSignatureKey", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<Func<TailwindUtilityLookupCandidate, string>>();
        var candidate = new TailwindUtilityLookupCandidate("имя", [
            new("first", "", false), new("second", "System.String", true), new("third", "世界\0type", false),
        ], "ignored target");
        for (var repeat = 0; repeat < 32; repeat++)
            Assert.Equal("имя\0\0System.String\0世界\0type", signature(candidate));
    }

    private static string SerializeTree(AkburaSyntax root) => JsonSerializer.Serialize(new
    {
        Text = root.ToFullString(),
        Tokens = root.DescendantTokens(descendIntoTrivia: true).Select(static token => new
        {
            token.RawKind, token.Text, token.ValueText, Full = token.ToFullString(), Span = token.Span.ToString(),
        }),
        Diagnostics = root.GetDiagnostics().Select(static diagnostic => new
        {
            diagnostic.Code, diagnostic.Severity, diagnostic.Message,
            Parameters = diagnostic.Parameters.Select(static parameter => parameter?.ToString()),
        }),
    });

}
