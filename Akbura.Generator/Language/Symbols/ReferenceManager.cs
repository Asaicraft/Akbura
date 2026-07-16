// This file is ported and adapted from the Roslyn (dotnet/roslyn)

#nullable enable

using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

namespace Akbura.Language;

internal sealed partial class AkburaCompilation
{
    /// <summary>
    /// Owns Akbura metadata imported through the underlying C# compilation references.
    /// </summary>
    internal sealed class ReferenceManager
    {
        private readonly Lazy<ImmutableArray<AkburaReferencedModule>> _lazyModules;

        private ReferenceManager(CSharpCompilation compilation)
        {
            if (compilation == null)
            {
                throw new ArgumentNullException(nameof(compilation));
            }

            _lazyModules = new Lazy<ImmutableArray<AkburaReferencedModule>>(
                () => AkburaReferencedModule.Load(compilation),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public ImmutableArray<AkburaReferencedModule> Modules => _lazyModules.Value;

        public bool IsBound => _lazyModules.IsValueCreated;

        public static ReferenceManager Create(
            CSharpCompilation compilation,
            CSharpCompilation? previousCompilation,
            ReferenceManager? previousManager)
        {
            if (compilation == null)
            {
                throw new ArgumentNullException(nameof(compilation));
            }

            if (previousManager != null &&
                previousCompilation != null &&
                HaveSameReferences(compilation, previousCompilation))
            {
                return previousManager;
            }

            return new ReferenceManager(compilation);
        }

        private static bool HaveSameReferences(
            CSharpCompilation compilation,
            CSharpCompilation previousCompilation)
        {
            if (ReferenceEquals(compilation, previousCompilation))
            {
                return true;
            }

            using var references = compilation.References.GetEnumerator();
            using var previousReferences = previousCompilation.References.GetEnumerator();
            while (references.MoveNext())
            {
                if (!previousReferences.MoveNext() ||
                    !ReferenceEquals(references.Current, previousReferences.Current))
                {
                    return false;
                }
            }

            return !previousReferences.MoveNext();
        }

        public bool ContainsComponentSyntaxTree(AkburaSyntaxTree syntaxTree)
        {
            return TryGetComponentDeclaration(syntaxTree, out _);
        }

        public IEnumerable<IAkburaComponentSymbol> GetComponentSymbols(string metadataName)
        {
            foreach (var module in Modules)
            {
                if (module.TryGetComponentSymbol(metadataName, out var symbol))
                {
                    yield return symbol;
                }
            }
        }

        public ImmutableArray<AkcssSyntaxTree> GetAkcssSyntaxTreesByLogicalName(string logicalName)
        {
            using var builder = ImmutableArrayBuilder<AkcssSyntaxTree>.Rent();
            foreach (var module in Modules)
            {
                builder.AddRange(module.GetAkcssSyntaxTreesByLogicalName(logicalName));
            }

            return builder.ToImmutable();
        }

        public bool TryGetComponentDeclaration(
            AkburaSyntaxTree syntaxTree,
            out AkburaModuleDeclaration declaration)
        {
            foreach (var module in Modules)
            {
                if (module.TryGetComponentDeclaration(syntaxTree, out declaration))
                {
                    return true;
                }
            }

            declaration = null!;
            return false;
        }

        public bool TryGetDeclaration(
            AkburaSyntax syntax,
            out Declaration declaration)
        {
            foreach (var module in Modules)
            {
                if (module.TryGetDeclaration(syntax, out declaration))
                {
                    return true;
                }
            }

            declaration = null!;
            return false;
        }

        public bool TryGetDeclarationPath(
            AkburaSyntax syntax,
            out ImmutableArray<Declaration> path)
        {
            foreach (var module in Modules)
            {
                if (module.TryGetDeclarationPath(syntax, out path))
                {
                    return true;
                }
            }

            path = default;
            return false;
        }

        public bool TryGetDeclarationPath(
            AkburaSyntax syntax,
            int position,
            out ImmutableArray<Declaration> path)
        {
            foreach (var module in Modules)
            {
                if (module.TryGetDeclarationPath(syntax, position, out path))
                {
                    return true;
                }
            }

            path = default;
            return false;
        }
    }
}
