// This file is ported and adapted from the Roslyn (dotnet/roslyn)

#nullable disable

using Akbura.Pools;
using System;
using System.Collections.Generic;

namespace Akbura.Language;

internal sealed partial class DeclarationTable
{
    internal sealed class Builder
    {
        private static readonly ObjectPool<Builder> s_builderPool = new(() => new Builder());

        private DeclarationTable _table;
        private readonly List<Lazy<RootSingleNamespaceDeclaration>> _addedLazyRootDeclarations;
        private readonly List<Lazy<RootSingleNamespaceDeclaration>> _removedLazyRootDeclarations;

        private Builder()
        {
            _table = Empty;
            _addedLazyRootDeclarations = [];
            _removedLazyRootDeclarations = [];
        }

        public static Builder GetInstance(DeclarationTable table)
        {
            var builder = s_builderPool.Allocate();
            builder._table = table;
            return builder;
        }

        public void AddRootDeclaration(Lazy<RootSingleNamespaceDeclaration> lazyRootDeclaration)
        {
            RealizeRemoves();
            _addedLazyRootDeclarations.Add(lazyRootDeclaration);
        }

        public void RemoveRootDeclaration(Lazy<RootSingleNamespaceDeclaration> lazyRootDeclaration)
        {
            RealizeAdds();
            _removedLazyRootDeclarations.Add(lazyRootDeclaration);
        }

        public DeclarationTable ToDeclarationTableAndFree()
        {
            RealizeAdds();
            RealizeRemoves();

            var result = _table;

            _table = Empty;
            s_builderPool.Free(this);

            return result;
        }

        private void RealizeAdds()
        {
            if (_addedLazyRootDeclarations.Count == 0)
            {
                return;
            }

            var lastDeclaration = _addedLazyRootDeclarations[^1];
            if (_addedLazyRootDeclarations.Count == 1)
            {
                _table = _table._latestLazyRootDeclaration == null
                    ? new DeclarationTable(
                        _table._allOlderRootDeclarations,
                        lastDeclaration,
                        _table._cache,
                        _table._components,
                        _table._akcssModules)
                    : new DeclarationTable(
                        _table._allOlderRootDeclarations.Add(_table._latestLazyRootDeclaration),
                        lastDeclaration,
                        null,
                        _table._components,
                        _table._akcssModules);
            }
            else
            {
                _addedLazyRootDeclarations.RemoveAt(_addedLazyRootDeclarations.Count - 1);

                if (_table._latestLazyRootDeclaration != null)
                {
                    _addedLazyRootDeclarations.Insert(0, _table._latestLazyRootDeclaration);
                }

                var olderRootDeclarations = _table._allOlderRootDeclarations.AddRange(_addedLazyRootDeclarations);
                _table = new DeclarationTable(
                    olderRootDeclarations,
                    lastDeclaration,
                    null,
                    _table._components,
                    _table._akcssModules);
            }

            _addedLazyRootDeclarations.Clear();
        }

        private void RealizeRemoves()
        {
            if (_removedLazyRootDeclarations.Count == 0)
            {
                return;
            }

            if (_removedLazyRootDeclarations.Count == 1)
            {
                var firstDeclaration = _removedLazyRootDeclarations[0];
                _table = _table._latestLazyRootDeclaration == firstDeclaration
                    ? new DeclarationTable(
                        _table._allOlderRootDeclarations,
                        null,
                        _table._cache,
                        _table._components,
                        _table._akcssModules)
                    : new DeclarationTable(
                        _table._allOlderRootDeclarations.Remove(firstDeclaration),
                        _table._latestLazyRootDeclaration,
                        null,
                        _table._components,
                        _table._akcssModules);
            }
            else
            {
                var isLatestRemoved = _table._latestLazyRootDeclaration != null &&
                                      _removedLazyRootDeclarations.Contains(_table._latestLazyRootDeclaration);
                var olderRootDeclarations = _table._allOlderRootDeclarations.RemoveRange(_removedLazyRootDeclarations);
                var latestLazyRootDeclaration = isLatestRemoved
                    ? null
                    : _table._latestLazyRootDeclaration;

                _table = new DeclarationTable(
                    olderRootDeclarations,
                    latestLazyRootDeclaration,
                    null,
                    _table._components,
                    _table._akcssModules);
            }

            _removedLazyRootDeclarations.Clear();
        }
    }
}
