using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using AkburaSymbol = Akbura.Language.Symbols.ISymbol;
using RoslynSymbol = Microsoft.CodeAnalysis.ISymbol;

namespace Akbura.Language;

/// <summary>
/// Immutable, owner-independent result subset captured after generation completes.
/// The caller must additionally validate declarations, global usings and dependencies
/// before import. No binder, bound node, operation, member model or mutable composite
/// symbol is retained. Unsupported result categories are recomputed by the new model.
/// </summary>
internal sealed class SemanticModelState
{
    private SemanticModelState(Builder builder)
    {
        SourceTree = builder.SourceTree;
        CSharpCompilation = builder.CSharpCompilation;
        RootNamespace = builder.RootNamespace;
        ProjectDirectory = builder.ProjectDirectory;
        SymbolInfos = builder.SymbolInfos.ToImmutable();
        DeclarationSymbolInfos = builder.DeclarationSymbolInfos.ToImmutable();
        Diagnostics = builder.Diagnostics.ToImmutable();
        AggregatedDiagnostics = builder.AggregatedDiagnostics.ToImmutable();
    }

    public AkburaSyntaxTree SourceTree { get; }
    public CSharpCompilation CSharpCompilation { get; }
    public string RootNamespace { get; }
    public string ProjectDirectory { get; }
    public int CachedResultCount => SymbolInfos.Count + DeclarationSymbolInfos.Count +
        Diagnostics.Count + AggregatedDiagnostics.Count;

    internal ImmutableDictionary<AkburaSyntax, AkburaSymbolInfo> SymbolInfos { get; }
    internal ImmutableDictionary<AkburaSyntax, AkburaSymbolInfo> DeclarationSymbolInfos { get; }
    internal ImmutableDictionary<AkburaSyntax, ImmutableArray<AkburaSemanticDiagnostic>> Diagnostics { get; }
    internal ImmutableDictionary<AkburaSyntax, ImmutableArray<AkburaSemanticDiagnostic>> AggregatedDiagnostics { get; }

    internal bool IsCompatibleWith(AkburaCompilation compilation, AkburaSyntaxTree syntaxTree)
    {
        return CachedResultCount > 0 && ReferenceEquals(SourceTree, syntaxTree) &&
            ReferenceEquals(CSharpCompilation, compilation.CSharpCompilation) &&
            string.Equals(RootNamespace, compilation.RootNamespace, StringComparison.Ordinal) &&
            string.Equals(ProjectDirectory, compilation.ProjectDirectory, StringComparison.Ordinal);
    }

    internal sealed class Builder
    {
        private readonly List<IAssemblySymbol> _allowedAssemblies = [];

        public Builder(AkburaSyntaxTree sourceTree, CSharpCompilation csharpCompilation, string rootNamespace, string projectDirectory)
        {
            SourceTree = sourceTree;
            CSharpCompilation = csharpCompilation;
            RootNamespace = rootNamespace;
            ProjectDirectory = projectDirectory;
            _allowedAssemblies.Add(csharpCompilation.Assembly);
            foreach (var reference in csharpCompilation.References)
            {
                if (csharpCompilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly)
                {
                    _allowedAssemblies.Add(assembly);
                }
            }
        }

        public AkburaSyntaxTree SourceTree { get; }
        public CSharpCompilation CSharpCompilation { get; }
        public string RootNamespace { get; }
        public string ProjectDirectory { get; }
        public ImmutableDictionary<AkburaSyntax, AkburaSymbolInfo>.Builder SymbolInfos { get; } =
            ImmutableDictionary.CreateBuilder<AkburaSyntax, AkburaSymbolInfo>();
        public ImmutableDictionary<AkburaSyntax, AkburaSymbolInfo>.Builder DeclarationSymbolInfos { get; } =
            ImmutableDictionary.CreateBuilder<AkburaSyntax, AkburaSymbolInfo>();
        public ImmutableDictionary<AkburaSyntax, ImmutableArray<AkburaSemanticDiagnostic>>.Builder Diagnostics { get; } =
            ImmutableDictionary.CreateBuilder<AkburaSyntax, ImmutableArray<AkburaSemanticDiagnostic>>();
        public ImmutableDictionary<AkburaSyntax, ImmutableArray<AkburaSemanticDiagnostic>>.Builder AggregatedDiagnostics { get; } =
            ImmutableDictionary.CreateBuilder<AkburaSyntax, ImmutableArray<AkburaSemanticDiagnostic>>();

        public void AddSymbolInfo(AkburaSyntax syntax, AkburaSymbolInfo info, bool declaration = false)
        {
            if (!OwnsSyntax(syntax) || !IsSafeSymbol(info.Symbol))
            {
                return;
            }

            for (var i = 0; !info.CandidateSymbols.IsDefault && i < info.CandidateSymbols.Length; i++)
            {
                if (!IsSafeSymbol(info.CandidateSymbols[i]))
                {
                    return;
                }
            }

            (declaration ? DeclarationSymbolInfos : SymbolInfos)[syntax] = info;
        }

