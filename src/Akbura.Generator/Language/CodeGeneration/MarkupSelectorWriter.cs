using Akbura.Language.Operations;

namespace Akbura.Language.CodeGeneration;

internal readonly ref struct MarkupSelectorWriter
{
    private readonly CodeWriter _writer;

    public MarkupSelectorWriter(CodeWriter writer) => _writer = writer;

    public void Write(MarkupSelectorValue value)
    {
        if (value.Branches.Length > 1)
        {
            _writer.Write("global::Avalonia.Styling.Selectors.Or(new global::Avalonia.Styling.Selector[] { ");
        }

        for (var i = 0; i < value.Branches.Length; i++)
        {
            if (i != 0)
            {
                _writer.Write(", ");
            }
            WriteNode(value.Branches[i], value.Branches[i].Length - 1);
        }

        if (value.Branches.Length > 1)
        {
            _writer.Write(" })");
        }
    }

    private void WriteNode(System.Collections.Immutable.ImmutableArray<MarkupSelectorNode> branch, int index)
    {
        if (index < 0)
        {
            _writer.Write("null");
            return;
        }

        var node = branch[index];
        _writer.Write("global::Avalonia.Styling.Selectors.");
        _writer.Write(node.Kind switch
        {
            MarkupSelectorNodeKind.Type => "OfType",
            MarkupSelectorNodeKind.IsType => "Is",
            MarkupSelectorNodeKind.Class => "Class",
            MarkupSelectorNodeKind.Name => "Name",
            MarkupSelectorNodeKind.Nesting => "Nesting",
            MarkupSelectorNodeKind.Descendant => "Descendant",
            MarkupSelectorNodeKind.Child => "Child",
            MarkupSelectorNodeKind.Template => "Template",
            _ => "Nesting",
        });
        _writer.Write("(");
        WriteNode(branch, index - 1);
        if (node.Type != null)
        {
            _writer.Write(", typeof(");
            new CSharpValueWriter(_writer).WriteTypeName(node.Type);
            _writer.Write(")");
        }
        else if (node.Name != null)
        {
            _writer.Write(", ").WriteStringLiteral(node.Name);
        }

        _writer.Write(")");
    }
}
