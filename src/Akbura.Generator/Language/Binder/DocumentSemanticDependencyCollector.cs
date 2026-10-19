using Akbura.Collections;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Operations;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

namespace Akbura.Language.Binder;

/// <summary>
/// Owned by one current binding session. Symbols are inspected only here; detach
/// exports sorted scalar facts and never the collector or its compilation owner.
/// Unsupported active paths close the proof rather than silently losing edges.
/// </summary>
internal sealed class DocumentSemanticDependencyCollector
{
    private static readonly IComparer<DetachedCSharpTypeIdentity> s_typeComparer = Comparer<DetachedCSharpTypeIdentity>.Create(static (left, right) =>
    {
        var assembly = StringComparer.Ordinal.Compare(left.AssemblyIdentity, right.AssemblyIdentity);
        return assembly != 0 ? assembly : StringComparer.Ordinal.Compare(left.MetadataName, right.MetadataName);
    });
    private static readonly IComparer<DetachedCSharpLookupDependency> s_lookupComparer = Comparer<DetachedCSharpLookupDependency>.Create(
        static (left, right) => StringComparer.Ordinal.Compare(left.Key, right.Key));

    private readonly AkburaSemanticModel _model;
    private readonly bool _enabled;
    private readonly object _gate = new();
    private readonly HashSet<DetachedCSharpTypeIdentity> _types = [];
    private readonly HashSet<string> _symbols = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DetachedCSharpLookupDependency> _lookups = new(StringComparer.Ordinal);
    private readonly HashSet<string> _conversions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _incomplete = new(StringComparer.Ordinal);
    private readonly HashSet<ITypeSymbol> _visited = new(SymbolEqualityComparer.Default);
    private readonly bool _fixedSurface;

    internal DocumentSemanticDependencyCollector(AkburaSemanticModel model)
    {
        _model = model;
        _enabled = model.Compilation.CollectCSharpSemanticDependencies;
        if (!_enabled)
        {
            return;
        }
        var root = model.SyntaxTree.GetRootSyntax();
        _fixedSurface = model.SyntaxTree.Kind == SyntaxTreeKind.Component && root.DescendantNodesAndSelf().All(static node =>
            node is not AkTopLevelMemberSyntax || node is UsingDirectiveSyntax or NamespaceDeclarationSyntax or MarkupRootSyntax);
        if (!_fixedSurface || root.ContainsDiagnostics || root.ContainsSkippedText)
        {
            _incomplete.Add("UnclassifiedDeclarationOrRecoveryPath");
        }
        if (model.GetCSharpUsingDirectivesCore().Any(static directive =>
            !directive.StaticKeyword.IsKind(CSharpSyntaxKind.None) || directive.Alias != null))
        {
            _incomplete.Add("UnclassifiedStaticOrAliasImportScope");
        }
        if (_fixedSurface && model.GetAkburaComponentMetadataName(model.SyntaxTree) is { Length: > 0 } ownerName &&
            model.Compilation.CSharpCompilation.GetTypeByMetadataName(ownerName) is { } owner &&
            IsOriginalSource(owner))
        {
            // A user partial declaration can add an inherited public surface that
            // is not described by the bare markup declaration graph.
            _incomplete.Add("OriginalSourceComponentDeclaration");
        }
        foreach (var node in root.DescendantNodesAndSelf())
        {
            if (node is MarkupContentSyntax && node is not (MarkupElementContentSyntax or MarkupTextLiteralSyntax or MarkupInlineExpressionSyntax) ||
                node is MarkupAttributeSyntax && node is not (MarkupPlainAttributeSyntax or MarkupAttachedPropertyAttributeSyntax) ||
                node is MarkupAttributeValueSyntax && node is not (MarkupLiteralAttributeValueSyntax or MarkupDynamicAttributeValueSyntax) ||
                node is MarkupStartTagSyntax tag && tag.Name.ToFullString().Contains('.') ||
                node is MarkupAttachedPropertyAttributeSyntax attached &&
                    (attached.OwnerType.ToFullString().Trim() != "x" || attached.Name.ToFullString().Trim() != "Name"))
            {
                _incomplete.Add("UnclassifiedMarkupScopeOrMetadataPath");
            }
        }
    }

