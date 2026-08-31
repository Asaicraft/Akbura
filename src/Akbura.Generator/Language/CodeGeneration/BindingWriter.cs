using Akbura.Language.Binder;
using Akbura.Language.Operations;
using CSharpSymbolDefinition = Akbura.Language.Symbols.CSharpSymbolDefinition;
using IAkburaComponentSymbol = Akbura.Language.Symbols.IAkburaComponentSymbol;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Diagnostics;
using System.Globalization;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// A generated expression that directly references a named markup element.
/// </summary>
internal readonly struct BindingElementReference
{
    public BindingElementReference(
        string name,
        string expression,
        int scopeId,
        bool isClassMember)
    {
        Name = name;
        Expression = expression;
        ScopeId = scopeId;
        IsClassMember = isClassMember;
    }

    public string Name { get; }

    public string Expression { get; }

    /// <summary>
    /// Identifies a template, deferred-content builder or another local scope.
    /// </summary>
    public int ScopeId { get; }

    /// <summary>
    /// Class members are visible from every instance generation scope.
    /// Local template and deferred-content variables are visible only
    /// from their own scope.
    /// </summary>
    public bool IsClassMember { get; }

    public bool IsVisibleFrom(int scopeId)
    {
        return IsClassMember ||
               ScopeId == scopeId;
    }
}

/// <summary>
/// Expressions required when a nested markup extension requests
/// an IServiceProvider or when a binding path uses ElementName.
/// </summary>
internal readonly struct MarkupExtensionWriteContext
{
    public MarkupExtensionWriteContext(
        string targetObjectExpression,
        string targetPropertyExpression,
        string intermediateRootExpression,
        string baseUriExpression,
        string directParentsStackExpression,
        string? fallbackServiceProviderExpression,
        string? nameScopeExpression,
        int scopeId)
    {
        TargetObjectExpression = targetObjectExpression;
        TargetPropertyExpression = targetPropertyExpression;
        IntermediateRootExpression = intermediateRootExpression;
        BaseUriExpression = baseUriExpression;
        DirectParentsStackExpression = directParentsStackExpression;
        FallbackServiceProviderExpression =
            fallbackServiceProviderExpression;
        NameScopeExpression = nameScopeExpression;
        ScopeId = scopeId;
    }

    public string TargetObjectExpression { get; }

    public string TargetPropertyExpression { get; }

    public string IntermediateRootExpression { get; }

    public string BaseUriExpression { get; }

    public string DirectParentsStackExpression { get; }

    public string? FallbackServiceProviderExpression { get; }

    public string? NameScopeExpression { get; }

    public int ScopeId { get; }
}

