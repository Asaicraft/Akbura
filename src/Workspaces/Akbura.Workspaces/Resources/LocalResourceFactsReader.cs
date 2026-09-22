using Akbura.Pools;
using Microsoft.Language.Xml;
using RoslynTextSpan = Microsoft.CodeAnalysis.Text.TextSpan;
using XmlElementSyntax = Microsoft.Language.Xml.IXmlElementSyntax;
using XmlSyntaxNode = Microsoft.Language.Xml.SyntaxNode;
using XmlSyntaxVisitor = Microsoft.Language.Xml.SyntaxVisitor;

namespace Akbura.Workspaces.Resources;

internal static class LocalResourceFactsReader
{
    private static readonly ObjectPool<LocalResourceFactsVisitor> s_pool = new(pool => new LocalResourceFactsVisitor(pool), size: 16);

    public static LocalResourceFacts Read(ResourceDocumentInput input, XmlDocumentSyntax document, CancellationToken cancellationToken)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        var visitor = s_pool.Allocate();
        try
        {
            visitor.Initialize(input, cancellationToken);
            visitor.Visit(document);
            return visitor.CreateFacts();
        }
        finally
        {
            visitor.Free();
        }
    }

    private sealed class LocalResourceFactsVisitor : XmlSyntaxVisitor
    {
        private const string AvaloniaXmlNamespace =
            "https://github.com/avaloniaui";
        private const string XamlXmlNamespace =
            "http://schemas.microsoft.com/winfx/2006/xaml";

        private readonly ObjectPool<LocalResourceFactsVisitor> _pool;
        private ArrayBuilder<LocalResourceDeclarationFact>? _declarations;
        private ArrayBuilder<LocalResourceImportFact>? _imports;
        private ArrayBuilder<LocalResourceScopeFact>? _scopes;
        private ResourceDocumentInput _input;
        private LocalApplicationResourceRootFact? _applicationRoot;
        private CancellationToken _cancellationToken;
        private int? _currentScopeId;
        private TraversalMode _mode;
        private string? _themeVariant;
        private bool _initialized;

        public LocalResourceFactsVisitor(ObjectPool<LocalResourceFactsVisitor> pool)
        {
            _pool = pool;
        }

        public void Initialize(ResourceDocumentInput input, CancellationToken cancellationToken)
        {
            if (_initialized)
            {
                throw new InvalidOperationException(
                    "The resource facts visitor is already initialized.");
            }

            _input = input;
            _cancellationToken = cancellationToken;
            _declarations = ArrayBuilder<LocalResourceDeclarationFact>.GetInstance();
            _imports = ArrayBuilder<LocalResourceImportFact>.GetInstance();
            _scopes = ArrayBuilder<LocalResourceScopeFact>.GetInstance();
            _applicationRoot = null;
            _currentScopeId = null;
            _mode = TraversalMode.Normal;
            _themeVariant = null;
            _initialized = true;
        }

        public LocalResourceFacts CreateFacts()
        {
            EnsureInitialized();
            return new LocalResourceFacts(
                new ResourcePathFacts(_input),
                _declarations!.ToImmutable(),
                _imports!.ToImmutable(),
                _scopes!.ToImmutable(),
                _applicationRoot);
        }

        public override XmlSyntaxNode VisitXmlDocument(XmlDocumentSyntax node)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (node.RootSyntax != null)
            {
                Visit(node.RootSyntax.AsNode);
            }

            return node;
        }

        public override XmlSyntaxNode VisitXmlElement(Microsoft.Language.Xml.XmlElementSyntax node)
        {
            VisitElement(node);
            return node;
        }

        public override XmlSyntaxNode VisitXmlEmptyElement(XmlEmptyElementSyntax node)
        {
            VisitElement(node);
            return node;
        }

        private void VisitElement(XmlElementSyntax element)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            switch (_mode)
            {
                case TraversalMode.Normal:
                    VisitNormalElement(element);
                    break;

                case TraversalMode.Resources:
                    VisitResourceElement(element);
                    break;

                case TraversalMode.Styles:
                    VisitStyleElement(element);
                    break;

                case TraversalMode.MergedDictionaries:
                    VisitMergedDictionaryElement(element);
                    break;

                case TraversalMode.ThemeDictionaries:
                    VisitThemeDictionaryElement(element);
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unsupported XML traversal mode '{_mode}'.");
            }
        }

        private void VisitNormalElement(XmlElementSyntax element)
        {
            var isDocumentRoot = element.Parent == null;

            if (isDocumentRoot && IsAvaloniaElement(element, "ResourceDictionary"))
            {
                var rootScopeId = AddScope(
                    element,
                    element.AsNode.Span,
                    LocalResourceScopeKind.ResourceDictionary,
                    isDocumentRoot: true);
                VisitChildren(
                    element,
                    rootScopeId,
                    TraversalMode.Resources,
                    themeVariant: null);
                return;
            }

            if (isDocumentRoot && IsAvaloniaElement(element, "Styles"))
            {
                var rootScopeId = AddScope(
                    element,
                    element.AsNode.Span,
                    LocalResourceScopeKind.Style,
                    isDocumentRoot: true);
                VisitChildren(
                    element,
                    rootScopeId,
                    TraversalMode.Styles,
                    themeVariant: null);
                return;
            }

            var resourceScopeProperty = FindResourceScopeProperty(element);
            var scopeId = _currentScopeId;

            if (resourceScopeProperty != null)
            {
                scopeId = AddScope(
                    element,
                    resourceScopeProperty.AsNode.Span,
                    GetScopeKind(element),
                    isDocumentRoot);
            }

            if (isDocumentRoot && IsAvaloniaElement(element, "Application"))
            {
                _applicationRoot =
                    new LocalApplicationResourceRootFact(
                        ToTextSpan(element.AsNode.Span),
                        scopeId);
            }

            var content = element.Content;
            for (var index = 0; index < content.Count; index++)
            {
                if (content[index] is not XmlElementSyntax child)
                {
                    continue;
                }

                if (IsResourcesProperty(child, element))
                {
                    if (scopeId.HasValue)
                    {
                        VisitChildren(
                            child,
                            scopeId,
                            TraversalMode.Resources,
                            themeVariant: null);
                    }

                    continue;
                }

                if (IsStylesProperty(child, element))
                {
                    if (scopeId.HasValue)
                    {
                        VisitChildren(
                            child,
                            scopeId,
                            TraversalMode.Styles,
                            themeVariant: null);
                    }

                    continue;
                }

                VisitChild(
                    child,
                    scopeId,
                    TraversalMode.Normal,
                    themeVariant: null);
            }
        }

        private void VisitResourceElement(XmlElementSyntax element)
        {
            if (!_currentScopeId.HasValue)
            {
                return;
            }

            if (IsMergedDictionariesProperty(element))
            {
                VisitChildren(
                    element,
                    _currentScopeId,
                    TraversalMode.MergedDictionaries,
                    _themeVariant);
                return;
            }

            if (IsThemeDictionariesProperty(element))
            {
                VisitChildren(
                    element,
                    _currentScopeId,
                    TraversalMode.ThemeDictionaries,
                    themeVariant: null);
                return;
            }

            if (IsResourceIncludeElement(element))
            {
                AddImport(element);
                return;
            }

            if (TryGetResourceKey(
                    element,
                    out var key,
                    out var keyAttribute))
            {
                AddDeclaration(
                    element,
                    key,
                    keyAttribute);
                return;
            }

            if (IsAvaloniaElement(element, "ResourceDictionary"))
            {
                VisitChildren(
                    element,
                    _currentScopeId,
                    TraversalMode.Resources,
                    _themeVariant);
                return;
            }

            VisitNormalElement(element);
        }

        private void VisitStyleElement(XmlElementSyntax element)
        {
            if (_currentScopeId.HasValue &&
                IsResourceIncludeElement(element))
            {
                AddImport(element);
                return;
            }

            VisitNormalElement(element);
        }

        private void VisitMergedDictionaryElement(XmlElementSyntax element)
        {
            if (_currentScopeId.HasValue &&
                IsResourceIncludeElement(element))
            {
                AddImport(element);
            }
        }

        private void VisitThemeDictionaryElement(XmlElementSyntax element)
        {
            if (!_currentScopeId.HasValue ||
                !IsAvaloniaElement(element, "ResourceDictionary") ||
                !TryGetResourceKey(
                    element,
                    out var themeVariant,
                    out _))
            {
                return;
            }

            VisitChildren(
                element,
                _currentScopeId,
                TraversalMode.Resources,
                themeVariant);
        }

        private void VisitChildren(XmlElementSyntax element, int? scopeId, TraversalMode mode, string? themeVariant)
        {
            var content = element.Content;
            for (var index = 0; index < content.Count; index++)
            {
                if (content[index] is XmlElementSyntax child)
                {
                    VisitChild(
                        child,
                        scopeId,
                        mode,
                        themeVariant);
                }
            }
        }

        private void VisitChild(XmlElementSyntax child, int? scopeId, TraversalMode mode, string? themeVariant)
        {
            var savedScopeId = _currentScopeId;
            var savedMode = _mode;
            var savedThemeVariant = _themeVariant;

            try
            {
                _currentScopeId = scopeId;
                _mode = mode;
                _themeVariant = themeVariant;
                Visit(child.AsNode);
            }
            finally
            {
                _currentScopeId = savedScopeId;
                _mode = savedMode;
                _themeVariant = savedThemeVariant;
            }
        }

        private int AddScope(XmlElementSyntax owner, Microsoft.Language.Xml.TextSpan resourcesSpan, LocalResourceScopeKind kind, bool isDocumentRoot)
        {
            var id = _scopes!.Count;
            _scopes.Add(
                new LocalResourceScopeFact(
                    id,
                    _currentScopeId,
                    kind,
                    ToTextSpan(owner.AsNode.Span),
                    ToTextSpan(resourcesSpan),
                    isDocumentRoot));
            return id;
        }

        private void AddDeclaration(XmlElementSyntax element, string key, XmlAttributeSyntax keyAttribute)
        {
            _declarations!.Add(
                new LocalResourceDeclarationFact(
                    key,
                    element.NameNode.FullName,
                    ResolveElementNamespace(element),
                    _currentScopeId!.Value,
                    _themeVariant,
                    ToTextSpan(element.AsNode.Span),
                    ToTextSpan(keyAttribute.ValueNode.TextTokens.Span)));
        }

        private void AddImport(XmlElementSyntax element)
        {
            if (!TryGetSource(
                    element,
                    out var source,
                    out var sourceAttribute))
            {
                return;
            }

            _imports!.Add(
                new LocalResourceImportFact(
                    source,
                    _currentScopeId!.Value,
                    ToTextSpan(element.AsNode.Span),
                    ToTextSpan(sourceAttribute.ValueNode.TextTokens.Span)));
        }

        private static XmlElementSyntax? FindResourceScopeProperty(XmlElementSyntax element)
        {
            XmlElementSyntax? styles = null;
            var content = element.Content;
            for (var index = 0; index < content.Count; index++)
            {
                if (content[index] is not XmlElementSyntax child)
                {
                    continue;
                }

                if (IsResourcesProperty(child, element))
                {
                    return child;
                }

                if (styles == null &&
                    IsStylesProperty(child, element))
                {
                    styles = child;
                }
            }

            return styles;
        }

        private static bool IsResourcesProperty(XmlElementSyntax property, XmlElementSyntax owner)
        {
            return IsOwnerCollectionProperty(
                property,
                owner,
                ".Resources");
        }

        private static bool IsStylesProperty(XmlElementSyntax property, XmlElementSyntax owner)
        {
            return IsOwnerCollectionProperty(
                property,
                owner,
                ".Styles");
        }

        private static bool IsOwnerCollectionProperty(XmlElementSyntax property, XmlElementSyntax owner, string suffix)
        {
            var propertyName = property.NameNode.LocalName;
            if (!propertyName.EndsWith(
                    suffix,
                    StringComparison.Ordinal))
            {
                return false;
            }

            var declaredOwnerName = propertyName.Substring(
                0,
                propertyName.Length - suffix.Length);
            if (declaredOwnerName.Length == 0)
            {
                return false;
            }

            var propertyNamespace =
                ResolveElementNamespace(property);
            var ownerNamespace = ResolveElementNamespace(owner);
            if (string.Equals(
                    declaredOwnerName,
                    owner.NameNode.LocalName,
                    StringComparison.Ordinal) &&
                string.Equals(
                    propertyNamespace,
                    ownerNamespace,
                    StringComparison.Ordinal))
            {
                return true;
            }

            return string.Equals(
                propertyNamespace,
                AvaloniaXmlNamespace,
                StringComparison.Ordinal);
        }

        private static bool IsMergedDictionariesProperty(XmlElementSyntax element)
        {
            return IsAvaloniaElement(
                element,
                "ResourceDictionary.MergedDictionaries") &&
                element.Parent is XmlElementSyntax owner &&
                IsAvaloniaElement(owner, "ResourceDictionary");
        }

        private static bool IsThemeDictionariesProperty(XmlElementSyntax element)
        {
            return IsAvaloniaElement(
                element,
                "ResourceDictionary.ThemeDictionaries") &&
                element.Parent is XmlElementSyntax owner &&
                IsAvaloniaElement(owner, "ResourceDictionary");
        }

        private static LocalResourceScopeKind GetScopeKind(XmlElementSyntax owner)
        {
            if (IsAvaloniaElement(owner, "Application"))
            {
                return LocalResourceScopeKind.Application;
            }

            if (IsAvaloniaElement(owner, "Style"))
            {
                return LocalResourceScopeKind.Style;
            }

            return LocalResourceScopeKind.Element;
        }

        private static bool IsAvaloniaElement(XmlElementSyntax element, string localName)
        {
            if (!string.Equals(
                    element.NameNode.LocalName,
                    localName,
                    StringComparison.Ordinal))
            {
                return false;
            }

            var xmlNamespace = ResolveElementNamespace(element);
            return string.IsNullOrEmpty(xmlNamespace) ||
                string.Equals(
                    xmlNamespace,
                    AvaloniaXmlNamespace,
                    StringComparison.Ordinal);
        }

        private static bool IsResourceIncludeElement(XmlElementSyntax element)
        {
            var localName = element.NameNode.LocalName;
            if (!string.Equals(
                    localName,
                    "ResourceInclude",
                    StringComparison.Ordinal) &&
                !string.Equals(
                    localName,
                    "MergeResourceInclude",
                    StringComparison.Ordinal) &&
                !string.Equals(
                    localName,
                    "StyleInclude",
                    StringComparison.Ordinal))
            {
                return false;
            }

            return string.Equals(
                ResolveElementNamespace(element),
                AvaloniaXmlNamespace,
                StringComparison.Ordinal);
        }

        private static bool TryGetResourceKey(XmlElementSyntax element, out string key, out XmlAttributeSyntax attribute)
        {
            var attributes = element.AttributesNode;
            for (var index = 0; index < attributes.Count; index++)
            {
                var candidate = attributes[index];
                var prefix = candidate.NameNode.Prefix;
                if (string.IsNullOrEmpty(prefix) ||
                    !string.Equals(
                        candidate.NameNode.LocalName,
                        "Key",
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        ResolveNamespace(element, prefix),
                        XamlXmlNamespace,
                        StringComparison.Ordinal) ||
                    !TryGetCompleteLiteral(
                        candidate,
                        out var value) ||
                    string.IsNullOrWhiteSpace(value) ||
                    IsComputedValue(value))
                {
                    continue;
                }

                key = value;
                attribute = candidate;
                return true;
            }

            key = string.Empty;
            attribute = null!;
            return false;
        }

        private static bool TryGetSource(XmlElementSyntax element, out string source, out XmlAttributeSyntax attribute)
        {
            var attributes = element.AttributesNode;
            for (var index = 0; index < attributes.Count; index++)
            {
                var candidate = attributes[index];
                if (!string.IsNullOrEmpty(candidate.NameNode.Prefix) ||
                    !string.Equals(
                        candidate.NameNode.LocalName,
                        "Source",
                        StringComparison.Ordinal) ||
                    !TryGetCompleteLiteral(
                        candidate,
                        out var value))
                {
                    continue;
                }

                var trimmed = value.Trim();
                if (trimmed.Length == 0 || IsComputedValue(trimmed))
                {
                    continue;
                }

                source = trimmed;
                attribute = candidate;
                return true;
            }

            source = string.Empty;
            attribute = null!;
            return false;
        }

        private static bool TryGetCompleteLiteral(XmlAttributeSyntax attribute, out string value)
        {
            var valueNode = attribute.ValueNode;
            var startQuote = valueNode.StartQuoteToken;
            var endQuote = valueNode.EndQuoteToken;

            if (attribute.Equals.Width == 0 ||
                startQuote.Width == 0 ||
                endQuote.Width == 0 ||
                startQuote.Kind != endQuote.Kind ||
                (startQuote.Kind != SyntaxKind.DoubleQuoteToken &&
                 startQuote.Kind != SyntaxKind.SingleQuoteToken))
            {
                value = string.Empty;
                return false;
            }

            value = attribute.Value;
            return true;
        }

        private static bool IsComputedValue(string value)
        {
            return value.Length > 0 && value[0] == '{';
        }

        private static string? ResolveElementNamespace(XmlElementSyntax element)
        {
            return ResolveNamespace(
                element,
                element.NameNode.Prefix);
        }

        private static string? ResolveNamespace(XmlElementSyntax element, string? prefix)
        {
            for (var current = element; current != null; current = current.Parent)
            {
                var attributes = current.AttributesNode;
                for (var index = 0; index < attributes.Count; index++)
                {
                    var attribute = attributes[index];
                    var name = attribute.NameNode;
                    var isMatch = string.IsNullOrEmpty(prefix)
                        ? string.IsNullOrEmpty(name.Prefix) &&
                          string.Equals(
                              name.LocalName,
                              "xmlns",
                              StringComparison.Ordinal)
                        : string.Equals(
                              name.Prefix,
                              "xmlns",
                              StringComparison.Ordinal) &&
                          string.Equals(
                              name.LocalName,
                              prefix,
                              StringComparison.Ordinal);

                    if (isMatch &&
                        TryGetCompleteLiteral(
                            attribute,
                            out var xmlNamespace))
                    {
                        return xmlNamespace;
                    }
                }
            }

            return null;
        }

        private static RoslynTextSpan ToTextSpan(Microsoft.Language.Xml.TextSpan span)
        {
            return new RoslynTextSpan(span.Start, span.Length);
        }

        private void EnsureInitialized()
        {
            if (!_initialized)
            {
                throw new InvalidOperationException(
                    "The resource facts visitor is not initialized.");
            }
        }

        public void Free()
        {
            if (!_initialized)
            {
                return;
            }

            _declarations!.Free();
            _imports!.Free();
            _scopes!.Free();
            _declarations = null;
            _imports = null;
            _scopes = null;
            _input = default;
            _applicationRoot = null;
            _cancellationToken = default;
            _currentScopeId = null;
            _mode = TraversalMode.Normal;
            _themeVariant = null;
            _initialized = false;
            _pool.Free(this);
        }

        private enum TraversalMode
        {
            Normal,
            Resources,
            Styles,
            MergedDictionaries,
            ThemeDictionaries,
        }
    }
}
