using Akbura.Language.Syntax;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Syntax-only external or inline module identity. Inline modules share their owner's version.
/// </summary>
internal sealed class AkcssDocumentDescriptor(
    AkburaSyntaxTree syntaxTree,
    AkburaSyntax root,
    string sourcePath,
    string moduleIdentity,
    string generatedTypeName,
    DocumentSyntaxVersion documentVersion)
{
    public AkburaSyntaxTree SyntaxTree { get; } = syntaxTree;
    public AkburaSyntax Root { get; } = root;
    public string SourcePath { get; } = sourcePath;
    public string ModuleIdentity { get; } = moduleIdentity;
    public string GeneratedTypeName { get; } = generatedTypeName;
    public DocumentSyntaxVersion DocumentVersion { get; } = documentVersion;
    public bool IsInline => Root is InlineAkcssBlockSyntax;
}
