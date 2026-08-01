using Akbura.Language.Symbols;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Language.Operations;

internal interface IMetadataAkcssOperation : IAkcssOperation
{
    AttributeData MetadataAttribute { get; }

    int Order { get; }

    int ParentOrder { get; }

    int Depth { get; }

    MetadataAkcssOperationOrigin Origin { get; }

    MetadataAkcssOperationPriority Priority { get; }

    string? DeclaringSymbolMetadataName { get; }

    CSharpSymbolDefinition MetadataTargetType { get; }

    string? Expression { get; }

    CSharpSymbolDefinition ExpressionType { get; }

    int ExpandedFromOrder { get; }

    string? SourcePath { get; }

    TextSpan SourceSpan { get; }
}

internal interface IMetadataAkcssApplyOperation : IMetadataAkcssOperation
{
    System.Collections.Immutable.ImmutableArray<string> AppliedSymbolMetadataNames { get; }

    System.Collections.Immutable.ImmutableArray<IAkcssOperation> ExpandedOperations { get; }
}

internal enum MetadataAkcssOperationOrigin
{
    Direct,
    ApplyExpansion,
    Synthesized,
}

internal enum MetadataAkcssOperationPriority
{
    Style,
    StyleTrigger,
}