    internal void MarkIncomplete(string reason)
    {
        if (_enabled)
        {
            lock (_gate)
            {
                _incomplete.Add(reason);
            }
        }
    }

    internal void Import(DetachedCSharpDependencySet? facts)
    {
        if (!_enabled)
        {
            return;
        }
        lock (_gate)
        {
            if (facts == null || facts.Schema != DetachedCSharpDependencySet.CurrentSchema)
            {
                _incomplete.Add("ImportedBindingWithoutSemanticDependencyFacts");
                return;
            }
            _types.UnionWith(facts.SourceTypes);
            _symbols.UnionWith(facts.Symbols);
            _conversions.UnionWith(facts.Conversions);
            _incomplete.UnionWith(facts.IncompleteReasons);
            foreach (var lookup in facts.Lookups)
            {
                _lookups[lookup.Key] = lookup;
            }
            if (!facts.IsComplete)
            {
                _incomplete.Add("IncompleteImportedSemanticDependencies");
            }
        }
    }

    internal void RecordMarkupType(ITypeSymbol? type)
    {
        if (!_enabled)
        {
            return;
        }
        lock (_gate)
        {
            if (type != null && (IsOriginalSource(type) || type.ContainingAssembly?.Name is not ("Avalonia.Controls" or "Avalonia.Base" or "Akbura")))
            {
                _incomplete.Add("OriginalSourceMarkupMetadata");
            }
            if (type is INamedTypeSymbol named && (named.Name.IndexOf("Template", StringComparison.Ordinal) >= 0 ||
                named.AllInterfaces.Any(static @interface => @interface.Name is "ITemplate" or "IDataTemplate" or "IControlTemplate")))
            {
                _incomplete.Add("UnclassifiedTemplateMetadataScope");
            }
            RecordType(type);
        }
    }

    internal void RecordTargetType(ITypeSymbol? type)
    {
        if (!_enabled)
        {
            return;
        }
        lock (_gate)
        {
            if (type != null && (IsOriginalSource(type) || !IsClosedLiteralTarget(type)))
            {
                _incomplete.Add("UnclassifiedAssignmentMetadata");
            }
            RecordType(type);
        }
    }

    internal void RecordComponentType(ITypeSymbol? type)
    {
        if (!_enabled || type == null)
        {
            return;
        }
        lock (_gate)
        {
            if (IsOriginalSource(type) || type.DeclaringSyntaxReferences.IsEmpty ||
                !Equals(type.ContainingAssembly?.Identity, _model.Compilation.CSharpCompilation.Assembly.Identity))
            {
                _incomplete.Add("UnclassifiedComponentMetadata");
            }
            RecordType(type);
        }
    }

    private static bool IsClosedLiteralTarget(ITypeSymbol type)
    {
        if (type.SpecialType != SpecialType.None)
        {
            return true;
        }
        if (type.ContainingAssembly?.Name is not ("Avalonia.Controls" or "Avalonia.Base") ||
            type.TypeKind != TypeKind.Enum && type.ToDisplayString() is not ("Avalonia.Media.IBrush" or "Avalonia.Media.Color" or
                "Avalonia.Thickness" or "Avalonia.CornerRadius" or "Avalonia.Vector" or "Avalonia.Point" or "Avalonia.Size" or
                "Avalonia.Media.FontFamily" or "Avalonia.Media.FontWeight" or "Avalonia.Media.FontStyle" or "Avalonia.Media.FontStretch"))
        {
            return false;
        }
        return !type.GetAttributes().Any(static attribute => attribute.AttributeClass?.Name == "TypeConverterAttribute" &&
            attribute.ConstructorArguments.Any(static argument => argument.Value is string name &&
                !name.StartsWith("System.", StringComparison.Ordinal) && !name.StartsWith("Avalonia.", StringComparison.Ordinal) &&
                !name.StartsWith("Akbura.", StringComparison.Ordinal)));
    }

