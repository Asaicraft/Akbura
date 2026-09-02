using Microsoft.CodeAnalysis;
using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Writes a lowered property read directly to the generated source.
/// </summary>
internal readonly ref struct PropertyReadWriter
{
    private readonly CodeWriter _writer;
    private readonly CSharpValueWriter _valueWriter;

    public PropertyReadWriter(CodeWriter writer)
    {
        Debug.Assert(writer != null);

        _writer = writer!;
        _valueWriter = new CSharpValueWriter(writer!);
    }

    public void Write(
        IPropertySymbol property,
        string targetExpression)
    {
        Debug.Assert(property != null);
        Debug.Assert(!string.IsNullOrEmpty(targetExpression));

        if (property == null ||
            property.IsStatic ||
            property.ContainingType == null ||
            string.IsNullOrEmpty(targetExpression))
        {
            Debug.Fail("An invalid CLR property read reached code generation.");
            _writer.Write("default");
            return;
        }

        WriteClrProperty(property, property.ContainingType, targetExpression);
    }

    public void Write(
        in PropertyReadPlan plan,
        string targetExpression)
    {
        Debug.Assert(plan.IsValid);
        Debug.Assert(!string.IsNullOrEmpty(targetExpression));

        if (!plan.IsValid || string.IsNullOrEmpty(targetExpression))
        {
            Debug.Fail("An invalid property read reached code generation.");
            return;
        }

        switch (plan.Kind)
        {
            case PropertyReadKind.ClrProperty:
                WriteClrProperty(plan, targetExpression);
                return;

            case PropertyReadKind.AvaloniaProperty:
                WriteAvaloniaProperty(plan, targetExpression);
                return;

            case PropertyReadKind.AttachedAccessor:
                WriteAttachedAccessor(plan, targetExpression);
                return;

            case PropertyReadKind.DirectMember:
                WriteDirectMember(plan, targetExpression);
                return;

            default:
                Debug.Fail("An invalid property read reached code generation.");
                return;
        }
    }

    private void WriteClrProperty(
        in PropertyReadPlan plan,
        string targetExpression)
    {
        Debug.Assert(plan.ClrProperty != null);
        Debug.Assert(plan.ReceiverType != null);

        if (plan.ClrProperty == null || plan.ReceiverType == null)
        {
            Debug.Fail("A CLR property read requires a property and receiver type.");
            return;
        }

        WriteClrProperty(plan.ClrProperty, plan.ReceiverType, targetExpression);
    }

    private void WriteClrProperty(
        IPropertySymbol property,
        ITypeSymbol receiverType,
        string targetExpression)
    {
        _writer.Write("((");
        _valueWriter.WriteTypeName(receiverType);
        _writer.Write(")").Write(targetExpression).Write(").");
        _valueWriter.WriteIdentifier(property.Name);
    }

    private void WriteAvaloniaProperty(
        in PropertyReadPlan plan,
        string targetExpression)
    {
        Debug.Assert(plan.AvaloniaProperty != null);
        Debug.Assert(plan.ReceiverType != null);

        if (plan.AvaloniaProperty == null || plan.ReceiverType == null)
        {
            Debug.Fail("An Avalonia property read requires a property and receiver type.");
            return;
        }

        _writer.Write("((global::Avalonia.AvaloniaObject)");
        _writer.Write(targetExpression);
        _writer.Write(").GetValue(");
        _valueWriter.WriteStaticMemberReference(plan.AvaloniaProperty);
        _writer.Write(")");
    }

    private void WriteAttachedAccessor(
        in PropertyReadPlan plan,
        string targetExpression)
    {
        Debug.Assert(plan.AttachedGetter != null);
        Debug.Assert(plan.AttachedGetter?.ContainingType != null);
        Debug.Assert(plan.ReceiverType != null);

        if (plan.AttachedGetter?.ContainingType == null || plan.ReceiverType == null)
        {
            Debug.Fail("An attached property read requires a getter and receiver type.");
            return;
        }

        _valueWriter.WriteTypeName(plan.AttachedGetter.ContainingType);
        _writer.Write(".");
        _valueWriter.WriteIdentifier(plan.AttachedGetter.Name);
        _writer.Write("((");
        _valueWriter.WriteTypeName(plan.ReceiverType);
        _writer.Write(")").Write(targetExpression).Write(")");
    }

    private void WriteDirectMember(
        in PropertyReadPlan plan,
        string targetExpression)
    {
        Debug.Assert(!string.IsNullOrEmpty(plan.MemberName));

        if (string.IsNullOrEmpty(plan.MemberName))
        {
            Debug.Fail("A direct property read requires a member name.");
            return;
        }

        _writer.Write(targetExpression).Write(".");
        _valueWriter.WriteIdentifier(plan.MemberName!);
    }
}
