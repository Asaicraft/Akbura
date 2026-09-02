using Microsoft.CodeAnalysis;
using System.Diagnostics;
using AkburaPropertySymbol = Akbura.Language.Symbols.IPropertySymbol;
using RoslynPropertySymbol = Microsoft.CodeAnalysis.IPropertySymbol;

namespace Akbura.Language.CodeGeneration;

internal enum PropertyObservationKind : byte
{
    None,
    AvaloniaProperty,
    GeneratedParameter,
    NotifyPropertyChanged,
}

internal readonly struct PropertyObservationPlan
{
    private PropertyObservationPlan(
        PropertyObservationKind kind,
        ISymbol? symbol,
        string? name)
    {
        Kind = kind;
        Symbol = symbol;
        Name = name;
    }

    public PropertyObservationKind Kind { get; }

    public ISymbol? Symbol { get; }

    public string? Name { get; }

    public bool IsValid => Kind != PropertyObservationKind.None;

    public MarkupTargetPropertyPlan TargetProperty => Kind switch
    {
        PropertyObservationKind.AvaloniaProperty when Symbol != null =>
            MarkupTargetPropertyPlan.CreateStaticMember(Symbol),
        PropertyObservationKind.GeneratedParameter
            when Symbol is ITypeSymbol ownerType && Name is { Length: > 0 } name =>
                MarkupTargetPropertyPlan.CreateGeneratedParameter(ownerType, name),
        _ => default,
    };

    public static PropertyObservationPlan CreateAvaloniaProperty(ISymbol property)
    {
        Debug.Assert(property is IFieldSymbol { IsStatic: true } or IPropertySymbol { IsStatic: true });

        if (property is not IFieldSymbol { IsStatic: true } and not IPropertySymbol { IsStatic: true })
        {
            return default;
        }

        return new PropertyObservationPlan(
            PropertyObservationKind.AvaloniaProperty,
            property,
            name: null);
    }

    public static PropertyObservationPlan CreateGeneratedParameter(
        ITypeSymbol ownerType,
        string name)
    {
        Debug.Assert(ownerType != null);
        Debug.Assert(!string.IsNullOrEmpty(name));

        if (ownerType == null || string.IsNullOrEmpty(name))
        {
            return default;
        }

        return new PropertyObservationPlan(
            PropertyObservationKind.GeneratedParameter,
            ownerType,
            name);
    }

    public static PropertyObservationPlan CreateNotifyPropertyChanged(IPropertySymbol property)
    {
        Debug.Assert(property != null);

        if (property == null)
        {
            return default;
        }

        return new PropertyObservationPlan(
            PropertyObservationKind.NotifyPropertyChanged,
            property,
            name: null);
    }

    public static PropertyObservationPlan Create(
        AkburaPropertySymbol property,
        ITypeSymbol ownerType)
    {
        Debug.Assert(property != null);
        Debug.Assert(ownerType != null);

        if (property == null || ownerType == null)
        {
            return default;
        }

        var avaloniaProperty =
            GetAvaloniaPropertyMember(property.ReadDefinition.Symbol) ??
            GetAvaloniaPropertyMember(property.WriteDefinition.Symbol) ??
            GetAvaloniaPropertyMember(property.AvaloniaPropertyDefinition.Symbol) ??
            GetAvaloniaPropertyMember(property.AttachedPropertyDefinition.Symbol);
        if (avaloniaProperty != null)
        {
            return CreateAvaloniaProperty(avaloniaProperty);
        }

        if (property.Parameter != null)
        {
            return CreateGeneratedParameter(ownerType, property.Name);
        }

        var read = PropertyReadPlan.Create(property);
        return read.ClrProperty == null
            ? default
            : CreateNotifyPropertyChanged(read.ClrProperty);
    }

    private static ISymbol? GetAvaloniaPropertyMember(ISymbol? symbol)
    {
        var type = symbol switch
        {
            IFieldSymbol { IsStatic: true } field => field.Type,
            RoslynPropertySymbol { IsStatic: true } property => property.Type,
            _ => null,
        };

        return type != null && IsAvaloniaPropertyType(type) ? symbol : null;
    }

    private static bool IsAvaloniaPropertyType(ITypeSymbol type)
    {
        for (var current = type as INamedTypeSymbol; current != null; current = current.BaseType)
        {
            if (current.Name == "AvaloniaProperty" && IsAvaloniaNamespace(current.ContainingNamespace))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAvaloniaNamespace(INamespaceSymbol @namespace)
    {
        return @namespace.Name == "Avalonia" && @namespace.ContainingNamespace.IsGlobalNamespace;
    }
}
