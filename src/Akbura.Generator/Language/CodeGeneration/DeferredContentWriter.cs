using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Writes deferred-content builder methods and the corresponding lazy factory
/// values without consulting the semantic model.
/// </summary>
internal readonly ref struct DeferredContentWriter
{
    private readonly CodeWriter _writer;
    private readonly BindingWriterEnvironment _bindingEnvironment;
    private readonly ComponentGenerationSourceMap _sourceMap;
    private readonly string _ownerTypeName;
    private readonly ComponentGenerationMode _generationMode;

    public DeferredContentWriter(
        CodeWriter writer,
        in BindingWriterEnvironment bindingEnvironment,
        ComponentGenerationSourceMap sourceMap,
        string ownerTypeName,
        ComponentGenerationMode generationMode =
            ComponentGenerationMode.ReleaseDirect)
    {
        Debug.Assert(writer != null);
        Debug.Assert(sourceMap != null);
        Debug.Assert(!string.IsNullOrEmpty(ownerTypeName));

        _writer = writer!;
        _bindingEnvironment = bindingEnvironment;
        _sourceMap = sourceMap!;
        _ownerTypeName = ownerTypeName;
        _generationMode = generationMode;
    }

    public bool WriteBuilder(
        in ComponentPlan plan,
        in ComponentDeferredContentPlan deferred)
    {
        Debug.Assert((uint)deferred.ScopeId < (uint)plan.Scopes.Length);

        if ((uint)deferred.ScopeId >= (uint)plan.Scopes.Length)
        {
            return false;
        }

        ref readonly var scope = ref plan.Scopes.ItemRef(deferred.ScopeId);
        Debug.Assert(scope.Id == deferred.ScopeId);
        Debug.Assert(scope.Kind == ComponentElementScopeKind.DeferredContent);
        Debug.Assert(scope.Roots.Length == 1);

        if (scope.Kind != ComponentElementScopeKind.DeferredContent ||
            scope.Roots.Length != 1)
        {
            return false;
        }

        var indent = _writer.CurrentIndent;

        try
        {
            var rootId = plan.ScopeRootElementIds[scope.Roots.Start];
            ref readonly var root = ref plan.Elements.ItemRef(rootId);
            _writer.WriteHiddenApiAttributes();
            _writer.Write(root.IsConditionalTemplateRoot ?
                "private global::Akbura.Markup.AkburaConditionalTemplateInstance " : "private object ");
            WriteBuilderName(deferred.Id);
            _writer.Write("(global::System.IServiceProvider __services");
            if (ComponentLocalScopeWriter.CanWrite(plan, scope))
            {
                var values = new CSharpValueWriter(_writer);
                foreach (var ancestor in TemplateWriter.GetAncestorTemplates(plan, scope.Id, deferred.ItemName))
                {
                    _writer.Write(", ");
                    values.WriteTypeName(ancestor.DataType);
                    _writer.Write(" ");
                    values.WriteIdentifier(ancestor.ItemName);
                }

                _writer.Write(", global::Akbura.HotReload.AkburaRenderState __parentRenderState, object __parentOwner");
                if (root.IsConditionalTemplateRoot)
                {
                    _writer.Write(", global::Avalonia.Controls.Control __templateHost, global::Avalonia.Controls.INameScope __nameScope");
                }
            }

            _writer.WriteLine(")");
            _writer.WriteLine("{");
            _writer.CurrentIndent += _writer.TabSize;

            if (root.IsConditionalTemplateRoot && deferred.DataType != null && !string.IsNullOrEmpty(deferred.ItemName))
            {
                var values = new CSharpValueWriter(_writer);
                _writer.Write("var ");
                values.WriteIdentifier(deferred.ItemName!);
                _writer.Write(" = (");
                values.WriteTypeName(deferred.DataType);
                _writer.WriteLine(")((global::Avalonia.Controls.Presenters.ContentPresenter)__templateHost).Content!;");
            }

            if (scope.RequiresNameScope && !root.IsConditionalTemplateRoot)
            {
                WriteNameScope();
            }

            Debug.Assert((uint)rootId < (uint)plan.Elements.Length);
            var rootExpression = root.Identifier;
            var context = new ComponentScopeWriteContext(
                rootExpression,
                "__akburaBaseUri",
                "__services",
                scope.RequiresNameScope || root.IsConditionalTemplateRoot ? "__nameScope" : null,
                scope.Id,
                MarkupParentStackTraversalKind.ExactScope,
                plan.Elements.AsSpan(),
                plan.ElementReferences.AsSpan());
            var scopeWriter = new ComponentScopeWriter(
                _writer,
                in _bindingEnvironment,
                _sourceMap,
                _ownerTypeName,
                _generationMode);

            if (ComponentLocalScopeWriter.CanWrite(plan, scope))
            {
                new ComponentLocalScopeWriter(_writer, in _bindingEnvironment, _sourceMap, _ownerTypeName, _generationMode)
                    .WriteInitialState(plan, scope, context);
            }
            else
            {
                scopeWriter.WriteLocalInitialState(plan, scope, context);
            }

            _writer.Write("return ");
            _writer.Write(root.IsConditionalTemplateRoot ? "__localRoot" : root.Identifier);
            _writer.WriteLine(";");

            _writer.CurrentIndent -= _writer.TabSize;
            _writer.WriteLine("}");
            return true;
        }
        finally
        {
            _writer.CurrentIndent = indent;
        }
    }

    public bool WriteValue(
        in ComponentPlan plan,
        in ComponentPropertyContentPlan content,
        in ComponentDeferredContentPlan deferred,
        in MarkupExtensionWriteContext parentContext)
    {
        Debug.Assert((uint)content.OwnerElementId < (uint)plan.Elements.Length);
        Debug.Assert(deferred.TargetElementId == content.OwnerElementId);

        if ((uint)content.OwnerElementId >= (uint)plan.Elements.Length ||
            !content.Destination.IsValid ||
            !CanWriteBuilder(plan, deferred))
        {
            return false;
        }

        ref readonly var owner = ref plan.Elements.ItemRef(content.OwnerElementId);
        var targetExpression = owner.Identifier;
        var targetContext = parentContext.WithTarget(
            targetExpression,
            content.Destination.TargetProperty,
            owner.ScopeId,
            plan.ElementReferences.AsSpan());

        if (!MarkupServiceProviderWriter.CanWrite(targetContext))
        {
            Debug.Fail("The deferred-content service-provider context is incomplete.");
            return false;
        }

        var conditional = ComponentLocalScopeWriter.CanWrite(plan, plan.Scopes.ItemRef(deferred.ScopeId));
        ref readonly var deferredScope = ref plan.Scopes.ItemRef(deferred.ScopeId);
        var conditionalRoot = plan.Elements.ItemRef(plan.ScopeRootElementIds[deferredScope.Roots.Start]).IsConditionalTemplateRoot;
        if (conditional)
        {
            _writer.Write("var __deferredParentState").WriteIntegerLiteral(deferred.Id)
                .WriteLine(" = __akburaRenderState;");
            _writer.Write("var __deferredOwner").WriteIntegerLiteral(deferred.Id).Write(" = ")
                .Write(targetExpression).WriteLine(";");
            if (conditionalRoot)
            {
                _writer.Write("var __deferredServices").WriteIntegerLiteral(deferred.Id).Write(" = ");
                new MarkupServiceProviderWriter(_writer).Write(targetContext, includeNameScope: true, retainedFactory: true);
                _writer.WriteLine(";");
            }
        }

        using var mapping = new SourceMappingWriter(_writer, _sourceMap)
            .WriteStart(content.Syntax);
        var propertyWriter = new PropertyWriter(_writer);
        var factoryIdentity = conditional
            ? ComponentHotReloadIdentity.CreateLocalDeferredFactoryIdentity(plan, content, deferred)
            : null;
        var structural = new ComponentContentWriter(_writer, _sourceMap)
            .WriteStructuralValueStart(plan, content, factoryIdentity);
        var end = PropertyWriteEnd.None;
        if (!structural)
        {
            end = propertyWriter.WriteStart(content.Destination, targetExpression);
            if (end == PropertyWriteEnd.None)
            {
                return false;
            }
        }

        if (conditionalRoot)
        {
            WriteConditionalRootFactory(plan, deferred);
        }
        else
        {
            WriteFactoryExpression(plan, deferred, targetContext, conditional);
        }
        if (structural)
        {
            new ComponentContentWriter(_writer, _sourceMap).WriteStructuralValueEnd();
        }
        else
        {
            propertyWriter.WriteEnd(end);
        }
        if (!structural)
        {
            _writer.WriteLine();
        }
        return true;
    }

    private void WriteConditionalRootFactory(in ComponentPlan plan, in ComponentDeferredContentPlan deferred)
    {
        _writer.Write("new global::Akbura.Markup.AkburaConditionalDeferredTemplate((__templateHost, __nameScope) => ");
        WriteBuilderName(deferred.Id);
        _writer.Write("(__deferredServices").WriteIntegerLiteral(deferred.Id);
        var values = new CSharpValueWriter(_writer);
        foreach (var ancestor in TemplateWriter.GetAncestorTemplates(plan, deferred.ScopeId, deferred.ItemName))
        {
            _writer.Write(", ");
            values.WriteIdentifier(ancestor.ItemName);
        }

        _writer.Write(", __deferredParentState").WriteIntegerLiteral(deferred.Id);
        _writer.Write(", __deferredOwner").WriteIntegerLiteral(deferred.Id);
        _writer.Write(", __templateHost, __nameScope))");
    }

    private void WriteFactoryExpression(
        in ComponentPlan plan,
        in ComponentDeferredContentPlan deferred,
        in MarkupExtensionWriteContext context,
        bool conditional)
    {
        var valueWriter = new CSharpValueWriter(_writer);

        var retainedFactory = conditional && !string.IsNullOrEmpty(context.NameScopeExpression);
        _writer.Write(retainedFactory ? "new global::Akbura.Markup.AkburaConditionalDeferredContent<" : "CreateDeferredContent<");
        valueWriter.WriteTypeName(deferred.ResultType);
        _writer.WriteLine(">(");
        _writer.CurrentIndent += _writer.TabSize;

        if (!conditional)
        {
            _writer.Write("static ");
        }

        _writer.Write("__services => ((");
        _writer.Write(_ownerTypeName);
        _writer.Write(")((global::Avalonia.Markup.Xaml.IRootObjectProvider)");
        _writer.Write("__services.GetService(typeof(");
        _writer.Write("global::Avalonia.Markup.Xaml.IRootObjectProvider))!).RootObject).");
        WriteBuilderName(deferred.Id);
        _writer.Write("(__services");
        if (conditional)
        {
            foreach (var ancestor in TemplateWriter.GetAncestorTemplates(plan, deferred.ScopeId))
            {
                _writer.Write(", ");
                valueWriter.WriteIdentifier(ancestor.ItemName);
            }

            _writer.Write(", __deferredParentState").WriteIntegerLiteral(deferred.Id);
            _writer.Write(", __deferredOwner").WriteIntegerLiteral(deferred.Id);
        }

        _writer.WriteLine("),");

        var serviceProviderWriter = new MarkupServiceProviderWriter(_writer);
        if (!serviceProviderWriter.Write(context, includeNameScope: conditional, retainedFactory: retainedFactory))
        {
            _writer.Write("default!");
        }

        _writer.CurrentIndent -= _writer.TabSize;
        _writer.Write(")");
    }

    private void WriteNameScope()
    {
        _writer.Write("var __nameScope = (global::Avalonia.Controls.INameScope)");
        _writer.Write("__services.GetService(typeof(");
        _writer.WriteLine("global::Avalonia.Controls.INameScope))!;");
    }

    private void WriteBuilderName(int id)
    {
        _writer.Write("__BuildDeferredContent");
        _writer.WriteIntegerLiteral(id);
    }

    internal static bool CanWriteBuilder(
        in ComponentPlan plan,
        in ComponentDeferredContentPlan deferred)
    {
        if ((uint)deferred.ScopeId >= (uint)plan.Scopes.Length)
        {
            return false;
        }

        ref readonly var scope = ref plan.Scopes.ItemRef(deferred.ScopeId);
        return scope.Id == deferred.ScopeId &&
            scope.Kind == ComponentElementScopeKind.DeferredContent &&
            scope.Roots.Length == 1;
    }

}
