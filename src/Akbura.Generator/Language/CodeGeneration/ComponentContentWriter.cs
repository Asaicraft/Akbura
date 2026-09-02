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

    public bool WriteCollection(
        in ComponentPlan component,
        in ComponentCollectionContentPlan plan)
    {
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

            if (!WriteValue(component, item.Value))
            {
                continue;
            }

            collectionWriter.WriteEnd();
            _writer.WriteLine();
            wroteAny = true;
        }

        return wroteAny;
    }

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
