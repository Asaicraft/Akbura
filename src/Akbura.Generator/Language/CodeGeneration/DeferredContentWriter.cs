using System.Diagnostics;
using CSharpSyntaxFacts = Microsoft.CodeAnalysis.CSharp.SyntaxFacts;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

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

    public DeferredContentWriter(
        CodeWriter writer,
        in BindingWriterEnvironment bindingEnvironment,
        ComponentGenerationSourceMap sourceMap,
        string ownerTypeName)
    {
        Debug.Assert(writer != null);
        Debug.Assert(sourceMap != null);
        Debug.Assert(!string.IsNullOrEmpty(ownerTypeName));

        _writer = writer!;
        _bindingEnvironment = bindingEnvironment;
        _sourceMap = sourceMap!;
        _ownerTypeName = ownerTypeName;
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
            _writer.Write("private object ");
            WriteBuilderName(deferred.Id);
            _writer.WriteLine("(global::System.IServiceProvider __services)");
            _writer.WriteLine("{");
            _writer.CurrentIndent += _writer.TabSize;

            if (scope.RequiresNameScope)
            {
                WriteNameScope();
            }

            var rootId = plan.ScopeRootElementIds[scope.Roots.Start];
            Debug.Assert((uint)rootId < (uint)plan.Elements.Length);
            ref readonly var root = ref plan.Elements.ItemRef(rootId);
            var rootExpression = EscapeIdentifier(root.Identifier);
            var context = new ComponentScopeWriteContext(
                rootExpression,
                "__akburaBaseUri",
                "__services",
                scope.RequiresNameScope ? "__nameScope" : null,
                scope.Id,
                plan.Elements.AsSpan(),
                plan.ElementReferences.AsSpan());
            var scopeWriter = new ComponentScopeWriter(
                _writer,
                in _bindingEnvironment,
                _sourceMap,
                _ownerTypeName);

            scopeWriter.WriteInitialState(plan, scope, context);

            _writer.Write("return ");
            var valueWriter = new CSharpValueWriter(_writer);
            valueWriter.WriteIdentifier(root.Identifier);
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
        var targetExpression = EscapeIdentifier(owner.Identifier);
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

        using var mapping = new SourceMappingWriter(_writer, _sourceMap)
            .WriteStart(content.Syntax);
        var propertyWriter = new PropertyWriter(_writer);
        var end = propertyWriter.WriteStart(content.Destination, targetExpression);

        if (end == PropertyWriteEnd.None)
        {
            return false;
        }

        WriteFactoryExpression(deferred, targetContext);
        propertyWriter.WriteEnd(end);
        _writer.WriteLine();
        return true;
    }

    private void WriteFactoryExpression(
        in ComponentDeferredContentPlan deferred,
        in MarkupExtensionWriteContext context)
    {
        var valueWriter = new CSharpValueWriter(_writer);

        _writer.Write("CreateDeferredContent<");
        valueWriter.WriteTypeName(deferred.ResultType);
        _writer.WriteLine(">(");
        _writer.CurrentIndent += _writer.TabSize;

        _writer.Write("static __services => ((");
        _writer.Write(_ownerTypeName);
        _writer.Write(")((global::Avalonia.Markup.Xaml.IRootObjectProvider)");
        _writer.Write("__services.GetService(typeof(");
        _writer.Write("global::Avalonia.Markup.Xaml.IRootObjectProvider))!).RootObject).");
        WriteBuilderName(deferred.Id);
        _writer.WriteLine("(__services),");

        var serviceProviderWriter = new MarkupServiceProviderWriter(_writer);
        if (!serviceProviderWriter.Write(context))
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

    private static string EscapeIdentifier(string identifier)
    {
        return CSharpSyntaxFacts.GetKeywordKind(identifier) != CSharpSyntaxKind.None ||
            CSharpSyntaxFacts.GetContextualKeywordKind(identifier) != CSharpSyntaxKind.None
                ? "@" + identifier
                : identifier;
    }
}
