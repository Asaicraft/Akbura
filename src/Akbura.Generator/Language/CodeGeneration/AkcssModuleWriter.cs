using Microsoft.CodeAnalysis;
using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Writes the generated module members shared by all AKCSS documents.
/// </summary>
internal readonly ref struct AkcssModuleWriter
{
    private const string RuntimeStyleType =
        "global::Akbura.Akcss.AkcssStyle";

    private const string AkcssModuleReferenceAttribute =
        "global::Akbura.CompilerAnotations.AkcssModuleReferenceAttribute";

    private readonly CodeWriter _writer;
    private readonly CSharpValueWriter _valueWriter;
    private readonly AkcssDeclarationMetadataWriter _metadataWriter;
    private readonly AkcssOperationMetadataWriter _operationMetadataWriter;

    public AkcssModuleWriter(
        CodeWriter writer,
        AkcssGenerationSourceMap sourceMap)
    {
        AkburaDebug.Assert(writer != null);
        AkburaDebug.Assert(sourceMap != null);

        _writer = writer!;
        _valueWriter = new CSharpValueWriter(_writer);
        _metadataWriter = new AkcssDeclarationMetadataWriter(_writer);
        _operationMetadataWriter = new AkcssOperationMetadataWriter(
            _writer,
            sourceMap);
    }

    public void WriteAssemblyReference(in AkcssModulePlan plan)
    {
        _writer.Write("[assembly: ");
        _writer.Write(AkcssModuleReferenceAttribute);
        _writer.Write("(typeof(global::");
        _writer.Write(plan.GeneratedNamespace);
        _writer.Write(".");
        _writer.Write(plan.GeneratedTypeName);
        _writer.WriteLine("))]");
    }

    public void WriteDeclarationAttributes(in AkcssModulePlan plan)
    {
        _metadataWriter.WriteHiddenApiAttributes();
        _metadataWriter.WriteModuleAttribute(plan);
    }

    public void WriteConstants(in AkcssModulePlan plan)
    {
        _metadataWriter.WriteHiddenApiAttributes();

        _writer.Write("public const string MetadataName = ");
        _writer.WriteStringLiteral(plan.MetadataName);
        _writer.WriteLine(";");

        _writer.WriteLine();

        _metadataWriter.WriteHiddenApiAttributes();

        _writer.Write("public const string SourcePath = ");
        _writer.WriteStringLiteral(plan.SourcePath);
        _writer.WriteLine(";");
    }

    public void WriteStyleCollection(in AkcssModulePlan plan)
    {
        _metadataWriter.WriteHiddenApiAttributes();

        _writer.Write("public static readonly global::System.Collections.Immutable.ImmutableArray<");
        _writer.Write(RuntimeStyleType);
        _writer.WriteLine("> Styles =");

        var indent = _writer.CurrentIndent;

        if (plan.RuntimeStyles.IsEmpty)
        {
            _writer.CurrentIndent = indent + _writer.TabSize;
            _writer.Write("global::System.Collections.Immutable.ImmutableArray<");
            _writer.Write(RuntimeStyleType);
            _writer.WriteLine(">.Empty;");
            _writer.CurrentIndent = indent;
            return;
        }

        _writer.CurrentIndent = indent + _writer.TabSize;
        _writer.Write("global::System.Collections.Immutable.ImmutableArray.Create<");
        _writer.Write(RuntimeStyleType);
        _writer.WriteLine(">(");

        _writer.CurrentIndent = indent + _writer.TabSize * 2;

        for (var i = 0; i < plan.RuntimeStyles.Length; i++)
        {
            ref readonly var style = ref plan.RuntimeStyles.ItemRef(i);

            WriteRuntimeStyleCreation(style);

            _writer.WriteLine(
                i + 1 == plan.RuntimeStyles.Length
                    ? string.Empty
                    : ",");
        }

        _writer.CurrentIndent = indent + _writer.TabSize;
        _writer.WriteLine(");");
        _writer.CurrentIndent = indent;
    }

    public void WriteMetadataCarriers(in AkcssModulePlan plan)
    {
        for (var i = 0; i < plan.Symbols.Length; i++)
        {
            if (i > 0)
            {
                _writer.WriteLine();
            }

            ref readonly var symbol = ref plan.Symbols.ItemRef(i);

            WriteMetadataCarrier(symbol);
        }
    }

    private void WriteRuntimeStyleCreation(in AkcssRuntimeStylePlan plan)
    {
        _writer.Write("new ");

        switch (plan.Kind)
        {
            case AkcssRuntimeStyleKind.Generated:
                AkcssGeneratedNameWriter.WriteStyleTypeName(
                    _writer,
                    plan.SymbolIndex);

                break;

            case AkcssRuntimeStyleKind.Interceptor:
                Debug.Assert(plan.InterceptorType != null);

                _valueWriter.WriteTypeName(plan.InterceptorType);
                break;

            default:
                Debug.Fail("Unexpected AKCSS runtime style kind.");

                _writer.Write("global::Akbura.Akcss.AkcssStyle");
                break;
        }

        _writer.Write("()");
    }

    private void WriteMetadataCarrier(in AkcssSymbolGenerationPlan plan)
    {
        _metadataWriter.WriteHiddenApiAttributes();
        _metadataWriter.WriteCompilerGeneratedAttribute();
        _metadataWriter.WriteSymbolAttributes(plan);
        _operationMetadataWriter.Write(plan.Symbol);

        _writer.Write("public static class ");
        AkcssGeneratedNameWriter.WriteMetadataTypeName(
            _writer,
            plan.SymbolIndex);
        _writer.WriteLine();

        _writer.WriteLine("{");
        _writer.WriteLine("}");
    }
}
