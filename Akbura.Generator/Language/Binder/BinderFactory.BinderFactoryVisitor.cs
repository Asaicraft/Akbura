using Akbura.Language.Syntax;
using System;
using System.Collections.Immutable;
using System.Diagnostics;

namespace Akbura.Language.Binder;

internal sealed partial class BinderFactory
{
    internal sealed class BinderFactoryVisitor : SyntaxVisitor<Binder>
    {
        private BinderFactory? _factory;
        private ImmutableArray<Declaration> _path;
        private BinderUsage _usage;
        private Binder? _next;
        private Declaration? _declaration;

        internal void Initialize(
            BinderFactory factory,
            ImmutableArray<Declaration> path,
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
                current = Visit(DeclarationFacts.GetSyntax(declaration)) ?? current;
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

            return new BinderCacheKey(syntax, usage);
        }

        public override Binder DefaultVisit(AkburaSyntax akburaSyntax)
        {
            return Next;
        }

        public override Binder VisitAkburaDocumentSyntax(AkburaDocumentSyntax node)
        {
            var declaration = RequiredDeclaration(DeclarationKind.Component);
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

        private MarkupBinder CreateMarkupBinder()
        {
            var declaration = RequiredDeclaration(
                DeclarationKind.MarkupRoot,
                DeclarationKind.MarkupElement);
            var next = Factory.BindingSession.AddContainingBlockBinders(
                Next,
                DeclarationFacts.GetSyntax(declaration),
                _usage);
            return new MarkupBinder(
                Factory.SemanticModel,
                next,
                declaration,
                next.Flags | GetUsageFlags(_usage));
        }

        private AkcssModuleBinder CreateAkcssModuleBinder()
        {
            var declaration = RequiredDeclaration(DeclarationKind.AkcssModule);
            return new AkcssModuleBinder(
                Factory.SemanticModel,
                Next,
                declaration,
                Next.Flags | GetUsageFlags(_usage));
        }

        private AkcssStyleBinder CreateAkcssStyleBinder()
        {
            var declaration = RequiredDeclaration(
                DeclarationKind.AkcssStyle,
                DeclarationKind.AkcssUtility);
            return new AkcssStyleBinder(
                Factory.SemanticModel,
                Next,
                declaration,
                Next.Flags | GetUsageFlags(_usage));
        }

        private Declaration RequiredDeclaration(params DeclarationKind[] expectedKinds)
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
                $"Declaration kind {declaration.Kind} cannot create binder for {DeclarationFacts.GetSyntax(declaration).GetType().Name}.");
        }

        private BinderFactory Factory =>
            _factory ?? throw new InvalidOperationException($"{nameof(BinderFactoryVisitor)} is not initialized.");

        private Binder Next =>
            _next ?? throw new InvalidOperationException($"{nameof(BinderFactoryVisitor)} does not have a next binder.");

        private static AkburaBinderFlags GetUsageFlags(BinderUsage usage)
        {
            return usage switch
            {
                BinderUsage.Markup => AkburaBinderFlags.InMarkup,
                BinderUsage.Akcss => AkburaBinderFlags.InAkcss,
                _ => AkburaBinderFlags.None,
            };
        }
    }
}
