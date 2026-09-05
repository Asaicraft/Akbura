using Akbura.Language.Symbols;
using Akbura.Pools;
using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Writes one generated AKCSS class-style runtime type.
/// </summary>
internal readonly ref struct AkcssStyleWriter
{
    private const string RuntimeClassType = "global::Akbura.Akcss.AkcssClass";
    private const string StyleNameAttribute = "global::Akbura.CompilerAnotations.StyleNameAttribute";
    private const string InlinedStyleAttribute = "global::Akbura.CompilerAnotations.InlinedStyleAttribute";

    private readonly CodeWriter _writer;
    private readonly AkcssGenerationSourceMap _sourceMap;
    private readonly AkcssOperationMetadataWriter _operationMetadataWriter;
    private readonly AkcssResetWriter _resetWriter;

    public AkcssStyleWriter(CodeWriter writer, AkcssGenerationSourceMap sourceMap)
    {
        Debug.Assert(writer != null);
        AkburaDebug.Assert(sourceMap != null);

        _writer = writer!;
        _sourceMap = sourceMap!;
        _operationMetadataWriter = new AkcssOperationMetadataWriter(_writer, sourceMap);
        _resetWriter = new AkcssResetWriter(_writer);
    }

    public bool Write(in AkcssModulePlan module, in AkcssSymbolGenerationPlan plan)
    {
        if (plan.Kind != AkcssSymbolGenerationKind.Style ||
            !plan.EmitsRuntimeStyle ||
            plan.Symbol.IsIntercepted)
        {
            return false;
        }

        var identifierValues = ArrayBuilder<AkcssIdentifierValue>.GetInstance();
        var expansionPath = PooledHashSet<IAkcssSymbol>.GetInstance();
        var resetProperties = ArrayBuilder<AkcssResetPropertyPlan>.GetInstance();

        try
        {
            var operationWriter = new AkcssRuntimeOperationWriter(
                _writer,
                _sourceMap,
                identifierValues,
                expansionPath);

            WriteAttributes(module.IsInlined, plan.Symbol);

            _writer.Write("private sealed class ");
            AkcssGeneratedNameWriter.WriteStyleTypeName(_writer, plan.SymbolIndex);
            _writer.Write(" : ");
            _writer.WriteLine(RuntimeClassType);

            _writer.WriteLine("{");
            _writer.CurrentIndent += _writer.TabSize;

            WriteUpdate(plan.Symbol, ref operationWriter);

            operationWriter.CollectResetProperties(plan.Symbol, resetProperties);

            if (!resetProperties.IsEmpty)
            {
                _writer.WriteLine();
                _resetWriter.WriteMethod(resetProperties);
            }

            _writer.CurrentIndent -= _writer.TabSize;
            _writer.WriteLine("}");

            return true;
        }
        finally
        {
            resetProperties.Free();
            expansionPath.Free();
            identifierValues.Free();
        }
    }

    private void WriteAttributes(bool isInlined, IAkcssSymbol symbol)
    {
        _writer.Write("[");
        _writer.Write(StyleNameAttribute);
        _writer.Write("(");
        _writer.WriteStringLiteral(symbol.Name);
        _writer.WriteLine(")]");

        if (isInlined)
        {
            _writer.Write("[");
            _writer.Write(InlinedStyleAttribute);
            _writer.WriteLine("]");
        }

        _operationMetadataWriter.WriteObservedPropertyAttributes(symbol);
    }

    private void WriteUpdate(
        IAkcssSymbol symbol,
        ref AkcssRuntimeOperationWriter operationWriter)
    {
        _writer.WriteLine("public override void Update(object __target)");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;

        _writer.WriteLine("global::System.ArgumentNullException.ThrowIfNull(__target);");
        operationWriter.Write(symbol, "__target");

        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }
}