    internal void Record(CSharp.ExpressionSyntax? syntax, ITypeSymbol? type, ISymbol? symbol, ITypeSymbol? receiver,
        ImmutableArray<ISymbol> candidates, CandidateReason reason, IOperation? operation, SemanticModel semanticModel)
    {
        if (!_enabled)
        {
            return;
        }
#if STATS
        using var measurement = GenerationStatistics.Measure(GenerationStatisticStage.SemanticDependencyCapture);
        GenerationStatistics.Increment(GenerationStatisticCounter.SemanticBindingOperationsObserved);
#endif
        lock (_gate)
        {
            if (_incomplete.Count != 0)
            {
                return;
            }
            if (_model.GetCSharpUsingDirectives().Any(static directive => !directive.StaticKeyword.IsKind(CSharpSyntaxKind.None) || directive.Alias != null))
            {
                _incomplete.Add("UnclassifiedStaticOrAliasImportScope");
                return;
            }
            RecordType(type);
            RecordType(receiver);
            RecordSymbol(symbol);
            foreach (var candidate in candidates)
            {
                RecordSymbol(candidate);
            }
            if (syntax != null && syntax.DescendantNodesAndSelf().Any(static node => node is CSharp.AnonymousFunctionExpressionSyntax or
                CSharp.QueryExpressionSyntax or CSharp.AwaitExpressionSyntax or CSharp.StackAllocArrayCreationExpressionSyntax or CSharp.GenericNameSyntax))
            {
                _incomplete.Add("UnclassifiedExpressionScope");
            }
            RecordOperation(operation);
            if (syntax != null)
            {
                // Static receivers are absent from IOperation children. Include
                // every explicit receiver, not just the outermost selected member:
                // Derived.BaseStatic().ToString() can change after Derived gains a
                // hiding member even though the previous selected owner was Base.
                foreach (var access in syntax.DescendantNodesAndSelf().OfType<CSharp.MemberAccessExpressionSyntax>())
                {
                    var receiverSymbol = semanticModel.GetSymbolInfo(access.Expression).Symbol;
                    var receiverType = semanticModel.GetTypeInfo(access.Expression).Type ?? receiverSymbol as ITypeSymbol;
                    RecordType(receiverType);
                    var result = semanticModel.GetSymbolInfo(access);
                    var lookupKey = TypeIdentity(receiverType) + "\0" + access.Name.Identifier.ValueText + "\0" + access;
                    var members = receiverType is INamedTypeSymbol owner ? owner.GetMembers(access.Name.Identifier.ValueText) : [];
                    _lookups[lookupKey] = new DetachedCSharpLookupDependency(lookupKey, TypeIdentity(receiverType), access.Name.Identifier.ValueText,
                        string.Join("\n", _model.GetCSharpUsingDirectives().Select(static directive => directive.ToFullString())),
                        SymbolIdentity(result.Symbol), (int)result.CandidateReason,
                        CollectCandidateIdentities(members, result.CandidateSymbols), [], result.Symbol == null);
                }
            }
            var name = syntax switch
            {
                CSharp.InvocationExpressionSyntax { Expression: CSharp.MemberAccessExpressionSyntax access } => access.Name.Identifier.ValueText,
                CSharp.MemberAccessExpressionSyntax access => access.Name.Identifier.ValueText,
                CSharp.IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                _ => string.Empty,
            };
            if (name.Length != 0)
            {
                var owner = receiver ?? symbol?.ContainingType;
                if (owner == null && symbol is not (INamespaceSymbol or ITypeSymbol))
                {
                    _incomplete.Add("UnknownLookupReceiver");
                }
                var wholeCandidates = PooledStringHashSet.GetInstance();
                try
                {
                    if (owner is INamedTypeSymbol named)
                    {
                        for (var current = named; current != null; current = current.BaseType)
                        {
                            RecordType(current);
                            foreach (var candidate in current.GetMembers(name))
                            {
                                RecordSymbol(candidate);
                                wholeCandidates.Add(SymbolIdentity(candidate));
                            }
                        }
                        foreach (var @interface in named.AllInterfaces)
                        {
                            RecordType(@interface);
                            foreach (var candidate in @interface.GetMembers(name))
                            {
                                RecordSymbol(candidate);
                                wholeCandidates.Add(SymbolIdentity(candidate));
                            }
                        }
                    }
                    if (symbol is IMethodSymbol { IsExtensionMethod: true } or IMethodSymbol { Arity: > 0 })
                    {
                        _incomplete.Add("ExtensionOrGenericLookup");
                    }
                    var arguments = operation is IInvocationOperation invocation
                        ? invocation.Arguments.Select(static argument => TypeIdentity(argument.Value.Type)).ToImmutableArray() : ImmutableArray<string>.Empty;
                    if (symbol == null && receiver == null && type == null)
                    {
                        _incomplete.Add("UnresolvedLookupUniverse");
                    }
                    var scope = string.Join("\n", _model.GetCSharpUsingDirectives().Select(static directive => directive.ToFullString()));
                    var receiverIdentity = TypeIdentity(owner);
                    var key = receiverIdentity + "\0" + name + "\0" + syntax?.ToString();
                    _lookups[key] = new DetachedCSharpLookupDependency(key, receiverIdentity, name, scope, SymbolIdentity(symbol), (int)reason,
                        wholeCandidates.ToSortedImmutableArray(StringComparer.Ordinal),
                        arguments, symbol == null);
                }
                finally
                {
                    wholeCandidates.Free();
                }
            }
        }
    }

