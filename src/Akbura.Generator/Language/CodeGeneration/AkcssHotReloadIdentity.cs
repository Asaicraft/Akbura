using Microsoft.CodeAnalysis.CSharp;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Fingerprints emitted styling behavior without comments, whitespace or source mappings.
/// Expanded apply operations are emitted before fingerprinting, including transitive dependencies.
/// </summary>
internal static class AkcssHotReloadIdentity
{
    public static string Create(string generatedCode)
    {
        var syntax = CSharpSyntaxTree.ParseText(generatedCode,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview)).GetRoot();
        var builder = new StringBuilder();
        foreach (var token in syntax.DescendantTokens(descendIntoTrivia: false))
        {
            builder.Append(token.RawKind.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(token.ValueText.Length.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(token.ValueText);
            builder.Append(';');
        }

        using var algorithm = SHA256.Create();
        var hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString()));
        builder.Clear();
        foreach (var value in hash)
        {
            builder.Append(value.ToString("X2", CultureInfo.InvariantCulture));
        }
        return builder.ToString();
    }
}
