using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Immutable;
using System.Linq;

namespace Akbura.Language;

internal abstract partial class AkburaSemanticModel
{
    internal MarkupSelectorValue? GetMarkupSelectorLiteral(MarkupLiteralAttributeValueSyntax literal)
    {
        if (literal.Parent is not MarkupAttributeSyntax attribute ||
            GetSymbolInfo(attribute).Symbol is not Akbura.Language.Symbols.IPropertySymbol property ||
            property.Type.Symbol is not ITypeSymbol type || type.Name != "Selector" ||
            type.ContainingNamespace.ToDisplayString() != "Avalonia.Styling")
        {
            return null;
        }

        return ResolveMarkupSelectorLiteral(GetMarkupLiteralAttributeValueText(literal), GetMarkupLiteralTextSpan(literal));
    }

    internal MarkupSelectorValue? ResolveMarkupSelectorLiteral(string text, TextSpan sourceSpan = default)
    {
        var branches = ImmutableArray.CreateBuilder<ImmutableArray<MarkupSelectorNode>>();
        var branch = ImmutableArray.CreateBuilder<MarkupSelectorNode>();
        var needsDescendant = false;
        for (var i = 0; i < text.Length;)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                needsDescendant |= branch.Count != 0;
                i++;
                continue;
            }

            if (c == ',')
            {
                if (branch.Count == 0 || IsCombinator(branch[branch.Count - 1].Kind))
                {
                    return null;
                }

                branches.Add(branch.ToImmutable());
                branch.Clear();
                needsDescendant = false;
                i++;
                continue;
            }

            var start = i;
            if (c == '>' || c == '/')
            {
                if (branch.Count == 0)
                {
                    return null;
                }

                if (c == '/')
                {
                    if (text.IndexOf("/template/", i, StringComparison.Ordinal) != i)
                    {
                        return null;
                    }
                    i += 10;
                }
                else
                {
                    i++;
                }

                branch.Add(new MarkupSelectorNode(c == '>' ? MarkupSelectorNodeKind.Child : MarkupSelectorNodeKind.Template,
                    new TextSpan(sourceSpan.Start + start, i - start)));
                needsDescendant = false;
                while (i < text.Length && char.IsWhiteSpace(text[i]))
                {
                    i++;
                }
                continue;
            }

            if (needsDescendant)
            {
                branch.Add(new MarkupSelectorNode(MarkupSelectorNodeKind.Descendant,
                    new TextSpan(sourceSpan.Start + start, 0)));
                needsDescendant = false;
            }

            if (c == '^')
            {
                branch.Add(new MarkupSelectorNode(MarkupSelectorNodeKind.Nesting,
                    new TextSpan(sourceSpan.Start + i++, 1)));
                continue;
            }

            if (c == ':' && text.IndexOf(":is(", i, StringComparison.Ordinal) == i)
            {
                i += 4;
                var typeStart = i;
                var end = text.IndexOf(')', i);
                if (end < 0)
                {
                    return null;
                }

                var typeText = text[typeStart..end].Trim();
                var type = ResolveMarkupReferenceOwner(typeText);
                if (type == null)
                {
                    return null;
                }

                var typeOffset = text.IndexOf(typeText, typeStart, StringComparison.Ordinal);
                branch.Add(new MarkupSelectorNode(MarkupSelectorNodeKind.IsType,
                    new TextSpan(sourceSpan.Start + typeOffset, typeText.Length), type));
                i = end + 1;
                continue;
            }

            if (c is '.' or '#' or ':')
            {
                i++;
                var nameStart = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '_' or '-'))
                {
                    i++;
                }

                if (i == nameStart)
                {
                    return null;
                }

                var name = text[nameStart..i];
                if (c == ':')
                {
                    name = ":" + name;
                }

                branch.Add(new MarkupSelectorNode(c == '#' ? MarkupSelectorNodeKind.Name : MarkupSelectorNodeKind.Class,
                    new TextSpan(sourceSpan.Start + nameStart, i - nameStart), name: name));
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '_' or '|'))
                {
                    i++;
                }

                var type = ResolveMarkupReferenceOwner(text[start..i]);
                if (type == null)
                {
                    return null;
                }

                branch.Add(new MarkupSelectorNode(MarkupSelectorNodeKind.Type,
                    new TextSpan(sourceSpan.Start + start, i - start), type));
                continue;
            }

            return null;
        }

        if (branch.Count == 0)
        {
            return null;
        }

        if (IsCombinator(branch[branch.Count - 1].Kind))
        {
            return null;
        }

        branches.Add(branch.ToImmutable());
        return new MarkupSelectorValue(branches.ToImmutable());
    }

    private static bool IsCombinator(MarkupSelectorNodeKind kind) =>
        kind is MarkupSelectorNodeKind.Child or MarkupSelectorNodeKind.Template or MarkupSelectorNodeKind.Descendant;

    private MarkupStyleTargetContext GetSelectorTargetContext(
        MarkupSelectorValue selector, MarkupStyleTargetContext inherited)
    {
        var types = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
        var unknown = false;
        foreach (var branch in selector.Branches)
        {
            INamedTypeSymbol? target = null;
            var nesting = false;
            foreach (var node in branch)
            {
                switch (node.Kind)
                {
                    case MarkupSelectorNodeKind.Type:
                    case MarkupSelectorNodeKind.IsType:
                        target = node.Type;
                        break;
                    case MarkupSelectorNodeKind.Nesting:
                        nesting = true;
                        break;
                    case MarkupSelectorNodeKind.Child:
                    case MarkupSelectorNodeKind.Descendant:
                    case MarkupSelectorNodeKind.Template:
                        target = null;
                        nesting = false;
                        break;
                }
            }

            if (target != null)
            {
                if (!types.Any(type => SymbolEqualityComparer.Default.Equals(type, target)))
                {
                    types.Add(target);
                }
            }
            else if (nesting && !inherited.IsUnknown)
            {
                foreach (var type in inherited.Types)
                {
                    if (!types.Any(existing => SymbolEqualityComparer.Default.Equals(existing, type)))
                    {
                        types.Add(type);
                    }
                }
            }
            else
            {
                unknown = true;
            }
        }

        return new MarkupStyleTargetContext(types.ToImmutable(), unknown);
    }
}
