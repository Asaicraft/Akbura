using Akbura.Language;
using Akbura.Language.Binder;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Reflection;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Location = Microsoft.CodeAnalysis.Location;

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

    [Fact]
    public void CompilerSnapshots_MatchPrePoolingImplementation()
    {
        var expected = new Dictionary<string, string>
        {
            // SHA-256 snapshots captured from commit 871bb441, including generated text and diagnostics.
            ["parser-0"] = "B4255E242870A1C4915E4EF0F8AAD3A64837C91954568FC5C87C75A5B42AA383",
            ["parser-1"] = "D5C008F8ED18641737A5ABB5421C5BE0A887FA8941D4E67470B49E029C7DD8B6",
            ["parser-2"] = "085122B9B9AFBF6DFF90760A86F0390A177AC7B46471028B674C04285A84E0C5",
            ["parser-3"] = "1C9ADF13218FAD7F2B53D2D6BC44E5AC93AA14ED7B4A9EBC47BF98E2B76C7ACE",
            ["parser-4"] = "39B0A9C382151EB8A4A7FD4B80744B45317E8CE1B24A24FCAC03FEBF5284AC8C",
            ["parser-5"] = "3A9448CDBF9543F26AD1A58ABE508357B7B123A0074086027EDB2FBBC788A059",
            ["parser-6"] = "8CA249D21B5F3FC858DE737368C4A088EBB1A170AB88779AC51E3740C403DCD1",
            ["parser-7"] = "27451EA087282E3F65B377B12273D292718D0FEF40D89ADD2FB930A6ADA7418F",
            ["parser-8"] = "19CA22FA16C546AF8F474D0DE58F26834E1A98EC3CACA0DC81F7E0AC530CDBE7",
            ["whitespace"] = "D30551F20219F2BB0DBCAAA9273C21DDC680D8F6B8CDBFF4A1DBC8196196195C",
            ["identifiers"] = "485C53468CB70BD3765045DFD6E286317C2D4FB3FAE15CD8089DBA4FE0A2A871",
            ["fingerprints"] = "281B48538C2C82F42CDF80AE7FD6E151E993FD4FA82A3F301EE1ACE6665379E6",
            ["diagnostics"] = "C974A86946AA3A5E0011BC063F3CA0A2292DB814E215DF43EA6FC70331EBFBB4",
            ["binding-diagnostics"] = "C974A86946AA3A5E0011BC063F3CA0A2292DB814E215DF43EA6FC70331EBFBB4",
        };
        var actual = CaptureSnapshots();
        Assert.Equal(expected.Count, actual.Count);
        foreach (var pair in expected) Assert.Equal(pair.Value, actual[pair.Key]);
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

    private static Dictionary<string, string> CaptureSnapshots()
    {
        var snapshots = new Dictionary<string, string>();
        void Add(string name, string value) => snapshots.Add(name, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))));
        for (var index = 0; index < s_sources.Length; index++)
        {
            using var parser = ParserHelper.MakeParser(s_sources[index]);
            Add("parser-" + index, SerializeTree(parser.ParseCompilationUnit().CreateRed()));
        }

        var normalize = typeof(AkburaSemanticModel).GetMethod("NormalizeTextFragment", BindingFlags.Static | BindingFlags.NonPublic)!;
        var fragments = new[] { "", "  ", "a", " a  b\r\n", "\t世界\0\u00a0 e\u0301\n", new string('x', 4096) + "\tend" };
        var normalized = new List<string>();
        foreach (var mode in new[] { MarkupWhitespaceMode.Default, MarkupWhitespaceMode.Preserve })
        foreach (var start in new[] { false, true })
        foreach (var end in new[] { false, true })
        foreach (var text in fragments)
            normalized.Add((string)normalize.Invoke(null, [text, mode, start, end])!);
        Add("whitespace", JsonSerializer.Serialize(normalized));

        var names = new[] { "", "9invalid-name.世界", "valid_Name", "\0a\r\nb", new string('x', 2048) };
        Add("identifiers", JsonSerializer.Serialize(names.Select(name => ComponentHotReloadIdentity.CreateGeneratedName(name, "state:" + name))));
        Add("fingerprints", JsonSerializer.Serialize(names.Select(name => AkcssHotReloadIdentity.Create("void Apply() { target.Text = " +
            Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(name, true) + "; }"))));

        var bag = new DiagnosticBag();
        Assert.Equal("<no diagnostics>", bag.ToString());
        var descriptor = new DiagnosticDescriptor("POOL001", "title", "message {0}", "tests", DiagnosticSeverity.Warning, true);
        bag.Add(Diagnostic.Create(descriptor, Location.None, "first\0世界"));
        bag.Add(Diagnostic.Create(descriptor, Location.None, "second\r\nline"));
        Add("diagnostics", bag.ToString());
        var binding = BindingDiagnosticBag.GetInstance();
        try
        {
            binding.DiagnosticBag!.AddRange(bag.ToReadOnly());
            Add("binding-diagnostics", binding.ToString());
        }
        finally { binding.Free(); }

        return snapshots;
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
