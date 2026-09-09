using Akbura.Language.Operations;
using Microsoft.CodeAnalysis;
using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

internal readonly ref struct ComponentPropertyWriter
{
    private readonly CodeWriter _writer;
    private readonly BindingWriterEnvironment _environment;
    private readonly SourceMappingWriter _mappings;
    private readonly ComponentValueWriter _valueWriter;

    public ComponentPropertyWriter(
        CodeWriter writer,
        in BindingWriterEnvironment environment,
        ComponentGenerationSourceMap sourceMap)
    {
        Debug.Assert(writer != null);
        Debug.Assert(sourceMap != null);

        _writer = writer!;
        _environment = environment;
        _mappings = new SourceMappingWriter(writer!, sourceMap!);
        _valueWriter = new ComponentValueWriter(writer!);
    }

    public void Write(
        in ComponentPlan component,
        in ComponentPropertyWritePlan plan,
        string targetExpression,
        in MarkupExtensionWriteContext context)
    {
        Debug.Assert(plan.Destination.IsValid);
        Debug.Assert(!string.IsNullOrEmpty(targetExpression));

        if (!plan.Destination.IsValid || string.IsNullOrEmpty(targetExpression))
        {
            Debug.Fail("An invalid component property write reached code generation.");
            return;
        }

        using var mapping = _mappings.WriteStart(plan.Syntax);

        switch (plan.ValueKind)
        {
            case ComponentPropertyValueKind.Constant:
            case ComponentPropertyValueKind.CSharpExpression:
                WriteCSharpValue(component, plan, targetExpression);
                break;
            case ComponentPropertyValueKind.ElementReference:
                WriteElementReference(component, plan, targetExpression);
                break;
            case ComponentPropertyValueKind.MarkupExtensionValue:
            case ComponentPropertyValueKind.MarkupBinding:
            case ComponentPropertyValueKind.DynamicResource:
            case ComponentPropertyValueKind.StaticResource:
            case ComponentPropertyValueKind.BindingBaseResult:
            case ComponentPropertyValueKind.RuntimeMarkupExtensionResult:
                WriteMarkupValue(component, plan, targetExpression, context);
                break;
            default:
                Debug.Fail("An invalid component property value reached code generation.");
                return;
        }

        _writer.WriteLine();
    }

    public bool WriteStructuralConstantValue(
        in ComponentPlan component,
        in ComponentPropertyWritePlan plan,
        int ownerRuntimeId,
        string targetExpression)
    {
        Debug.Assert(plan.Destination.IsValid);
        Debug.Assert(plan.ValueKind == ComponentPropertyValueKind.Constant);
        Debug.Assert(ownerRuntimeId >= 0);
        Debug.Assert(!string.IsNullOrEmpty(targetExpression));

        if (!plan.Destination.IsValid ||
            plan.ValueKind != ComponentPropertyValueKind.Constant ||
            ownerRuntimeId < 0 ||
            string.IsNullOrEmpty(targetExpression))
        {
            Debug.Fail(
                "An invalid structural constant property reached code generation.");
            return false;
        }

        var destination = plan.Destination;
        if (destination.Kind == PropertyWriteKind.ClrProperty &&
            destination.ClrProperty?.GetMethod == null)
        {
            return false;
        }

        var methodName = destination.Kind switch
        {
            PropertyWriteKind.ClrProperty or
                PropertyWriteKind.ComponentParameter or
                PropertyWriteKind.DirectMember =>
                "ReconcileClrValue",
            PropertyWriteKind.AvaloniaProperty =>
                "ReconcileAvaloniaValue",
            _ => null,
        };
        if (methodName == null)
        {
            return false;
        }

        using var mapping = _mappings.WriteStart(plan.Syntax);
        _writer.Write(ComponentStructuralHotReloadWriter.RenderStateFieldName);
        _writer.Write(".");
        _writer.Write(methodName);
        _writer.WriteLine("(");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteIntegerLiteral(ownerRuntimeId);
        _writer.WriteLine(",");
        _writer.WriteStringLiteral(
            ComponentHotReloadIdentity.CreatePropertySlot(destination));
        _writer.WriteLine(",");
        _writer.Write(targetExpression);
        _writer.WriteLine(",");

        switch (destination.Kind)
        {
            case PropertyWriteKind.ClrProperty:
                Debug.Assert(destination.ClrProperty != null);
                WriteClrDeclaringType(destination);
                _writer.WriteLine(",");
                _writer.WriteStringLiteral(destination.ClrProperty!.Name);
                _writer.WriteLine(",");
                break;

            case PropertyWriteKind.ComponentParameter:
            case PropertyWriteKind.DirectMember:
                Debug.Assert(!string.IsNullOrEmpty(destination.MemberName));
                WriteClrDeclaringType(destination);
                _writer.WriteLine(",");
                _writer.WriteStringLiteral(destination.MemberName!);
                _writer.WriteLine(",");
                break;

            case PropertyWriteKind.AvaloniaProperty:
                Debug.Assert(destination.AvaloniaProperty != null);
                var valueWriter = new CSharpValueWriter(_writer);
                valueWriter.WriteStaticMemberReference(
                    destination.AvaloniaProperty!);
                _writer.WriteLine(",");
                break;

            default:
                _writer.CurrentIndent -= _writer.TabSize;
                Debug.Fail(
                    "An unsupported structural constant property reached code generation.");
                return false;
        }

        _writer.WriteStringLiteral(
            ComponentHotReloadIdentity.CreateOperationSyntaxIdentity(
                plan.Syntax));
        _writer.WriteLine(",");

        ref readonly var value = ref GetCSharpValue(
            component,
            plan.PayloadIndex);
        _valueWriter.WriteConstant(value);
        _writer.WriteLine(");");
        _writer.CurrentIndent -= _writer.TabSize;
        return true;
    }

    public static bool CanWriteStructuralBindingValue(
        in ComponentPropertyWritePlan plan)
    {
        return plan.Destination.HasAvaloniaPropertyTarget &&
            plan.ValueKind is
                ComponentPropertyValueKind.MarkupBinding or
                ComponentPropertyValueKind.DynamicResource or
                ComponentPropertyValueKind.BindingBaseResult;
    }

    public bool WriteStructuralBindingValue(
        in ComponentPlan component,
        in ComponentPropertyWritePlan plan,
        int ownerRuntimeId,
        string targetExpression,
        in MarkupExtensionWriteContext context)
    {
        Debug.Assert(ownerRuntimeId >= 0);
        Debug.Assert(!string.IsNullOrEmpty(targetExpression));

        if (!CanWriteStructuralBindingValue(plan) ||
            ownerRuntimeId < 0 ||
            string.IsNullOrEmpty(targetExpression))
        {
            return false;
        }

        var slot = ComponentHotReloadIdentity.CreatePropertySlot(
            plan.Destination);
        using var mapping = _mappings.WriteStart(plan.Syntax);

        _writer.Write("if (");
        _writer.Write(
            ComponentStructuralHotReloadWriter.RenderStateFieldName);
        _writer.WriteLine(".ShouldApplyOwnedOperation(");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteIntegerLiteral(ownerRuntimeId);
        _writer.WriteLine(",");
        _writer.WriteStringLiteral(slot);
        _writer.WriteLine(",");
        _writer.WriteStringLiteral(
            ComponentHotReloadIdentity.CreateOperationSyntaxIdentity(
                plan.Syntax));
        _writer.WriteLine("))");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;

        _writer.Write(
            ComponentStructuralHotReloadWriter.RenderStateFieldName);
        _writer.WriteLine(".ApplyBindingOperation(");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteIntegerLiteral(ownerRuntimeId);
        _writer.WriteLine(",");
        _writer.WriteStringLiteral(slot);
        _writer.WriteLine(",");
        _writer.Write("(global::Avalonia.AvaloniaObject)");
        _writer.Write(targetExpression);
        _writer.WriteLine(",");
        var targetPropertyWriter = new MarkupTargetPropertyWriter(_writer);
        targetPropertyWriter.Write(plan.Destination.TargetProperty);
        _writer.WriteLine(",");

        var targetContext = context.WithTarget(
            targetExpression,
            plan.Destination.TargetProperty);
        var extensionWriter = new MarkupExtensionWriter(
            _writer,
            in _environment);
        switch (plan.ValueKind)
        {
            case ComponentPropertyValueKind.MarkupBinding:
                extensionWriter.WriteBinding(
                    GetBinding(component, plan.PayloadIndex),
                    targetContext);
                break;

            case ComponentPropertyValueKind.DynamicResource:
            case ComponentPropertyValueKind.BindingBaseResult:
                extensionWriter.Write(
                    GetMarkupExtension(component, plan.PayloadIndex).Extension,
                    targetContext);
                break;

            default:
                Debug.Fail(
                    "An unsupported structural binding reached code generation.");
                break;
        }

        _writer.WriteLine(");");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
        return true;
    }

    private void WriteClrDeclaringType(in PropertyWritePlan destination)
    {
        if (destination.ReceiverType == null)
        {
            _writer.Write("null");
            return;
        }

        _writer.Write("typeof(");
        var valueWriter = new CSharpValueWriter(_writer);
        valueWriter.WriteTypeName(destination.ReceiverType);
        _writer.Write(")");
    }

    private void WriteMarkupValue(
        in ComponentPlan component,
        in ComponentPropertyWritePlan plan,
        string targetExpression,
        in MarkupExtensionWriteContext context)
    {
        var targetProperty = plan.Destination.TargetProperty;
        Debug.Assert(plan.ValueKind == ComponentPropertyValueKind.MarkupExtensionValue || targetProperty.IsValid);

        if (plan.ValueKind != ComponentPropertyValueKind.MarkupExtensionValue && !targetProperty.IsValid)
        {
            Debug.Fail("A markup property write requires a target-property plan.");
            return;
        }

        var targetContext = context.WithTarget(targetExpression, targetProperty);

        switch (plan.ValueKind)
        {
            case ComponentPropertyValueKind.MarkupExtensionValue:
                WriteMarkupExtension(component, plan, targetExpression, targetContext);
                return;
            case ComponentPropertyValueKind.MarkupBinding:
                WriteBinding(component, plan, targetExpression, targetContext);
                return;
            case ComponentPropertyValueKind.DynamicResource:
                WriteDynamicResource(component, plan, targetExpression, targetContext);
                return;
            case ComponentPropertyValueKind.StaticResource:
                WriteStaticResource(component, plan, targetExpression, targetContext);
                return;
            case ComponentPropertyValueKind.BindingBaseResult:
                WriteBindingBase(component, plan, targetExpression, targetContext);
                return;
            case ComponentPropertyValueKind.RuntimeMarkupExtensionResult:
                WriteRuntimeResult(component, plan, targetExpression, targetContext);
                return;
            default:
                Debug.Fail("A non-markup value reached markup code generation.");
                return;
        }
    }

    public void WriteCachedBindingPath(in BindingWritePlan plan)
    {
        var writer = new BindingWriter(_writer, in _environment);
        writer.WriteCachedPathField(plan);
    }

    private void WriteCSharpValue(
        in ComponentPlan component,
        in ComponentPropertyWritePlan plan,
        string target)
    {
        ref readonly var value = ref GetCSharpValue(component, plan.PayloadIndex);
        var propertyWriter = new PropertyWriter(_writer);
        var end = propertyWriter.WriteStart(plan.Destination, target);

        if (plan.ValueKind == ComponentPropertyValueKind.Constant)
        {
            _valueWriter.WriteConstant(value);
        }
        else
        {
            _valueWriter.WriteExpression(value);
        }

        propertyWriter.WriteEnd(end);
    }

    private void WriteElementReference(
        in ComponentPlan component,
        in ComponentPropertyWritePlan plan,
        string target)
    {
        Debug.Assert((uint)plan.PayloadIndex < (uint)component.Elements.Length);

        ref readonly var element = ref component.Elements.ItemRef(plan.PayloadIndex);
        var propertyWriter = new PropertyWriter(_writer);
        var end = propertyWriter.WriteStart(plan.Destination, target);
        _writer.Write(element.Identifier);
        propertyWriter.WriteEnd(end);
    }

    private void WriteMarkupExtension(
        in ComponentPlan component,
        in ComponentPropertyWritePlan plan,
        string target,
        in MarkupExtensionWriteContext context)
    {
        var propertyWriter = new PropertyWriter(_writer);
        var end = propertyWriter.WriteStart(plan.Destination, target);
        var writer = new MarkupExtensionWriter(_writer, in _environment);
        writer.Write(GetMarkupExtension(component, plan.PayloadIndex).Extension, context);
        propertyWriter.WriteEnd(end);
    }

    private void WriteBinding(
        in ComponentPlan component,
        in ComponentPropertyWritePlan plan,
        string target,
        in MarkupExtensionWriteContext context)
    {
        var resultWriter = new BindingBaseResultWriter(_writer, in _environment);
        resultWriter.WriteBinding(
            CreateTarget(plan.Destination, target),
            GetBinding(component, plan.PayloadIndex),
            context);
    }

    private void WriteDynamicResource(
        in ComponentPlan component,
        in ComponentPropertyWritePlan plan,
        string target,
        in MarkupExtensionWriteContext context)
    {
        var writer = new DynamicResourceWriter(_writer, in _environment);
        ref readonly var result = ref GetMarkupExtension(component, plan.PayloadIndex);
        writer.Write(CreateTarget(plan.Destination, target), result, context);
    }

    private void WriteStaticResource(
        in ComponentPlan component,
        in ComponentPropertyWritePlan plan,
        string target,
        in MarkupExtensionWriteContext context)
    {
        var writer = new StaticResourceWriter(_writer, in _environment);
        ref readonly var result = ref GetMarkupExtension(component, plan.PayloadIndex);
        writer.Write(CreateTarget(plan.Destination, target), result, context);
    }

    private void WriteBindingBase(
        in ComponentPlan component,
        in ComponentPropertyWritePlan plan,
        string target,
        in MarkupExtensionWriteContext context)
    {
        var writer = new BindingBaseResultWriter(_writer, in _environment);
        writer.WriteMarkupExtension(
            CreateTarget(plan.Destination, target),
            GetMarkupExtension(component, plan.PayloadIndex).Extension,
            context);
    }

    private void WriteRuntimeResult(
        in ComponentPlan component,
        in ComponentPropertyWritePlan plan,
        string target,
        in MarkupExtensionWriteContext context)
    {
        var writer = new RuntimeMarkupExtensionResultWriter(_writer, in _environment);
        writer.Write(
            CreateTarget(plan.Destination, target),
            GetMarkupExtension(component, plan.PayloadIndex).Extension,
            context);
    }

    private static ref readonly ComponentCSharpValuePlan GetCSharpValue(
        in ComponentPlan component,
        int index)
    {
        Debug.Assert((uint)index < (uint)component.CSharpValues.Length);

        return ref component.CSharpValues.ItemRef(index);
    }

    private static ref readonly MarkupExtensionResultPlan GetMarkupExtension(
        in ComponentPlan component,
        int index)
    {
        Debug.Assert((uint)index < (uint)component.MarkupExtensions.Length);

        return ref component.MarkupExtensions.ItemRef(index);
    }

    private static ref readonly BindingWritePlan GetBinding(
        in ComponentPlan component,
        int index)
    {
        Debug.Assert((uint)index < (uint)component.Bindings.Length);

        return ref component.Bindings.ItemRef(index);
    }

    private static AvaloniaPropertyWriteTarget CreateTarget(
        in PropertyWritePlan destination,
        string target)
    {
        Debug.Assert(destination.HasAvaloniaPropertyTarget);

        return new AvaloniaPropertyWriteTarget(target, destination.TargetProperty);
    }
}
