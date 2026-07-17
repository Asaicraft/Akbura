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
    /// Owns both live project snapshots and Akbura metadata imported from PE references.
    /// </summary>
    internal sealed class ReferenceManager
    {
        private readonly ImmutableArray<AkburaCompilationReference> _compilationReferences;
        private readonly Lazy<ImmutableArray<AkburaReferencedModule>> _lazyModules;

        private ReferenceManager(
            CSharpCompilation compilation,
            ImmutableArray<AkburaCompilationReference> compilationReferences)
        {
            if (compilation == null)
            {
                throw new ArgumentNullException(nameof(compilation));
            }

            _compilationReferences = ValidateCompilationReferences(
                compilation,
                compilationReferences);
            _lazyModules = new Lazy<ImmutableArray<AkburaReferencedModule>>(
                () => AkburaReferencedModule.Load(compilation),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public ImmutableArray<AkburaCompilationReference> CompilationReferences =>
            _compilationReferences;

        public ImmutableArray<AkburaReferencedModule> Modules => _lazyModules.Value;

        public bool IsBound => _lazyModules.IsValueCreated;

        public static ReferenceManager Create(
            CSharpCompilation compilation,
            ImmutableArray<AkburaCompilationReference> compilationReferences,
            CSharpCompilation? previousCompilation,
            ReferenceManager? previousManager)
        {
            if (compilation == null)
            {
                throw new ArgumentNullException(nameof(compilation));
            }

            compilationReferences = compilationReferences.IsDefault
                ? ImmutableArray<AkburaCompilationReference>.Empty
                : compilationReferences;
            if (previousManager != null &&
                previousCompilation != null &&
                HaveSameReferences(compilation, previousCompilation) &&
                HaveSameCompilationReferences(
                    compilationReferences,
                    previousManager._compilationReferences))
            {
                return previousManager;
            }

            return new ReferenceManager(compilation, compilationReferences);
        }

        private static ImmutableArray<AkburaCompilationReference> ValidateCompilationReferences(
            CSharpCompilation compilation,
            ImmutableArray<AkburaCompilationReference> references)
        {
            if (references.IsDefaultOrEmpty)
            {
                return ImmutableArray<AkburaCompilationReference>.Empty;
            }

            var seen = new HashSet<AkburaCompilation>();
            foreach (var reference in references)
            {
                if (reference == null)
                {
                    throw new ArgumentException(
                        "Akbura compilation references cannot contain null.",
                        nameof(references));
                }

                if (ReferenceEquals(reference.Compilation.CSharpCompilation, compilation))
                {
                    throw new ArgumentException(
                        "An Akbura compilation cannot reference itself.",
                        nameof(references));
                }

                if (!seen.Add(reference.Compilation))
                {
                    throw new ArgumentException(
                        "Duplicate Akbura compilation reference.",
                        nameof(references));
                }
            }

            return references;
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

        private static bool HaveSameCompilationReferences(
            ImmutableArray<AkburaCompilationReference> references,
            ImmutableArray<AkburaCompilationReference> previousReferences)
        {
            if (references.Length != previousReferences.Length)
            {
                return false;
            }

            for (var index = 0; index < references.Length; index++)
            {
                if (!ReferenceEquals(
                        references[index].Compilation,
                        previousReferences[index].Compilation))
                {
                    return false;
                }
            }

            return true;
        }

        public bool ContainsComponentSyntaxTree(AkburaSyntaxTree syntaxTree)
        {
            foreach (var reference in _compilationReferences)
            {
                if (reference.ContainsComponentSyntaxTree(syntaxTree))
                {
                    return true;
                }
            }

            foreach (var module in Modules)
            {
                if (module.TryGetComponentDeclaration(syntaxTree, out _))
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryGetSemanticModel(
            AkburaSyntaxTree syntaxTree,
            out AkburaSemanticModel semanticModel)
        {
            foreach (var reference in _compilationReferences)
            {
                if (reference.TryGetSemanticModel(syntaxTree, out semanticModel))
                {
                    return true;
                }
            }

            semanticModel = null!;
            return false;
        }

        public IEnumerable<IAkburaComponentSymbol> GetComponentSymbols(string metadataName)
        {
            foreach (var reference in _compilationReferences)
            {
                if (reference.TryGetComponentSymbol(metadataName, out var symbol))
                {
                    yield return symbol;
                }
            }

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
            foreach (var reference in _compilationReferences)
            {
                builder.AddRange(reference.GetAkcssSyntaxTreesByLogicalName(logicalName));
            }

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
            foreach (var reference in _compilationReferences)
            {
                if (reference.TryGetDeclaration(syntax, out declaration))
                {
                    return true;
                }
            }

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
            foreach (var reference in _compilationReferences)
            {
                if (reference.TryGetDeclarationPath(syntax, out path))
                {
                    return true;
                }
            }

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
            foreach (var reference in _compilationReferences)
            {
                if (reference.TryGetDeclarationPath(syntax, position, out path))
                {
                    return true;
                }
            }

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
