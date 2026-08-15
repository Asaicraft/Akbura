using Akbura.Language.Symbols;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using AkburaPropertySymbol = Akbura.Language.Symbols.IPropertySymbol;

namespace Akbura.Workspaces;

internal sealed class AkcssSymbolDisplayService
{
    private static readonly SymbolDisplayFormat s_typeFormat =
        SymbolDisplayFormat.MinimallyQualifiedFormat
            .WithMiscellaneousOptions(
                SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
                SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
                SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public string FormatProperty(AkburaPropertySymbol property)
    {
        var type = FormatType(
            property.Type.Symbol as ITypeSymbol,
            property.Type.Name);
        var owner = AkcssPropertyReferenceFacts.GetPropertyOwnerType(property);
        var ownerName = FormatType(owner, owner?.Name ?? string.Empty);
        var accessors = (property.CanRead, property.CanWrite) switch
        {
            (true, true) => "{ get; set; }",
            (true, false) => "{ get; }",
            (false, true) => "{ set; }",
            _ => "{ }",
        };

        return string.IsNullOrEmpty(ownerName)
            ? $"{type} {property.Name} {accessors}"
            : $"{type} {ownerName}.{property.Name} {accessors}";
    }

    public string FormatStyle(IAkcssSymbol style)
    {
        var target = FormatTarget(style);
        if (style.ClassName is { Length: > 0 } className)
        {
            return string.IsNullOrEmpty(target)
                ? $"style {className}"
                : $"style {target}.{className}";
        }

        return $"style {target}".TrimEnd();
    }

    public string FormatUtility(
        ITailwindUtilitySymbol utility,
        CancellationToken cancellationToken)
    {
        var target = FormatTarget(utility);
        using var parameters = ImmutableArrayBuilder<string>.Rent(
            utility.Parameters.Length);
        foreach (var parameter in utility.Parameters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            parameters.Add(
                $"{FormatType(parameter.Type.Symbol as ITypeSymbol, parameter.Type.Name)} " +
                parameter.Name);
        }

        var name = string.IsNullOrEmpty(target)
            ? utility.Name
            : target + "." + utility.Name;
        return $"utility {name}({string.Join(", ", parameters.ToImmutable())})";
    }

    public string FormatParameter(ITailwindUtilityParameterSymbol parameter)
    {
        return $"{FormatType(parameter.Type.Symbol as ITypeSymbol, parameter.Type.Name)} " +
            parameter.Name;
    }

    public string FormatModule(IAkcssModuleSymbol module)
    {
        return "AKCSS module " + module.MetadataName;
    }

    public string FormatType(ITypeSymbol type)
    {
        return FormatType(type, type.Name);
    }

    public string FormatTarget(IAkcssSymbol symbol)
    {
        return symbol.HasTargetType
            ? FormatType(
                symbol.TargetType.Symbol as ITypeSymbol,
                symbol.TargetType.Name)
            : string.Empty;
    }

    public static string GetPropertyKind(AkburaPropertySymbol property)
    {
        if (property.IsAttachedProperty)
        {
            return "Avalonia attached property";
        }

        if (property.IsAvaloniaProperty)
        {
            return "Avalonia property";
        }

        if (property.IsClrProperty)
        {
            return "CLR property";
        }

        if (property.IsParameter)
        {
            return "Akbura parameter";
        }

        return property.IsCommand
            ? "Akbura command"
            : "Property";
    }

    private static string FormatType(ITypeSymbol? type, string fallback)
    {
        return type?.ToDisplayString(s_typeFormat) ?? fallback;
    }
}
