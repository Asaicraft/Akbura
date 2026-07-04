using Akbura.Language.Syntax;
using System;
using System.Collections.Immutable;

namespace Akbura.Language;

/// <summary>
/// Allows asking semantic questions about any node in an Akbura syntax tree within a compilation.
/// </summary>
internal sealed class SyntaxTreeSemanticModel : PublicSemanticModel
{
    /// <summary>
    /// Note, the name of this field could be somewhat confusing because it is also
    /// used to store models for initializers, executable blocks, markup and AKCSS roots,
    /// which are not component members.
    /// </summary>
    private ImmutableDictionary<AkburaSyntax, MemberSemanticModel> _memberModels =
        ImmutableDictionary<AkburaSyntax, MemberSemanticModel>.Empty;

    private readonly MemberSemanticModelFactory _memberSemanticModelFactory;

    internal SyntaxTreeSemanticModel(
        AkburaCompilation compilation,
        AkburaSyntaxTree syntaxTree)
        : base(compilation, syntaxTree)
    {
        _memberSemanticModelFactory = new MemberSemanticModelFactory(this);
    }

    internal override MemberSemanticModel GetMemberSemanticModel(AkburaSyntax syntax)
    {
        if (syntax == null)
        {
            throw new ArgumentNullException(nameof(syntax));
        }

        ValidateSyntaxTreeOwnership(syntax);

        var scope = _memberSemanticModelFactory.FindDocumentScope(syntax);
        var kind = MemberSemanticModelFactory.GetModelKind(syntax);
        var root = MemberSemanticModelFactory.GetModelRoot(syntax, kind, scope);
        return ImmutableInterlocked.GetOrAdd(
            ref _memberModels,
            root,
            static (node, arg) => arg.Factory.CreateMemberSemanticModel(
                node,
                arg.Kind,
                arg.Scope),
            (Factory: _memberSemanticModelFactory, Kind: kind, Scope: scope));
    }
}
