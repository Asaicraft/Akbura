using Akbura.Language.Syntax;
using StateBindingKind = Akbura.Language.Symbols.StateBindingKind;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Akbura.Language.Operations;
using System.Globalization;
using System.Security.Cryptography;
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
        var propertyKind = kind == ComponentParameterKind.Dictionary
            ? "direct:content-dictionary"
            : kind == ComponentParameterKind.Collection
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
        ComponentStateFactoryKind factoryKind,
        bool isComposable = false,
        StateBindingKind bindingKind = StateBindingKind.None,
        string? bindingPath = null,
        ReadOnlySpan<string> bindingDependencies = default,
        ComponentStateBindingRootKind bindingRootKind = ComponentStateBindingRootKind.Component,
        string? bindingRootIdentity = null,
        ITypeSymbol? bindingSourceType = null,
        ReadOnlySpan<ComponentStateBindingPropertyDependencyPlan> bindingPropertyDependencies = default)
    {
        var identity = "state:" +
            name + ":" +
            GetTypeIdentity(type) + ":" +
            (isComposable ? "hook" :
                factoryKind == ComponentStateFactoryKind.State ? "state" : "value") + ":" +
            bindingKind + ":" +
            bindingPath;
        if (bindingKind == StateBindingKind.None)
        {
            return identity;
        }

        var builder = new StringBuilder(identity);
        builder.Append(":root:");
        builder.Append(bindingRootKind);
        builder.Append(':');
        builder.Append(bindingRootIdentity);
        builder.Append(':');
        builder.Append(bindingSourceType == null
            ? "<unknown>"
            : GetTypeIdentity(bindingSourceType));
        for (var index = 0; index < bindingDependencies.Length; index++)
        {
            builder.Append(":dependency:");
            builder.Append("State:");
            builder.Append(bindingDependencies[index]);
        }

        for (var index = 0; index < bindingPropertyDependencies.Length; index++)
        {
            builder.Append(":dependency:");
            builder.Append(bindingPropertyDependencies[index].Kind);
            builder.Append(':');
            builder.Append(bindingPropertyDependencies[index].HotReloadIdentity);
        }

        return builder.ToString();
    }

    public static string CreateAvaloniaPropertyKey(ISymbol property)
    {
        var propertyType = property switch
        {
            IFieldSymbol { IsStatic: true } field => field.Type,
            IPropertySymbol { IsStatic: true } staticProperty => staticProperty.Type,
            _ => null,
        };

        return "avalonia-property:" +
            property.ContainingType?.ToDisplayString(s_typeFormat) + "." +
            property.MetadataName + ":" +
            (propertyType == null ? "<unknown>" : GetTypeIdentity(propertyType));
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

    public static string CreateRenderFingerprint(in ComponentPlan plan)
    {
        var hash = new FingerprintHash();

        for (var i = 0; i < plan.Elements.Length; i++)
        {
            ref readonly var element = ref plan.Elements.ItemRef(i);
            if (!element.UsesRuntimeStorage && !element.IsStyleSubtree)
            {
                continue;
            }

            if (element.IsStyleSubtree)
            {
                hash.Add(element.Type);
                hash.Add(CreateOperationSyntaxIdentity(element.Syntax));
                continue;
            }

            hash.Add(element.RuntimeStorageId);
            hash.Add(GetRuntimeParentId(plan, element));
            hash.Add(
                ComponentStructuralHotReloadWriter.GetElementSlot(
                    plan,
                    element));
            hash.Add(element.Type);
            hash.Add(element.ExplicitKey ?? string.Empty);
            hash.Add(CreateRenderSyntaxIdentity(element.Syntax));
        }

        foreach (ref readonly var content in plan.CollectionContents.AsSpan())
        {
            hash.Add(content.DictionaryShape.ContractType);
            hash.Add(content.DictionaryShape.KeyType);
            hash.Add(content.DictionaryShape.ValueType);
            hash.Add(content.ReplacesStyles);
        }

        return hash.ToString();
    }

    public static string CreateLocalTemplateFactoryIdentity(in ComponentPlan plan,
        in ComponentPropertyContentPlan content, in ComponentTemplatePlan template)
    {
        var hash = CreateLocalFactoryContract(plan, content, template.ScopeId,
            "__BuildConditionalTemplate" + template.Id, template.Syntax);
        AddFactoryType(ref hash, template.DataType);
        hash.Add(template.ItemName);
        AddLocalFactoryCaptures(ref hash, plan, template.ScopeId, template.ItemName);
        return hash.ToString();
    }

    public static string CreateLocalDeferredFactoryIdentity(in ComponentPlan plan,
        in ComponentPropertyContentPlan content, in ComponentDeferredContentPlan deferred)
    {
        var hash = CreateLocalFactoryContract(plan, content, deferred.ScopeId,
            "__BuildDeferredContent" + deferred.Id, deferred.Syntax);
        AddFactoryType(ref hash, deferred.ResultType);
        AddFactoryType(ref hash, deferred.DataType);
        hash.Add(deferred.ItemName ?? string.Empty);
        AddLocalFactoryCaptures(ref hash, plan, deferred.ScopeId, deferred.ItemName);
        return hash.ToString();
    }

    private static FingerprintHash CreateLocalFactoryContract(in ComponentPlan plan,
        in ComponentPropertyContentPlan content, int scopeId, string helperName, AkburaSyntax boundary)
    {
        var hash = new FingerprintHash();
        ref readonly var scope = ref plan.Scopes.ItemRef(scopeId);
        ref readonly var root = ref plan.Elements.ItemRef(plan.ScopeRootElementIds[scope.Roots.Start]);
        hash.Add(helperName);
        hash.Add((int)scope.Kind);
        AddFactoryType(ref hash, plan.Elements.ItemRef(content.OwnerElementId).Type);
        AddFactoryType(ref hash, root.Type);
        hash.Add(CreatePropertySlot(content.Destination));
        hash.Add(boundary is MarkupElementSyntax { StartTag: { } startTag }
            ? CreateOperationSyntaxIdentity(startTag)
            : boundary.Kind.ToString());
        return hash;
    }

    private static void AddLocalFactoryCaptures(ref FingerprintHash hash, in ComponentPlan plan,
        int scopeId, string? itemName)
    {
        foreach (var ancestor in TemplateWriter.GetAncestorTemplates(plan, scopeId, itemName))
        {
            hash.Add(ancestor.ItemName);
            AddFactoryType(ref hash, ancestor.DataType);
        }

        var captures = new System.Collections.Generic.SortedDictionary<string, ITypeSymbol>(StringComparer.Ordinal);
        foreach (var statement in plan.RenderStatements)
        {
            if (statement.RenderCaptures.IsDefaultOrEmpty)
            {
                continue;
            }
            foreach (var capture in statement.RenderCaptures)
            {
                if (capture.IsReadByScope(scopeId))
                {
                    captures[capture.Key] = capture.Type;
                }
            }
        }
        foreach (var region in plan.ConditionalRegions)
        {
            foreach (var branch in region.Branches)
            {
                foreach (var capture in branch.Captures)
                {
                    if (capture.IsReadByScope(scopeId))
                    {
                        captures[capture.Key] = capture.Type;
                    }
                }
            }
        }
        foreach (var capture in captures)
        {
            hash.Add(capture.Key);
            AddFactoryType(ref hash, capture.Value);
        }
    }

    private static void AddFactoryType(ref FingerprintHash hash, ITypeSymbol? type)
    {
        hash.Add(type?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier)) ?? string.Empty);
    }

    public static string CreateRenderSyntaxIdentity(MarkupElementSyntax syntax)
    {
        if (syntax == null)
        {
            throw new ArgumentNullException(nameof(syntax));
        }

        var builder = new StringBuilder();
        AppendRenderTokens(builder, syntax.StartTag);

        for (var i = 0; i < syntax.Body.Count; i++)
        {
            var content = syntax.Body[i];
            if (content is MarkupElementContentSyntax)
            {
                continue;
            }

            AppendRenderTokens(builder, content);
        }

        return ComputeRenderSyntaxHash(builder.ToString());
    }

    public static string CreateOperationSyntaxIdentity(AkburaSyntax syntax)
    {
        if (syntax == null)
        {
            throw new ArgumentNullException(nameof(syntax));
        }

        var builder = new StringBuilder();
        AppendRenderTokens(builder, syntax);
        return ComputeRenderSyntaxHash(builder.ToString());
    }

    public static string CreateContentSyntaxIdentity(AkburaSyntax syntax)
    {
        if (syntax == null)
        {
            throw new ArgumentNullException(nameof(syntax));
        }

        var builder = new StringBuilder();
        if (syntax is MarkupElementSyntax element)
        {
            for (var i = 0; i < element.Body.Count; i++)
            {
                AppendRenderTokens(builder, element.Body[i]);
            }
        }
        else
        {
            AppendRenderTokens(builder, syntax);
        }

        return ComputeRenderSyntaxHash(builder.ToString());
    }

    internal static string CreateForeachTemplateRevision(IMarkupForeachOperation operation)
    {
        AkburaDebug.AssertNotNull(operation);

        return CreateContentSyntaxIdentity(operation.Syntax.Body);
    }

    internal static string CreateForeachKeyContractIdentity(IMarkupForeachOperation operation)
    {
        AkburaDebug.AssertNotNull(operation);

        if (operation.Key.IsDefault)
        {
            return string.Empty;
        }

        if (operation.KeySyntax == null)
        {
            throw new InvalidOperationException(
                "A keyed foreach operation must retain the syntax that defines its key contract.");
        }

        var hash = new FingerprintHash();
        hash.Add("foreach-key-contract");
        hash.Add(CreateOperationSyntaxIdentity(operation.KeySyntax));
        hash.Add(operation.IterationType.Symbol as ITypeSymbol);
        hash.Add(operation.Key.Type);
        hash.Add(operation.Key.Kind is { } kind ? (int)kind : -1);

        // Generated foreach regions currently use EqualityComparer<TKey>.Default.
        // Keep comparer policy in the identity so future comparer support has
        // an explicit place in the hot reload contract.
        hash.Add("comparer:default");

        return hash.ToString();
    }

    private static string ComputeRenderSyntaxHash(string normalizedSyntax)
    {
        using var algorithm = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(normalizedSyntax);
        var digest = algorithm.ComputeHash(bytes);
        var builder = new StringBuilder(digest.Length * 2);

        for (var i = 0; i < digest.Length; i++)
        {
            builder.Append(digest[i].ToString(
                "X2",
                CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static void AppendRenderTokens(
        StringBuilder builder,
        AkburaSyntax? syntax)
    {
        if (syntax == null)
        {
            return;
        }

        foreach (var token in syntax.DescendantTokens(descendIntoTrivia: false))
        {
            var value = token.ValueText;

            builder.Append(token.RawKind);
            builder.Append(':');
            builder.Append(value.Length);
            builder.Append(':');
            builder.Append(value);
            builder.Append(';');
        }
    }

    public static string CreateCollectionSlot(
        in CollectionWritePlan plan)
    {
        return plan.Kind switch
        {
            CollectionWriteKind.Property =>
                "collection:" + CreatePropertyReadIdentity(plan.Property),
            CollectionWriteKind.ComponentParameter =>
                "parameter:" + (plan.ComponentParameterName ?? string.Empty),
            _ => "content",
        };
    }

    public static string CreatePropertySlot(
        in PropertyWritePlan plan)
    {
        var identity = plan.Kind switch
        {
            PropertyWriteKind.ClrProperty =>
                GetSymbolIdentity(plan.ClrProperty),
            PropertyWriteKind.AvaloniaProperty =>
                GetSymbolIdentity(plan.AvaloniaProperty),
            PropertyWriteKind.AttachedAccessor =>
                GetSymbolIdentity(plan.AttachedSetter),
            PropertyWriteKind.ComponentParameter or
                PropertyWriteKind.DirectMember =>
                plan.MemberName ?? string.Empty,
            _ => string.Empty,
        };

        return "property:" + identity;
    }

    public static string CreatePropertySubscriptionSlot(in PropertyObservationPlan observation, int sourceOrder)
    {
        var identity = observation.Kind switch
        {
            PropertyObservationKind.GeneratedParameter
                when observation.Symbol is ITypeSymbol ownerType =>
                    GetTypeIdentity(ownerType) + "." +
                    (observation.Name ?? string.Empty),
            _ => GetSymbolIdentity(observation.Symbol),
        };

        return "subscription:" + identity + ":" +
            sourceOrder.ToString(CultureInfo.InvariantCulture);
    }

    public static string CreateRoutedEventSlot(
        in ComponentRoutedEventPlan plan)
    {
        return "event:" + GetSymbolIdentity(plan.EventSymbol);
    }

    private static string CreatePropertyReadIdentity(
        in PropertyReadPlan plan)
    {
        return plan.Kind switch
        {
            PropertyReadKind.ClrProperty =>
                GetSymbolIdentity(plan.ClrProperty),
            PropertyReadKind.AvaloniaProperty =>
                GetSymbolIdentity(plan.AvaloniaProperty),
            PropertyReadKind.AttachedAccessor =>
                GetSymbolIdentity(plan.AttachedGetter),
            PropertyReadKind.DirectMember =>
                plan.MemberName ?? string.Empty,
            _ => string.Empty,
        };
    }

    private static string GetSymbolIdentity(ISymbol? symbol)
    {
        if (symbol == null)
        {
            return string.Empty;
        }

        var containingType = GetTypeIdentity(symbol.ContainingType);
        return containingType.Length == 0
            ? symbol.MetadataName
            : containingType + "." + symbol.MetadataName;
    }

    private static int GetRuntimeParentId(
        in ComponentPlan plan,
        in ComponentElementPlan element)
    {
        if (element.ParentId < 0)
        {
            return -1;
        }

        ref readonly var parent = ref plan.Elements.ItemRef(element.ParentId);
        return parent.UsesRuntimeStorage
            ? parent.RuntimeStorageId
            : -1;
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
