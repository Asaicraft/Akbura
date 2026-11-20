using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using RoslynSymbol = Microsoft.CodeAnalysis.ISymbol;

namespace Akbura.Language.CodeGeneration;

internal sealed record AkcssNativeModulePlan(
    ImmutableArray<AkcssNativeStylePlan> Styles,
    ImmutableArray<AkcssNativeThemePlan> Themes,
    ImmutableDictionary<AkburaSyntax, AkcssNativeApplicationPlan> Applications,
    AkcssModuleExport? Export,
    ImmutableArray<AkburaSemanticDiagnostic> Diagnostics,
    ImmutableHashSet<string> ReservedNames);

internal sealed record AkcssNativeApplicationPlan(INamedTypeSymbol Target,
    ImmutableArray<AkcssNativeApplicationParameter> Parameters, ImmutableArray<AkcssNativeMemberPlan> Members);

internal sealed record AkcssNativeApplicationParameter(string LocalName, ITypeSymbol Type, int Ordinal);

internal abstract record AkcssNativeMemberPlan(AkburaSyntax Syntax);

internal sealed record AkcssNativeStylePlan(AkburaSyntax Syntax, AkcssSelectorBinding Selector,
    ImmutableArray<AkcssNativeMemberPlan> Members) : AkcssNativeMemberPlan(Syntax);

internal sealed record AkcssNativeThemePlan(AkcssThemeSymbol Theme, INamedTypeSymbol Target,
    ImmutableArray<AkcssNativeMemberPlan> Members);

internal sealed record AkcssNativeSetterPlan(AkburaSyntax Syntax, RoslynSymbol Property, ITypeSymbol ValueType,
    AkcssSetterPropertyKind PropertyKind, string Expression, bool IsDynamicResource) : AkcssNativeMemberPlan(Syntax);

internal sealed record AkcssNativeArgumentPlan(AkburaSyntax Syntax, AkcssMixinParameterSymbol Parameter,
    string Name, ITypeSymbol Type, string Expression);

internal sealed record AkcssNativeIncludePlan(AkburaSyntax Syntax, ImmutableArray<AkcssNativeArgumentPlan> Arguments,
    ImmutableArray<AkcssNativeMemberPlan> Members) : AkcssNativeMemberPlan(Syntax);

internal sealed record AkcssNativeBaseThemePlan(AkburaSyntax Syntax, string Reference) : AkcssNativeMemberPlan(Syntax);

internal sealed record AkcssNativeTemplateBindingPlan(AkburaSyntax Syntax, RoslynSymbol Source, RoslynSymbol Destination)
    : AkcssNativeMemberPlan(Syntax);

internal sealed record AkcssNativeControlTemplatePlan(AkburaSyntax Syntax, AkcssTemplateContext Template,
    ImmutableDictionary<MarkupElementSyntax, ImmutableArray<AkcssNativeMemberPlan>> Configurations)
    : AkcssNativeMemberPlan(Syntax);

internal sealed class AkcssNativeNames(IEnumerable<string> reserved)
{
    private readonly HashSet<string> _used = new(reserved, StringComparer.Ordinal);
    private int _next;

    public string Allocate(string prefix)
    {
        string name;
        do
        {
            name = prefix + (_next++).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        while (!_used.Add(name));

        return name;
    }

    public ImmutableHashSet<string> ToImmutable() => _used.ToImmutableHashSet(StringComparer.Ordinal);
}
