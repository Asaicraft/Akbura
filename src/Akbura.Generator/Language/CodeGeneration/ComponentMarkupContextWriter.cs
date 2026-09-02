using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Writes component-level members used to construct markup service-provider
/// contexts.
/// </summary>
internal readonly ref struct ComponentMarkupContextWriter
{
    internal const string BaseUriFieldName = "__akburaBaseUri";

    private readonly CodeWriter _writer;
    private readonly string _ownerTypeName;
    private readonly string _resourcePath;

    public ComponentMarkupContextWriter(
        CodeWriter writer,
        string ownerTypeName,
        string resourcePath)
    {
        Debug.Assert(writer != null);
        Debug.Assert(!string.IsNullOrEmpty(ownerTypeName));
        Debug.Assert(!string.IsNullOrEmpty(resourcePath));

        _writer = writer!;
        _ownerTypeName = ownerTypeName;
        _resourcePath = resourcePath;
    }

    public bool WriteFields(in ComponentLifecyclePlan plan)
    {
        if (!plan.RequiresBaseUri)
        {
            return false;
        }

        WriteBaseUriField();
        return true;
    }

    public void WriteBaseUriField()
    {
        var indent = _writer.CurrentIndent;

        try
        {
            _writer.Write("private static readonly global::System.Uri ");
            _writer.Write(BaseUriFieldName);
            _writer.WriteLine(" =");
            _writer.CurrentIndent += _writer.TabSize;

            _writer.Write("new global::System.Uri(");
            _writer.WriteStringLiteral("avares://");
            _writer.Write(" + typeof(");
            _writer.Write(_ownerTypeName);
            _writer.Write(").Assembly.GetName().Name + ");
            _writer.WriteStringLiteral("/" + _resourcePath);
            _writer.WriteLine(");");
        }
        finally
        {
            _writer.CurrentIndent = indent;
        }
    }
}