/// <summary>
/// Semantic information shared by every binding generated
/// for one component.
/// </summary>
internal readonly struct BindingWriterEnvironment
{
    private readonly CSharpCompilation _compilation;
    private readonly INamedTypeSymbol? _withinType;
    private readonly INamedTypeSymbol? _avaloniaObjectType;
    private readonly INamedTypeSymbol? _avaloniaPropertyType;

    private BindingWriterEnvironment(
        CSharpCompilation compilation,
        INamedTypeSymbol? withinType,
        INamedTypeSymbol? avaloniaObjectType,
        INamedTypeSymbol? avaloniaPropertyType)
    {
        _compilation = compilation;
        _withinType = withinType;
        _avaloniaObjectType = avaloniaObjectType;
        _avaloniaPropertyType = avaloniaPropertyType;
    }

    public static BindingWriterEnvironment Create(
        AkburaSemanticModel semanticModel,
        IAkburaComponentSymbol component)
    {
        var compilation =
            semanticModel.Compilation.CSharpCompilation;

        var withinType =
            component.PartialTypes.IsDefaultOrEmpty
                ? null
                : component.PartialTypes[0];

        return Create(
            compilation,
            withinType);
    }

    internal static BindingWriterEnvironment Create(
        CSharpCompilation compilation,
        INamedTypeSymbol? withinType)
    {
        if (compilation == null)
        {
            throw new ArgumentNullException(
                nameof(compilation));
        }

        return new BindingWriterEnvironment(
            compilation,
            withinType,
            compilation.GetTypeByMetadataName(
                "Avalonia.AvaloniaObject"),
            compilation.GetTypeByMetadataName(
                "Avalonia.AvaloniaProperty"));
    }

    public bool IsAccessible(ISymbol symbol)
    {
        if (_withinType != null)
        {
            return _compilation.IsSymbolAccessibleWithin(
                symbol,
                _withinType);
        }

        var sameAssembly =
            SymbolEqualityComparer.Default.Equals(
                symbol.ContainingAssembly,
                _compilation.Assembly);

        if (!IsAccessibilityAllowed(
                symbol.DeclaredAccessibility,
                sameAssembly))
        {
            return false;
        }

        for (var type = symbol.ContainingType;
             type != null;
             type = type.ContainingType)
        {
            sameAssembly =
                SymbolEqualityComparer.Default.Equals(
                    type.ContainingAssembly,
                    _compilation.Assembly);

            if (!IsAccessibilityAllowed(
                    type.DeclaredAccessibility,
                    sameAssembly))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Resolves a CLR Avalonia property wrapper such as Text
    /// to its static TextProperty field.
    /// </summary>
    public bool TryGetAvaloniaProperty(
        IPropertySymbol property,
        out ISymbol avaloniaProperty)
    {
        avaloniaProperty = null!;

        if (_avaloniaObjectType == null ||
            _avaloniaPropertyType == null ||
            property.ContainingType == null ||
            !IsDerivedFrom(
                property.ContainingType,
                _avaloniaObjectType))
        {
            return false;
        }

        for (var type = property.ContainingType;
             type != null;
             type = type.BaseType)
        {
            var members = type.GetMembers();

            for (var i = 0; i < members.Length; i++)
            {
                var member = members[i];

                if (!IsAvaloniaPropertyMemberName(
                        member.Name,
                        property.Name) ||
                    !IsAccessible(member))
                {
                    continue;
                }

                var memberType = member switch
                {
                    IFieldSymbol { IsStatic: true } field =>
                        field.Type,

                    IPropertySymbol
                    {
                        IsStatic: true,
                    } staticProperty =>
                        staticProperty.Type,

                    _ => null,
                };

                if (memberType == null ||
                    !IsAvaloniaPropertyType(memberType))
                {
                    continue;
                }

                avaloniaProperty = member;
                return true;
            }
        }

        return false;
    }

    private bool IsAvaloniaPropertyType(
        ITypeSymbol type)
    {
        Debug.Assert(
            _avaloniaPropertyType != null);

        return SymbolEqualityComparer.Default.Equals(
                   type,
                   _avaloniaPropertyType) ||
               _compilation.ClassifyConversion(
                       type,
                       _avaloniaPropertyType!)
                   .IsImplicit;
    }

    private static bool IsDerivedFrom(
        INamedTypeSymbol type,
        INamedTypeSymbol baseType)
    {
        for (var current = type;
             current != null;
             current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(
                    current,
                    baseType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAvaloniaPropertyMemberName(
        string candidate,
        string propertyName)
    {
        const string suffix = "Property";

        if (candidate.Length !=
            propertyName.Length + suffix.Length)
        {
            return false;
        }

        return candidate.AsSpan(
                       0,
                       propertyName.Length)
                   .SequenceEqual(
                       propertyName.AsSpan()) &&
               candidate.AsSpan(
                       propertyName.Length)
                   .SequenceEqual(
                       suffix.AsSpan());
    }

    private static bool IsAccessibilityAllowed(
        Accessibility accessibility,
        bool sameAssembly)
    {
        return accessibility switch
        {
            Accessibility.Public => true,

            Accessibility.Internal =>
                sameAssembly,

            Accessibility.ProtectedOrInternal =>
                sameAssembly,

            Accessibility.NotApplicable => true,

            _ => false,
        };
    }
}

/// <summary>
/// Compact immutable plan for writing one binding.
///
/// The plan owns no collections. It references the semantic binding
/// and stores only the decisions made during the planning phase.
/// </summary>
internal readonly struct BindingWritePlan
{
    private BindingWritePlan(
        MarkupExtensionValue extension,
        MarkupBindingValue binding,
        string? sourceExpression,
        int pathElementStart,
        int reflectionPathStart,
        int cachedPathId,
        int consumedElementNamePropertyIndex,
        int explicitElementNamePathPropertyIndex,
        bool isValid)
    {
        Extension = extension;
        Binding = binding;
        SourceExpression = sourceExpression;
        PathElementStart = pathElementStart;
        ReflectionPathStart = reflectionPathStart;
        CachedPathId = cachedPathId;
        ConsumedElementNamePropertyIndex =
            consumedElementNamePropertyIndex;
        ExplicitElementNamePathPropertyIndex =
            explicitElementNamePathPropertyIndex;
        IsValid = isValid;
    }

    public MarkupExtensionValue Extension { get; }

    public MarkupBindingValue Binding { get; }

    /// <summary>
    /// A direct reference to a named element.
    /// When present, the ElementName path root is removed
    /// and this expression is assigned to Binding.Source.
    /// </summary>
    public string? SourceExpression { get; }

    public int PathElementStart { get; }

    public int ReflectionPathStart { get; }

    /// <summary>
    /// -1 means that the path must be constructed inline.
    /// </summary>
    public int CachedPathId { get; }

    /// <summary>
    /// ElementName property consumed by Source or by the compiled path.
    /// It must not be emitted into the object initializer.
    /// </summary>
    public int ConsumedElementNamePropertyIndex { get; }

    /// <summary>
    /// Explicit ElementName property that must be emitted through
    /// CompiledBindingPathBuilder.ElementName.
    /// </summary>
    public int ExplicitElementNamePathPropertyIndex { get; }

    public bool IsValid { get; }

    public bool HasCachedPath =>
        CachedPathId >= 0;

    public static BindingWritePlan Create(
        in BindingWriterEnvironment environment,
        MarkupExtensionValue extension,
        int scopeId,
        string? nameScopeExpression,
        ReadOnlySpan<BindingElementReference> elements,
        ref int nextCachedPathId)
    {
        return CreateCore(
            in environment,
            extension,
            scopeId,
            nameScopeExpression,
            elements,
            allowCache: true,
            ref nextCachedPathId);
    }

    internal static BindingWritePlan CreateInline(
        in BindingWriterEnvironment environment,
        MarkupExtensionValue extension,
        int scopeId,
        string? nameScopeExpression,
        ReadOnlySpan<BindingElementReference> elements =
            default)
    {
        var ignoredPathId = 0;

        return CreateCore(
            in environment,
            extension,
            scopeId,
            nameScopeExpression,
            elements,
            allowCache: false,
            ref ignoredPathId);
    }

    private static BindingWritePlan CreateCore(
        in BindingWriterEnvironment environment,
        MarkupExtensionValue extension,
        int scopeId,
        string? nameScopeExpression,
        ReadOnlySpan<BindingElementReference> elements,
        bool allowCache,
        ref int nextCachedPathId)
    {
        var binding = extension.Binding;

        if (binding == null)
        {
            Debug.Fail(
                "BindingWritePlan requires a binding markup extension.");

            return new BindingWritePlan(
                extension,
                null!,
                sourceExpression: null,
                pathElementStart: 0,
                reflectionPathStart: 0,
                cachedPathId: -1,
                consumedElementNamePropertyIndex: -1,
                explicitElementNamePathPropertyIndex: -1,
                isValid: false);
        }

        var sourcePropertyIndex =
            FindPropertyIndex(
                extension,
                "Source");

        var relativeSourcePropertyIndex =
            FindPropertyIndex(
                extension,
                "RelativeSource");

        var elementNamePropertyIndex =
            FindPropertyIndex(
                extension,
                "ElementName");

        var hasExplicitSource =
            sourcePropertyIndex >= 0 ||
            relativeSourcePropertyIndex >= 0;

        string? sourceExpression = null;
        var pathElementStart = 0;
        var reflectionPathStart = 0;
        var consumedElementNamePropertyIndex = -1;
        var explicitElementNamePathPropertyIndex = -1;
        var requiresNameScope = false;

        /*
         * ElementName supplied as a markup-extension property:
         *
         * {CompiledBinding Path=Name, ElementName=header}
         */
        if (!hasExplicitSource &&
            elementNamePropertyIndex >= 0)
        {
            var property =
                extension.Properties[
                    elementNamePropertyIndex];

            if (TryGetStringValue(
                    property,
                    out var elementName) &&
                TryFindDirectElementReference(
                    elements,
                    elementName.Span,
                    scopeId,
                    out sourceExpression))
            {
                consumedElementNamePropertyIndex =
                    elementNamePropertyIndex;
            }
            else if (binding.Kind ==
                     MarkupBindingKind.Compiled)
            {
                explicitElementNamePathPropertyIndex =
                    elementNamePropertyIndex;

                consumedElementNamePropertyIndex =
                    elementNamePropertyIndex;

                requiresNameScope = true;
            }
        }

        /*
         * ElementName supplied as the first path element:
         *
         * #header.Text
         */
        if (sourceExpression == null &&
            !hasExplicitSource &&
            elementNamePropertyIndex < 0 &&
            !binding.PathElements.IsDefaultOrEmpty)
        {
            var root = binding.PathElements[0];

            if (root.Kind ==
                MarkupBindingPathElementKind.ElementName)
            {
                var elementName =
                    GetElementNameText(
                        root.Text);

                if (TryFindDirectElementReference(
                        elements,
                        elementName.Span,
                        scopeId,
                        out sourceExpression))
                {
                    pathElementStart = 1;

                    reflectionPathStart =
                        GetPathAfterRootStart(
                            binding.Path,
                            root.Text);
                }
                else if (binding.Kind ==
                         MarkupBindingKind.Compiled)
                {
                    requiresNameScope = true;
                }
            }
        }

        var isValid = true;
        var isCacheable = false;

        if (binding.Kind ==
            MarkupBindingKind.Compiled)
        {
            var analysis =
                AnalyzeCompiledPath(
                    in environment,
                    binding,
                    pathElementStart,
                    explicitElementNamePathPropertyIndex,
                    requiresNameScope,
                    nameScopeExpression);

            isValid = analysis.IsValid;

            // CompiledBinding does not expose RelativeSource as
            // a writable runtime property. It must eventually be
            // represented as a path root.
            if (relativeSourcePropertyIndex >= 0)
            {
                isValid = false;
            }

            isCacheable =
                isValid &&
                allowCache &&
                analysis.IsCacheable;
        }
        else
        {
            isValid =
                binding.BindingType.Symbol
                    is ITypeSymbol;
        }

        var cachedPathId =
            isCacheable
                ? nextCachedPathId++
                : -1;

        return new BindingWritePlan(
            extension,
            binding,
            sourceExpression,
            pathElementStart,
            reflectionPathStart,
            cachedPathId,
            consumedElementNamePropertyIndex,
            explicitElementNamePathPropertyIndex,
            isValid);
    }

    private static PathAnalysis AnalyzeCompiledPath(
        in BindingWriterEnvironment environment,
        MarkupBindingValue binding,
        int pathElementStart,
        int explicitElementNamePropertyIndex,
        bool requiresNameScope,
        string? nameScopeExpression)
    {
        var isValid = true;
        var isCacheable =
            explicitElementNamePropertyIndex < 0 &&
            !requiresNameScope;

        if ((explicitElementNamePropertyIndex >= 0 ||
             requiresNameScope) &&
            string.IsNullOrEmpty(nameScopeExpression))
        {
            isValid = false;
        }

        var pathElements =
            binding.PathElements;

        var currentType =
            GetInitialPathType(
                binding,
                pathElementStart);

        for (var i = pathElementStart;
             i < pathElements.Length;
             i++)
        {
            var element = pathElements[i];

            switch (element.Kind)
            {
                case MarkupBindingPathElementKind.Property:
                {
                    if (element.Symbol.Symbol
                            is not IPropertySymbol property ||
                        property.IsStatic ||
                        property.IsIndexer ||
                        property.GetMethod == null ||
                        !environment.IsAccessible(
                            property.GetMethod))
                    {
                        isValid = false;
                        break;
                    }

                    currentType = property.Type;
                    break;
                }

                case MarkupBindingPathElementKind.Field:
                {
                    if (element.Symbol.Symbol
                            is not IFieldSymbol field ||
                        field.IsStatic ||
                        !environment.IsAccessible(field))
                    {
                        isValid = false;
                        break;
                    }

                    currentType = field.Type;
                    break;
                }

                case MarkupBindingPathElementKind.Indexer:
                {
                    var argumentCount =
                        GetArgumentCount(element);

                    if (argumentCount == 0)
                    {
                        isValid = false;
                        break;
                    }

                    if (currentType
                        is IArrayTypeSymbol arrayType)
                    {
                        if (argumentCount != arrayType.Rank)
                        {
                            isValid = false;
                            break;
                        }

                        currentType =
                            arrayType.ElementType;
                    }
                    else
                    {
                        if (element.Symbol.Symbol
                                is not IPropertySymbol
                                {
                                    IsIndexer: true,
                                } indexer ||
                            indexer.GetMethod == null ||
                            !environment.IsAccessible(
                                indexer.GetMethod))
                        {
                            isValid = false;
                            break;
                        }

                        currentType = indexer.Type;
                    }

                    if (!AreArgumentsConstant(element))
                    {
                        isCacheable = false;
                    }

                    break;
                }

                case MarkupBindingPathElementKind.ElementName:
                    if (string.IsNullOrEmpty(
                            nameScopeExpression))
                    {
                        isValid = false;
                    }

                    isCacheable = false;

                    currentType =
                        element.Type.Symbol
                            as ITypeSymbol ??
                        currentType;

                    break;

                case MarkupBindingPathElementKind.Self:
                case MarkupBindingPathElementKind.Ancestor:
                case MarkupBindingPathElementKind.TemplatedParent:
                    currentType =
                        element.Type.Symbol
                            as ITypeSymbol ??
                        currentType;
                    break;

                case MarkupBindingPathElementKind.TypeCast:
                    if (element.Type.Symbol
                        is not ITypeSymbol castType)
                    {
                        isValid = false;
                        break;
                    }

                    currentType = castType;
                    break;

                case MarkupBindingPathElementKind.Not:
                    break;

                default:
                    isValid = false;
                    break;
            }

            if (!isValid)
            {
                break;
            }
        }

        return new PathAnalysis(
            isValid,
            isValid && isCacheable);
    }

    private static int FindPropertyIndex(
        MarkupExtensionValue extension,
        string name)
    {
        for (var i = 0;
             i < extension.Properties.Length;
             i++)
        {
            if (string.Equals(
                    extension.Properties[i].Name,
                    name,
                    StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool TryGetStringValue(
        in MarkupExtensionPropertyValue property,
        out ReadOnlyMemory<char> value)
    {
        var constant =
            property.Operation.ConstantValue;

        if (constant.HasValue &&
            constant.Value is string constantString)
        {
            value = constantString.AsMemory();
            return true;
        }

        if (property.ConvertedValue
            is string convertedString)
        {
            value = convertedString.AsMemory();
            return true;
        }

        if (property.Operation.IsDefault)
        {
            value = TrimQuotes(
                property.Value);

            return !value.IsEmpty;
        }

        value = default;
        return false;
    }

    private static bool TryFindDirectElementReference(
        ReadOnlySpan<BindingElementReference> elements,
        ReadOnlySpan<char> name,
        int scopeId,
        out string? expression)
    {
        // Search backwards so a local name shadows a class member.
        for (var i = elements.Length - 1;
             i >= 0;
             i--)
        {
            ref readonly var element =
                ref elements[i];

            if (!element.IsVisibleFrom(scopeId) ||
                !element.Name.AsSpan()
                    .SequenceEqual(name))
            {
                continue;
            }

            expression = element.Expression;
            return true;
        }

        expression = null;
        return false;
    }

    internal static int GetArgumentCount(
        in MarkupBindingPathElement element)
    {
        return !element.BoundArguments.IsDefaultOrEmpty
            ? element.BoundArguments.Length
            : element.Arguments.Length;
    }

    internal static bool AreArgumentsConstant(
        in MarkupBindingPathElement element)
    {
        var count = GetArgumentCount(element);

        for (var i = 0; i < count; i++)
        {
            if (!IsArgumentConstant(
                    element,
                    i))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool TryGetInt32Argument(
        in MarkupBindingPathElement element,
        int index,
        out int value)
    {
        if (index <
            element.BoundArguments.Length)
        {
            var argument =
                element.BoundArguments[index];

            var constant =
                argument.Operation.ConstantValue;

            if (constant.HasValue &&
                TryConvertToInt32(
                    constant.Value,
                    out value))
            {
                return true;
            }

            if (argument.Operation.IsDefault &&
                TryConvertToInt32(
                    argument.ConvertedValue,
                    out value))
            {
                return true;
            }

            return int.TryParse(
                argument.Text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value);
        }

        if (index <
            element.Arguments.Length)
        {
            return int.TryParse(
                element.Arguments[index],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value);
        }

        value = 0;
        return false;
    }

    private static bool IsArgumentConstant(
        in MarkupBindingPathElement element,
        int index)
    {
        if (index <
            element.BoundArguments.Length)
        {
            var argument =
                element.BoundArguments[index];

            if (argument.Operation
                .ConstantValue.HasValue)
            {
                return true;
            }

            if (argument.Operation.IsDefault &&
                argument.ConvertedValue != null)
            {
                return true;
            }

            return IsSimpleConstantExpression(
                argument.Text.AsSpan());
        }

        return index < element.Arguments.Length &&
               IsSimpleConstantExpression(
                   element.Arguments[index].AsSpan());
    }

    private static bool IsSimpleConstantExpression(
        ReadOnlySpan<char> expression)
    {
        expression = TrimWhitespace(
            expression);

        if (expression.IsEmpty)
        {
            return false;
        }

        if ((expression[0] == '"' &&
             expression[expression.Length - 1] == '"') ||
            (expression[0] == '\'' &&
             expression[expression.Length - 1] == '\''))
        {
            return true;
        }

        if (expression.SequenceEqual(
                "true".AsSpan()) ||
            expression.SequenceEqual(
                "false".AsSpan()) ||
            expression.SequenceEqual(
                "null".AsSpan()))
        {
            return true;
        }

        // netstandard2.0 does not expose the span-based numeric parsing overloads.
        var numericText = expression.ToString();

        if (long.TryParse(
                numericText,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out _))
        {
            return true;
        }

        return double.TryParse(
            numericText,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out _);
    }

    private static bool TryConvertToInt32(
        object? value,
        out int result)
    {
        switch (value)
        {
            case byte number:
                result = number;
                return true;

            case sbyte number:
                result = number;
                return true;

            case short number:
                result = number;
                return true;

            case ushort number:
                result = number;
                return true;

            case int number:
                result = number;
                return true;

            case uint number
                when number <= int.MaxValue:
                result = (int)number;
                return true;

            case long number
                when number is >= int.MinValue
                    and <= int.MaxValue:
                result = (int)number;
                return true;

            case ulong number
                when number <= int.MaxValue:
                result = (int)number;
                return true;

            default:
                result = 0;
                return false;
        }
    }

    private static ITypeSymbol? GetInitialPathType(
        MarkupBindingValue binding,
        int pathElementStart)
    {
        if (pathElementStart > 0 &&
            pathElementStart <=
                binding.PathElements.Length &&
            binding.PathElements[
                    pathElementStart - 1]
                .Type.Symbol
                is ITypeSymbol rootedType)
        {
            return rootedType;
        }

        return binding.SourceType.Symbol
            as ITypeSymbol;
    }

    private static int GetPathAfterRootStart(
        string path,
        string rootText)
    {
        var start = 0;

        while (start < path.Length &&
               char.IsWhiteSpace(path[start]))
        {
            start++;
        }

        if (rootText.Length == 0 ||
            start > path.Length -
                rootText.Length ||
            !path.AsSpan(
                    start,
                    rootText.Length)
                .SequenceEqual(
                    rootText.AsSpan()))
        {
            return 0;
        }

        start += rootText.Length;

        while (start < path.Length &&
               char.IsWhiteSpace(path[start]))
        {
            start++;
        }

        if (start < path.Length &&
            path[start] == '.')
        {
            start++;
        }

        while (start < path.Length &&
               char.IsWhiteSpace(path[start]))
        {
            start++;
        }

        return start;
    }

    private static ReadOnlyMemory<char>
        GetElementNameText(
            string text)
    {
        var result = text.AsMemory();

        return result.Length > 0 &&
               result.Span[0] == '#'
            ? result.Slice(1)
            : result;
    }

    private static ReadOnlyMemory<char>
        TrimQuotes(
            string text)
    {
        var result = text.AsMemory();

        if (result.Length >= 2)
        {
            var span = result.Span;

            if ((span[0] == '"' &&
                 span[span.Length - 1] == '"') ||
                (span[0] == '\'' &&
                 span[span.Length - 1] == '\''))
            {
                return result.Slice(
                    1,
                    result.Length - 2);
            }
        }

        return result;
    }

    private static ReadOnlySpan<char>
        TrimWhitespace(
            ReadOnlySpan<char> value)
    {
        var start = 0;
        var end = value.Length;

        while (start < end &&
               char.IsWhiteSpace(value[start]))
        {
            start++;
        }

        while (end > start &&
               char.IsWhiteSpace(value[end - 1]))
        {
            end--;
        }

        return value.Slice(
            start,
            end - start);
    }

    private readonly struct PathAnalysis
    {
        public PathAnalysis(
            bool isValid,
            bool isCacheable)
        {
            IsValid = isValid;
            IsCacheable = isCacheable;
        }

        public bool IsValid { get; }

        public bool IsCacheable { get; }
    }
}

/// <summary>
/// Allocation-free writer facade for binding code generation.
///
/// The writer itself is stack-only. Long-lived information belongs
/// to BindingWritePlan and BindingWriterEnvironment.
/// </summary>
internal ref struct BindingWriter
{
    private const string CompiledBindingType =
        "global::Avalonia.Data.CompiledBinding";

    private const string CompiledBindingPathType =
        "global::Avalonia.Data.CompiledBindingPath";

    private const string CompiledBindingPathBuilderType =
        "global::Avalonia.Data.CompiledBindingPathBuilder";

    private const string ClrPropertyInfoType =
        "global::Avalonia.Data.Core.ClrPropertyInfo";

    private const string AccessorFactoryType =
        "global::Avalonia.Markup.Xaml.MarkupExtensions." +
        "CompiledBindings.PropertyInfoAccessorFactory";

    private static readonly SymbolDisplayFormat
        s_typeDisplayFormat =
            SymbolDisplayFormat.FullyQualifiedFormat;

    private readonly CodeWriter _writer;
    private readonly BindingWriterEnvironment _environment;
    private readonly ReadOnlySpan<BindingElementReference>
        _elementReferences;

    public BindingWriter(
        CodeWriter writer,
        in BindingWriterEnvironment environment,
        ReadOnlySpan<BindingElementReference> elementReferences =
            default)
    {
        _writer = writer ??
            throw new ArgumentNullException(nameof(writer));
        _environment = environment;
        _elementReferences = elementReferences;
    }

    /// <summary>
    /// Writes a static path field when the plan is cacheable.
    ///
    /// The caller invokes this once while writing component fields.
    /// No separate path table is required.
    /// </summary>
    public void WriteCachedPathField(
        in BindingWritePlan plan)
    {
        if (!plan.HasCachedPath)
        {
            return;
        }

        Debug.Assert(plan.IsValid);
        Debug.Assert(
            plan.Binding.Kind ==
            MarkupBindingKind.Compiled);

        _writer
            .Write("private static readonly ")
            .Write(CompiledBindingPathType)
            .Write(" ");

        WritePathFieldName(
            plan.CachedPathId);

        _writer.Write(" = ");

        WriteCompiledBindingPath(
            plan,
            default);

        _writer.WriteLine(";");
    }

    /// <summary>
    /// Writes a complete binding expression.
    /// </summary>
    public void WriteBinding(
        in BindingWritePlan plan,
        in MarkupExtensionWriteContext context)
    {
        if (!plan.IsValid)
        {
            Debug.Fail(
                "An invalid binding plan reached code generation.");

            _writer.Write("default!");
            return;
        }

        if (plan.Binding.Kind ==
            MarkupBindingKind.Compiled)
        {
            WriteCompiledBinding(
                plan,
                context);
        }
        else
        {
            WriteReflectionBinding(
                plan,
                context);
        }
    }

    private void WriteCompiledBinding(
        in BindingWritePlan plan,
        in MarkupExtensionWriteContext context)
    {
        _writer
            .Write("new ")
            .Write(CompiledBindingType)
            .Write("(");

        if (plan.HasCachedPath)
        {
            WritePathFieldName(
                plan.CachedPathId);
        }
        else
        {
            WriteCompiledBindingPath(
                plan,
                context);
        }

        _writer.Write(")");

        WriteBindingInitializer(
            plan,
            context,
            isCompiled: true);
    }

    private void WriteReflectionBinding(
        in BindingWritePlan plan,
        in MarkupExtensionWriteContext context)
    {
        var bindingType =
            plan.Binding.BindingType.Symbol
                as ITypeSymbol;

        Debug.Assert(bindingType != null);

        _writer.Write("new ");

        WriteTypeName(bindingType);

        _writer.Write("(");

        _writer.WriteStringLiteral(
            plan.Binding.Path.AsMemory(
                plan.ReflectionPathStart));

        _writer.Write(")");

        WriteBindingInitializer(
            plan,
            context,
            isCompiled: false);
    }

    private void WriteCompiledBindingPath(
        in BindingWritePlan plan,
        in MarkupExtensionWriteContext context)
    {
        var binding = plan.Binding;
        var pathElements = binding.PathElements;

        var currentType =
            GetInitialPathType(
                binding,
                plan.PathElementStart);

        _writer
            .Write("new ")
            .Write(CompiledBindingPathBuilderType)
            .Write("()");

        if (plan.ExplicitElementNamePathPropertyIndex >= 0)
        {
            WriteExplicitElementNamePath(
                plan,
                context);
        }

        for (var i = plan.PathElementStart;
             i < pathElements.Length;
             i++)
        {
            var element = pathElements[i];

            switch (element.Kind)
            {
                case MarkupBindingPathElementKind.Property:
                {
                    var property =
                        element.Symbol.Symbol
                            as IPropertySymbol;

                    Debug.Assert(property != null);

                    WritePropertyPathElement(
                        property!);

                    currentType = property!.Type;
                    break;
                }

                case MarkupBindingPathElementKind.Field:
                {
                    var field =
                        element.Symbol.Symbol
                            as IFieldSymbol;

                    Debug.Assert(field != null);

                    WriteFieldPathElement(
                        field!);

                    currentType = field!.Type;
                    break;
                }

                case MarkupBindingPathElementKind.Indexer:
                    if (currentType
                        is IArrayTypeSymbol arrayType)
                    {
                        WriteArrayPathElement(
                            element,
                            arrayType);

                        currentType =
                            arrayType.ElementType;
                    }
                    else
                    {
                        var indexer =
                            element.Symbol.Symbol
                                as IPropertySymbol;

                        Debug.Assert(indexer != null);

                        WriteIndexerPathElement(
                            element,
                            indexer!);

                        currentType = indexer!.Type;
                    }

                    break;

                case MarkupBindingPathElementKind.ElementName:
                    WriteElementNamePathElement(
                        element,
                        context);

                    currentType =
                        element.Type.Symbol
                            as ITypeSymbol ??
                        currentType;

                    break;

                case MarkupBindingPathElementKind.Self:
                    _writer.Write(".Self()");

                    currentType =
                        element.Type.Symbol
                            as ITypeSymbol ??
                        currentType;

                    break;

                case MarkupBindingPathElementKind.Ancestor:
                    WriteAncestorPathElement(
                        element);

                    currentType =
                        element.Type.Symbol
                            as ITypeSymbol ??
                        currentType;

                    break;

                case MarkupBindingPathElementKind.TemplatedParent:
                    _writer.Write(
                        ".TemplatedParent()");

                    currentType =
                        element.Type.Symbol
                            as ITypeSymbol ??
                        currentType;

                    break;

                case MarkupBindingPathElementKind.Not:
                    _writer.Write(".Not()");
                    break;

                case MarkupBindingPathElementKind.TypeCast:
                {
                    var castType =
                        element.Type.Symbol
                            as ITypeSymbol;

                    Debug.Assert(castType != null);

                    WriteTypeCastPathElement(
                        castType!);

                    currentType = castType;
                    break;
                }

                default:
                    Debug.Fail(
                        "Unsupported binding path element: " +
                        element.Kind);

                    break;
            }
        }

        _writer.Write(".Build()");
    }

    private void WritePropertyPathElement(
        IPropertySymbol property)
    {
        if (_environment.TryGetAvaloniaProperty(
                property,
                out var avaloniaProperty))
        {
            _writer.Write(".Property(");

            WriteStaticMemberReference(
                avaloniaProperty);

            _writer
                .Write(", ")
                .Write(AccessorFactoryType)
                .Write(
                    ".CreateAvaloniaPropertyAccessor, false)");

            return;
        }

        WriteClrPropertyPathElement(
            property);
    }

    private void WriteClrPropertyPathElement(
        IPropertySymbol property)
    {
        var sourceType =
            property.ContainingType;

        var valueType =
            property.Type;

        _writer
            .Write(".Property(new ")
            .Write(ClrPropertyInfoType)
            .Write("(");

        _writer.WriteStringLiteral(
            property.Name);

        _writer.Write(
            ", static __source => ((");

        WriteTypeName(sourceType);

        _writer.Write(")__source).");

        WriteIdentifier(property.Name);

        _writer.Write(", ");

        if (CanWritePropertySetter(property))
        {
            _writer.Write(
                "static (__source, __value) => ((");

            WriteTypeName(sourceType);

            _writer.Write(")__source).");

            WriteIdentifier(property.Name);

            _writer.Write(" = (");

            WriteTypeName(valueType);

            _writer.Write(")__value!");
        }
        else
        {
            _writer.Write("null");
        }

        _writer.Write(", typeof(");

        WriteTypeName(valueType);

        _writer
            .Write(")), ")
            .Write(AccessorFactoryType)
            .Write(
                ".CreateInpcPropertyAccessor, false)");
    }

    private void WriteFieldPathElement(
        IFieldSymbol field)
    {
        var sourceType =
            field.ContainingType;

        var valueType =
            field.Type;

        _writer
            .Write(".Property(new ")
            .Write(ClrPropertyInfoType)
            .Write("(");

        _writer.WriteStringLiteral(
            field.Name);

        _writer.Write(
            ", static __source => ((");

        WriteTypeName(sourceType);

        _writer.Write(")__source).");

        WriteIdentifier(field.Name);

        _writer.Write(", ");

        if (CanWriteFieldSetter(field))
        {
            _writer.Write(
                "static (__source, __value) => ((");

            WriteTypeName(sourceType);

            _writer.Write(")__source).");

            WriteIdentifier(field.Name);

            _writer.Write(" = (");

            WriteTypeName(valueType);

            _writer.Write(")__value!");
        }
        else
        {
            _writer.Write("null");
        }

        _writer.Write(", typeof(");

        WriteTypeName(valueType);

        _writer
            .Write(")), ")
            .Write(AccessorFactoryType)
            .Write(
                ".CreateInpcPropertyAccessor, false)");
    }

    private void WriteIndexerPathElement(
        in MarkupBindingPathElement element,
        IPropertySymbol indexer)
    {
        var sourceType =
            indexer.ContainingType;

        var valueType =
            indexer.Type;

        var argumentsAreConstant =
            BindingWritePlan.AreArgumentsConstant(
                element);

        _writer
            .Write(".Property(new ")
            .Write(ClrPropertyInfoType)
            .Write("(");

        // Standard INotifyPropertyChanged name for indexers.
        _writer.WriteStringLiteral("Item[]");
        _writer.Write(", ");

        if (argumentsAreConstant)
        {
            _writer.Write("static ");
        }

        _writer.Write("__source => ((");

        WriteTypeName(sourceType);

        _writer.Write(")__source)[");

        WriteIndexerArguments(element);

        _writer.Write("], ");

        if (CanWritePropertySetter(indexer))
        {
            if (argumentsAreConstant)
            {
                _writer.Write("static ");
            }

            _writer.Write(
                "(__source, __value) => ((");

            WriteTypeName(sourceType);

            _writer.Write(")__source)[");

            WriteIndexerArguments(element);

            _writer.Write("] = (");

            WriteTypeName(valueType);

            _writer.Write(")__value!");
        }
        else
        {
            _writer.Write("null");
        }

        _writer.Write(", typeof(");

        WriteTypeName(valueType);

        _writer.Write(")), ");

        if (indexer.Parameters.Length == 1 &&
            indexer.Parameters[0].Type.SpecialType ==
                SpecialType.System_Int32)
        {
            if (argumentsAreConstant)
            {
                _writer.Write("static ");
            }

            _writer
                .Write(
                    "(__reference, __property) => ")
                .Write(AccessorFactoryType)
                .Write(
                    ".CreateIndexerPropertyAccessor(" +
                    "__reference, __property, ");

            WriteIndexerArgument(
                element,
                index: 0);

            _writer.Write(")");
        }
        else
        {
            _writer
                .Write(AccessorFactoryType)
                .Write(
                    ".CreateInpcPropertyAccessor");
        }

        _writer.Write(", false)");
    }
    private void WriteArrayPathElement(
        in MarkupBindingPathElement element,
        IArrayTypeSymbol arrayType)
    {
        var argumentCount =
            BindingWritePlan.GetArgumentCount(
                element);

        _writer.Write(
            ".ArrayElement(new int[] { ");

        for (var i = 0;
             i < argumentCount;
             i++)
        {
            if (i > 0)
            {
                _writer.Write(", ");
            }

            WriteIndexerArgument(
                element,
                i);
        }

        _writer.Write(" }, typeof(");

        WriteTypeName(
            arrayType.ElementType);

        _writer.Write("))");
    }

    private void WriteExplicitElementNamePath(
        in BindingWritePlan plan,
        in MarkupExtensionWriteContext context)
    {
        Debug.Assert(
            !string.IsNullOrEmpty(
                context.NameScopeExpression));

        var property =
            plan.Extension.Properties[
                plan.ExplicitElementNamePathPropertyIndex];

        _writer
            .Write(".ElementName(")
            .Write(context.NameScopeExpression!)
            .Write(", ");

        WriteMarkupExtensionPropertyValue(
            property,
            context);

        _writer.Write(")");
    }

    private void WriteElementNamePathElement(
        in MarkupBindingPathElement element,
        in MarkupExtensionWriteContext context)
    {
        Debug.Assert(
            !string.IsNullOrEmpty(
                context.NameScopeExpression));

        _writer
            .Write(".ElementName(")
            .Write(context.NameScopeExpression!)
            .Write(", ");

        _writer.WriteStringLiteral(
            GetElementNameText(
                element.Text));

        _writer.Write(")");
    }

    private void WriteAncestorPathElement(
        in MarkupBindingPathElement element)
    {
        _writer.Write(".Ancestor(");

        if (element.Type.Symbol
            is ITypeSymbol ancestorType)
        {
            _writer.Write("typeof(");

            WriteTypeName(ancestorType);

            _writer.Write(")");
        }
        else
        {
            _writer.Write("null!");
        }

        _writer.Write(", ");

        _writer.WriteIntegerLiteral(
            element.Level ?? 0);

        _writer.Write(")");
    }

    private void WriteTypeCastPathElement(
        ITypeSymbol castType)
    {
        _writer.Write(".TypeCast<");

        WriteTypeName(castType);

        _writer.Write(">()");
    }

    private void WriteIndexerArguments(
        in MarkupBindingPathElement element)
    {
        var count =
            BindingWritePlan.GetArgumentCount(
                element);

        for (var i = 0; i < count; i++)
        {
            if (i > 0)
            {
                _writer.Write(", ");
            }

            WriteIndexerArgument(
                element,
                i);
        }
    }

    private void WriteIndexerArgument(
        in MarkupBindingPathElement element,
        int index)
    {
        if (index <
            element.BoundArguments.Length)
        {
            var argument =
                element.BoundArguments[index];

            var constant =
                argument.Operation.ConstantValue;

            if (constant.HasValue)
            {
                WriteConstant(
                    constant.Value,
                    argument.Type.Symbol);

                return;
            }

            if (!argument.Operation.IsDefault &&
                argument.Operation.Syntax != null)
            {
                _writer.Write(
                    argument.Operation.Syntax.ToString());

                return;
            }

            if (argument.Operation.IsDefault &&
                argument.ConvertedValue != null)
            {
                WriteConstant(
                    argument.ConvertedValue,
                    argument.Type.Symbol);

                return;
            }

            _writer.Write(argument.Text);
            return;
        }

        _writer.Write(
            element.Arguments[index]);
    }

    private void WriteBindingInitializer(
        in BindingWritePlan plan,
        in MarkupExtensionWriteContext context,
        bool isCompiled)
    {
        if (plan.SourceExpression == null &&
            !HasWritableProperties(
                plan,
                isCompiled))
        {
            return;
        }

        _writer.Write(" { ");

        var hasPreviousValue = false;

        if (plan.SourceExpression != null)
        {
            _writer
                .Write("Source = ")
                .Write(plan.SourceExpression);

            hasPreviousValue = true;
        }

        var properties =
            plan.Extension.Properties;

        for (var i = 0;
             i < properties.Length;
             i++)
        {
            if (!ShouldWriteProperty(
                    plan,
                    i,
                    isCompiled))
            {
                continue;
            }

            if (hasPreviousValue)
            {
                _writer.Write(", ");
            }

            var property = properties[i];

            WriteIdentifier(property.Name);

            _writer.Write(" = ");

            WriteMarkupExtensionPropertyValue(
                property,
                context);

            hasPreviousValue = true;
        }

        _writer.Write(" }");
    }

    private static bool HasWritableProperties(
        in BindingWritePlan plan,
        bool isCompiled)
    {
        for (var i = 0;
             i < plan.Extension.Properties.Length;
             i++)
        {
            if (ShouldWriteProperty(
                    plan,
                    i,
                    isCompiled))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ShouldWriteProperty(
        in BindingWritePlan plan,
        int index,
        bool isCompiled)
    {
        if (index ==
            plan.ConsumedElementNamePropertyIndex)
        {
            return false;
        }

        var name =
            plan.Extension.Properties[index].Name;

        if (string.Equals(
                name,
                "Path",
                StringComparison.Ordinal))
        {
            return false;
        }

        if (!isCompiled)
        {
            return true;
        }

        // DataType participates only in semantic binding.
        if (string.Equals(
                name,
                "DataType",
                StringComparison.Ordinal))
        {
            return false;
        }

        // CompiledBinding has no ElementName or RelativeSource
        // initializer properties. Both must be represented by
        // path roots.
        if (string.Equals(
                name,
                "ElementName",
                StringComparison.Ordinal) ||
            string.Equals(
                name,
                "RelativeSource",
                StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private void WriteMarkupExtensionPropertyValue(
        in MarkupExtensionPropertyValue property,
        in MarkupExtensionWriteContext context)
    {
        WriteBoundValue(
            property.Operation,
            property.ConvertedValue,
            property.Value,
            property.Type.Symbol,
            property.NestedValue,
            context);
    }

    private void WriteMarkupExtensionArgumentValue(
        in MarkupExtensionArgumentValue argument,
        in MarkupExtensionWriteContext context)
    {
        WriteBoundValue(
            argument.Operation,
            argument.ConvertedValue,
            argument.Text,
            argument.Type.Symbol,
            argument.NestedValue,
            context);
    }

    private void WriteBoundValue(
        CSharpOperationDefinition operation,
        object? convertedValue,
        string text,
        ISymbol? targetType,
        MarkupExtensionValue? nestedValue,
        in MarkupExtensionWriteContext context)
    {
        if (nestedValue != null)
        {
            WriteNestedMarkupExtension(
                nestedValue,
                context);

            return;
        }

        var constant =
            operation.ConstantValue;

        if (constant.HasValue)
        {
            WriteConstant(
                constant.Value,
                targetType);

            return;
        }

        if (!operation.IsDefault &&
            operation.Syntax != null)
        {
            _writer.Write(
                operation.Syntax.ToString());

            return;
        }

        if (convertedValue
            is CSharpSymbolDefinition definition &&
            TryWriteStaticMemberReference(
                definition.Symbol))
        {
            return;
        }

        if (convertedValue != null)
        {
            WriteConstant(
                convertedValue,
                targetType);

            return;
        }

        _writer.WriteStringLiteral(
            TrimQuotes(text));
    }

    private void WriteNestedMarkupExtension(
        MarkupExtensionValue extension,
        in MarkupExtensionWriteContext context)
    {
        if (extension.Binding != null)
        {
            var inlinePlan =
                BindingWritePlan.CreateInline(
                    in _environment,
                    extension,
                    context.ScopeId,
                    context.NameScopeExpression,
                    _elementReferences);

            WriteBinding(
                inlinePlan,
                context);

            return;
        }

        var provideValue =
            extension.ProvideValueMethod.Symbol
                as IMethodSymbol;

        if (provideValue != null)
        {
            _writer.Write("(");
        }

        WriteMarkupExtensionCreation(
            extension,
            context);

        if (provideValue == null)
        {
            return;
        }

        _writer.Write(").");

        WriteIdentifier(
            provideValue.Name);

        _writer.Write("(");

        Debug.Assert(
            provideValue.Parameters.Length <= 1);

        if (provideValue.Parameters.Length == 1)
        {
            WriteMarkupServiceProvider(
                context);
        }

        _writer.Write(")");
    }

    private void WriteMarkupExtensionCreation(
        MarkupExtensionValue extension,
        in MarkupExtensionWriteContext context)
    {
        var extensionType =
            extension.ExtensionType.Symbol
                as ITypeSymbol;

        Debug.Assert(extensionType != null);

        _writer.Write("new ");

        WriteTypeName(extensionType);

        _writer.Write("(");

        for (var i = 0;
             i < extension.Arguments.Length;
             i++)
        {
            if (i > 0)
            {
                _writer.Write(", ");
            }

            var argument =
                extension.Arguments[i];

            WriteMarkupExtensionArgumentValue(
                argument,
                context);
        }

        _writer.Write(")");

        if (extension.Properties.IsDefaultOrEmpty)
        {
            return;
        }

        _writer.Write(" { ");

        for (var i = 0;
             i < extension.Properties.Length;
             i++)
        {
            if (i > 0)
            {
                _writer.Write(", ");
            }

            var property =
                extension.Properties[i];

            WriteIdentifier(property.Name);

            _writer.Write(" = ");

            WriteMarkupExtensionPropertyValue(
                property,
                context);
        }

        _writer.Write(" }");
    }

    private void WriteMarkupServiceProvider(
        in MarkupExtensionWriteContext context)
    {
        if (string.IsNullOrEmpty(
                context.TargetObjectExpression) ||
            string.IsNullOrEmpty(
                context.TargetPropertyExpression) ||
            string.IsNullOrEmpty(
                context.IntermediateRootExpression) ||
            string.IsNullOrEmpty(
                context.BaseUriExpression) ||
            string.IsNullOrEmpty(
                context.DirectParentsStackExpression))
        {
            Debug.Fail(
                "The markup extension service-provider context is incomplete.");

            _writer.Write("default!");
            return;
        }

        _writer
            .Write(
                "CreateMarkupServiceProvider(" +
                "targetObject: ")
            .Write(
                context.TargetObjectExpression)
            .Write(
                ", targetProperty: ")
            .Write(
                context.TargetPropertyExpression)
            .Write(
                ", intermediateRootObject: ")
            .Write(
                context.IntermediateRootExpression)
            .Write(
                ", baseUri: ")
            .Write(
                context.BaseUriExpression)
            .Write(
                ", directParentsStack: ")
            .Write(
                context.DirectParentsStackExpression);

        if (!string.IsNullOrEmpty(
                context.FallbackServiceProviderExpression))
        {
            _writer
                .Write(
                    ", fallbackServiceProvider: ")
                .Write(
                    context.FallbackServiceProviderExpression!);
        }

        _writer.Write(")");
    }

    private bool CanWritePropertySetter(
        IPropertySymbol property)
    {
        return !property.ContainingType.IsValueType &&
               property.SetMethod is
               {
                   IsInitOnly: false,
               } setter &&
               _environment.IsAccessible(setter);
    }

    private bool CanWriteFieldSetter(
        IFieldSymbol field)
    {
        return !field.ContainingType.IsValueType &&
               !field.IsReadOnly &&
               !field.IsConst &&
               _environment.IsAccessible(field);
    }

    private void WritePathFieldName(
        int id)
    {
        _writer.Write("s_bindingPath");
        _writer.WriteIntegerLiteral(id);
    }

    private void WriteIdentifier(
        string identifier)
    {
        _writer.WriteIdentifierEscapeIfNeeded(
            identifier);

        _writer.Write(identifier);
    }

    private void WriteTypeName(
        ITypeSymbol? type)
    {
        if (type == null ||
            ContainsErrorType(type))
        {
            _writer.Write(
                "global::System.Object");

            return;
        }

        // SymbolDisplay currently allocates the resulting string,
        // but avoids introducing a generator-wide dictionary whose
        // retained memory would usually cost more than these temporary
        // generation-time strings.
        _writer.Write(
            type.ToDisplayString(
                s_typeDisplayFormat));
    }

    private void WriteStaticMemberReference(
        ISymbol symbol)
    {
        Debug.Assert(
            symbol is IFieldSymbol
            {
                IsStatic: true,
            } or
            IPropertySymbol
            {
                IsStatic: true,
            });

        WriteTypeName(
            symbol.ContainingType);

        _writer.Write(".");

        WriteIdentifier(symbol.Name);
    }

    private bool TryWriteStaticMemberReference(
        ISymbol? symbol)
    {
        if (symbol is not
            IFieldSymbol
            {
                IsStatic: true,
            } and not
            IPropertySymbol
            {
                IsStatic: true,
            })
        {
            return false;
        }

        WriteStaticMemberReference(symbol);
        return true;
    }

    private void WriteConstant(
        object? value,
        ISymbol? targetType)
    {
        if (value == null)
        {
            _writer.Write("null");
            return;
        }

        if (targetType is
            INamedTypeSymbol
            {
                TypeKind: TypeKind.Enum,
            } unsignedEnumType &&
            value is ulong unsignedEnumValue &&
            unsignedEnumValue > long.MaxValue)
        {
            _writer.Write("unchecked((");

            WriteTypeName(unsignedEnumType);

            _writer.Write(")");

            _writer.Write(
                unsignedEnumValue.ToString(
                    CultureInfo.InvariantCulture));

            _writer.Write("UL)");
            return;
        }

        if (targetType is
            INamedTypeSymbol
            {
                TypeKind: TypeKind.Enum,
            } enumType &&
            TryConvertToInt64(
                value,
                out var enumValue))
        {
            _writer.Write("(");

            WriteTypeName(enumType);

            _writer.Write(")");

            _writer.Write(
                enumValue.ToString(
                    CultureInfo.InvariantCulture));

            return;
        }

        switch (value)
        {
            case CSharpSymbolDefinition definition
                when TryWriteStaticMemberReference(
                    definition.Symbol):
                return;

            case ITypeSymbol type:
                _writer.Write("typeof(");
                WriteTypeName(type);
                _writer.Write(")");
                return;

            case string text:
                _writer.WriteStringLiteral(text);
                return;

            case char character:
                _writer.Write(
                    SymbolDisplay.FormatLiteral(
                        character,
                        quote: true));
                return;

            case bool boolean:
                _writer.WriteBooleanLiteral(boolean);
                return;

            case byte number:
                _writer.WriteIntegerLiteral(number);
                return;

            case sbyte number:
                _writer.WriteIntegerLiteral(number);
                return;

            case short number:
                _writer.WriteIntegerLiteral(number);
                return;

            case ushort number:
                _writer.WriteIntegerLiteral(number);
                return;

            case int number:
                _writer.WriteIntegerLiteral(number);
                return;

            case uint number:
                _writer.Write(
                    number.ToString(
                        CultureInfo.InvariantCulture));

                _writer.Write("u");
                return;

            case long number:
                _writer.Write(
                    number.ToString(
                        CultureInfo.InvariantCulture));

                _writer.Write("L");
                return;

            case ulong number:
                _writer.Write(
                    number.ToString(
                        CultureInfo.InvariantCulture));

                _writer.Write("UL");
                return;

            case float number:
                WriteSingleLiteral(number);
                return;

            case double number:
                WriteDoubleLiteral(number);
                return;

            case decimal number:
                _writer.Write(
                    number.ToString(
                        CultureInfo.InvariantCulture));

                _writer.Write("m");
                return;

            default:
                Debug.Fail(
                    "Unsupported constant value: " +
                    value.GetType().FullName);

                _writer.WriteStringLiteral(
                    value.ToString() ??
                    string.Empty);

                return;
        }
    }

    private void WriteSingleLiteral(
        float value)
    {
        if (float.IsNaN(value))
        {
            _writer.Write(
                "global::System.Single.NaN");

            return;
        }

        if (float.IsPositiveInfinity(value))
        {
            _writer.Write(
                "global::System.Single.PositiveInfinity");

            return;
        }

        if (float.IsNegativeInfinity(value))
        {
            _writer.Write(
                "global::System.Single.NegativeInfinity");

            return;
        }

        _writer.Write(
            value.ToString(
                "R",
                CultureInfo.InvariantCulture));

        _writer.Write("f");
    }

    private void WriteDoubleLiteral(
        double value)
    {
        if (double.IsNaN(value))
        {
            _writer.Write(
                "global::System.Double.NaN");

            return;
        }

        if (double.IsPositiveInfinity(value))
        {
            _writer.Write(
                "global::System.Double.PositiveInfinity");

            return;
        }

        if (double.IsNegativeInfinity(value))
        {
            _writer.Write(
                "global::System.Double.NegativeInfinity");

            return;
        }

        _writer.Write(
            value.ToString(
                "R",
                CultureInfo.InvariantCulture));

        _writer.Write("d");
    }

    private static bool TryConvertToInt64(
        object value,
        out long result)
    {
        switch (value)
        {
            case byte number:
                result = number;
                return true;

            case sbyte number:
                result = number;
                return true;

            case short number:
                result = number;
                return true;

            case ushort number:
                result = number;
                return true;

            case int number:
                result = number;
                return true;

            case uint number:
                result = number;
                return true;

            case long number:
                result = number;
                return true;

            case ulong number
                when number <= long.MaxValue:
                result = (long)number;
                return true;

            default:
                result = 0;
                return false;
        }
    }

    private static ITypeSymbol? GetInitialPathType(
        MarkupBindingValue binding,
        int pathElementStart)
    {
        if (pathElementStart > 0 &&
            pathElementStart <=
                binding.PathElements.Length &&
            binding.PathElements[
                    pathElementStart - 1]
                .Type.Symbol
                is ITypeSymbol rootedType)
        {
            return rootedType;
        }

        return binding.SourceType.Symbol
            as ITypeSymbol;
    }

    private static ReadOnlyMemory<char>
        GetElementNameText(
            string text)
    {
        var value = text.AsMemory();

        return value.Length > 0 &&
               value.Span[0] == '#'
            ? value.Slice(1)
            : value;
    }

    private static ReadOnlyMemory<char>
        TrimQuotes(
            string text)
    {
        var value = text.AsMemory();

        if (value.Length < 2)
        {
            return value;
        }

        var span = value.Span;

        if ((span[0] == '"' &&
             span[span.Length - 1] == '"') ||
            (span[0] == '\'' &&
             span[span.Length - 1] == '\''))
        {
            return value.Slice(
                1,
                value.Length - 2);
        }

        return value;
    }

    private static bool ContainsErrorType(
        ITypeSymbol type)
    {
        if (type is IErrorTypeSymbol ||
            type.TypeKind == TypeKind.Error)
        {
            return true;
        }

        switch (type)
        {
            case IArrayTypeSymbol array:
                return ContainsErrorType(
                    array.ElementType);

            case IPointerTypeSymbol pointer:
                return ContainsErrorType(
                    pointer.PointedAtType);

            case INamedTypeSymbol named:
                for (var i = 0;
                     i < named.TypeArguments.Length;
                     i++)
                {
                    if (ContainsErrorType(
                            named.TypeArguments[i]))
                    {
                        return true;
                    }
                }

                return false;

            case IFunctionPointerTypeSymbol functionPointer:
                if (ContainsErrorType(
                        functionPointer.Signature.ReturnType))
                {
                    return true;
                }

                var parameters =
                    functionPointer.Signature.Parameters;

                for (var i = 0;
                     i < parameters.Length;
                     i++)
                {
                    if (ContainsErrorType(
                            parameters[i].Type))
                    {
                        return true;
                    }
                }

                return false;

            default:
                return false;
        }
    }
}
