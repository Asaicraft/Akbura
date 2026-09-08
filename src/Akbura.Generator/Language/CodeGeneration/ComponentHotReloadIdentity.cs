using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Globalization;
using System.Text;

namespace Akbura.Language.CodeGeneration;

internal static class ComponentHotReloadIdentity
{
    private static readonly SymbolDisplayFormat s_typeFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions:
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.ExpandNullable);

    public static string CreateParameterKey(
        string name,
        ITypeSymbol type,
        ComponentParameterKind kind,
        ComponentParameterFlags flags)
    {
        var isContent =
            (flags & ComponentParameterFlags.IsContent) != 0;
        var propertyKind = kind == ComponentParameterKind.Collection
            ? isContent
                ? "direct:content-collection"
                : "direct:collection"
            : isContent
                ? "styled:content"
                : "styled:normal";

        return "param:" +
            name + ":" +
            GetTypeIdentity(type) + ":" +
            propertyKind;
    }

    public static string CreateServiceKey(string name, ITypeSymbol type)
    {
        return "service:" + name + ":" + GetTypeIdentity(type) + ":direct";
    }

    public static string CreateCommandKey(
        string name,
        ITypeSymbol resultType,
        ReadOnlySpan<ComponentCommandParameterPlan> parameters)
    {
        var builder = new StringBuilder("command:");
        builder.Append(name);

        for (var i = 0; i < parameters.Length; i++)
        {
            builder.Append(':');
            builder.Append(GetTypeIdentity(parameters[i].Type));
        }

        builder.Append(':');
        builder.Append(GetTypeIdentity(resultType));
        return builder.ToString();
    }

    public static string CreateStateKey(
        string name,
        ITypeSymbol type,
        ComponentStateFactoryKind factoryKind)
    {
        return "state:" +
            name + ":" +
            GetTypeIdentity(type) + ":" +
            (factoryKind == ComponentStateFactoryKind.State ? "state" : "value");
    }

    public static string CreateGeneratedName(string name, string identity)
    {
        var builder = new StringBuilder(name.Length + 17);

        for (var i = 0; i < name.Length; i++)
        {
            var character = name[i];
            builder.Append(char.IsLetterOrDigit(character) || character == '_'
                ? character
                : '_');
        }

        if (builder.Length == 0)
        {
            builder.Append("member");
        }

        builder.Append('_');
        builder.Append(ComputeHash(identity).ToString("x16", CultureInfo.InvariantCulture));
        return builder.ToString();
    }

    public static string CreateDescriptorFingerprint(in ComponentMemberPlan plan)
    {
        var hash = new FingerprintHash();

        for (var i = 0; i < plan.Parameters.Length; i++)
        {
            ref readonly var parameter = ref plan.Parameters.ItemRef(i);
            hash.Add(parameter.HotReloadKey);
            hash.Add((int)parameter.BindingKind);
            hash.Add((int)parameter.Kind);
            hash.Add((int)parameter.Flags);
            hash.Add(parameter.Collection.ObservesChanges);
            hash.Add(parameter.Collection.PropertyType);
            hash.Add(parameter.Collection.ElementType);
            hash.Add(parameter.Collection.BackingType);
            hash.Add(parameter.DefaultValue);
        }

        for (var i = 0; i < plan.Services.Length; i++)
        {
            ref readonly var service = ref plan.Services.ItemRef(i);
            hash.Add(service.HotReloadKey);
            hash.Add(service.IsOptional);
        }

        for (var i = 0; i < plan.Commands.Length; i++)
        {
            ref readonly var command = ref plan.Commands.ItemRef(i);
            hash.Add(command.HotReloadKey);
        }

        return hash.ToString();
    }

    public static string CreateStateFingerprint(in ComponentMemberPlan plan)
    {
        var hash = new FingerprintHash();

        for (var i = 0; i < plan.States.Length; i++)
        {
            ref readonly var state = ref plan.States.ItemRef(i);
            hash.Add(state.HotReloadKey);
        }

        return hash.ToString();
    }

    private static string GetTypeIdentity(ITypeSymbol? type)
    {
        return type?.ToDisplayString(s_typeFormat) ?? string.Empty;
    }

    private static ulong ComputeHash(string value)
    {
        var hash = new FingerprintHash();
        hash.Add(value);
        return hash.Value;
    }

    private struct FingerprintHash
    {
        private const ulong Offset = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        private ulong _value;

        public readonly ulong Value => _value == 0 ? Offset : _value;

        public void Add(bool value)
        {
            Add(value ? 1 : 0);
        }

        public void Add(int value)
        {
            Add(value.ToString(CultureInfo.InvariantCulture));
        }

        public void Add(ITypeSymbol? type)
        {
            Add(GetTypeIdentity(type));
        }

        public void Add(CSharpSyntaxNode? syntax)
        {
            if (syntax == null)
            {
                Add(string.Empty);
                return;
            }

            foreach (var token in syntax.DescendantTokens(descendIntoTrivia: false))
            {
                Add(token.RawKind);
                Add(token.ValueText);
            }
        }

        public void Add(string value)
        {
            if (_value == 0)
            {
                _value = Offset;
            }

            for (var i = 0; i < value.Length; i++)
            {
                _value ^= value[i];
                _value *= Prime;
            }

            _value ^= 0xff;
            _value *= Prime;
        }

        public override readonly string ToString()
        {
            return Value.ToString("X16", CultureInfo.InvariantCulture);
        }
    }
}
