using Akbura.Workspaces;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text.Classification;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akbura.VisualStudio.Classification;

internal sealed class AkburaClassificationTypeMap
{
    private readonly IClassificationType[] _types;

    public AkburaClassificationTypeMap(
        IClassificationTypeRegistryService registry)
    {
        if (registry == null)
        {
            throw new ArgumentNullException(nameof(registry));
        }

        var last = (int)AkburaClassificationKind.LastKind;

        _types = new IClassificationType[last + 1];

        _types[(int)AkburaClassificationKind.Keyword] =
            GetRequired(
                registry,
                PredefinedClassificationTypeNames.Keyword);

        _types[(int)AkburaClassificationKind.Namespace] =
            GetRequired(
                registry,
                PredefinedClassificationTypeNames.Identifier);

        _types[(int)AkburaClassificationKind.Type] =
            GetRequired(
                registry,
                PredefinedClassificationTypeNames.Type);

        _types[(int)AkburaClassificationKind.Component] =
            GetRequired(
                registry,
                PredefinedClassificationTypeNames.MarkupNode);

        _types[(int)AkburaClassificationKind.Attribute] =
            GetRequired(
                registry,
                PredefinedClassificationTypeNames.MarkupAttribute);

        _types[(int)AkburaClassificationKind.Identifier] =
            GetRequired(
                registry,
                PredefinedClassificationTypeNames.Identifier);

        _types[(int)AkburaClassificationKind.Directive] =
            GetRequired(
                registry,
                PredefinedClassificationTypeNames.PreprocessorKeyword);

        _types[(int)AkburaClassificationKind.String] =
            GetRequired(
                registry,
                PredefinedClassificationTypeNames.String);

        _types[(int)AkburaClassificationKind.Number] =
            GetRequired(
                registry,
                PredefinedClassificationTypeNames.Number);

        _types[(int)AkburaClassificationKind.Comment] =
            GetRequired(
                registry,
                PredefinedClassificationTypeNames.Comment);

        _types[(int)AkburaClassificationKind.Operator] =
            GetRequired(
                registry,
                PredefinedClassificationTypeNames.Operator);

        _types[(int)AkburaClassificationKind.Punctuation] =
            GetRequired(
                registry,
                PredefinedClassificationTypeNames.Punctuation);

        _types[(int)AkburaClassificationKind.MarkupText] =
            GetRequired(
                registry,
                PredefinedClassificationTypeNames.Text);

        _types[(int)AkburaClassificationKind.Utility] =
            GetRequired(
                registry,
                PredefinedClassificationTypeNames.MarkupAttributeValue);

        _types[(int)AkburaClassificationKind.UtilityModifier] =
            GetRequired(
                registry,
                PredefinedClassificationTypeNames.PreprocessorKeyword);

        _types[(int)AkburaClassificationKind.MarkupExtensionType] =
            GetRequired(
                registry,
                PredefinedClassificationTypeNames.Type);

        _types[(int)AkburaClassificationKind.MarkupExtensionProperty] =
            GetRequired(
                registry,
                PredefinedClassificationTypeNames.MarkupAttribute);

        _types[(int)AkburaClassificationKind.MarkupExtensionValue] =
            GetRequired(
                registry,
                PredefinedClassificationTypeNames.MarkupAttributeValue);

        _types[(int)AkburaClassificationKind.MarkupExtensionPunctuation] =
            GetRequired(
                registry,
                PredefinedClassificationTypeNames.Punctuation);

        _types[(int)AkburaClassificationKind.EmbeddedCSharp] =
            GetRequired(
                registry,
                PredefinedClassificationTypeNames.FormalLanguage);
    }

    public IClassificationType Get(AkburaClassificationKind kind)
    {
        var index = (int)kind;

        if ((uint)index >= (uint)_types.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }

        return _types[index];
    }

    private static IClassificationType GetRequired(IClassificationTypeRegistryService registry, string name)
    {
        return registry.GetClassificationType(name) ??
            throw new InvalidOperationException(
                $"Visual Studio classification " +
                $"type '{name}' was not found.");
    }
}
