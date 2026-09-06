using Akbura.Language.Syntax;
using System.Collections.Immutable;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Syntax-only component identity. Semantic inputs are resolved only for dirty documents.
/// </summary>
internal sealed class ComponentDocumentDescriptor(
    ComponentSyntaxTree syntaxTree,
    string sourcePath,
    string componentMetadataName,
    DocumentSyntaxVersion documentVersion,
    ImmutableArray<AkcssDocumentDescriptor> inlineAkcssDescriptors)
{
    public ComponentSyntaxTree SyntaxTree { get; } = syntaxTree;
    public AkburaDocumentSyntax Root => SyntaxTree.GetRoot();
    public string SourcePath { get; } = sourcePath;
    public string ComponentMetadataName { get; } = componentMetadataName;
    public DocumentSyntaxVersion DocumentVersion { get; } = documentVersion;
    public ImmutableArray<AkcssDocumentDescriptor> InlineAkcssDescriptors { get; } = inlineAkcssDescriptors;
}
