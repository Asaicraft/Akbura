using Microsoft.CodeAnalysis;
using System.Diagnostics;
using CSharpSyntaxFacts = Microsoft.CodeAnalysis.CSharp.SyntaxFacts;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

namespace Akbura.Language.CodeGeneration;

internal enum MarkupTargetPropertyKind : byte
{
    None,
    Expression,
    StaticMember,
    ClrProperty,
    AttachedSetter,
    GeneratedParameter,
}

internal readonly struct MarkupTargetPropertyPlan
{
    private MarkupTargetPropertyPlan(
        MarkupTargetPropertyKind kind,
        ISymbol? symbol,
        string? text)
    {
        Kind = kind;
        Symbol = symbol;
        Text = text;
    }

    public MarkupTargetPropertyKind Kind { get; }

    public ISymbol? Symbol { get; }

    public string? Text { get; }

    public bool IsValid => Kind != MarkupTargetPropertyKind.None;

    public static MarkupTargetPropertyPlan CreateStaticMember(ISymbol member)
    {
        Debug.Assert(member is IFieldSymbol { IsStatic: true } or IPropertySymbol { IsStatic: true });

        return new MarkupTargetPropertyPlan(
            MarkupTargetPropertyKind.StaticMember,
            member,
            text: null);
    }

    public static MarkupTargetPropertyPlan CreateGeneratedParameter(
        ITypeSymbol targetType,
        string name)
    {
        Debug.Assert(targetType != null);
        Debug.Assert(!string.IsNullOrEmpty(name));

        return new MarkupTargetPropertyPlan(
            MarkupTargetPropertyKind.GeneratedParameter,
            targetType,
            name);
    }

    public static MarkupTargetPropertyPlan CreateExpression(string expression)
    {
        Debug.Assert(!string.IsNullOrWhiteSpace(expression));

        return new MarkupTargetPropertyPlan(
            MarkupTargetPropertyKind.Expression,
            symbol: null,
            expression);
    }

    public static MarkupTargetPropertyPlan CreateClrProperty(IPropertySymbol property)
    {
        Debug.Assert(property != null);
        return new MarkupTargetPropertyPlan(
            MarkupTargetPropertyKind.ClrProperty,
            property,
            text: null);
    }

    public static MarkupTargetPropertyPlan CreateAttachedSetter(IMethodSymbol setter)
    {
        Debug.Assert(setter is { IsStatic: true });
        return new MarkupTargetPropertyPlan(
            MarkupTargetPropertyKind.AttachedSetter,
            setter,
            text: null);
    }
}

internal readonly ref struct MarkupTargetPropertyWriter
{
    private readonly CodeWriter _writer;
    private readonly CSharpValueWriter _valueWriter;

    public MarkupTargetPropertyWriter(CodeWriter writer)
    {
        Debug.Assert(writer != null);
        _writer = writer!;
        _valueWriter = new CSharpValueWriter(writer!);
    }

    public void Write(in MarkupTargetPropertyPlan plan)
    {
        switch (plan.Kind)
        {
            case MarkupTargetPropertyKind.StaticMember:
                _valueWriter.WriteStaticMemberReference(plan.Symbol!);
                return;
            case MarkupTargetPropertyKind.ClrProperty:
                WritePropertyInfo((IPropertySymbol)plan.Symbol!);
                return;
            case MarkupTargetPropertyKind.AttachedSetter:
                WriteMethodInfo((IMethodSymbol)plan.Symbol!);
                return;
            case MarkupTargetPropertyKind.GeneratedParameter:
                _valueWriter.WriteTypeName((ITypeSymbol)plan.Symbol!);
                _writer.Write(".");
                WriteGeneratedParameterName(plan.Text!);
                _writer.Write("Property.AvaloniaProperty");
                return;
            case MarkupTargetPropertyKind.Expression:
                _writer.Write(plan.Text!);
                return;
            default:
                _writer.Write("null!");
                return;
        }
    }

    private void WritePropertyInfo(IPropertySymbol property)
    {
        _writer.Write("typeof(");
        _valueWriter.WriteTypeName(property.ContainingType);
        _writer.Write(").GetProperty(");
        _writer.WriteStringLiteral(property.Name);
        _writer.Write(")!");
    }

    private void WriteMethodInfo(IMethodSymbol method)
    {
        _writer.Write("typeof(");
        _valueWriter.WriteTypeName(method.ContainingType);
        _writer.Write(").GetMethod(");
        _writer.WriteStringLiteral(method.Name);
        _writer.Write(")!");
    }

    private void WriteGeneratedParameterName(string name)
    {
        if (CSharpSyntaxFacts.GetKeywordKind(name) != CSharpSyntaxKind.None ||
            CSharpSyntaxFacts.GetContextualKeywordKind(name) != CSharpSyntaxKind.None)
        {
            _writer.Write("@");
        }

        _writer.Write(name);
    }
}
