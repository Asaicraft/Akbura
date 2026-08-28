using Avalonia.Controls;
using Avalonia.Media;
using System.Collections;

namespace Akbura.Diagnostics;

/// <summary>
/// Edits collection values using a dedicated JSON input surface.
/// </summary>
public sealed class CollectionInputBuilder : InputBuilder
{
    public override Type OutputType => typeof(IEnumerable);

    public override bool CanProvide(InputRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var editorType = request.EditorType;
        return CollectionInputValue.CanEdit(editorType) &&
            StateValueConverter.CanEdit(editorType);
    }

    protected override Control BuildCore(InputRequest request)
    {
        var inputBuilders = request.InputBuilders ??
            DefaultInputBuilders.Instance;

        return new CollectionInput
        {
            InputBuilders = inputBuilders,
            Request = request with
            {
                InputBuilders = inputBuilders,
            },
        };
    }

    private static class DefaultInputBuilders
    {
        public static readonly IInputBuilderProvider Instance =
            new InputBuilderProvider(
                InputBuilderProvider.CreateDefaultBuilders());
    }
}
