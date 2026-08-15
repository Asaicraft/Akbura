using Akbura.Language.Symbols;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using AkburaPropertySymbol = Akbura.Language.Symbols.IPropertySymbol;

namespace Akbura.Workspaces;

internal sealed class AkburaQuickInfoService : IAkburaQuickInfoService
{
    private readonly AkcssReferenceResolver _referenceResolver;
    private readonly AkcssSymbolDisplayService _display = new();

    public AkburaQuickInfoService(AkcssReferenceResolver referenceResolver)
    {
        _referenceResolver = referenceResolver ??
            throw new ArgumentNullException(nameof(referenceResolver));
    }

    public AkburaQuickInfo? GetQuickInfo(
        AkburaDocumentContext context,
        int position,
        CancellationToken cancellationToken = default)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!_referenceResolver.TryResolve(
                context,
                position,
                out var reference,
                cancellationToken))
        {
            return null;
        }

        return CreateQuickInfo(reference, cancellationToken);
    }

    private AkburaQuickInfo? CreateQuickInfo(
        AkcssResolvedReference reference,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (reference.Kind == AkcssReferenceKind.PropertyOwnerType &&
            reference.CSharpDefinition.Symbol is ITypeSymbol type)
        {
            return new AkburaQuickInfo(
                reference.SourceSpan,
                AkburaQuickInfoKind.Type,
                GetTypeSignature(type),
                ImmutableArray<string>.Empty);
        }

        return reference.Symbol switch
        {
            AkburaPropertySymbol property => CreateProperty(reference, property),
            ITailwindUtilityParameterSymbol parameter =>
                CreateParameter(reference, parameter),
            ITailwindUtilitySymbol utility =>
                CreateUtility(reference, utility, cancellationToken),
            IAkcssModuleSymbol module =>
                CreateModule(reference, module, cancellationToken),
            IAkcssSymbol style => CreateStyle(reference, style),
            _ => null,
        };
    }

    private AkburaQuickInfo CreateProperty(
        AkcssResolvedReference reference,
        AkburaPropertySymbol property)
    {
        return new AkburaQuickInfo(
            reference.SourceSpan,
            AkburaQuickInfoKind.Property,
            _display.FormatProperty(property),
            [AkcssSymbolDisplayService.GetPropertyKind(property)]);
    }

    private AkburaQuickInfo CreateStyle(
        AkcssResolvedReference reference,
        IAkcssSymbol style)
    {
        using var details = ImmutableArrayBuilder<string>.Rent();
        var target = _display.FormatTarget(style);
        details.Add(string.IsNullOrEmpty(target)
            ? "Target: default AKCSS target"
            : "Target: " + target);
        if (style.IsIntercepted && style.InterceptType.Symbol is ITypeSymbol intercept)
        {
            details.Add("Intercept: " + _display.FormatType(intercept));
        }
        AddDeclaredIn(style, details);

        return new AkburaQuickInfo(
            reference.SourceSpan,
            AkburaQuickInfoKind.Style,
            _display.FormatStyle(style),
            details.ToImmutable());
    }

    private AkburaQuickInfo CreateUtility(
        AkcssResolvedReference reference,
        ITailwindUtilitySymbol utility,
        CancellationToken cancellationToken)
    {
        using var details = ImmutableArrayBuilder<string>.Rent();
        var target = _display.FormatTarget(utility);
        details.Add(string.IsNullOrEmpty(target)
            ? "Target: default AKCSS target"
            : "Target: " + target);
        AddDeclaredIn(utility, details);

        return new AkburaQuickInfo(
            reference.SourceSpan,
            AkburaQuickInfoKind.Utility,
            _display.FormatUtility(utility, cancellationToken),
            details.ToImmutable());
    }

    private AkburaQuickInfo CreateParameter(
        AkcssResolvedReference reference,
        ITailwindUtilityParameterSymbol parameter)
    {
        using var details = ImmutableArrayBuilder<string>.Rent();
        details.Add("AKCSS utility parameter");
        if (parameter.ContainingSymbol is ITailwindUtilitySymbol utility)
        {
            var target = _display.FormatTarget(utility);
            details.Add("Containing utility: " +
                (string.IsNullOrEmpty(target)
                    ? utility.Name
                    : target + "." + utility.Name));
        }

        return new AkburaQuickInfo(
            reference.SourceSpan,
            AkburaQuickInfoKind.Parameter,
            _display.FormatParameter(parameter),
            details.ToImmutable());
    }

    private AkburaQuickInfo CreateModule(
        AkcssResolvedReference reference,
        IAkcssModuleSymbol module,
        CancellationToken cancellationToken)
    {
        var styles = 0;
        var utilities = 0;
        foreach (var symbol in module.AkcssSymbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (symbol is ITailwindUtilitySymbol)
            {
                utilities++;
            }
            else
            {
                styles++;
            }
        }

        using var details = ImmutableArrayBuilder<string>.Rent();
        details.Add($"Styles: {styles} · Utilities: {utilities}");
        if (module.Path is { Length: > 0 } path)
        {
            details.Add("Source: " + path);
        }
        if (module is IMetadataAkcssModuleSymbol metadata)
        {
            details.Add("Assembly: " +
                metadata.RuntimeModuleType.ContainingAssembly.Name);
        }

        return new AkburaQuickInfo(
            reference.SourceSpan,
            AkburaQuickInfoKind.Module,
            _display.FormatModule(module),
            details.ToImmutable());
    }

    private static string GetTypeSignature(ITypeSymbol type)
    {
        var prefix = type.TypeKind switch
        {
            TypeKind.Class => "class ",
            TypeKind.Struct => "struct ",
            TypeKind.Interface => "interface ",
            TypeKind.Enum => "enum ",
            TypeKind.Delegate => "delegate ",
            _ => string.Empty,
        };
        return prefix + type.ToDisplayString(
            SymbolDisplayFormat.MinimallyQualifiedFormat);
    }

    private static void AddDeclaredIn(
        IAkcssSymbol symbol,
        ImmutableArrayBuilder<string> details)
    {
        if (symbol.ContainingSymbol is IAkcssModuleSymbol
            {
                Path: { Length: > 0 } path,
            })
        {
            details.Add("Declared in: " + path);
        }
    }
}
