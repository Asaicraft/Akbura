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
    private readonly Dictionary<AkburaClassificationKind, IClassificationType> _types;

    public AkburaClassificationTypeMap(
        IClassificationTypeRegistryService registry)
    {
        if (registry == null)
        {
            throw new ArgumentNullException(nameof(registry));
        }

        _types = new Dictionary<
            AkburaClassificationKind,
            IClassificationType>
        {
            [AkburaClassificationKind.Keyword] =
                GetRequired(
                    registry,
                    PredefinedClassificationTypeNames.Keyword),

            [AkburaClassificationKind.Namespace] =
                GetRequired(
                    registry,
                    PredefinedClassificationTypeNames.Identifier),

            [AkburaClassificationKind.Type] =
                GetRequired(
                    registry,
                    PredefinedClassificationTypeNames.Type),

            [AkburaClassificationKind.Component] =
                GetRequired(
                    registry,
                    PredefinedClassificationTypeNames.MarkupNode),

            [AkburaClassificationKind.Attribute] =
                GetRequired(
                    registry,
                    PredefinedClassificationTypeNames.MarkupAttribute),

            [AkburaClassificationKind.Identifier] =
                GetRequired(
                    registry,
                    PredefinedClassificationTypeNames.Identifier),

            [AkburaClassificationKind.Directive] =
                GetRequired(
                    registry,
                    PredefinedClassificationTypeNames.PreprocessorKeyword),

            [AkburaClassificationKind.String] =
                GetRequired(
                    registry,
                    PredefinedClassificationTypeNames.String),

            [AkburaClassificationKind.Number] =
                GetRequired(
                    registry,
                    PredefinedClassificationTypeNames.Number),

            [AkburaClassificationKind.Comment] =
                GetRequired(
                    registry,
                    PredefinedClassificationTypeNames.Comment),

            [AkburaClassificationKind.Operator] =
                GetRequired(
                    registry,
                    PredefinedClassificationTypeNames.Operator),

            [AkburaClassificationKind.Punctuation] =
                GetRequired(
                    registry,
                    PredefinedClassificationTypeNames.Punctuation),

            [AkburaClassificationKind.MarkupText] =
                GetRequired(
                    registry,
                    PredefinedClassificationTypeNames.Text),

            [AkburaClassificationKind.EmbeddedCSharp] =
                GetRequired(
                    registry,
                    PredefinedClassificationTypeNames.FormalLanguage),

            [AkburaClassificationKind.Utility] =
                GetRequired(
                    registry,
                    PredefinedClassificationTypeNames.MarkupAttributeValue),

                        [AkburaClassificationKind.UtilityModifier] =
                GetRequired(
                    registry,
                    PredefinedClassificationTypeNames.PreprocessorKeyword),
        };
    }

    public IClassificationType Get(
        AkburaClassificationKind kind)
    {
        return _types[kind];
    }

    private static IClassificationType GetRequired(
        IClassificationTypeRegistryService registry,
        string name)
    {
        return registry.GetClassificationType(name) ??
            throw new InvalidOperationException(
                $"Visual Studio classification " +
                $"type '{name}' was not found.");
    }
}