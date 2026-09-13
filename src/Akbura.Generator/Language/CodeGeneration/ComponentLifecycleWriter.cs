using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Writes the support fields and runtime lifecycle methods for one component.
/// </summary>
internal readonly ref struct ComponentLifecycleWriter
{
    internal const string HotReloadUpdateInitialValuesMethodName =
        "__AkburaHotReloadUpdateInitialValues";

    private const string FallbackRootFieldName = "__generatedRoot";
    private const string RootDataContextOperationIdentity =
        "implicit-root-data-context";
    private const string RootDataContextOperationSlot =
        "property:Avalonia.StyledElement.DataContextProperty";
    private const string RenderRevisionChangedLocalName =
        "__akburaRenderRevisionChanged";

    private readonly CodeWriter _writer;
    private readonly BindingWriterEnvironment _bindingEnvironment;
    private readonly ComponentGenerationSourceMap _sourceMap;
    private readonly string _ownerTypeName;
    private readonly string _resourcePath;
    private readonly ComponentGenerationMode _generationMode;

    public ComponentLifecycleWriter(
        CodeWriter writer,
        in BindingWriterEnvironment bindingEnvironment,
        ComponentGenerationSourceMap sourceMap,
        string ownerTypeName,
        string resourcePath,
        ComponentGenerationMode generationMode =
            ComponentGenerationMode.ReleaseDirect)
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
        _generationMode = generationMode;
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
            bool wroteAny;
            if (_generationMode == ComponentGenerationMode.DebugStructural)
            {
                markupContextWriter.WriteBaseUriField();
                wroteAny = true;
            }
            else
            {
                wroteAny = markupContextWriter.WriteFields(lifecycle);
            }

            if (_generationMode == ComponentGenerationMode.DebugStructural ||
                lifecycle.UsesFallbackRoot)
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
            _writer.WriteLine();
            WriteHotReloadUpdateInitialValues(plan);
        }
        finally
        {
            _writer.CurrentIndent = indent;
        }
    }

    private void WriteFallbackRootField()
    {
        if (_generationMode == ComponentGenerationMode.DebugStructural)
        {
            _writer.WriteLine("#pragma warning disable CS0414");
        }

        _writer.Write("private global::Avalonia.Controls.Control ");
        _writer.Write(FallbackRootFieldName);
        _writer.WriteLine(" = null!;");

        if (_generationMode == ComponentGenerationMode.DebugStructural)
        {
            _writer.WriteLine("#pragma warning restore CS0414");
        }
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

    private void WriteValidateFallbackUpdate()
    {
        if (_generationMode != ComponentGenerationMode.DebugStructural)
        {
            return;
        }

        _writer.Write("if (");
        _writer.Write(FallbackRootFieldName);
        _writer.WriteLine(" is null)");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine(
            "throw new global::System.InvalidOperationException(");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine(
            "\"Changing a component to zero or multiple root controls \" +");
        _writer.WriteLine(
            "\"requires an application restart during Hot Reload.\");");
        _writer.CurrentIndent -= _writer.TabSize * 2;
        _writer.WriteLine("}");
        _writer.WriteLine();
    }

    private void WriteComponentFirstUpdate(in ComponentPlan plan)
    {
        var lifecycle = plan.Lifecycle;

        Debug.Assert(lifecycle.HasRootElement);
        Debug.Assert((uint)lifecycle.RootElementId < (uint)plan.Elements.Length);
        Debug.Assert(!plan.Scopes.IsDefaultOrEmpty);

        ref readonly var scope = ref plan.Scopes.ItemRef(0);
        ref readonly var root = ref plan.Elements.ItemRef(lifecycle.RootElementId);

        var usesStructuralHotReload = UsesStructuralHotReload(plan);
        if (usesStructuralHotReload)
        {
            WriteBeginRenderRevision();
            _writer.WriteLine();
            _writer.Write("if (!");
            _writer.Write(RenderRevisionChangedLocalName);
            _writer.WriteLine(")");
            _writer.WriteLine("{");
            _writer.CurrentIndent += _writer.TabSize;
            WriteReturnRoot(plan);
            _writer.CurrentIndent -= _writer.TabSize;
            _writer.WriteLine("}");
            _writer.WriteLine();
            WriteRenderRevisionTryStart();
        }

        var context = CreateComponentScopeContext(plan);
        var scopeWriter = new ComponentScopeWriter(
            _writer,
            in _bindingEnvironment,
            _sourceMap,
            _ownerTypeName,
            _generationMode);

        scopeWriter.WriteComponentInitialState(plan, scope, context);
        if (!lifecycle.HasExplicitRootDataContext)
        {
            if (usesStructuralHotReload)
            {
                WriteStructuralRootDataContextBinding(root);
            }
            else
            {
                WriteRootDataContextBinding(root);
            }
        }

        if (usesStructuralHotReload)
        {
            _writer.WriteLine();
            WritePrepareRenderRevision();
        }

        WriteContentPresenterRefresh(plan, scope);

        if (usesStructuralHotReload)
        {
            _writer.WriteLine();
            WriteCompleteRenderRevision();
        }

        _writer.Write("return ");
        _writer.Write(root.Identifier);
        _writer.WriteLine(";");

        if (usesStructuralHotReload)
        {
            WriteRenderRevisionTryEnd();
        }
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

    private void WriteStructuralRootDataContextBinding(
        in ComponentElementPlan root)
    {
        _writer.Write("if (");
        _writer.Write(
            ComponentStructuralHotReloadWriter.RenderStateFieldName);
        _writer.WriteLine(".ShouldApplyOwnedOperation(");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteIntegerLiteral(root.RuntimeStorageId);
        _writer.WriteLine(",");
        _writer.WriteStringLiteral(RootDataContextOperationSlot);
        _writer.WriteLine(",");
        _writer.WriteStringLiteral(RootDataContextOperationIdentity);
        _writer.WriteLine("))");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write(
            ComponentStructuralHotReloadWriter.RenderStateFieldName);
        _writer.WriteLine(".ApplyObservableBindingOperation(");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteIntegerLiteral(root.RuntimeStorageId);
        _writer.WriteLine(",");
        _writer.WriteStringLiteral(RootDataContextOperationSlot);
        _writer.WriteLine(",");
        _writer.Write(root.Identifier);
        _writer.WriteLine(",");
        _writer.WriteLine(
            "global::Avalonia.StyledElement.DataContextProperty,");
        _writer.WriteLine(
            "global::Avalonia.AvaloniaObjectExtensions.GetObservable(" +
            "this, global::Avalonia.StyledElement.DataContextProperty));");
        _writer.CurrentIndent -= _writer.TabSize * 2;
        _writer.WriteLine("}");
    }

    private void WriteUpdate(in ComponentPlan plan)
    {
        _writer.WriteLine(
            "protected override global::Avalonia.Controls.Control Update()");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;

        var usesStructuralHotReload = UsesStructuralHotReload(plan);
        if (usesStructuralHotReload)
        {
            WriteBeginRenderRevision();
            _writer.WriteLine();
            WriteRenderRevisionTryStart();
        }
        else if (plan.Lifecycle.UsesFallbackRoot)
        {
            WriteValidateFallbackUpdate();
        }

        WriteRenderStatements(plan);

        if (usesStructuralHotReload)
        {
            WriteStructuralRevisionInitialization(plan);
            _writer.WriteLine();
        }

        if (!plan.Lifecycle.UsesFallbackRoot)
        {
            Debug.Assert(!plan.Scopes.IsDefaultOrEmpty);

            ref readonly var scope = ref plan.Scopes.ItemRef(0);
            var context = CreateComponentScopeContext(plan);
            var scopeWriter = new ComponentScopeWriter(
                _writer,
                in _bindingEnvironment,
                _sourceMap,
                _ownerTypeName,
                _generationMode);

            scopeWriter.WriteUpdateState(plan, scope, context);

            if (usesStructuralHotReload)
            {
                _writer.WriteLine();
                WritePrepareRenderRevision();
            }

            WriteContentPresenterRefresh(plan, scope);
        }

        if (usesStructuralHotReload)
        {
            _writer.WriteLine();
            WriteCompleteRenderRevision();
        }

        WriteReturnRoot(plan);

        if (usesStructuralHotReload)
        {
            WriteRenderRevisionTryEnd();
        }

        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WriteBeginRenderRevision()
    {
        _writer.Write("var ");
        _writer.Write(RenderRevisionChangedLocalName);
        _writer.Write(" = ");
        _writer.Write(
            ComponentStructuralHotReloadWriter.EnsureRenderTreeMethodName);
        _writer.WriteLine("();");
    }

    private void WriteStructuralRevisionInitialization(in ComponentPlan plan)
    {
        _writer.Write("if (");
        _writer.Write(RenderRevisionChangedLocalName);
        _writer.WriteLine(")");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write(HotReloadUpdateInitialValuesMethodName);
        _writer.WriteLine("();");

        var lifecycle = plan.Lifecycle;
        if (!lifecycle.HasExplicitRootDataContext)
        {
            Debug.Assert(lifecycle.HasRootElement);
            Debug.Assert(
                (uint)lifecycle.RootElementId < (uint)plan.Elements.Length);

            ref readonly var root = ref plan.Elements.ItemRef(
                lifecycle.RootElementId);

            _writer.WriteLine();
            WriteStructuralRootDataContextBinding(root);
        }

        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WriteRenderRevisionTryStart()
    {
        _writer.WriteLine("try");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
    }

    private void WriteRenderRevisionTryEnd()
    {
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
        _writer.WriteLine(
            "catch (global::System.Exception __exception)");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write("if (");
        _writer.Write(RenderRevisionChangedLocalName);
        _writer.WriteLine(")");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write(ComponentStructuralHotReloadWriter.RenderStateFieldName);
        _writer.WriteLine(".AbortRevision(__exception);");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
        _writer.WriteLine();
        _writer.WriteLine("throw;");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WriteCompleteRenderRevision()
    {
        _writer.Write("if (");
        _writer.Write(RenderRevisionChangedLocalName);
        _writer.WriteLine(")");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write(
            ComponentStructuralHotReloadWriter.RenderStateFieldName);
        _writer.WriteLine(".CompleteRevision();");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WritePrepareRenderRevision()
    {
        _writer.Write("if (");
        _writer.Write(RenderRevisionChangedLocalName);
        _writer.WriteLine(")");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write(
            ComponentStructuralHotReloadWriter.RenderStateFieldName);
        _writer.WriteLine(".PrepareRevisionCompletion();");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WriteHotReloadUpdateInitialValues(in ComponentPlan plan)
    {
        _writer.WriteLine("#if DEBUG");
        _writer.WriteHiddenApiAttributes();
        _writer.Write("private void ");
        _writer.Write(HotReloadUpdateInitialValuesMethodName);
        _writer.WriteLine("()");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;

        if (!plan.Lifecycle.UsesFallbackRoot)
        {
            Debug.Assert(!plan.Scopes.IsDefaultOrEmpty);

            ref readonly var scope = ref plan.Scopes.ItemRef(0);
            var context = CreateComponentScopeContext(plan);
            var scopeWriter = new ComponentScopeWriter(
                _writer,
                in _bindingEnvironment,
                _sourceMap,
                _ownerTypeName,
                _generationMode);

            scopeWriter.WriteHotReloadState(plan, scope, context);
        }

        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
        _writer.WriteLine("#endif");
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

    private bool UsesStructuralHotReload(in ComponentPlan plan)
    {
        return _generationMode == ComponentGenerationMode.DebugStructural &&
            !plan.Lifecycle.UsesFallbackRoot;
    }
}
