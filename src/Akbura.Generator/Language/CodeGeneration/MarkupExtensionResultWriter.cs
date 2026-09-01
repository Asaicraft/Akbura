using Akbura.Language.Operations;
using System;
using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Applies a BindingBase expression to an Avalonia property.
/// </summary>
internal readonly ref struct BindingBaseResultWriter
{
    private readonly CodeWriter _writer;
    private readonly BindingWriterEnvironment _environment;

    public BindingBaseResultWriter(
        CodeWriter writer,
        in BindingWriterEnvironment environment)
    {
        Debug.Assert(writer != null);

        _writer = writer!;
        _environment = environment;
    }

    public void WriteMarkupExtension(
        in AvaloniaPropertyWriteTarget target,
        MarkupExtensionValue extension,
        in MarkupExtensionWriteContext context)
    {
        WriteStart(target);

        var extensionWriter = new MarkupExtensionWriter(_writer, in _environment);
        extensionWriter.Write(extension, context);

        WriteEnd();
    }

    public void WriteBinding(
        in AvaloniaPropertyWriteTarget target,
        in BindingWritePlan plan,
        in MarkupExtensionWriteContext context)
    {
        WriteStart(target);

        var extensionWriter = new MarkupExtensionWriter(_writer, in _environment);
        extensionWriter.WriteBinding(plan, context);

        WriteEnd();
    }

    public void WriteStart(in AvaloniaPropertyWriteTarget target)
    {
        EnsureValidTarget(target);

        _writer
            .Write("((global::Avalonia.AvaloniaObject)")
            .Write(target.TargetExpression)
            .Write(").Bind(");
        var valueWriter = new CSharpValueWriter(_writer);
        valueWriter.WriteStaticMemberReference(target.AvaloniaProperty);
        _writer.Write(", ");
    }

    public void WriteEnd()
    {
        _writer.Write(");");
    }

    private static void EnsureValidTarget(
        in AvaloniaPropertyWriteTarget target)
    {
        if (!target.IsValid)
        {
            throw new InvalidOperationException("The Avalonia property write target is not initialized.");
        }
    }
}

/// <summary>
/// Applies DynamicResource through its BindingBase runtime semantics.
/// </summary>
internal readonly ref struct DynamicResourceWriter
{
    private readonly CodeWriter _writer;
    private readonly BindingWriterEnvironment _environment;

    public DynamicResourceWriter(
        CodeWriter writer,
        in BindingWriterEnvironment environment)
    {
        Debug.Assert(writer != null);

        _writer = writer!;
        _environment = environment;
    }

    public void Write(
        in AvaloniaPropertyWriteTarget target,
        in MarkupExtensionResultPlan plan,
        in MarkupExtensionWriteContext context)
    {
        if (!plan.IsValid || plan.Kind != MarkupExtensionResultKind.DynamicResource)
        {
            throw new InvalidOperationException("DynamicResourceWriter requires a DynamicResource result plan.");
        }

        var resultWriter = new BindingBaseResultWriter(_writer, in _environment);
        resultWriter.WriteMarkupExtension(target, plan.Extension, context);
    }
}

/// <summary>
/// Applies a markup-extension result whose concrete runtime kind is unknown.
/// </summary>
internal readonly ref struct RuntimeMarkupExtensionResultWriter
{
    private readonly CodeWriter _writer;
    private readonly BindingWriterEnvironment _environment;

    public RuntimeMarkupExtensionResultWriter(
        CodeWriter writer,
        in BindingWriterEnvironment environment)
    {
        Debug.Assert(writer != null);

        _writer = writer!;
        _environment = environment;
    }

    public void Write(
        in AvaloniaPropertyWriteTarget target,
        MarkupExtensionValue extension,
        in MarkupExtensionWriteContext context)
    {
        WriteStart(target);

        var extensionWriter = new MarkupExtensionWriter(_writer, in _environment);
        extensionWriter.Write(extension, context);

        WriteEnd();
    }

    public void WriteStart(in AvaloniaPropertyWriteTarget target)
    {
        EnsureValidTarget(target);

        _writer
            .Write("ApplyMarkupExtensionResult(")
            .Write("(global::Avalonia.AvaloniaObject)")
            .Write(target.TargetExpression)
            .Write(", ");
        var valueWriter = new CSharpValueWriter(_writer);
        valueWriter.WriteStaticMemberReference(target.AvaloniaProperty);
        _writer.Write(", ");
    }

    public void WriteEnd()
    {
        _writer.Write(");");
    }

    private static void EnsureValidTarget(
        in AvaloniaPropertyWriteTarget target)
    {
        if (!target.IsValid)
        {
            throw new InvalidOperationException("The Avalonia property write target is not initialized.");
        }
    }
}

/// <summary>
/// Applies StaticResource through runtime markup-extension result handling.
/// </summary>
internal readonly ref struct StaticResourceWriter
{
    private readonly CodeWriter _writer;
    private readonly BindingWriterEnvironment _environment;

    public StaticResourceWriter(
        CodeWriter writer,
        in BindingWriterEnvironment environment)
    {
        Debug.Assert(writer != null);

        _writer = writer!;
        _environment = environment;
    }

    public void Write(
        in AvaloniaPropertyWriteTarget target,
        in MarkupExtensionResultPlan plan,
        in MarkupExtensionWriteContext context)
    {
        if (!plan.IsValid || plan.Kind != MarkupExtensionResultKind.StaticResource)
        {
            throw new InvalidOperationException("StaticResourceWriter requires a StaticResource result plan.");
        }

        var resultWriter = new RuntimeMarkupExtensionResultWriter(_writer, in _environment);
        resultWriter.Write(target, plan.Extension, context);
    }
}
