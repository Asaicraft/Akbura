namespace Akbura.Language.CodeGeneration;

internal readonly ref struct ComponentLocalScopeWriter
{
    private readonly CodeWriter _writer;
    private readonly BindingWriterEnvironment _bindingEnvironment;
    private readonly ComponentGenerationSourceMap _sourceMap;
    private readonly string _ownerTypeName;
    private readonly ComponentGenerationMode _generationMode;

    public ComponentLocalScopeWriter(CodeWriter writer, in BindingWriterEnvironment bindingEnvironment,
        ComponentGenerationSourceMap sourceMap, string ownerTypeName, ComponentGenerationMode generationMode)
    {
        _writer = writer;
        _bindingEnvironment = bindingEnvironment;
        _sourceMap = sourceMap;
        _ownerTypeName = ownerTypeName;
        _generationMode = generationMode;
    }

    internal static bool CanWrite(in ComponentPlan plan, in ComponentScopePlan scope)
    {
        if (scope.Roots.Length != 1)
        {
            return false;
        }

        ref readonly var root = ref plan.Elements.ItemRef(plan.ScopeRootElementIds[scope.Roots.Start]);
        return root.UsesRuntimeStorage && (root.IsControl || root.IsConditionalTemplateRoot) &&
            root.RuntimeStorageRootScopeId == scope.Id;
    }

    public void WriteInitialState(in ComponentPlan plan, in ComponentScopePlan scope,
        in ComponentScopeWriteContext context)
    {
        var rootId = plan.ScopeRootElementIds[scope.Roots.Start];
        ref readonly var root = ref plan.Elements.ItemRef(rootId);
        var values = new CSharpValueWriter(_writer);
        if (root.IsConditionalTemplateRoot)
        {
            _writer.WriteLine("global::Akbura.Markup.AkburaConditionalTemplateInstance __localRoot = null!;");
        }
        else
        {
            _writer.Write("var __localRoot = new ");
            values.WriteTypeName(root.Type);
            _writer.WriteLine("();");
        }
        _writer.WriteLine("var __akburaRenderState = new global::Akbura.HotReload.AkburaRenderState();");
        _writer.WriteLine("global::System.Action<global::Akbura.HotReload.AkburaRenderPlanBuilder> __localDescribe = __builder =>");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        new ComponentStructuralHotReloadWriter(_writer).WriteDescriptionContents(plan, scope.Id);
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("};");
        _writer.WriteLine("global::System.Func<int, object> __localFactory = __localId => __localId switch");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        foreach (var element in plan.Elements)
        {
            if (!element.UsesRuntimeStorage || element.RuntimeStorageRootScopeId != scope.Id)
            {
                continue;
            }

            _writer.WriteIntegerLiteral(element.RuntimeStorageId).Write(" => ");
            if (element.Id == rootId)
            {
                _writer.Write("__localRoot");
            }
            else
            {
                _writer.Write("new ");
                values.WriteTypeName(element.Type);
                _writer.Write("()");
            }

            _writer.WriteLine(",");
        }

        _writer.WriteLine("_ => throw new global::System.ArgumentOutOfRangeException(nameof(__localId)),");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("};");
        _writer.WriteHiddenApiAttributes();
        _writer.WriteLine("void __RenderLocalScope()");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("__akburaRenderState.SetRenderCaptureParent(__parentRenderState);");
        new ComponentRenderCaptureWriter(_writer).WriteReads(plan, scope.Id);
        new ComponentConditionalCaptureWriter(_writer).WriteReads(plan, scope.Id);
        _writer.Write("__akburaRenderState.BeginRevision(");
        _writer.WriteStringLiteral(ComponentHotReloadIdentity.CreateRenderFingerprint(plan));
        _writer.WriteLine(", __localDescribe, __localFactory);");
        _writer.WriteLine("try");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        var scopes = new ComponentScopeWriter(_writer, in _bindingEnvironment, _sourceMap, _ownerTypeName, _generationMode);
        if (scope.Kind == ComponentElementScopeKind.DataTemplate ||
            !string.IsNullOrEmpty(context.NameScopeExpression))
        {
            scopes.WriteLocalNameScope(plan, scope);
            var localContext = new ComponentScopeWriteContext(context.IntermediateRootExpression,
                context.BaseUriExpression, context.FallbackServiceProviderExpression,
                "__localNameScope", scope.Id, context.ParentStackTraversalKind,
                plan.Elements.AsSpan(), plan.ElementReferences.AsSpan(), isConditionalNameScope: true);
            scopes.WriteLocalConditionalRender(plan, scope, localContext);
        }
        else
        {
            scopes.WriteLocalConditionalRender(plan, scope, context);
        }
        _writer.WriteLine("if (__akburaRenderState.HasPendingRevision)");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("__akburaRenderState.CompleteRevision();");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
        _writer.WriteLine("catch (global::System.Exception __exception)");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("__akburaRenderState.AbortRevision(__exception);");
        _writer.WriteLine("throw;");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
        if (root.IsConditionalTemplateRoot)
        {
            _writer.WriteLine("__localRoot = new global::Akbura.Markup.AkburaConditionalTemplateInstance(__RenderLocalScope,");
            _writer.CurrentIndent += _writer.TabSize;
            _writer.WriteLine("__akburaRenderState.Dispose);");
            _writer.CurrentIndent -= _writer.TabSize;
            _writer.WriteLine("var __localScopeLease = __AkburaRegisterLocalRenderScope(__templateHost, __localRoot.Update,");
        }
        else
        {
            _writer.WriteLine("__RenderLocalScope();");
            _writer.WriteLine("var __localScopeLease = __AkburaRegisterLocalRenderScope(__localRoot, __RenderLocalScope,");
        }
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("__akburaRenderState.Dispose, __parentRenderState);");
        _writer.CurrentIndent -= _writer.TabSize;
        if (root.IsConditionalTemplateRoot)
        {
            _writer.WriteLine("__localRoot.SetLifetime(__localScopeLease, __parentRenderState);");
        }

        _writer.WriteLine("try");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write("__parentRenderState.RegisterLocalRenderScopeLifetime(__parentOwner, ")
            .Write(root.IsConditionalTemplateRoot ? "__localRoot" : "__localScopeLease").WriteLine(");");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
        _writer.WriteLine("catch");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write(root.IsConditionalTemplateRoot ? "__localRoot" : "__localScopeLease").WriteLine(".Dispose();");
        _writer.WriteLine("throw;");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }
}
