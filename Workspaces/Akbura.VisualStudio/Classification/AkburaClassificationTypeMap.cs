using Akbura.Workspaces;
using Microsoft.VisualStudio.Text.Classification;

using RoslynClassificationTypeNames =
    Microsoft.CodeAnalysis.Classification.ClassificationTypeNames;

using VisualStudioClassificationTypeNames =
    Microsoft.VisualStudio.Language.StandardClassification
        .PredefinedClassificationTypeNames;

namespace Akbura.VisualStudio.Classification;

internal sealed class AkburaClassificationTypeMap
{
    private readonly IClassificationType[] _types;

    public AkburaClassificationTypeMap(IClassificationTypeRegistryService registry)
    {
        if (registry == null)
        {
            throw new ArgumentNullException(
                nameof(registry));
        }

        var last = (int)AkburaClassificationKind.LastKind;

        _types = new IClassificationType[last + 1];

        _types[
            (int)AkburaClassificationKind.Keyword] =
            GetRequired(
                registry,
                VisualStudioClassificationTypeNames.Keyword);

        _types[
            (int)AkburaClassificationKind.Namespace] =
            GetRequired(
                registry,
                RoslynClassificationTypeNames.NamespaceName);

        _types[
            (int)AkburaClassificationKind.Type] =
            GetRequired(
                registry,
                VisualStudioClassificationTypeNames.Type);

        _types[
            (int)AkburaClassificationKind.Component] =
            GetRequired(
                registry,
                VisualStudioClassificationTypeNames.MarkupNode);

        _types[
            (int)AkburaClassificationKind.Attribute] =
            GetRequired(
                registry,
                VisualStudioClassificationTypeNames.MarkupAttribute);

        _types[
            (int)AkburaClassificationKind.Identifier] =
            GetRequired(
                registry,
                VisualStudioClassificationTypeNames.Identifier);

        _types[
            (int)AkburaClassificationKind.Directive] =
            GetRequired(
                registry,
                VisualStudioClassificationTypeNames.PreprocessorKeyword);

        _types[
            (int)AkburaClassificationKind.String] =
            GetRequired(
                registry,
                VisualStudioClassificationTypeNames.String);

        _types[
            (int)AkburaClassificationKind.Number] =
            GetRequired(
                registry,
                VisualStudioClassificationTypeNames.Number);

        _types[
            (int)AkburaClassificationKind.Comment] =
            GetRequired(
                registry,
                VisualStudioClassificationTypeNames.Comment);

        _types[
            (int)AkburaClassificationKind.Operator] =
            GetRequired(
                registry,
                VisualStudioClassificationTypeNames.Operator);

        _types[
            (int)AkburaClassificationKind.Punctuation] =
            GetRequired(
                registry,
                VisualStudioClassificationTypeNames.Punctuation);

        _types[
            (int)AkburaClassificationKind.MarkupText] =
            GetRequired(
                registry,
                VisualStudioClassificationTypeNames.Text);

        _types[
            (int)AkburaClassificationKind.Utility] =
            GetRequired(
                registry,
                VisualStudioClassificationTypeNames
                    .MarkupAttributeValue);

        _types[
            (int)AkburaClassificationKind.UtilityModifier] =
            GetRequired(
                registry,
                VisualStudioClassificationTypeNames
                    .PreprocessorKeyword);

        _types[
            (int)AkburaClassificationKind.MarkupExtensionType] =
            GetRequired(
                registry,
                VisualStudioClassificationTypeNames.Type);

        _types[
            (int)AkburaClassificationKind
                .MarkupExtensionProperty] =
            GetRequired(
                registry,
                VisualStudioClassificationTypeNames
                    .MarkupAttribute);

        _types[
            (int)AkburaClassificationKind
                .MarkupExtensionValue] =
            GetRequired(
                registry,
                VisualStudioClassificationTypeNames
                    .MarkupAttributeValue);

        _types[
            (int)AkburaClassificationKind
                .MarkupExtensionPunctuation] =
            GetRequired(
                registry,
                VisualStudioClassificationTypeNames
                    .Punctuation);

        _types[
            (int)AkburaClassificationKind.EmbeddedCSharp] =
            GetRequired(
                registry,
                VisualStudioClassificationTypeNames
                    .FormalLanguage);

        _types[
            (int)AkburaClassificationKind.ClassName] =
            GetRequired(
                registry,
                RoslynClassificationTypeNames.ClassName);

        _types[
            (int)AkburaClassificationKind.StructName] =
            GetRequired(
                registry,
                RoslynClassificationTypeNames.StructName);

        _types[
            (int)AkburaClassificationKind.InterfaceName] =
            GetRequired(
                registry,
                RoslynClassificationTypeNames.InterfaceName);

        _types[
            (int)AkburaClassificationKind.EnumName] =
            GetRequired(
                registry,
                RoslynClassificationTypeNames.EnumName);

        _types[
            (int)AkburaClassificationKind.DelegateName] =
            GetRequired(
                registry,
                RoslynClassificationTypeNames.DelegateName);

        _types[
            (int)AkburaClassificationKind.TypeParameterName] =
            GetRequired(
                registry,
                RoslynClassificationTypeNames
                    .TypeParameterName);

        _types[
            (int)AkburaClassificationKind.MethodName] =
            GetRequired(
                registry,
                RoslynClassificationTypeNames.MethodName);

        _types[
            (int)AkburaClassificationKind.ExtensionMethodName] =
            GetRequired(
                registry,
                RoslynClassificationTypeNames
                    .ExtensionMethodName);

        _types[
            (int)AkburaClassificationKind.PropertyName] =
            GetRequired(
                registry,
                RoslynClassificationTypeNames.PropertyName);

        _types[
            (int)AkburaClassificationKind.EventName] =
            GetRequired(
                registry,
                RoslynClassificationTypeNames.EventName);

        _types[
            (int)AkburaClassificationKind.FieldName] =
            GetRequired(
                registry,
                RoslynClassificationTypeNames.FieldName);

        _types[
            (int)AkburaClassificationKind.EnumMemberName] =
            GetRequired(
                registry,
                RoslynClassificationTypeNames
                    .EnumMemberName);

        _types[
            (int)AkburaClassificationKind.ConstantName] =
            GetRequired(
                registry,
                RoslynClassificationTypeNames.ConstantName);

        _types[
            (int)AkburaClassificationKind.LocalName] =
            GetRequired(
                registry,
                RoslynClassificationTypeNames.LocalName);

        _types[
            (int)AkburaClassificationKind.ParameterName] =
            GetRequired(
                registry,
                RoslynClassificationTypeNames.ParameterName);

        _types[
            (int)AkburaClassificationKind.LabelName] =
            GetRequired(
                registry,
                RoslynClassificationTypeNames.LabelName);
    }

    public IClassificationType Get(
        AkburaClassificationKind kind)
    {
        var index = (int)kind;

        if ((uint)index >=
            (uint)_types.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                null);
        }

        return _types[index];
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