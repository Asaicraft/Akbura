using Akbura.Language.Symbols;
using Akbura.Pools;
using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Writes one generated AKCSS utility runtime type.
/// </summary>
internal readonly ref struct AkcssUtilityWriter
{
    private const string RuntimeUtilityType = "global::Akbura.Akcss.AkcssUtility";
    private const string RuntimeZeroUtilityType = "global::Akbura.Akcss.ZeroAkcssUtility";
    private const string RuntimeOperationType = "global::Akbura.Akcss.AkcssUtilityOperation";
    private const string StyleNameAttribute = "global::Akbura.CompilerAnotations.StyleNameAttribute";
    private const string InlinedStyleAttribute = "global::Akbura.CompilerAnotations.InlinedStyleAttribute";

    private readonly CodeWriter _writer;
    private readonly CSharpValueWriter _valueWriter;
    private readonly AkcssGenerationSourceMap _sourceMap;
    private readonly AkcssOperationMetadataWriter _operationMetadataWriter;
    private readonly AkcssResetWriter _resetWriter;

    public AkcssUtilityWriter(CodeWriter writer, AkcssGenerationSourceMap sourceMap)
    {
        Debug.Assert(writer != null);
        AkburaDebug.Assert(sourceMap != null);

        _writer = writer!;
        _valueWriter = new CSharpValueWriter(_writer);
        _sourceMap = sourceMap!;
        _operationMetadataWriter = new AkcssOperationMetadataWriter(_writer, sourceMap);
        _resetWriter = new AkcssResetWriter(_writer);
    }

    public bool Write(in AkcssModulePlan module, in AkcssSymbolGenerationPlan symbolPlan)
    {
        if (symbolPlan.Kind != AkcssSymbolGenerationKind.Utility ||
            !symbolPlan.EmitsRuntimeStyle ||
            symbolPlan.Symbol is not ITailwindUtilitySymbol utility ||
            symbolPlan.Symbol.IsIntercepted)
        {
            return false;
        }

        var plan = AkcssUtilityPlanner.Create(utility, _sourceMap);
        var identifierValues = ArrayBuilder<AkcssIdentifierValue>.GetInstance();
        var expansionPath = PooledHashSet<IAkcssSymbol>.GetInstance();
        var resetProperties = ArrayBuilder<AkcssResetPropertyPlan>.GetInstance();
        var conditionOrders = ArrayBuilder<int>.GetInstance();

        try
        {
            var runtimeWriter = new AkcssRuntimeOperationWriter(
                _writer,
                _sourceMap,
                identifierValues,
                expansionPath);

            WriteAttributes(module.IsInlined, utility);

            _writer.Write("private sealed class ");
            AkcssGeneratedNameWriter.WriteStyleTypeName(_writer, symbolPlan.SymbolIndex);
            _writer.Write(" : ");
            WriteBaseType(utility);
            _writer.WriteLine();

            _writer.WriteLine("{");
            _writer.CurrentIndent += _writer.TabSize;

            if (!plan.IsEmpty)
            {
                WriteOperationField();
                _writer.WriteLine();
            }

            WriteConstructor(symbolPlan.SymbolIndex, plan);

            if (utility.Parameters.Length > 16)
            {
                _writer.WriteLine();
                WriteParameters(utility);
            }

            if (!plan.IsEmpty)
            {
                _writer.WriteLine();
                WriteOperationsProperty();
            }

            _writer.WriteLine();
            WriteUpdate(utility, identifierValues, ref runtimeWriter);

            runtimeWriter.CollectResetProperties(utility, resetProperties);

            if (!resetProperties.IsEmpty)
            {
                _writer.WriteLine();
                _resetWriter.WriteMethod(resetProperties);
            }

            if (!plan.IsEmpty)
            {
                _writer.WriteLine();

                var operationWriter = new AkcssUtilityOperationWriter(
                    _writer,
                    _sourceMap,
                    plan,
                    identifierValues,
                    expansionPath,
                    conditionOrders);

                operationWriter.Write(symbolPlan.SymbolIndex);
            }

            _writer.CurrentIndent -= _writer.TabSize;
            _writer.WriteLine("}");

            return true;
        }
        finally
        {
            conditionOrders.Free();
            resetProperties.Free();
            expansionPath.Free();
            identifierValues.Free();
            plan.ReturnToPool();
        }
    }

    private void WriteAttributes(bool isInlined, ITailwindUtilitySymbol utility)
    {
        _writer.Write("[");
        _writer.Write(StyleNameAttribute);
        _writer.Write("(");
        _writer.WriteStringLiteral(utility.Name);
        _writer.WriteLine(")]");

        if (isInlined)
        {
            _writer.Write("[");
            _writer.Write(InlinedStyleAttribute);
            _writer.WriteLine("]");
        }

        _operationMetadataWriter.WriteObservedPropertyAttributes(utility);
    }

    private void WriteOperationField()
    {
        _writer.Write("private readonly global::System.Collections.Immutable.ImmutableArray<");
        _writer.Write(RuntimeOperationType);
        _writer.WriteLine("> __operations;");
    }

    private void WriteConstructor(int styleIndex, in AkcssUtilityPlan plan)
    {
        _writer.Write("public ");
        AkcssGeneratedNameWriter.WriteStyleTypeName(_writer, styleIndex);
        _writer.WriteLine("()");

        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;

        if (plan.HasConditionalOperations)
        {
            _writer.WriteLine("IsConditional = true;");
        }

        if (!plan.IsEmpty)
        {
            _writer.Write("__operations =");
            _writer.WriteLine();
            _writer.CurrentIndent += _writer.TabSize;

            _writer.Write("global::System.Collections.Immutable.ImmutableArray.Create<");
            _writer.Write(RuntimeOperationType);
            _writer.WriteLine(">(");

            _writer.CurrentIndent += _writer.TabSize;

            for (var i = 0; i < plan.Operations.Length; i++)
            {
                _writer.Write("new ");
                AkcssGeneratedNameWriter.WriteUtilityOperationTypeName(_writer, i);
                _writer.Write("(this)");

                _writer.WriteLine(
                    i + 1 == plan.Operations.Length
                        ? string.Empty
                        : ",");
            }

            _writer.CurrentIndent -= _writer.TabSize;
            _writer.WriteLine(");");
            _writer.CurrentIndent -= _writer.TabSize;
        }

        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WriteOperationsProperty()
    {
        _writer.Write("public override global::System.Collections.Immutable.ImmutableArray<");
        _writer.Write(RuntimeOperationType);
        _writer.WriteLine("> Operations => __operations;");
    }

    private void WriteParameters(ITailwindUtilitySymbol utility)
    {
        _writer.WriteLine(
            "public override global::System.Collections.Immutable.ImmutableArray<" +
            "global::System.Type> Parameters =>");

        _writer.CurrentIndent += _writer.TabSize;

        _writer.WriteLine(
            "global::System.Collections.Immutable.ImmutableArray.Create<global::System.Type>(");

        _writer.CurrentIndent += _writer.TabSize;

        var parameters = utility.Parameters;

        for (var i = 0; i < parameters.Length; i++)
        {
            _writer.Write("typeof(");
            _valueWriter.WriteTypeName(parameters[i].Type.Symbol);
            _writer.Write(")");

            _writer.WriteLine(
                i + 1 == parameters.Length
                    ? string.Empty
                    : ",");
        }

        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine(");");

        _writer.CurrentIndent -= _writer.TabSize;
    }

    private void WriteUpdate(
        ITailwindUtilitySymbol utility,
        ArrayBuilder<AkcssIdentifierValue> identifierValues,
        ref AkcssRuntimeOperationWriter runtimeWriter)
    {
        var parameters = utility.Parameters;
        var targetName = AkcssExpressionGenerator.GetTargetParameterName(utility);

        _writer.Write("public override void Update(object ");
        _writer.Write(targetName);

        if (parameters.Length > 16)
        {
            _writer.Write(", params object[] __arguments");
        }
        else
        {
            for (var i = 0; i < parameters.Length; i++)
            {
                _writer.Write(", ");
                _valueWriter.WriteTypeName(parameters[i].Type.Symbol);
                _writer.Write(" ");
                _writer.Write(AkcssExpressionGenerator.GetParameterName(parameters[i]));
            }
        }

        _writer.WriteLine(")");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;

        _writer.Write("global::System.ArgumentNullException.ThrowIfNull(");
        _writer.Write(targetName);
        _writer.WriteLine(");");

        if (parameters.Length <= 16)
        {
            runtimeWriter.Write(utility, targetName);
        }
        else
        {
            var previousCount = identifierValues.Count;

            try
            {
                AkcssExpressionGenerator.AddArgumentParameterValues(
                    utility,
                    "__arguments",
                    identifierValues);

                runtimeWriter.Write(utility, targetName);
            }
            finally
            {
                identifierValues.Count = previousCount;
            }
        }

        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WriteBaseType(ITailwindUtilitySymbol utility)
    {
        var parameters = utility.Parameters;

        if (parameters.IsEmpty)
        {
            _writer.Write(RuntimeZeroUtilityType);
            return;
        }

        if (parameters.Length > 16)
        {
            _writer.Write(RuntimeUtilityType);
            return;
        }

        _writer.Write(RuntimeUtilityType);
        _writer.Write("<");

        for (var i = 0; i < parameters.Length; i++)
        {
            if (i > 0)
            {
                _writer.Write(", ");
            }

            _valueWriter.WriteTypeName(parameters[i].Type.Symbol);
        }

        _writer.Write(">");
    }
}
