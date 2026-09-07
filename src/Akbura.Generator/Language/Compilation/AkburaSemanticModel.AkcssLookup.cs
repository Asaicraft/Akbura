using Akbura.Language.Syntax;
using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace Akbura.Language;

internal abstract partial class AkburaSemanticModel
{
    // SyntaxList equality uses the exact red list-node identity, distinguishing
    // separate inline scopes and source revisions even when paths/text match.
    // Share only with wrappers of this model; never export through reusable state
    // because resolved types/parameters belong to this compilation's probes.
    private readonly ConcurrentDictionary<SyntaxList<AkcssTopLevelMemberSyntax>,
        ImmutableArray<AkcssLookupSymbolDescriptor>> _akcssLookupSymbols;
}
