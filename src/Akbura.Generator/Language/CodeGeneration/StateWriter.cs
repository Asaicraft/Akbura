using Akbura.Language.Symbols;
using System.Diagnostics;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akbura.Language.CodeGeneration;

internal readonly ref struct StateWriter
{
    private readonly CodeWriter _writer;
    private readonly CSharpValueWriter _valueWriter;
    private readonly CSharpSyntaxWriter _syntaxWriter;
    private readonly UseHookInvocationWriter _hookWriter;
    private readonly SourceMappingWriter _mappings;
    private readonly BindingWriterEnvironment _bindingEnvironment;
    private readonly string _ownerTypeName;

    public StateWriter(
        CodeWriter writer,
        ComponentGenerationSourceMap sourceMap,
        string ownerTypeName)
    {
        Debug.Assert(writer != null);
        Debug.Assert(sourceMap != null);
        Debug.Assert(!string.IsNullOrEmpty(ownerTypeName));

        _writer = writer!;
        _valueWriter = new CSharpValueWriter(writer!);
        _syntaxWriter = new CSharpSyntaxWriter(writer!);
        _hookWriter = new UseHookInvocationWriter(writer!);
        _mappings = new SourceMappingWriter(writer!, sourceMap!);
        _bindingEnvironment = default;
        _ownerTypeName = ownerTypeName;
    }

    public StateWriter(
        CodeWriter writer,
        in BindingWriterEnvironment bindingEnvironment,
        ComponentGenerationSourceMap sourceMap,
        string ownerTypeName)
    {
        Debug.Assert(writer != null);
        Debug.Assert(sourceMap != null);
        Debug.Assert(!string.IsNullOrEmpty(ownerTypeName));

        _writer = writer!;
        _valueWriter = new CSharpValueWriter(writer!);
        _syntaxWriter = new CSharpSyntaxWriter(writer!);
        _hookWriter = new UseHookInvocationWriter(writer!);
        _mappings = new SourceMappingWriter(writer!, sourceMap!);
        _bindingEnvironment = bindingEnvironment;
        _ownerTypeName = ownerTypeName;
    }

    public void Write(in ComponentStatePlan plan)
    {
        WriteStateInfo(plan);
        _writer.WriteLine();
        WriteStateInfoFactory(plan);
        _writer.WriteLine();
        WriteStateInfoDelegate(plan);
        _writer.WriteLine();
        WriteStateField(plan);
        _writer.WriteLine();
        WriteStateAccessor(plan);
        _writer.WriteLine();
        WriteValueProperty(plan);
        _writer.WriteLine();
        WriteFactory(plan);
    }

    public void WriteHotReloadAssignment(in ComponentStatePlan plan)
    {
        GeneratedMemberNameWriter.WriteStateInfoField(
            _writer,
            plan.GeneratedName);
        _writer.Write(" = ");
        GeneratedMemberNameWriter.WriteStateInfoFactory(
            _writer,
            plan.GeneratedName);
        _writer.WriteLine("();");
    }

    private void WriteStateInfo(in ComponentStatePlan plan)
    {
        _writer.WriteLine("#if DEBUG");
        WriteStateInfoDeclaration(plan, isReadOnly: false);
        _writer.WriteLine("#else");
        WriteStateInfoDeclaration(plan, isReadOnly: true);
        _writer.WriteLine("#endif");
        _writer.CurrentIndent += _writer.TabSize;
        GeneratedMemberNameWriter.WriteStateInfoFactory(
            _writer,
            plan.GeneratedName);
        _writer.WriteLine("();");
        _writer.CurrentIndent -= _writer.TabSize;
    }

    private void WriteStateInfoDeclaration(
        in ComponentStatePlan plan,
        bool isReadOnly)
    {
        _writer.Write("private static ");

        if (isReadOnly)
        {
            _writer.Write("readonly ");
        }

        _writer.Write("global::Akbura.ComponentTree.StateInfo<");
        WriteValueType(plan);
        _writer.Write("> ");
        GeneratedMemberNameWriter.WriteStateInfoField(
            _writer,
            plan.GeneratedName);
        _writer.WriteLine(" =");
    }

    private void WriteStateInfoFactory(in ComponentStatePlan plan)
    {
        _writer.WriteHiddenApiAttributes();
        _writer.Write("private static global::Akbura.ComponentTree.StateInfo<");
        WriteValueType(plan);
        _writer.Write("> ");
        GeneratedMemberNameWriter.WriteStateInfoFactory(
            _writer,
            plan.GeneratedName);
        _writer.WriteLine("()");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write("return ");

        if (plan.FactoryKind == ComponentStateFactoryKind.State)
        {
            _writer.Write("global::Akbura.ComponentTree.StateInfo<");
            WriteValueType(plan);
            _writer.WriteLine(">.FromState(");
        }
        else
        {
            _writer.Write("new global::Akbura.ComponentTree.StateInfo<");
            WriteValueType(plan);
            _writer.WriteLine(">(");
        }

        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteStringLiteral(plan.Name);
        _writer.WriteLine(",");

        if (plan.FactoryKind == ComponentStateFactoryKind.State)
        {
            GeneratedMemberNameWriter.WriteStateInfoStateFactory(
                _writer,
                plan.GeneratedName);
        }
        else
        {
            GeneratedMemberNameWriter.WriteStateInfoValueFactory(
                _writer,
                plan.GeneratedName);
        }

        _writer.WriteLine(");");
        _writer.CurrentIndent -= _writer.TabSize * 2;
        _writer.WriteLine("}");
    }

    private void WriteStateInfoDelegate(in ComponentStatePlan plan)
    {
        _writer.WriteHiddenApiAttributes();
        _writer.Write("private static ");

        if (plan.FactoryKind == ComponentStateFactoryKind.State)
        {
            _writer.Write("global::Akbura.ComponentTree.State<");
            WriteValueType(plan);
            _writer.Write(">");
        }
        else
        {
            WriteValueType(plan);
        }

        _writer.Write(" ");

        if (plan.FactoryKind == ComponentStateFactoryKind.State)
        {
            GeneratedMemberNameWriter.WriteStateInfoStateFactory(
                _writer,
                plan.GeneratedName);
        }
        else
        {
            GeneratedMemberNameWriter.WriteStateInfoValueFactory(
                _writer,
                plan.GeneratedName);
        }

        _writer.WriteLine("(");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("global::Akbura.AkburaControl __owner) =>");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write("((");
        _writer.Write(_ownerTypeName);
        _writer.Write(")__owner).");

        if (plan.FactoryKind == ComponentStateFactoryKind.State)
        {
            GeneratedMemberNameWriter.WriteStateFactory(
                _writer,
                plan.GeneratedName);
        }
        else
        {
            GeneratedMemberNameWriter.WriteStateValueFactory(
                _writer,
                plan.GeneratedName);
        }

        _writer.WriteLine("();");
        _writer.CurrentIndent -= _writer.TabSize * 2;
    }

    private void WriteStateField(in ComponentStatePlan plan)
    {
        _writer.Write("private global::Akbura.ComponentTree.State<");
        WriteValueType(plan);
        _writer.Write(">? ");
        GeneratedMemberNameWriter.WriteStateField(
            _writer,
            plan.GeneratedName);
        _writer.WriteLine(";");
    }

    private void WriteStateAccessor(in ComponentStatePlan plan)
    {
        _writer.Write("private global::Akbura.ComponentTree.State<");
        WriteValueType(plan);
        _writer.Write("> ");
        GeneratedMemberNameWriter.WriteStateAccessor(
            _writer,
            plan.GeneratedName);
        _writer.WriteLine(" =>");
        _writer.CurrentIndent += _writer.TabSize;
        GeneratedMemberNameWriter.WriteStateField(
            _writer,
            plan.GeneratedName);
        if (plan.IsComposable)
        {
            _writer.WriteLine(" ?? throw new global::System.InvalidOperationException(");
            _writer.CurrentIndent += _writer.TabSize;
            _writer.WriteLine("\"The composed hook state is not prepared for this frame.\");");
            _writer.CurrentIndent -= _writer.TabSize * 2;
            return;
        }

        _writer.Write(" ??= CreateState(");
        GeneratedMemberNameWriter.WriteStateInfoField(
            _writer,
            plan.GeneratedName);
        _writer.WriteLine(");");
        _writer.CurrentIndent -= _writer.TabSize;
    }

    private void WriteValueProperty(in ComponentStatePlan plan)
    {
        _writer.Write("private ");
        WriteValueType(plan);
        _writer.Write(" ");
        _valueWriter.WriteIdentifier(plan.Name);
        _writer.WriteLine();
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write("get => ");
        GeneratedMemberNameWriter.WriteStateAccessor(
            _writer,
            plan.GeneratedName);
        _writer.WriteLine(".Value;");

        if (!plan.IsReadOnly)
        {
            _writer.Write("set => ");
            GeneratedMemberNameWriter.WriteStateAccessor(
                _writer,
                plan.GeneratedName);
            _writer.WriteLine(".Value = value;");
        }

        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WriteFactory(in ComponentStatePlan plan)
    {
        _writer.WriteHiddenApiAttributes();
        _writer.Write("private ");

        if (plan.FactoryKind == ComponentStateFactoryKind.State)
        {
            _writer.Write("global::Akbura.ComponentTree.State<");
            WriteValueType(plan);
            _writer.Write(">");
        }
        else
        {
            WriteValueType(plan);
        }

        _writer.Write(" ");

        if (plan.FactoryKind == ComponentStateFactoryKind.State)
        {
            GeneratedMemberNameWriter.WriteStateFactory(
                _writer,
                plan.GeneratedName);
        }
        else
        {
            GeneratedMemberNameWriter.WriteStateValueFactory(
                _writer,
                plan.GeneratedName);
        }

        _writer.WriteLine("()");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;

        if (plan.BindingKind != StateBindingKind.None)
        {
            WriteDirectionalFactory(plan);
            _writer.CurrentIndent -= _writer.TabSize;
            _writer.WriteLine("}");
            return;
        }

        const string returnPrefix = "return ";

        if (plan.Syntax.Initializer.Expression is { } sourceSyntax &&
            sourceSyntax.GetRawCSharpExpression() is { } sourceExpression)
        {
            using var mapping = _mappings.WriteStart(
                sourceSyntax,
                sourceExpression.Span,
                returnPrefix.Length);

            _writer.Write(returnPrefix);
            WriteInitializer(plan);
            _writer.WriteLine(";");
        }
        else
        {
            _writer.Write(returnPrefix);
            WriteInitializer(plan);
            _writer.WriteLine(";");
        }

        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WriteDirectionalFactory(in ComponentStatePlan plan)
    {
        var method = plan.BindingKind switch
        {
            StateBindingKind.Bind => "CreateBind",
            StateBindingKind.In => "CreateIn",
            StateBindingKind.Out when plan.IsObservableSource => "CreateOutObservable",
            StateBindingKind.Out => "CreateOut",
            _ => throw new InvalidOperationException("Unknown state binding direction."),
        };

        using var mapping = _mappings.WriteStart(plan.Syntax.Initializer.Expression);
        _writer.Write("return global::Akbura.ComponentTree.StateBindings.");
        _writer.Write(method);
        _writer.Write("<");
        WriteValueType(plan);
        _writer.WriteLine(">(");
        _writer.CurrentIndent += _writer.TabSize;

        if (plan.BindingKind is StateBindingKind.Bind or StateBindingKind.Out &&
            !plan.IsObservableSource)
        {
            WriteReadConverter(plan);
        }
        else
        {
            WriteReadLambda(plan);
        }

        _writer.WriteLine(",");
        if (plan.BindingKind is StateBindingKind.Bind or StateBindingKind.In)
        {
            WriteWriteLambda(plan);
            _writer.WriteLine(",");
        }

        WriteRootLambda(plan);
        _writer.WriteLine(",");
        WritePathFactory(plan);
        _writer.WriteLine(",");
        _writer.WriteIntegerLiteral(plan.BindingPathElements.Length);
        _writer.WriteLine(",");
        _writer.Write(plan.BindingFullPathElementCount > 0 ? "true" : "false");
        _writer.WriteLine(",");
        WriteRootObservation(plan);
        _writer.WriteLine(");");
        _writer.CurrentIndent -= _writer.TabSize;
    }

    private void WriteReadLambda(in ComponentStatePlan plan)
    {
        if (!plan.CanReadBindingSource)
        {
            _writer.Write("null");
            return;
        }

        _writer.Write("() => ");
        _syntaxWriter.WriteExpression(plan.Initializer);
    }

    private void WriteReadConverter(in ComponentStatePlan plan)
    {
        Debug.Assert(plan.BindingValueType != null);

        if (!plan.CanReadBindingSource)
        {
            _writer.Write("null");
            return;
        }

        _writer.Write("static __value => (");
        _valueWriter.WriteTypeNameWithNullableAnnotation(plan.BindingValueType);
        _writer.Write(")__value!");
    }

    private void WriteWriteLambda(in ComponentStatePlan plan)
    {
        _writer.Write("__value => ");
        _syntaxWriter.WriteExpression(plan.Initializer);
        _writer.Write(" = __value");
    }

    private void WriteRootLambda(in ComponentStatePlan plan)
    {
        _writer.Write("() => (object?)(");
        var root = GetPathRoot(plan.Initializer);
        if (plan.BindingRootKind == ComponentStateBindingRootKind.Static)
        {
            Debug.Assert(plan.BindingRootExpression != null);
            _syntaxWriter.WriteExpression(plan.BindingRootExpression!);
        }
        else if (plan.BindingRootKind == ComponentStateBindingRootKind.Component &&
            root is IdentifierNameSyntax or BaseExpressionSyntax)
        {
            _writer.Write("this");
        }
        else
        {
            _syntaxWriter.WriteExpression(root);
        }

        _writer.Write(")");
    }

    private void WritePathFactory(in ComponentStatePlan plan)
    {
        Debug.Assert(plan.BindingSourceType != null);

        _writer.Write("() => ");
        var bindingWriter = new BindingWriter(
            _writer,
            in _bindingEnvironment);
        bindingWriter.WriteCompiledBindingPath(
            plan.BindingSourceType!,
            plan.BindingPathElements);
    }

    private void WriteRootObservation(in ComponentStatePlan plan)
    {
        var stateDependencies = plan.BindingDependencyStateGeneratedNames;
        var propertyDependencies = plan.BindingPropertyDependencies;
        var observesGeneratedRoot = plan.BindingRootKind is
            ComponentStateBindingRootKind.Parameter or
            ComponentStateBindingRootKind.Service or
            ComponentStateBindingRootKind.Command;
        var observationCount =
            stateDependencies.Length +
            propertyDependencies.Length +
            (observesGeneratedRoot ? 1 : 0);
        if (observationCount == 0)
        {
            _writer.Write("null");
            return;
        }

        _writer.Write("__callback => ");
        if (observationCount == 1)
        {
            if (observesGeneratedRoot)
            {
                WriteGeneratedRootObservation(plan);
            }
            else if (!stateDependencies.IsEmpty)
            {
                WriteStateObservation(stateDependencies[0]);
            }
            else
            {
                WritePropertyDependencyObservation(propertyDependencies[0]);
            }

            return;
        }

        _writer.WriteLine("global::Akbura.ComponentTree.StateBindings.CombineSubscriptions(");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("new global::System.IDisposable[]");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        if (observesGeneratedRoot)
        {
            WriteGeneratedRootObservation(plan);
            _writer.WriteLine(",");
        }

        for (var index = 0; index < stateDependencies.Length; index++)
        {
            WriteStateObservation(stateDependencies[index]);
            _writer.WriteLine(",");
        }

        for (var index = 0; index < propertyDependencies.Length; index++)
        {
            WritePropertyDependencyObservation(propertyDependencies[index]);
            _writer.WriteLine(",");
        }

        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.Write(")");
    }

    private void WriteGeneratedRootObservation(in ComponentStatePlan plan)
    {
        Debug.Assert(!string.IsNullOrEmpty(plan.BindingRootName));

        _writer.Write(
            "global::Akbura.ComponentTree.StateBindings.ObserveAvaloniaProperty(" +
            "this, ");
        _valueWriter.WriteIdentifier(plan.BindingRootName!);
        _writer.Write("Property");
        if (plan.BindingRootKind is
            ComponentStateBindingRootKind.Parameter or
            ComponentStateBindingRootKind.Service)
        {
            _writer.Write(".AvaloniaProperty");
        }

        _writer.Write(", __callback)");
    }

    private void WriteStateObservation(string generatedName)
    {
        GeneratedMemberNameWriter.WriteStateAccessor(_writer, generatedName);
        _writer.Write(".Subscribe(__value => __callback())");
    }

    private void WritePropertyDependencyObservation(
        in ComponentStateBindingPropertyDependencyPlan dependency)
    {
        _writer.Write(
            "global::Akbura.ComponentTree.StateBindings.ObserveAvaloniaProperty(" +
            "this, ");
        if (dependency.Kind == ComponentStateBindingPropertyDependencyKind.AvaloniaProperty)
        {
            Debug.Assert(dependency.AvaloniaProperty.Symbol != null);
            _valueWriter.WriteStaticMemberReference(
                dependency.AvaloniaProperty.Symbol!);
        }
        else
        {
            Debug.Assert(!string.IsNullOrEmpty(dependency.Name));
            _valueWriter.WriteIdentifier(dependency.Name!);
            _writer.Write("Property");
            if (dependency.Kind is
                ComponentStateBindingPropertyDependencyKind.Parameter or
                ComponentStateBindingPropertyDependencyKind.Service)
            {
                _writer.Write(".AvaloniaProperty");
            }
        }

        _writer.Write(", __callback)");
    }

    private static ExpressionSyntax GetPathRoot(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    continue;
                case MemberAccessExpressionSyntax memberAccess:
                    expression = memberAccess.Expression;
                    continue;
                case ElementAccessExpressionSyntax elementAccess:
                    expression = elementAccess.Expression;
                    continue;
                default:
                    return expression;
            }
        }
    }

    private void WriteInitializer(in ComponentStatePlan plan)
    {
        if (plan.HookMethod is { } method)
        {
            _hookWriter.Write(method, (InvocationExpressionSyntax)plan.Initializer, plan.StateArguments);
        }
        else
        {
            _syntaxWriter.WriteExpression(plan.Initializer);
        }
    }

    private void WriteValueType(in ComponentStatePlan plan)
    {
        _valueWriter.WriteTypeNameWithNullableAnnotation(plan.ValueType);
    }
}
