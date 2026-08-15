using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace Akbura.Language;

internal abstract partial class AkburaSemanticModel
{
    private readonly ConcurrentDictionary<
        AkcssApplyDirectiveSyntax,
        ImmutableArray<AkcssApplyItemReference>>
        _akcssApplyItemReferences = new();

    internal ImmutableArray<AkcssApplyItemReference>
        GetAkcssApplyItemReferences(
            AkcssApplyDirectiveSyntax apply,
            CancellationToken cancellationToken = default)
    {
        if (apply == null)
        {
            throw new ArgumentNullException(nameof(apply));
        }
        ValidateSyntaxTreeOwnership(apply);
        cancellationToken.ThrowIfCancellationRequested();

        if (_akcssApplyItemReferences.TryGetValue(apply, out var cached))
        {
            return cached;
        }

        var references = CreateAkcssApplyItemReferences(
            apply,
            cancellationToken);
        return _akcssApplyItemReferences.GetOrAdd(apply, references);
    }

    internal bool TryResolveAkcssModuleImport(
        AkcssUsingDirectiveSyntax usingDirective,
        out IAkcssModuleSymbol module,
        CancellationToken cancellationToken = default)
    {
        if (usingDirective == null)
        {
            throw new ArgumentNullException(nameof(usingDirective));
        }
        ValidateSyntaxTreeOwnership(usingDirective);
        cancellationToken.ThrowIfCancellationRequested();

        if (!usingDirective.IsAkcssModuleImport)
        {
            module = null!;
            return false;
        }

        var logicalName = usingDirective.Name.ToFullString().Trim();
        if (IsSelfAkcssImport(logicalName))
        {
            module = null!;
            return false;
        }

        var matches = Compilation.LookupAkcssModulesByLogicalName(
            logicalName,
            cancellationToken);
        if (matches.Length != 1)
        {
            module = null!;
            return false;
        }

        module = matches[0];
        return true;
    }

    private ImmutableArray<AkcssApplyItemReference>
        CreateAkcssApplyItemReferences(
            AkcssApplyDirectiveSyntax apply,
            CancellationToken cancellationToken)
    {
        if (GetOperation(apply) is not IAkcssApplyOperation operation)
        {
            return ImmutableArray<AkcssApplyItemReference>.Empty;
        }

        var items = AkcssApplyItemFacts.GetItems(SyntaxTree.Text, apply);
        using var references =
            ImmutableArrayBuilder<AkcssApplyItemReference>.Rent(items.Length);
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var diagnostics =
                ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
            var symbol = ResolveAkcssApplyItem(
                apply,
                item.Text,
                operation.ContainingAkcssSymbol,
                diagnostics);
            references.Add(new AkcssApplyItemReference(
                item.Span,
                item.Text,
                symbol));
        }

        return references.ToImmutable();
    }
}
