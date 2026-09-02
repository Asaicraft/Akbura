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

    public TemplateWriter(
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
        _bindingEnvironment = bindingEnvironment;
        _sourceMap = sourceMap!;
        _ownerTypeName = ownerTypeName;
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

        // Mapping is deliberately closed before the lambda body. Statements
        // emitted by ComponentScopeWriter carry their own source mappings.
        {
            using var mapping = new SourceMappingWriter(_writer, _sourceMap)
                .WriteStart(content.Syntax);

            end = propertyWriter.WriteStart(content.Destination, targetExpression);
            if (end == PropertyWriteEnd.None)
            {
                return false;
            }

            WriteHeader(template);
        }

        WriteBody(plan, template, parentContext);

        _writer.Write(")");
        propertyWriter.WriteEnd(end);
        _writer.WriteLine();
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
        return root.ScopeId == template.ScopeId && root.IsControl;
    }

    private void WriteHeader(in ComponentTemplatePlan template)
    {
        _writer.Write("new global::Avalonia.Controls.Templates.FuncDataTemplate<");
        _valueWriter.WriteTypeName(template.DataType);
        _writer.Write(">((");
        _valueWriter.WriteIdentifier(template.ItemName);
        _writer.Write(", __nameScope) =>");
    }

    private void WriteBody(
        in ComponentPlan plan,
        in ComponentTemplatePlan template,
        in MarkupExtensionWriteContext parentContext)
    {
        ref readonly var scope = ref plan.Scopes.ItemRef(template.ScopeId);
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
            _ownerTypeName);

        scopeWriter.WriteLocalInitialState(plan, scope, scopeContext);

        _writer.Write("return ");
        _writer.Write(root.Identifier);
        _writer.WriteLine(";");

        _writer.CurrentIndent -= _writer.TabSize;
        _writer.Write("}");
    }

}
