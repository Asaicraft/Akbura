using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Writes the service-provider expression shared by markup extensions and
/// deferred content factories.
/// </summary>
internal readonly ref struct MarkupServiceProviderWriter
{
    private readonly CodeWriter _writer;

    public MarkupServiceProviderWriter(CodeWriter writer)
    {
        Debug.Assert(writer != null);
        _writer = writer!;
    }

    public bool Write(in MarkupExtensionWriteContext context)
    {
        var isComplete = CanWrite(context);

        Debug.Assert(
            isComplete,
            "The markup service-provider context is incomplete.");

        if (!isComplete)
        {
            return false;
        }

        _writer
            .Write("CreateMarkupServiceProvider(targetObject: ")
            .Write(context.TargetObjectExpression)
            .Write(", targetProperty: ");

        var targetPropertyWriter = new MarkupTargetPropertyWriter(_writer);
        targetPropertyWriter.Write(context.TargetProperty);

        _writer
            .Write(", intermediateRootObject: ")
            .Write(context.IntermediateRootExpression)
            .Write(", baseUri: ")
            .Write(context.BaseUriExpression)
            .Write(", directParentsStack: ");

        var parentStackWriter = new MarkupParentStackWriter(_writer);
        parentStackWriter.Write(context.DirectParentsStack);

        if (!string.IsNullOrEmpty(context.FallbackServiceProviderExpression))
        {
            _writer
                .Write(", fallbackServiceProvider: ")
                .Write(context.FallbackServiceProviderExpression!);
        }

        _writer.Write(")");
        return true;
    }

    internal static bool CanWrite(in MarkupExtensionWriteContext context)
    {
        return !string.IsNullOrEmpty(context.TargetObjectExpression) &&
            !string.IsNullOrEmpty(context.IntermediateRootExpression) &&
            !string.IsNullOrEmpty(context.BaseUriExpression) &&
            MarkupParentStackWriter.CanWrite(context.DirectParentsStack);
    }
}