    internal void RecordConversion(ITypeSymbol? source, ITypeSymbol? target, IMethodSymbol? method, string kind)
    {
        if (!_enabled)
        {
            return;
        }
        lock (_gate)
        {
            if (_incomplete.Count != 0)
            {
                return;
            }
            RecordType(source);
            RecordType(target);
            RecordSymbol(method);
            _conversions.Add(TypeIdentity(source) + " -> " + TypeIdentity(target) + " : " + kind + " : " + SymbolIdentity(method));
        }
    }

    private void RecordOperation(IOperation? operation)
    {
        if (operation == null)
        {
            return;
        }
        RecordType(operation.Type);
        switch (operation)
        {
            case IMemberReferenceOperation member:
                RecordSymbol(member.Member);
                break;
            case IInvocationOperation invocation:
                RecordSymbol(invocation.TargetMethod);
                if (invocation.TargetMethod.IsExtensionMethod || invocation.TargetMethod.Arity != 0)
                {
                    _incomplete.Add("ExtensionOrGenericLookup");
                }
                break;
            case IObjectCreationOperation creation:
                RecordSymbol(creation.Constructor);
                break;
            case IConversionOperation conversion:
                RecordSymbol(conversion.OperatorMethod);
                _conversions.Add(TypeIdentity(conversion.Operand.Type) + " -> " + TypeIdentity(conversion.Type) + " : " + SymbolIdentity(conversion.OperatorMethod));
                break;
            case IBinaryOperation binary:
                RecordSymbol(binary.OperatorMethod);
                break;
            case IUnaryOperation unary:
                RecordSymbol(unary.OperatorMethod);
                break;
            case ILiteralOperation or IInstanceReferenceOperation or IArgumentOperation or ILocalReferenceOperation or
                IParameterReferenceOperation or IInvalidOperation or IParenthesizedOperation:
                break;
            default:
                _incomplete.Add("UnclassifiedOperation:" + operation.Kind);
                break;
        }
        foreach (var child in operation.ChildOperations)
        {
            RecordOperation(child);
        }
    }

    private void RecordSymbol(ISymbol? symbol)
    {
        if (symbol == null)
        {
            return;
        }
        _symbols.Add(SymbolIdentity(symbol));
        RecordType(symbol.ContainingType);
        switch (symbol)
        {
            case ITypeSymbol type:
                RecordType(type);
                break;
            case IMethodSymbol method:
                RecordType(method.ReturnType);
                foreach (var parameter in method.Parameters)
                {
                    RecordType(parameter.Type);
                }
                break;
            case IPropertySymbol property:
                RecordType(property.Type);
                break;
            case IFieldSymbol field:
                RecordType(field.Type);
                break;
            case IEventSymbol @event:
                RecordType(@event.Type);
                break;
        }
    }

