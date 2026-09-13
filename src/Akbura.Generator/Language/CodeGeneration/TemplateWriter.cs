using System.Collections.Generic;
using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Writes typed FuncDataTemplate values by reusing the common component-scope
/// initialization pipeline.
/// </summary>
internal readonly ref struct TemplateWriter
{
    private readonly CodeWriter _writer;
    private readonly CSharpValueWriter _valueWriter;
    private readonly BindingWriterEnvironment _bindingEnvironment;
    private readonly ComponentGenerationSourceMap _sourceMap;
    private readonly string _ownerTypeName;
    private readonly ComponentGenerationMode _generationMode;

    public TemplateWriter(
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
        _valueWriter = new CSharpValueWriter(writer!);
        _bindingEnvironment = bindingEnvironment;
        _sourceMap = sourceMap!;
        _ownerTypeName = ownerTypeName;
        _generationMode = generationMode;
    }

    public bool WriteValue(
        in ComponentPlan plan,
        in ComponentPropertyContentPlan content,
        in ComponentTemplatePlan template,
        in MarkupExtensionWriteContext parentContext)
    {
        Debug.Assert(template.OwnerElementId == content.OwnerElementId);

        if ((uint)content.OwnerElementId >= (uint)plan.Elements.Length ||
            !content.Destination.IsValid ||
            template.OwnerElementId != content.OwnerElementId ||
            !CanWrite(plan, template))
        {
            Debug.Fail("An invalid template plan reached code generation.");
            return false;
        }

        ref readonly var owner = ref plan.Elements.ItemRef(content.OwnerElementId);
        var targetExpression = owner.Identifier;
        var propertyWriter = new PropertyWriter(_writer);
        var end = PropertyWriteEnd.None;
        var structural = false;
        string? conditionalServices = null;
        ref readonly var scope = ref plan.Scopes.ItemRef(template.ScopeId);
        if (ComponentLocalScopeWriter.CanWrite(plan, scope))
        {
            conditionalServices = "__templateServices" + template.Id;
            _writer.Write("var __templateParentState").WriteIntegerLiteral(template.Id)
                .WriteLine(" = __akburaRenderState;");
            _writer.Write("var __templateOwner").WriteIntegerLiteral(template.Id).Write(" = ")
                .Write(targetExpression).WriteLine(";");
            var targetContext = parentContext.WithTarget(targetExpression, content.Destination.TargetProperty,
                owner.ScopeId, plan.ElementReferences.AsSpan());
            if (!MarkupServiceProviderWriter.CanWrite(targetContext))
            {
                Debug.Fail("The conditional-template service-provider context is incomplete.");
                return false;
            }

            _writer.Write("var ").Write(conditionalServices).Write(" = ");
            new MarkupServiceProviderWriter(_writer).Write(targetContext, includeNameScope: true, retainedFactory: true);
            _writer.WriteLine(";");
        }

        // Mapping is deliberately closed before the lambda body. Statements
        // emitted by ComponentScopeWriter carry their own source mappings.
        {
            using var mapping = new SourceMappingWriter(_writer, _sourceMap)
                .WriteStart(content.Syntax);

            var factoryIdentity = ComponentLocalScopeWriter.CanWrite(plan, scope)
                ? ComponentHotReloadIdentity.CreateLocalTemplateFactoryIdentity(plan, content, template)
                : null;
            structural = new ComponentContentWriter(_writer, _sourceMap)
                .WriteStructuralValueStart(plan, content, factoryIdentity);
            if (!structural)
            {
                end = propertyWriter.WriteStart(content.Destination, targetExpression);
                if (end == PropertyWriteEnd.None)
                {
                    return false;
                }
            }

            WriteHeader(plan, template);
        }

        WriteBody(plan, template, parentContext, conditionalServices);

        _writer.Write(")");
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

    internal static bool CanWrite(
        in ComponentPlan plan,
        in ComponentTemplatePlan template)
    {
        if ((uint)template.ScopeId >= (uint)plan.Scopes.Length ||
            template.DataType == null ||
            string.IsNullOrEmpty(template.ItemName))
        {
            return false;
        }

        ref readonly var scope = ref plan.Scopes.ItemRef(template.ScopeId);
        if (scope.Id != template.ScopeId ||
            scope.Kind != ComponentElementScopeKind.DataTemplate ||
            scope.Roots.Length != 1 ||
            (uint)scope.Roots.Start >= (uint)plan.ScopeRootElementIds.Length)
        {
            return false;
        }

        var rootId = plan.ScopeRootElementIds[scope.Roots.Start];
        if ((uint)rootId >= (uint)plan.Elements.Length)
        {
            return false;
        }

        ref readonly var root = ref plan.Elements.ItemRef(rootId);
        return root.ScopeId == template.ScopeId && (root.IsControl || root.IsConditionalTemplateRoot);
    }

    private void WriteHeader(in ComponentPlan plan, in ComponentTemplatePlan template)
    {
        ref readonly var scope = ref plan.Scopes.ItemRef(template.ScopeId);
        var conditionalRoot = plan.Elements.ItemRef(plan.ScopeRootElementIds[scope.Roots.Start]).IsConditionalTemplateRoot;
        _writer.Write(conditionalRoot ? "new global::Akbura.Markup.AkburaConditionalDataTemplate<" :
            "new global::Avalonia.Controls.Templates.FuncDataTemplate<");
        _valueWriter.WriteTypeName(template.DataType);
        _writer.Write(">((");
        _valueWriter.WriteIdentifier(template.ItemName);
        _writer.Write(conditionalRoot ? ", __nameScope, __templateHost) =>" : ", __nameScope) =>");
    }

    private void WriteBody(
        in ComponentPlan plan,
        in ComponentTemplatePlan template,
        in MarkupExtensionWriteContext parentContext,
        string? conditionalServices)
    {
        ref readonly var scope = ref plan.Scopes.ItemRef(template.ScopeId);
        if (ComponentLocalScopeWriter.CanWrite(plan, scope))
        {
            WriteConditionalBuilderName(template.Id);
            _writer.Write("(");
            _valueWriter.WriteIdentifier(template.ItemName);
            foreach (var ancestor in GetAncestorTemplates(plan, template))
            {
                _writer.Write(", ");
                _valueWriter.WriteIdentifier(ancestor.ItemName);
            }
            _writer.Write(", __nameScope, ");
            _writer.Write(conditionalServices!);
            _writer.Write(", __templateParentState").WriteIntegerLiteral(template.Id);
            _writer.Write(", __templateOwner").WriteIntegerLiteral(template.Id);
            if (plan.Elements.ItemRef(plan.ScopeRootElementIds[scope.Roots.Start]).IsConditionalTemplateRoot)
            {
                _writer.Write(", __templateHost");
            }

            _writer.Write(")");
            return;
        }
        var rootId = plan.ScopeRootElementIds[scope.Roots.Start];
        ref readonly var root = ref plan.Elements.ItemRef(rootId);
        var rootExpression = root.Identifier;

        _writer.WriteLine();
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;

        var traversalKind = string.IsNullOrEmpty(parentContext.FallbackServiceProviderExpression)
            ? MarkupParentStackTraversalKind.FullHierarchy
            : MarkupParentStackTraversalKind.ExactScope;
        var scopeContext = new ComponentScopeWriteContext(
            rootExpression,
            parentContext.BaseUriExpression,
            parentContext.FallbackServiceProviderExpression,
            "__nameScope",
            template.ScopeId,
            traversalKind,
            plan.Elements.AsSpan(),
            plan.ElementReferences.AsSpan());
        var scopeWriter = new ComponentScopeWriter(
            _writer,
            in _bindingEnvironment,
            _sourceMap,
            _ownerTypeName,
            _generationMode);

        scopeWriter.WriteLocalInitialState(plan, scope, scopeContext);

        _writer.Write("return ");
        _writer.Write(root.Identifier);
        _writer.WriteLine(";");

        _writer.CurrentIndent -= _writer.TabSize;
        _writer.Write("}");
    }

    public bool WriteConditionalBuilder(in ComponentPlan plan, in ComponentTemplatePlan template)
    {
        ref readonly var scope = ref plan.Scopes.ItemRef(template.ScopeId);
        if (!ComponentLocalScopeWriter.CanWrite(plan, scope))
        {
            return false;
        }

        ref readonly var root = ref plan.Elements.ItemRef(plan.ScopeRootElementIds[scope.Roots.Start]);
        _writer.WriteHiddenApiAttributes();
        _writer.Write("private ");
        _valueWriter.WriteTypeName(root.Type);
        _writer.Write(" ");
        WriteConditionalBuilderName(template.Id);
        _writer.Write("(");
        _valueWriter.WriteTypeName(template.DataType);
        _writer.Write(" ");
        _valueWriter.WriteIdentifier(template.ItemName);
        foreach (var ancestor in GetAncestorTemplates(plan, template))
        {
            _writer.Write(", ");
            _valueWriter.WriteTypeName(ancestor.DataType);
            _writer.Write(" ");
            _valueWriter.WriteIdentifier(ancestor.ItemName);
        }
        _writer.WriteLine(", global::Avalonia.Controls.INameScope __nameScope, global::System.IServiceProvider? __services,");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write("global::Akbura.HotReload.AkburaRenderState __parentRenderState, object __parentOwner");
        if (root.IsConditionalTemplateRoot)
        {
            _writer.Write(", global::Avalonia.Controls.Presenters.ContentPresenter __templateHost");
        }

        _writer.WriteLine(")");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        var context = new ComponentScopeWriteContext(root.Identifier, "__akburaBaseUri", "__services",
            "__nameScope", scope.Id, MarkupParentStackTraversalKind.ExactScope,
            plan.Elements.AsSpan(), plan.ElementReferences.AsSpan());
        new ComponentLocalScopeWriter(_writer, in _bindingEnvironment, _sourceMap, _ownerTypeName, _generationMode)
            .WriteInitialState(plan, scope, context);
        _writer.Write("return ").Write(root.IsConditionalTemplateRoot ? "__localRoot" : root.Identifier).WriteLine(";");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
        return true;
    }

    private void WriteConditionalBuilderName(int id) =>
        _writer.Write("__BuildConditionalTemplate").WriteIntegerLiteral(id);

    private static List<ComponentTemplatePlan> GetAncestorTemplates(in ComponentPlan plan, in ComponentTemplatePlan template)
        => GetAncestorTemplates(plan, template.ScopeId, template.ItemName);

    internal static List<ComponentTemplatePlan> GetAncestorTemplates(in ComponentPlan plan, int scopeId,
        string? excludedItemName = null)
    {
        var ancestors = new List<ComponentTemplatePlan>();
        var names = new HashSet<string>();
        if (excludedItemName != null)
        {
            names.Add(excludedItemName);
        }

        for (var ancestorScopeId = plan.Scopes.ItemRef(scopeId).ParentScopeId; ancestorScopeId > 0;
            ancestorScopeId = plan.Scopes.ItemRef(ancestorScopeId).ParentScopeId)
        {
            foreach (var candidate in plan.Templates)
            {
                if (candidate.ScopeId == ancestorScopeId && names.Add(candidate.ItemName))
                {
                    ancestors.Add(candidate);
                }
            }
        }

        return ancestors;
    }

}
