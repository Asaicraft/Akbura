using Akbura.Language.Symbols;
using Microsoft.CodeAnalysis;
using System.Diagnostics;
using CSharpSyntaxFacts = Microsoft.CodeAnalysis.CSharp.SyntaxFacts;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;
using RoslynSymbol = Microsoft.CodeAnalysis.ISymbol;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Writes AKCSS declaration metadata that does not require operation lowering.
/// </summary>
internal readonly ref struct AkcssDeclarationMetadataWriter
{
    private const string AkcssModuleAttribute =
        "global::Akbura.CompilerAnotations.AkcssModuleAttribute";

    private const string AkcssSymbolAttribute =
        "global::Akbura.CompilerAnotations.AkcssSymbolAttribute";

    private const string AkcssSymbolKind =
        "global::Akbura.CompilerAnotations.AkcssSymbolKind";

    private const string AkcssUtilityParameterAttribute =
        "global::Akbura.CompilerAnotations.AkcssUtilityParameterAttribute";

    private readonly CodeWriter _writer;
    private readonly CSharpValueWriter _valueWriter;

    public AkcssDeclarationMetadataWriter(CodeWriter writer)
    {
        Debug.Assert(writer != null);

        _writer = writer!;
        _valueWriter = new CSharpValueWriter(_writer);
    }

    public void WriteHiddenApiAttributes()
    {
        _writer.WriteLine(
            "[global::System.ComponentModel.EditorBrowsableAttribute(" +
            "global::System.ComponentModel.EditorBrowsableState.Never)]");

        _writer.WriteLine(
            "[global::System.ComponentModel.BrowsableAttribute(false)]");
    }

    public void WriteCompilerGeneratedAttribute()
    {
        _writer.WriteLine(
            "[global::System.Runtime.CompilerServices.CompilerGeneratedAttribute]");
    }

    public void WriteModuleAttribute(in AkcssModulePlan plan)
    {
        _writer.Write("[");
        _writer.Write(AkcssModuleAttribute);
        _writer.Write("(");
        _writer.WriteStringLiteral(plan.SourcePath);
        _writer.Write(", MetadataName = ");
        _writer.WriteStringLiteral(plan.MetadataName);
        _writer.WriteLine(", FormatVersion = 4)]");
    }

    public void WriteSymbolAttributes(in AkcssSymbolGenerationPlan plan)
    {
        WriteSymbolAttribute(plan);

        if (plan.Symbol is ITailwindUtilitySymbol utility)
        {
            WriteUtilityParameterAttributes(utility);
        }
    }

    private void WriteSymbolAttribute(in AkcssSymbolGenerationPlan plan)
    {
        var symbol = plan.Symbol;
        var targetType = symbol.TargetType.Symbol as ITypeSymbol;
        var interceptType = symbol.InterceptType.Symbol as ITypeSymbol;
        var hasClassName = symbol.ClassName != null;

        var argumentCount = 4;

        if (targetType != null)
        {
            argumentCount++;
        }

        if (interceptType != null)
        {
            argumentCount++;
        }

        if (hasClassName)
        {
            argumentCount++;
        }

        if (plan.HasErrors)
        {
            argumentCount++;
        }

        _writer.Write("[");
        _writer.Write(AkcssSymbolAttribute);
        _writer.WriteLine("(");
        _writer.CurrentIndent += _writer.TabSize;

        WriteStringArgument("Name", symbol.Name, ref argumentCount);
        WriteStringArgument("MetadataName", symbol.MetadataName, ref argumentCount);

        _writer.Write("Kind = ");
        _writer.Write(AkcssSymbolKind);
        _writer.Write(".");
        _writer.Write(GetSymbolKindName(plan.Kind));
        WriteArgumentEnd(ref argumentCount);

        WriteIntegerArgument("RuntimeStyleIndex", plan.RuntimeStyleIndex, ref argumentCount);

        if (targetType != null)
        {
            WriteTypeArgument("TargetType", targetType, ref argumentCount);
        }

        if (interceptType != null)
        {
            WriteTypeArgument("InterceptType", interceptType, ref argumentCount);
        }

        if (hasClassName)
        {
            WriteStringArgument("ClassName", symbol.ClassName!, ref argumentCount);
        }

        if (plan.HasErrors)
        {
            WriteBooleanArgument("HasErrors", true, ref argumentCount);
        }

        Debug.Assert(argumentCount == 0);

        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine(")]");
    }

    private void WriteUtilityParameterAttributes(ITailwindUtilitySymbol utility)
    {
        var parameters = utility.Parameters;

        for (var i = 0; i < parameters.Length; i++)
        {
            WriteUtilityParameterAttribute(parameters[i]);
        }
    }

    private void WriteUtilityParameterAttribute(ITailwindUtilityParameterSymbol parameter)
    {
        var argumentCount = 5;

        _writer.Write("[");
        _writer.Write(AkcssUtilityParameterAttribute);
        _writer.WriteLine("(");
        _writer.CurrentIndent += _writer.TabSize;

        WriteIntegerArgument("Ordinal", parameter.Ordinal, ref argumentCount);
        WriteStringArgument("Name", parameter.Name, ref argumentCount);
        WriteTypeArgument("Type", parameter.Type.Symbol, ref argumentCount);

        _writer.Write("CSharpName = ");
        WriteCSharpParameterNameLiteral(parameter);
        WriteArgumentEnd(ref argumentCount);

        WriteBooleanArgument("IsOptional", parameter.IsOptional, ref argumentCount);

        Debug.Assert(argumentCount == 0);

        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine(")]");
    }

    private void WriteStringArgument(string name, string value, ref int remaining)
    {
        _writer.Write(name);
        _writer.Write(" = ");
        _writer.WriteStringLiteral(value);
        WriteArgumentEnd(ref remaining);
    }

    private void WriteIntegerArgument(string name, int value, ref int remaining)
    {
        _writer.Write(name);
        _writer.Write(" = ");
        _writer.WriteIntegerLiteral(value);
        WriteArgumentEnd(ref remaining);
    }

    private void WriteBooleanArgument(string name, bool value, ref int remaining)
    {
        _writer.Write(name);
        _writer.Write(" = ");
        _writer.WriteBooleanLiteral(value);
        WriteArgumentEnd(ref remaining);
    }

    private void WriteTypeArgument(string name, RoslynSymbol? type, ref int remaining)
    {
        _writer.Write(name);
        _writer.Write(" = typeof(");
        _valueWriter.WriteTypeName(type);
        _writer.Write(")");
        WriteArgumentEnd(ref remaining);
    }

    private void WriteCSharpParameterNameLiteral(ITailwindUtilityParameterSymbol parameter)
    {
        var name = parameter.CSharpName;

        if (!CSharpSyntaxFacts.IsValidIdentifier(name))
        {
            _writer.Write("\"parameter");
            _writer.WriteIntegerLiteral(parameter.Ordinal);
            _writer.Write("\"");
            return;
        }

        if (CSharpSyntaxFacts.GetKeywordKind(name) != CSharpSyntaxKind.None ||
            CSharpSyntaxFacts.GetContextualKeywordKind(name) != CSharpSyntaxKind.None)
        {
            _writer.Write("\"@");
            _writer.Write(name);
            _writer.Write("\"");
            return;
        }

        _writer.WriteStringLiteral(name);
    }

    private void WriteArgumentEnd(ref int remaining)
    {
        Debug.Assert(remaining > 0);

        remaining--;
        _writer.WriteLine(remaining == 0 ? string.Empty : ",");
    }

    private static string GetSymbolKindName(AkcssSymbolGenerationKind kind)
    {
        return kind switch
        {
            AkcssSymbolGenerationKind.Style => "Style",
            AkcssSymbolGenerationKind.Utility => "Utility",
            AkcssSymbolGenerationKind.InterceptMetadata => "Intercept",
            _ => "Style",
        };
    }
}