    private void RecordType(ITypeSymbol? type)
    {
        if (type == null || !_visited.Add(type))
        {
            return;
        }
        if (type is IArrayTypeSymbol array)
        {
            RecordType(array.ElementType);
            return;
        }
        if (type is not INamedTypeSymbol named || named.TypeKind == TypeKind.Error)
        {
            _incomplete.Add("UnclassifiedType:" + type.TypeKind);
            return;
        }
        foreach (var argument in named.TypeArguments)
        {
            RecordType(argument);
        }
        RecordType(named.BaseType);
        foreach (var @interface in named.Interfaces)
        {
            RecordType(@interface);
        }
        if (!IsOriginalSource(named))
        {
            return;
        }
        _types.Add(new DetachedCSharpTypeIdentity(named.ContainingAssembly.Identity.ToString(), MetadataName(named.OriginalDefinition)));
        if (named.Arity != 0 || named.ContainingType != null || named.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax() is not CSharp.ClassDeclarationSyntax declaration || declaration.Modifiers.Any(CSharpSyntaxKind.PartialKeyword) ||
            declaration.DescendantNodes().Any(static node => node is CSharp.AttributeSyntax ||
                node is CSharp.ParameterSyntax { Default.Value: not CSharp.LiteralExpressionSyntax } ||
                node is CSharp.FieldDeclarationSyntax field && field.Modifiers.Any(CSharpSyntaxKind.ConstKeyword) &&
                    field.Declaration.Variables.Any(static variable => variable.Initializer?.Value is not CSharp.LiteralExpressionSyntax))))
        {
            _incomplete.Add("UnclassifiedSourceDeclaration");
        }
    }

    private bool IsOriginalSource(ITypeSymbol type) => type.DeclaringSyntaxReferences.Any(reference =>
        _model.Compilation.CSharpCompilation.SyntaxTrees.Any(tree => ReferenceEquals(tree, reference.SyntaxTree)));

    private static string MetadataName(INamedTypeSymbol type) => type.ContainingType != null
        ? MetadataName(type.ContainingType) + "+" + type.MetadataName
        : (type.ContainingNamespace.IsGlobalNamespace ? string.Empty : type.ContainingNamespace.ToDisplayString() + ".") + type.MetadataName;

    private static string TypeIdentity(ITypeSymbol? type) => type == null ? string.Empty :
        (type.ContainingAssembly?.Identity.ToString() ?? string.Empty) + "|" + type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string SymbolIdentity(ISymbol? symbol) => symbol == null ? string.Empty :
        (symbol.ContainingAssembly?.Identity.ToString() ?? string.Empty) + "|" + (symbol.GetDocumentationCommentId() ?? symbol.ToDisplayString()) +
        "|" + symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

    internal static ImmutableArray<string> CollectCandidateIdentities(ImmutableArray<ISymbol> members, ImmutableArray<ISymbol> candidates)
    {
        var identities = PooledStringHashSet.GetInstance();
        try
        {
            foreach (var member in members)
            {
                identities.Add(SymbolIdentity(member));
            }
            foreach (var candidate in candidates)
            {
                identities.Add(SymbolIdentity(candidate));
            }
            // Equal ordinal strings are the same scalar fact; stable tie order is irrelevant.
            return identities.ToSortedImmutableArray(StringComparer.Ordinal);
        }
        finally
        {
            identities.Free();
        }
    }

    internal DetachedCSharpDependencySet Detach(CancellationToken cancellationToken = default)
    {
#if STATS
        using var measurement = GenerationStatistics.Measure(GenerationStatisticStage.DependencySetDetachment);
#endif
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
#if STATS
            GenerationStatistics.Increment(GenerationStatisticCounter.DetachedSemanticDependencyEntries);
#endif
            return !_enabled ? DetachedCSharpDependencySet.Missing : new(DetachedCSharpDependencySet.CurrentSchema, _incomplete.Count == 0, _fixedSurface && _incomplete.Count == 0,
                _types.ToSortedImmutableArray(s_typeComparer),
                _symbols.ToSortedImmutableArray(StringComparer.Ordinal),
                _lookups.Values.ToSortedImmutableArray(s_lookupComparer),
                _conversions.ToSortedImmutableArray(StringComparer.Ordinal),
                _incomplete.ToSortedImmutableArray(StringComparer.Ordinal));
        }
    }
}
