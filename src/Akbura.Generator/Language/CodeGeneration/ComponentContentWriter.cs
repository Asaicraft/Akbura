using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Writes lowered eager content without consulting the semantic model.
/// </summary>
internal readonly ref struct ComponentContentWriter
{
    private readonly CodeWriter _writer;
    private readonly ComponentValueWriter _valueWriter;
    private readonly SourceMappingWriter _mappings;

    public ComponentContentWriter(
        CodeWriter writer,
        ComponentGenerationSourceMap sourceMap)
    {
        Debug.Assert(writer != null);
        Debug.Assert(sourceMap != null);

        _writer = writer!;
        _valueWriter = new ComponentValueWriter(writer!);
        _mappings = new SourceMappingWriter(writer!, sourceMap!);
    }

    public bool WriteProperty(
        in ComponentPlan component,
        in ComponentPropertyContentPlan plan,
        bool isFirstUpdate)
    {
        var value = isFirstUpdate
            ? plan.FirstUpdateValue
            : plan.UpdateValue;
        if (!value.IsEager)
        {
            return false;
        }

        Debug.Assert((uint)plan.OwnerElementId < (uint)component.Elements.Length);
        ref readonly var owner = ref component.Elements.ItemRef(plan.OwnerElementId);
        var targetExpression = owner.Identifier;

        using var mapping = _mappings.WriteStart(plan.Syntax);
        var propertyWriter = new PropertyWriter(_writer);
        var end = propertyWriter.WriteStart(plan.Destination, targetExpression);
        if (end == PropertyWriteEnd.None || !WriteValue(component, value))
        {
            return false;
        }

        propertyWriter.WriteEnd(end);
        _writer.WriteLine();
        return true;
    }

    public bool WriteStructuralProperty(
        in ComponentPlan component,
        in ComponentPropertyContentPlan plan)
    {
        var value = plan.FirstUpdateValue;
        if (value.Kind != ComponentContentValueKind.Element ||
            (uint)plan.OwnerElementId >= (uint)component.Elements.Length ||
            (uint)value.Index >= (uint)component.Elements.Length)
        {
            return false;
        }

        ref readonly var owner = ref component.Elements.ItemRef(
            plan.OwnerElementId);
        ref readonly var child = ref component.Elements.ItemRef(value.Index);
        if (!owner.UsesRuntimeStorage || !child.UsesRuntimeStorage)
        {
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
                "ReconcileClrProperty",
            PropertyWriteKind.AvaloniaProperty =>
                "ReconcileAvaloniaProperty",
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
        _writer.WriteIntegerLiteral(owner.RuntimeStorageId);
        _writer.WriteLine(",");
        _writer.WriteStringLiteral(
            ComponentHotReloadIdentity.CreatePropertySlot(destination));
        _writer.WriteLine(",");
        _writer.Write(owner.Identifier);
        _writer.WriteLine(",");

        switch (destination.Kind)
        {
            case PropertyWriteKind.ClrProperty:
                Debug.Assert(destination.ClrProperty != null);
                WriteClrDeclaringType(destination);
                _writer.WriteLine(",");
                _writer.WriteStringLiteral(destination.ClrProperty!.Name);
                break;

            case PropertyWriteKind.ComponentParameter:
            case PropertyWriteKind.DirectMember:
                Debug.Assert(!string.IsNullOrEmpty(destination.MemberName));
                WriteClrDeclaringType(destination);
                _writer.WriteLine(",");
                _writer.WriteStringLiteral(destination.MemberName!);
                break;

            case PropertyWriteKind.AvaloniaProperty:
                Debug.Assert(destination.AvaloniaProperty != null);
                var valueWriter = new CSharpValueWriter(_writer);
                valueWriter.WriteStaticMemberReference(
                    destination.AvaloniaProperty!);
                break;

            default:
                _writer.CurrentIndent -= _writer.TabSize;
                Debug.Fail(
                    "An unsupported structural property reached code generation.");
                return false;
        }

        _writer.WriteLine(",");
        _writer.Write(child.Identifier);
        _writer.WriteLine(");");
        _writer.CurrentIndent -= _writer.TabSize;
        return true;
    }

    public bool WriteStructuralConstantValue(
        in ComponentPlan component,
        in ComponentPropertyContentPlan plan)
    {
        if (!CanWriteStructuralConstantValue(component, plan))
        {
            return false;
        }

        var value = plan.FirstUpdateValue;
        ref readonly var owner = ref component.Elements.ItemRef(
            plan.OwnerElementId);
        var destination = plan.Destination;
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
        _writer.WriteIntegerLiteral(owner.RuntimeStorageId);
        _writer.WriteLine(",");
        _writer.WriteStringLiteral(
            ComponentHotReloadIdentity.CreatePropertySlot(destination));
        _writer.WriteLine(",");
        _writer.Write(owner.Identifier);
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
                    "An unsupported structural constant content property reached code generation.");
                return false;
        }

        _writer.WriteStringLiteral(
            ComponentHotReloadIdentity.CreateContentSyntaxIdentity(
                plan.Syntax));
        _writer.WriteLine(",");
        WriteValue(component, value);
        _writer.WriteLine(");");
        _writer.CurrentIndent -= _writer.TabSize;
        return true;
    }

    public static bool CanWriteStructuralConstantValue(
        in ComponentPlan component,
        in ComponentPropertyContentPlan plan)
    {
        var value = plan.FirstUpdateValue;
        if (value.Kind != ComponentContentValueKind.Constant ||
            (uint)plan.OwnerElementId >= (uint)component.Elements.Length ||
            (uint)value.Index >= (uint)component.CSharpValues.Length ||
            !component.Elements.ItemRef(plan.OwnerElementId).UsesRuntimeStorage)
        {
            return false;
        }

        return plan.Destination.Kind switch
        {
            PropertyWriteKind.ClrProperty =>
                plan.Destination.ClrProperty?.GetMethod != null,
            PropertyWriteKind.AvaloniaProperty or
                PropertyWriteKind.ComponentParameter or
                PropertyWriteKind.DirectMember => true,
            _ => false,
        };
    }

    public bool WriteCollection(
        in ComponentPlan component,
        in ComponentCollectionContentPlan plan)
    {
        if (plan.DictionaryShape.IsDictionary)
        {
            return WriteDictionary(component, plan, useRenderState: false);
        }

        if (plan.ReplacesStyles)
        {
            ref readonly var styleOwner = ref component.Elements.ItemRef(plan.OwnerElementId);
            var types = new CSharpValueWriter(_writer);
            using var styleMapping = _mappings.WriteStart(plan.Syntax);
            _writer.Write(GetStyleStateName(plan.Id)).Write(".Reconcile(");
            _writer.Write("((global::System.Collections.Generic.IList<");
            types.WriteTypeName(plan.Destination.ElementType);
            _writer.Write(">)");
            new CollectionWriter(_writer).WriteTarget(plan.Destination, styleOwner.Identifier);
            _writer.Write("), new ");
            types.WriteTypeName(plan.Destination.ElementType);
            _writer.WriteLine("[] {");
            _writer.CurrentIndent += _writer.TabSize;
            for (var i = 0; i < plan.Items.Length; i++)
            {
                WriteValue(component, component.ContentItems.ItemRef(plan.Items.Start + i).Value);
                _writer.WriteLine(",");
            }

            _writer.CurrentIndent -= _writer.TabSize;
            _writer.WriteLine("});");
            return true;
        }

        Debug.Assert((uint)plan.OwnerElementId < (uint)component.Elements.Length);
        ref readonly var owner = ref component.Elements.ItemRef(plan.OwnerElementId);
        var targetExpression = owner.Identifier;
        var collectionWriter = new CollectionWriter(_writer);
        var wroteAny = false;

        for (var i = 0; i < plan.Items.Length; i++)
        {
            ref readonly var item = ref component.ContentItems.ItemRef(
                plan.Items.Start + i);
            if (!item.Value.IsEager)
            {
                continue;
            }

            using var mapping = _mappings.WriteStart(item.Syntax);
            if (!collectionWriter.WriteStart(plan.Destination, targetExpression))
            {
                continue;
            }

            if (item.InsertionMethod != null)
            {
                _writer.Write("(");
                new CSharpValueWriter(_writer).WriteTypeName(item.InsertionMethod.Parameters[0].Type);
                _writer.Write(")(");
            }

            if (!WriteValue(component, item.Value))
            {
                continue;
            }

            if (item.InsertionMethod != null)
            {
                _writer.Write(")");
            }

            collectionWriter.WriteEnd();
            _writer.WriteLine();
            wroteAny = true;
        }

        return wroteAny;
    }

    public bool WriteStructuralCollection(
        in ComponentPlan component,
        in ComponentCollectionContentPlan plan)
    {
        if (plan.DictionaryShape.IsDictionary)
        {
            return WriteDictionary(component, plan, useRenderState: true);
        }

        if (!CanWriteStructuralCollection(component, plan))
        {
            return false;
        }

        ref readonly var owner = ref component.Elements.ItemRef(
            plan.OwnerElementId);
        var usesTypedReconciliation =
            !plan.Destination.SupportsUntypedReconciliation &&
            plan.Destination.SupportsTypedReconciliation;

        using var mapping = _mappings.WriteStart(plan.Syntax);
        _writer.Write(ComponentStructuralHotReloadWriter.RenderStateFieldName);
        if (plan.Destination.Kind == CollectionWriteKind.ComponentParameter)
        {
            _writer.Write(".ReconcileComponentCollection");
            WriteReconciliationTypeArgument(plan, usesTypedReconciliation);
            _writer.WriteLine("(");
        }
        else if (usesTypedReconciliation)
        {
            _writer.Write(".ReconcileCollection");
            WriteReconciliationTypeArgument(plan, usesTypedReconciliation);
            _writer.WriteLine("(");
        }
        else
        {
            _writer.WriteLine(".ReconcileCollection(");
        }
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteIntegerLiteral(owner.RuntimeStorageId);
        _writer.WriteLine(",");
        _writer.WriteStringLiteral(
            ComponentHotReloadIdentity.CreateCollectionSlot(plan.Destination));
        _writer.WriteLine(",");

        if (plan.Destination.Kind == CollectionWriteKind.ComponentParameter)
        {
            _writer.Write(owner.Identifier);
            _writer.WriteLine(",");
        }

        var collectionWriter = new CollectionWriter(_writer);
        if (!collectionWriter.WriteTarget(plan.Destination, owner.Identifier))
        {
            _writer.CurrentIndent -= _writer.TabSize;
            return false;
        }

        _writer.WriteLine(",");
        if (usesTypedReconciliation)
        {
            _writer.Write("new ");
            new CSharpValueWriter(_writer).WriteTypeNameWithNullableAnnotation(
                plan.Destination.ElementType);
            _writer.WriteLine("[]");
        }
        else
        {
            _writer.WriteLine("new object[]");
        }
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;

        for (var i = 0; i < plan.Items.Length; i++)
        {
            ref readonly var item = ref component.ContentItems.ItemRef(
                plan.Items.Start + i);
            Debug.Assert(item.Value.IsEager);
            WriteValue(component, item.Value);
            _writer.WriteLine(",");
        }

        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("});");
        _writer.CurrentIndent -= _writer.TabSize;

        return true;
    }

    public static bool CanWriteStructuralCollection(
        in ComponentPlan component,
        in ComponentCollectionContentPlan plan)
    {
        Debug.Assert(
            (uint)plan.OwnerElementId <
            (uint)component.Elements.Length);
        ref readonly var owner =
            ref component.Elements.ItemRef(plan.OwnerElementId);

        if (plan.DictionaryShape.IsDictionary)
        {
            return owner.UsesRuntimeStorage;
        }

        return owner.UsesRuntimeStorage &&
            plan.Destination.Kind is (
                CollectionWriteKind.Property or
                CollectionWriteKind.ComponentParameter) &&
            ContainsOnlyStructuralValues(component, plan) &&
            (plan.Destination.SupportsUntypedReconciliation ||
                plan.Destination.SupportsTypedReconciliation);
    }

    private void WriteReconciliationTypeArgument(
        in ComponentCollectionContentPlan plan,
        bool usesTypedReconciliation)
    {
        if (!usesTypedReconciliation)
        {
            return;
        }

        _writer.Write("<");
        new CSharpValueWriter(_writer).WriteTypeNameWithNullableAnnotation(
            plan.Destination.ElementType);
        _writer.Write(">");
    }

    private static bool ContainsOnlyStructuralValues(
        in ComponentPlan component,
        in ComponentCollectionContentPlan plan)
    {
        for (var i = 0; i < plan.Items.Length; i++)
        {
            ref readonly var item = ref component.ContentItems.ItemRef(
                plan.Items.Start + i);
            if (!item.Value.IsEager)
            {
                return false;
            }

            if (item.Value.Kind == ComponentContentValueKind.Element &&
                ((uint)item.Value.Index >= (uint)component.Elements.Length ||
                    !(component.Elements.ItemRef(item.Value.Index).UsesRuntimeStorage ||
                        component.Elements.ItemRef(item.Value.Index).IsStyleSubtree)))
            {
                return false;
            }
        }

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

    public static string GetDictionaryStateName(int contentId) => "__dictionaryContent" + contentId;

    public static string GetStyleStateName(int contentId) => "__styleContent" + contentId;

    public static void WriteStyleStateType(CodeWriter writer, in ComponentCollectionContentPlan plan)
    {
        writer.Write("global::Akbura.HotReload.AkburaRenderCollectionOwner<");
        new CSharpValueWriter(writer).WriteTypeName(plan.Destination.ElementType);
        writer.Write(">");
    }

    public static void WriteDictionaryStateType(CodeWriter writer, in ComponentCollectionContentPlan plan)
    {
        writer.Write("global::Akbura.HotReload.AkburaRenderDictionaryReconciler");
        if (plan.DictionaryShape.IsGeneric)
        {
            var types = new CSharpValueWriter(writer);
            writer.Write("<");
            types.WriteTypeNameWithNullableAnnotation(plan.DictionaryShape.KeyType);
            writer.Write(", ");
            types.WriteTypeNameWithNullableAnnotation(plan.DictionaryShape.ValueType);
            writer.Write(">");
        }
    }

    public bool WriteDictionary(
        in ComponentPlan component,
        in ComponentCollectionContentPlan plan,
        bool useRenderState)
    {
        ref readonly var owner = ref component.Elements.ItemRef(plan.OwnerElementId);
        var shape = plan.DictionaryShape;
        if (shape.ContractType == null || shape.IsAmbiguous || shape.IsReadOnlyOnly)
        {
            return false;
        }

        using var mapping = _mappings.WriteStart(plan.Syntax);
        var types = new CSharpValueWriter(_writer);
        if (useRenderState && owner.UsesRuntimeStorage)
        {
            _writer.Write(ComponentStructuralHotReloadWriter.RenderStateFieldName);
            _writer.Write(".ReconcileDictionary(");
            _writer.WriteIntegerLiteral(owner.RuntimeStorageId).Write(", ");
            _writer.WriteStringLiteral(GetDictionarySlot(plan)).Write(", ");
        }
        else
        {
            _writer.Write(GetDictionaryStateName(plan.Id)).Write(".Reconcile(");
        }

        _writer.Write("((");
        types.WriteTypeNameWithNullableAnnotation(shape.ContractType);
        _writer.Write(")");
        new CollectionWriter(_writer).WriteTarget(plan.Destination, owner.Identifier);
        _writer.Write("), new ");
        WriteDictionaryEntryType(shape);
        _writer.WriteLine("[]");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        for (var i = 0; i < plan.Items.Length; i++)
        {
            ref readonly var item = ref component.ContentItems.ItemRef(plan.Items.Start + i);
            _writer.Write("new ");
            WriteDictionaryEntryType(shape);
            _writer.Write("(");
            WriteValue(component, item.Key);
            _writer.Write(", ");
            WriteValue(component, item.Value);
            _writer.WriteLine("),");
        }

        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("});");
        return true;
    }

    private void WriteDictionaryEntryType(Akbura.Language.Symbols.MarkupDictionaryShape shape)
    {
        if (!shape.IsGeneric)
        {
            _writer.Write("global::System.Collections.DictionaryEntry");
            return;
        }

        var types = new CSharpValueWriter(_writer);
        _writer.Write("global::System.Collections.Generic.KeyValuePair<");
        types.WriteTypeNameWithNullableAnnotation(shape.KeyType);
        _writer.Write(", ");
        types.WriteTypeNameWithNullableAnnotation(shape.ValueType);
        _writer.Write(">");
    }

    private static string GetDictionarySlot(in ComponentCollectionContentPlan plan) =>
        plan.Destination.Kind == CollectionWriteKind.Self
            ? "$dictionary"
            : "dictionary:" + (plan.Destination.ComponentParameterName ??
                plan.Destination.Property.ClrProperty?.Name ?? "content");

    private bool WriteValue(
        in ComponentPlan component,
        in ComponentContentValueReference value)
    {
        switch (value.Kind)
        {
            case ComponentContentValueKind.Element:
                Debug.Assert((uint)value.Index < (uint)component.Elements.Length);
                _writer.Write(component.Elements.ItemRef(value.Index).Identifier);
                return true;

            case ComponentContentValueKind.Constant:
                Debug.Assert((uint)value.Index < (uint)component.CSharpValues.Length);
                _valueWriter.WriteConstant(component.CSharpValues.ItemRef(value.Index));
                return true;

            case ComponentContentValueKind.CSharpExpression:
                Debug.Assert((uint)value.Index < (uint)component.CSharpValues.Length);
                _valueWriter.WriteExpression(component.CSharpValues.ItemRef(value.Index));
                return true;

            default:
                return false;
        }
    }

}
