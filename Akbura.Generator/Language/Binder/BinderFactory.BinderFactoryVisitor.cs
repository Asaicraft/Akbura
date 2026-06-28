using Akbura.Language.Declarations;
using Akbura.Language.Syntax;
using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Akbura.Language.Binder;

internal sealed partial class BinderFactory
{
    internal sealed class BinderFactoryVisitor : SyntaxVisitor<Binder>
    {
        private BinderFactory? _factory;
        private ImmutableArray<AkburaDeclaration> _path;
        private BinderUsage _usage;
        private Binder? _next;
        private AkburaDeclaration? _declaration;

        internal void Initialize(
            BinderFactory factory,
            ImmutableArray<AkburaDeclaration> path,
            BinderUsage usage)
        {
            Debug.Assert(!path.IsDefault);

            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _path = path;
            _usage = usage;
            _next = factory.RootBinder;
            _declaration = null;
        }

        internal void Clear()
        {
            _factory = null;
            _path = default;
            _usage = BinderUsage.Default;
            _next = null;
            _declaration = null;
        }

        internal Binder VisitPath()
        {
            Binder current = Factory.RootBinder;
            foreach (var declaration in _path)
            {
                _next = current;
                _declaration = declaration;
                current = Visit(declaration.Syntax) ?? current;
            }

            return current;
        }

        internal static BinderCacheKey CreateBinderCacheKey(
            AkburaSyntax syntax,
            BinderUsage usage)
        {
            if (syntax == null)
            {
                throw new ArgumentNullException(nameof(syntax));
            }

            return new BinderCacheKey(syntax.Green, usage);
        }

        internal static SemanticReuseKey CreateSemanticReuseKey(
            AkburaSyntax syntax,
            ImmutableArray<AkburaDeclaration> path,
            BinderUsage usage)
        {
            if (syntax == null)
            {
                throw new ArgumentNullException(nameof(syntax));
            }

            if (path.IsDefaultOrEmpty)
            {
                return new SemanticReuseKey(
                    syntax.Green,
                    usage,
                    AkburaBinderFlags.None,
                    AkburaDeclarationKind.None,
                    scopeKey: string.Empty);
            }

            var declaration = path[path.Length - 1];
            return new SemanticReuseKey(
                syntax.Green,
                usage,
                GetPathFlags(path),
                declaration.Kind,
                GetStableScopeChainKey(path));
        }

        public override Binder DefaultVisit(AkburaSyntax akburaSyntax)
        {
            return Next;
        }

        public override Binder VisitAkburaDocumentSyntax(AkburaDocumentSyntax node)
        {
            var declaration = RequiredDeclaration(AkburaDeclarationKind.Component);
            return new ComponentBinder(
                Factory.SemanticModel,
                Next,
                declaration,
                Next.Flags | GetUsageFlags(_usage));
        }

        public override Binder VisitMarkupRootSyntax(MarkupRootSyntax node)
        {
            return CreateMarkupBinder();
        }

        public override Binder VisitMarkupElementSyntax(MarkupElementSyntax node)
        {
            return CreateMarkupBinder();
        }

        public override Binder VisitInlineAkcssBlockSyntax(InlineAkcssBlockSyntax node)
        {
            return CreateAkcssModuleBinder();
        }

        public override Binder VisitAkcssDocumentSyntax(AkcssDocumentSyntax node)
        {
            return CreateAkcssModuleBinder();
        }

        public override Binder VisitAkcssStyleRuleSyntax(AkcssStyleRuleSyntax node)
        {
            return CreateAkcssStyleBinder();
        }

        public override Binder VisitAkcssUtilityDeclarationSyntax(AkcssUtilityDeclarationSyntax node)
        {
            return CreateAkcssStyleBinder();
        }

        public override Binder VisitCSharpBlockSyntax(CSharpBlockSyntax node)
        {
            var declaration = RequiredDeclaration(AkburaDeclarationKind.CSharpBlock);
            return new BlockBinder(
                Factory.SemanticModel,
                Next,
                declaration,
                Next.Flags | GetUsageFlags(_usage));
        }

        private MarkupBinder CreateMarkupBinder()
        {
            var declaration = RequiredDeclaration(
                AkburaDeclarationKind.MarkupRoot,
                AkburaDeclarationKind.MarkupElement);
            return new MarkupBinder(
                Factory.SemanticModel,
                Next,
                declaration,
                Next.Flags | GetUsageFlags(_usage));
        }

        private AkcssModuleBinder CreateAkcssModuleBinder()
        {
            var declaration = RequiredDeclaration(AkburaDeclarationKind.AkcssModule);
            return new AkcssModuleBinder(
                Factory.SemanticModel,
                Next,
                declaration,
                Next.Flags | GetUsageFlags(_usage));
        }

