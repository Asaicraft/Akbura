using Akbura.Language.Syntax;
using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Language.Binder;

internal sealed class MarkupWhitespaceResolver
{
    private readonly AkburaSemanticModel _semanticModel;

    public MarkupWhitespaceResolver(AkburaSemanticModel semanticModel)
    {
        _semanticModel = semanticModel ??
            throw new ArgumentNullException(nameof(semanticModel));
    }

    public MarkupWhitespaceMode GetEffectiveMode(
        MarkupElementSyntax element)
    {
        for (var current = element;
             current != null;
             current = AkburaSemanticModel.GetParentMarkupElement(current))
        {
            if (TryGetDeclaredMode(current, out var mode))
            {
                return mode;
            }
        }

        return MarkupWhitespaceMode.Default;
    }

    public MarkupWhitespaceMode GetInheritedMode(
        MarkupElementSyntax element)
    {
        var parent = AkburaSemanticModel.GetParentMarkupElement(element);

        return parent == null
            ? MarkupWhitespaceMode.Default
            : GetEffectiveMode(parent);
    }

    public bool TryGetDeclaredMode(
        MarkupElementSyntax element,
        out MarkupWhitespaceMode mode)
    {
        mode = MarkupWhitespaceMode.Default;

        if (element.StartTag == null)
        {
            return false;
        }

        foreach (var attribute in element.StartTag.Attributes)
        {
            if (!AkburaSemanticModel.IsMarkupWhitespaceDirective(attribute))
            {
                continue;
            }

            return AkburaSemanticModel.TryGetMarkupWhitespaceMode(
                attribute,
                out mode,
                out _);
        }

        return false;
    }
}