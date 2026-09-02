using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Writes the support fields and runtime lifecycle methods for one component.
/// </summary>
internal readonly ref struct ComponentLifecycleWriter
{
    private const string FallbackRootFieldName = "__generatedRoot";

    private readonly CodeWriter _writer;
    private readonly BindingWriterEnvironment _bindingEnvironment;
    private readonly ComponentGenerationSourceMap _sourceMap;
    private readonly string _ownerTypeName;
    private readonly string _resourcePath;

    public ComponentLifecycleWriter(
        CodeWriter writer,
        in BindingWriterEnvironment bindingEnvironment,
        ComponentGenerationSourceMap sourceMap,
        string ownerTypeName,
        string resourcePath)
    {
        Debug.Assert(writer != null);
        Debug.Assert(sourceMap != null);
        Debug.Assert(!string.IsNullOrEmpty(ownerTypeName));
        Debug.Assert(!string.IsNullOrEmpty(resourcePath));

        _writer = writer!;
        _bindingEnvironment = bindingEnvironment;
        _sourceMap = sourceMap!;
        _ownerTypeName = ownerTypeName;
        _resourcePath = resourcePath;
    }

    public bool WriteSupportFields(in ComponentPlan plan)
    {
        var indent = _writer.CurrentIndent;

        try
        {
            var markupContextWriter = new ComponentMarkupContextWriter(
                _writer,
                _ownerTypeName,
                _resourcePath);
            var lifecycle = plan.Lifecycle;
            var wroteAny = markupContextWriter.WriteFields(lifecycle);

            if (lifecycle.UsesFallbackRoot)
            {
                if (wroteAny)
                {
                    _writer.WriteLine();
                }

                WriteFallbackRootField();
                wroteAny = true;
            }

            return wroteAny;
        }
        finally
        {
            _writer.CurrentIndent = indent;
        }
    }

    public void WriteMembers(in ComponentPlan plan)
    {
        var indent = _writer.CurrentIndent;

        try
        {
            WriteFirstUpdate(plan);
            _writer.WriteLine();
            WriteUpdate(plan);
        }
        finally
        {
            _writer.CurrentIndent = indent;
        }
    }

    private void WriteFallbackRootField()
    {
        _writer.Write("private global::Avalonia.Controls.Control ");
        _writer.Write(FallbackRootFieldName);
        _writer.WriteLine(" = null!;");
    }

    private void WriteFirstUpdate(in ComponentPlan plan)
    {
        _writer.WriteLine(
            "protected override global::Avalonia.Controls.Control FirstUpdate()");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;

        if (plan.Lifecycle.UsesFallbackRoot)
        {
            WriteFallbackFirstUpdate();
        }
        else
        {
            WriteComponentFirstUpdate(plan);
        }

        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WriteFallbackFirstUpdate()
    {
        _writer.Write(FallbackRootFieldName);
        _writer.WriteLine(" = new global::Avalonia.Controls.Control();");

        _writer.Write("((global::System.ComponentModel.ISupportInitialize)");
        _writer.Write(FallbackRootFieldName);
        _writer.WriteLine(").BeginInit();");

        _writer.Write("((global::System.ComponentModel.ISupportInitialize)");
        _writer.Write(FallbackRootFieldName);
        _writer.WriteLine(").EndInit();");

        _writer.Write("return ");
        _writer.Write(FallbackRootFieldName);
        _writer.WriteLine(";");
    }

    private void WriteComponentFirstUpdate(in ComponentPlan plan)
    {
        var lifecycle = plan.Lifecycle;

        Debug.Assert(lifecycle.HasRootElement);
        Debug.Assert((uint)lifecycle.RootElementId < (uint)plan.Elements.Length);
        Debug.Assert(!plan.Scopes.IsDefaultOrEmpty);

        ref readonly var scope = ref plan.Scopes.ItemRef(0);
        ref readonly var root = ref plan.Elements.ItemRef(lifecycle.RootElementId);
        var context = CreateComponentScopeContext(plan);
        var scopeWriter = new ComponentScopeWriter(
            _writer,
            in _bindingEnvironment,
            _sourceMap,
            _ownerTypeName);

        scopeWriter.WriteComponentInitialState(plan, scope, context);
        if (!lifecycle.HasExplicitRootDataContext)
        {
            WriteRootDataContextBinding(root);
        }

        WriteContentPresenterRefresh(plan, scope);

        _writer.Write("return ");
        _writer.Write(root.Identifier);
        _writer.WriteLine(";");
    }

    private void WriteRootDataContextBinding(in ComponentElementPlan root)
    {
        _writer.Write(root.Identifier);
        _writer.Write(
            ".Bind(global::Avalonia.StyledElement.DataContextProperty, " +
            "global::Avalonia.AvaloniaObjectExtensions.GetObservable(" +
            "this, global::Avalonia.StyledElement.DataContextProperty))");
        _writer.WriteLine(";");
    }

    private void WriteUpdate(in ComponentPlan plan)
    {
        _writer.WriteLine(
            "protected override global::Avalonia.Controls.Control Update()");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;

        WriteRenderStatements(plan);

        if (!plan.Lifecycle.UsesFallbackRoot)
        {
            Debug.Assert(!plan.Scopes.IsDefaultOrEmpty);

            ref readonly var scope = ref plan.Scopes.ItemRef(0);
            var context = CreateComponentScopeContext(plan);
            var scopeWriter = new ComponentScopeWriter(
                _writer,
                in _bindingEnvironment,
                _sourceMap,
                _ownerTypeName);

            scopeWriter.WriteUpdateState(plan, scope, context);
            WriteContentPresenterRefresh(plan, scope);
        }

        WriteReturnRoot(plan);

        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WriteRenderStatements(in ComponentPlan plan)
    {
        if (plan.RenderStatements.IsDefaultOrEmpty)
        {
            return;
        }

        var writer = new ComponentRenderStatementWriter(_writer, _sourceMap);

        for (var i = 0; i < plan.RenderStatements.Length; i++)
        {
            ref readonly var statement = ref plan.RenderStatements.ItemRef(i);
            if (statement.WritesDuringUpdate)
            {
                writer.Write(statement);
            }
        }
    }

    private void WriteContentPresenterRefresh(
        in ComponentPlan plan,
        in ComponentScopePlan scope)
    {
        if (!plan.Lifecycle.HasComponentContentPresenters)
        {
            return;
        }

        for (var i = 0; i < scope.Elements.Length; i++)
        {
            var elementId = plan.ScopeElementIds[scope.Elements.Start + i];
            ref readonly var element = ref plan.Elements.ItemRef(elementId);
            if (!element.RequiresContentPresenterRefresh)
            {
                continue;
            }

            _writer.Write(element.Identifier);
            _writer.WriteLine(".UpdateChild();");
        }
    }

    private void WriteReturnRoot(in ComponentPlan plan)
    {
        _writer.Write("return ");

        if (plan.Lifecycle.UsesFallbackRoot)
        {
            _writer.Write(FallbackRootFieldName);
        }
        else
        {
            Debug.Assert(plan.Lifecycle.HasRootElement);
            Debug.Assert(
                (uint)plan.Lifecycle.RootElementId < (uint)plan.Elements.Length);

            ref readonly var root = ref plan.Elements.ItemRef(
                plan.Lifecycle.RootElementId);
            _writer.Write(root.Identifier);
        }

        _writer.WriteLine(";");
    }

    private static ComponentScopeWriteContext CreateComponentScopeContext(
        in ComponentPlan plan)
    {
        return new ComponentScopeWriteContext(
            intermediateRootExpression: "this",
            baseUriExpression: ComponentMarkupContextWriter.BaseUriFieldName,
            fallbackServiceProviderExpression: null,
            nameScopeExpression: null,
            scopeId: 0,
            parentStackTraversalKind: MarkupParentStackTraversalKind.ExactScope,
            elements: plan.Elements.AsSpan(),
            elementReferences: plan.ElementReferences.AsSpan());
    }
}
