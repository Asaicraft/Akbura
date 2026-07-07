// This file is ported and adapted from the Roslyn (dotnet/roslyn)

#nullable disable

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace Akbura.Language;

internal sealed partial class DeclarationTable
{
    private sealed class Cache
    {
        private readonly DeclarationTable _table;

        private MergedNamespaceDeclaration _mergedRoot;
        private ISet<string> _declarationNames;

        public Cache(DeclarationTable table)
        {
            _table = table;
        }

        public MergedNamespaceDeclaration MergedRoot
        {
            get
            {
                if (_mergedRoot is null)
                {
                    Interlocked.CompareExchange(
                        ref _mergedRoot,
                        MergedNamespaceDeclaration.Create(
                            _table._allOlderRootDeclarations
                                .Select(static lazyRoot => lazyRoot.Value)
                                .ToImmutableArray()),
                        comparand: null);
                }

                return _mergedRoot;
            }
        }

        public ImmutableArray<Declaration> RootDeclarations => MergedRoot.Declarations;

        public ISet<string> DeclarationNames
        {
            get
            {
                if (_declarationNames is null)
                {
                    Interlocked.CompareExchange(
                        ref _declarationNames,
                        GetNames(MergedRoot.Declarations),
                        comparand: null);
                }

                return _declarationNames;
            }
        }
    }
}