        public void AddDiagnostics(AkburaSyntax syntax, ImmutableArray<AkburaSemanticDiagnostic> diagnostics, bool aggregated = false)
        {
            if (!OwnsSyntax(syntax))
            {
                return;
            }

            diagnostics = diagnostics.IsDefault ? [] : diagnostics;
            for (var i = 0; i < diagnostics.Length; i++)
            {
                var diagnostic = diagnostics[i];
                if (!OwnsSyntax(diagnostic.Syntax))
                {
                    return;
                }

                for (var parameterIndex = 0; !diagnostic.Parameters.IsDefault && parameterIndex < diagnostic.Parameters.Length; parameterIndex++)
                {
                    var parameter = diagnostic.Parameters[parameterIndex];
                    if (parameter is not (null or string or char or bool or byte or sbyte or short or ushort or
                        int or uint or long or ulong or float or double or decimal or Enum))
                    {
                        // Never preserve arbitrary diagnostic payloads that could retain a model.
                        return;
                    }
                }
            }

            (aggregated ? AggregatedDiagnostics : Diagnostics)[syntax] = diagnostics;
        }

        public SemanticModelState ToState() => new(this);

        private bool OwnsSyntax(AkburaSyntax syntax) => ReferenceEquals(syntax.Root, SourceTree.GetRootSyntax());

        private bool IsSafeSymbol(AkburaSymbol? symbol)
        {
            if (symbol == null)
            {
                return true;
            }

            if (symbol.ContainingSymbol != null || !symbol.Locations.IsDefaultOrEmpty ||
                !symbol.DeclaringSyntaxReferences.IsDefaultOrEmpty)
            {
                return false;
            }

            // Explicit sealed-type allowlist. New symbol kinds are unsafe until audited.
            return symbol switch
            {
                ParamSymbol parameter => OwnsSyntax(parameter.DeclarationSyntax) &&
                    IsSafeCSharpSymbol(parameter.Type.Symbol) && IsSafeCSharpSymbol(parameter.DefaultValueType.Symbol),
                InjectSymbol injected => OwnsSyntax(injected.DeclarationSyntax) && IsSafeCSharpSymbol(injected.Type.Symbol),
                StateSymbol { UseHook: null } state => OwnsSyntax(state.DeclarationSyntax) &&
                    IsSafeCSharpSymbol(state.Type.Symbol) && IsSafeCSharpSymbol(state.InitializerType.Symbol),
                PropertySymbol { Parameter: null, Command: null } property =>
                    IsSafeCSharpSymbol(property.Type.Symbol) && IsSafeCSharpSymbol(property.AvaloniaPropertyDefinition.Symbol) &&
                    IsSafeCSharpSymbol(property.AttachedPropertyDefinition.Symbol) && IsSafeCSharpSymbol(property.AttachedGetterDefinition.Symbol) &&
                    IsSafeCSharpSymbol(property.AttachedSetterDefinition.Symbol) && IsSafeCSharpSymbol(property.AttachedTargetType.Symbol) &&
                    IsSafeCSharpSymbol(property.ClrPropertyDefinition.Symbol),
                _ => false,
            };
        }

        private bool IsSafeCSharpSymbol(RoslynSymbol? symbol)
        {
            if (symbol == null)
            {
                return true;
            }

            if (symbol is IArrayTypeSymbol array)
            {
                return IsSafeCSharpSymbol(array.ElementType);
            }

            if (symbol is IPointerTypeSymbol pointer)
            {
                return IsSafeCSharpSymbol(pointer.PointedAtType);
            }

            if (symbol is IDynamicTypeSymbol)
            {
                return true;
            }

            if (!HasAllowedAssembly(symbol.ContainingAssembly))
            {
                return false;
            }

            if (symbol is ITypeParameterSymbol)
            {
                return true;
            }

            if (symbol is INamedTypeSymbol type)
            {
                if (type.TypeKind == TypeKind.Error)
                {
                    return false;
                }

                foreach (var argument in type.TypeArguments)
                {
                    if (!IsSafeCSharpSymbol(argument))
                    {
                        return false;
                    }
                }
            }

            if (symbol is IMethodSymbol method)
            {
                foreach (var argument in method.TypeArguments)
                {
                    if (!IsSafeCSharpSymbol(argument))
                    {
                        return false;
                    }
                }
            }

            return symbol.ContainingType == null || IsSafeCSharpSymbol(symbol.ContainingType);
        }

        private bool HasAllowedAssembly(IAssemblySymbol? assembly)
        {
            for (var i = 0; i < _allowedAssemblies.Count; i++)
            {
                if (ReferenceEquals(assembly, _allowedAssemblies[i]))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