        private AkcssStyleBinder CreateAkcssStyleBinder()
        {
            var declaration = RequiredDeclaration(
                AkburaDeclarationKind.AkcssStyle,
                AkburaDeclarationKind.AkcssUtility);
            return new AkcssStyleBinder(
                Factory.SemanticModel,
                Next,
                declaration,
                Next.Flags | GetUsageFlags(_usage));
        }

        private AkburaDeclaration RequiredDeclaration(params AkburaDeclarationKind[] expectedKinds)
        {
            var declaration = _declaration ??
                throw new InvalidOperationException($"{nameof(BinderFactoryVisitor)} is not visiting a declaration.");

            foreach (var expectedKind in expectedKinds)
            {
                if (declaration.Kind == expectedKind)
                {
                    return declaration;
                }
            }

            throw new InvalidOperationException(
                $"Declaration kind {declaration.Kind} cannot create binder for {declaration.Syntax.GetType().Name}.");
        }

        private BinderFactory Factory =>
            _factory ?? throw new InvalidOperationException($"{nameof(BinderFactoryVisitor)} is not initialized.");

        private Binder Next =>
            _next ?? throw new InvalidOperationException($"{nameof(BinderFactoryVisitor)} does not have a next binder.");

        private static AkburaBinderFlags GetPathFlags(ImmutableArray<AkburaDeclaration> path)
        {
            var flags = AkburaBinderFlags.None;
            foreach (var declaration in path)
            {
                flags |= declaration.Kind switch
                {
                    AkburaDeclarationKind.Component => AkburaBinderFlags.InComponent,
                    AkburaDeclarationKind.MarkupRoot or AkburaDeclarationKind.MarkupElement => AkburaBinderFlags.InMarkup,
                    AkburaDeclarationKind.CSharpBlock => AkburaBinderFlags.InCSharpBlock,
                    AkburaDeclarationKind.AkcssModule => AkburaBinderFlags.InAkcss,
                    AkburaDeclarationKind.AkcssStyle => AkburaBinderFlags.InAkcss | AkburaBinderFlags.InAkcssStyle,
                    AkburaDeclarationKind.AkcssUtility => AkburaBinderFlags.InAkcss | AkburaBinderFlags.InAkcssUtility,
                    _ => AkburaBinderFlags.None,
                };
            }

            return flags;
        }

        private static AkburaBinderFlags GetUsageFlags(BinderUsage usage)
        {
            return usage switch
            {
                BinderUsage.Markup => AkburaBinderFlags.InMarkup,
                BinderUsage.Akcss => AkburaBinderFlags.InAkcss,
                _ => AkburaBinderFlags.None,
            };
        }

        private static bool IsScopeDeclaration(AkburaDeclarationKind kind)
        {
            return kind is
                AkburaDeclarationKind.Component or
                AkburaDeclarationKind.MarkupRoot or
                AkburaDeclarationKind.MarkupElement or
                AkburaDeclarationKind.CSharpBlock or
                AkburaDeclarationKind.AkcssModule or
                AkburaDeclarationKind.AkcssStyle or
                AkburaDeclarationKind.AkcssUtility;
        }

        private static string GetStableScopeChainKey(ImmutableArray<AkburaDeclaration> path)
        {
            var builder = new StringBuilder();
            foreach (var declaration in path)
            {
                if (IsScopeDeclaration(declaration.Kind))
                {
                    builder.Append(GetStableScopeKey(declaration));
                    builder.Append('|');
                }
            }

            return builder.ToString();
        }

        private static string GetStableScopeKey(AkburaDeclaration declaration)
        {
            return declaration.Kind switch
            {
                AkburaDeclarationKind.Component =>
                    $"{declaration.Kind}:{declaration.Name}:{GetChildSyntaxSignature(declaration, AkburaDeclarationKind.Namespace, AkburaDeclarationKind.Using)}",
                AkburaDeclarationKind.AkcssModule =>
                    $"{declaration.Kind}:{declaration.Name}:{GetChildSyntaxSignature(declaration, AkburaDeclarationKind.AkcssUsing)}",
                AkburaDeclarationKind.MarkupRoot or
                    AkburaDeclarationKind.MarkupElement or
                    AkburaDeclarationKind.AkcssStyle or
                    AkburaDeclarationKind.AkcssUtility =>
                    $"{declaration.Kind}:{declaration.Name}",
                _ =>
                    $"{declaration.Kind}:{declaration.Name}:{RuntimeHelpers.GetHashCode(declaration.Syntax.Green)}",
            };
        }

        private static string GetChildSyntaxSignature(
            AkburaDeclaration declaration,
            params AkburaDeclarationKind[] kinds)
        {
            var builder = new StringBuilder();
            foreach (var child in declaration.Children)
            {
                foreach (var kind in kinds)
                {
                    if (child.Kind == kind)
                    {
                        builder.Append(child.Syntax.ToFullString());
                        builder.Append('|');
                        break;
                    }
                }
            }

            return builder.ToString();
        }
    }
}
