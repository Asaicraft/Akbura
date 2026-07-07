// This file is ported and adapted from the Roslyn (dotnet/roslyn)

#nullable disable

using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Akbura.Pools;
using Akbura.Collections;
using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using AkburaSyntaxKind = Akbura.Language.Syntax.SyntaxKind;
using BoxedMemberNames = System.Runtime.CompilerServices.StrongBox<Akbura.Collections.ImmutableSegmentedHashSet<string>>;

namespace Akbura.Language;

internal sealed class DeclarationTreeBuilder : SyntaxVisitor<SingleNamespaceOrTypeDeclaration>
{
    private static readonly ConditionalWeakTable<GreenNode, BoxedMemberNames> s_nodeToMemberNames = new();
    private static readonly BoxedMemberNames s_emptyMemberNames = new(
        ImmutableSegmentedHashSet<string>.Empty.WithComparer(StringComparer.Ordinal));

    private string _componentName;
    private string _akcssLogicalName;
    private OneOrMany<WeakReference<BoxedMemberNames>> _previousMemberNames;
    private int _currentTypeIndex;

    private DeclarationTreeBuilder(
        OneOrMany<WeakReference<BoxedMemberNames>> previousMemberNames = default)
    {
        _previousMemberNames = previousMemberNames.IsDefault
            ? OneOrMany<WeakReference<BoxedMemberNames>>.Empty
            : previousMemberNames;
    }

    public static RootSingleNamespaceDeclaration ForTree(
        AkburaSyntaxTree syntaxTree,
        OneOrMany<WeakReference<BoxedMemberNames>> previousMemberNames = default)
    {
        if (syntaxTree == null)
        {
            throw new ArgumentNullException(nameof(syntaxTree));
        }

        var builder = new DeclarationTreeBuilder(previousMemberNames)
        {
            _componentName = syntaxTree.ComponentName,
        };

        return builder.CreateAkburaRootDeclaration(syntaxTree.GetRoot());
    }

    public static RootSingleNamespaceDeclaration ForTree(
        AkcssSyntaxTree syntaxTree,
        OneOrMany<WeakReference<BoxedMemberNames>> previousMemberNames = default)
    {
        if (syntaxTree == null)
        {
            throw new ArgumentNullException(nameof(syntaxTree));
        }

        var builder = new DeclarationTreeBuilder(previousMemberNames)
        {
            _akcssLogicalName = syntaxTree.LogicalName,
        };

        return builder.CreateAkcssRootDeclaration(syntaxTree.GetRoot());
    }

    public static bool CachesComputedMemberNames(SingleTypeDeclaration typeDeclaration)
    {
        return typeDeclaration.Kind switch
        {
            DeclarationKind.Namespace => ThrowHelper.UnexpectedValue<bool>(typeDeclaration.Kind),
            DeclarationKind.Component or
            DeclarationKind.AkcssModule => true,
            DeclarationKind.AkcssStyle or
            DeclarationKind.AkcssUtility => false,
            _ => false,
        };
    }

    public override SingleNamespaceOrTypeDeclaration VisitNamespaceDeclarationSyntax(NamespaceDeclarationSyntax node)
    {
        return SingleNamespaceDeclaration.Create(
            name: node.Name.ToFullString().Trim(),
            hasUsings: false,
            hasExternAliases: false,
            syntax: node,
            nameLocation: new SourceLocation(node.Name),
            children: ImmutableArray<SingleNamespaceOrTypeDeclaration>.Empty,
            diagnostics: GetDiagnostics(node));
    }

    public override SingleNamespaceOrTypeDeclaration VisitInlineAkcssBlockSyntax(InlineAkcssBlockSyntax node)
    {
        return CreateAkcssModuleDeclaration(
            name: "@akcss",
            syntax: node,
            members: node.Members);
    }

    public override SingleNamespaceOrTypeDeclaration VisitAkcssStyleRuleSyntax(AkcssStyleRuleSyntax node)
    {
        return CreateLeafDeclaration(
            DeclarationKind.AkcssStyle,
            node.Selector.ToFullString().Trim(),
            node);
    }

    public override SingleNamespaceOrTypeDeclaration VisitAkcssUtilityDeclarationSyntax(AkcssUtilityDeclarationSyntax node)
    {
        return CreateLeafDeclaration(
            DeclarationKind.AkcssUtility,
            node.Selector.ToFullString().Trim(),
            node);
    }

    private RootSingleNamespaceDeclaration CreateAkburaRootDeclaration(AkburaDocumentSyntax root)
    {
        var component = CreateComponentDeclaration(root);
        var child = WrapInNamespaces(component, GetNamespaceName(root), root);

        return new RootSingleNamespaceDeclaration(
            hasGlobalUsings: HasGlobalUsings(root),
            hasUsings: HasUsings(root),
            hasExternAliases: false,
            treeNode: root,
            children: ImmutableArray.Create(child),
            referenceDirectives: ImmutableArray<ReferenceDirective>.Empty,
            hasAssemblyAttributes: false,
            diagnostics: GetDiagnostics(root),
            globalAliasedQuickAttributes: QuickAttributes.None);
    }

    private RootSingleNamespaceDeclaration CreateAkcssRootDeclaration(AkcssDocumentSyntax root)
    {
        var module = CreateAkcssModuleDeclaration(
            string.IsNullOrWhiteSpace(_akcssLogicalName) ? "akcss" : _akcssLogicalName,
            root,
            root.Members);

        return new RootSingleNamespaceDeclaration(
            hasGlobalUsings: false,
            hasUsings: HasAkcssUsings(root),
            hasExternAliases: false,
            treeNode: root,
            children: ImmutableArray.Create<SingleNamespaceOrTypeDeclaration>(module),
            referenceDirectives: ImmutableArray<ReferenceDirective>.Empty,
            hasAssemblyAttributes: false,
            diagnostics: GetDiagnostics(root),
            globalAliasedQuickAttributes: QuickAttributes.None);
    }

    private SingleTypeDeclaration CreateComponentDeclaration(AkburaDocumentSyntax root)
    {
        var children = ArrayBuilder<SingleTypeDeclaration>.GetInstance();
        foreach (var member in root.Members)
        {
            if (member.Kind == AkburaSyntaxKind.InlineAkcssBlockSyntax)
            {
                children.Add((SingleTypeDeclaration)Visit(member)!);
            }
        }

        return new SingleTypeDeclaration(
            kind: DeclarationKind.Component,
            name: _componentName ?? string.Empty,
            arity: 0,
            modifiers: DeclarationModifiers.None,
            declFlags: SingleTypeDeclaration.TypeDeclarationFlags.None,
            syntax: root,
            nameLocation: new SourceLocation(root),
            memberNames: GetComponentMemberNames(root),
            children: children.ToImmutableAndFree(),
            diagnostics: GetDiagnostics(root),
            quickAttributes: QuickAttributes.None);
    }

    private SingleTypeDeclaration CreateAkcssModuleDeclaration(
        string name,
        AkburaSyntax syntax,
        SyntaxList<AkcssTopLevelMemberSyntax> members)
    {
        var children = ArrayBuilder<SingleTypeDeclaration>.GetInstance();
        foreach (var member in members)
        {
            if (member.Kind == AkburaSyntaxKind.AkcssStyleRuleSyntax)
            {
                children.Add((SingleTypeDeclaration)Visit(member)!);
            }
            else if (member.Kind == AkburaSyntaxKind.AkcssUtilitiesSectionSyntax)
            {
                foreach (var utility in ((AkcssUtilitiesSectionSyntax)member).Utilities)
                {
                    children.Add((SingleTypeDeclaration)Visit(utility)!);
                }
            }
        }

        return new SingleTypeDeclaration(
            kind: DeclarationKind.AkcssModule,
            name: name ?? string.Empty,
            arity: 0,
            modifiers: DeclarationModifiers.None,
            declFlags: SingleTypeDeclaration.TypeDeclarationFlags.None,
            syntax: syntax,
            nameLocation: new SourceLocation(syntax),
            memberNames: GetAkcssMemberNames(syntax, members),
            children: children.ToImmutableAndFree(),
            diagnostics: GetDiagnostics(syntax),
            quickAttributes: QuickAttributes.None);
    }

    private SingleTypeDeclaration CreateLeafDeclaration(
        DeclarationKind kind,
        string name,
        AkburaSyntax syntax)
    {
        return new SingleTypeDeclaration(
            kind,
            name ?? string.Empty,
            arity: 0,
            modifiers: DeclarationModifiers.None,
            declFlags: SingleTypeDeclaration.TypeDeclarationFlags.None,
            syntax: syntax,
            nameLocation: new SourceLocation(syntax),
            memberNames: s_emptyMemberNames,
            children: ImmutableArray<SingleTypeDeclaration>.Empty,
            diagnostics: GetDiagnostics(syntax),
            quickAttributes: QuickAttributes.None);
    }

    private static SingleNamespaceOrTypeDeclaration WrapInNamespaces(
        SingleNamespaceOrTypeDeclaration declaration,
        string namespaceName,
        AkburaDocumentSyntax root)
    {
        if (string.IsNullOrWhiteSpace(namespaceName))
        {
            return declaration;
        }

        var parts = namespaceName.Split('.');
        var current = declaration;
        for (var index = parts.Length - 1; index >= 0; index--)
        {
            var name = parts[index].Trim();
            if (name.Length == 0)
            {
                continue;
            }

            current = SingleNamespaceDeclaration.Create(
                name,
                hasUsings: false,
                hasExternAliases: false,
                root,
                new SourceLocation(root),
                ImmutableArray.Create(current),
                ImmutableArray<AkburaDiagnostic>.Empty);
        }

        return current;
    }

    private static string GetNamespaceName(AkburaDocumentSyntax root)
    {
        foreach (var member in root.Members)
        {
            if (member.Kind == AkburaSyntaxKind.NamespaceDeclarationSyntax)
            {
                return ((NamespaceDeclarationSyntax)member).Name.ToFullString().Trim();
            }
        }

        return string.Empty;
    }

    private static bool HasUsings(AkburaDocumentSyntax root)
    {
        foreach (var member in root.Members)
        {
            if (member.Kind == AkburaSyntaxKind.UsingDirectiveSyntax)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasGlobalUsings(AkburaDocumentSyntax root)
    {
        foreach (var member in root.Members)
        {
            if (member.Kind == AkburaSyntaxKind.UsingDirectiveSyntax &&
                ((UsingDirectiveSyntax)member).GlobalKeyword.RawKind != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAkcssUsings(AkcssDocumentSyntax root)
    {
        foreach (var member in root.Members)
        {
            if (member.Kind == AkburaSyntaxKind.AkcssUsingDirectiveSyntax)
            {
                return true;
            }
        }

        return false;
    }

    private BoxedMemberNames GetComponentMemberNames(AkburaDocumentSyntax root)
    {
        return GetOrComputeMemberNames(
            root,
            static (builder, root) =>
            {
                foreach (var member in root.Members)
                {
                    AddMemberName(builder, GetComponentMemberName(member));
                }
            },
            root);
    }

    private static string GetComponentMemberName(AkTopLevelMemberSyntax member)
    {
        return member.Kind switch
        {
            AkburaSyntaxKind.StateDeclarationSyntax => ((StateDeclarationSyntax)member).Name.ToFullString().Trim(),
            AkburaSyntaxKind.ParamDeclarationSyntax => ((ParamDeclarationSyntax)member).Name.ToFullString().Trim(),
            AkburaSyntaxKind.InjectDeclarationSyntax => ((InjectDeclarationSyntax)member).Name.ToFullString().Trim(),
            AkburaSyntaxKind.CommandDeclarationSyntax => ((CommandDeclarationSyntax)member).Name.ToFullString().Trim(),
            AkburaSyntaxKind.UserHook => ((UserHookSyntax)member).Name.ToFullString().Trim(),
            AkburaSyntaxKind.InlineAkcssBlockSyntax => "@akcss",
            _ => string.Empty,
        };
    }

    private BoxedMemberNames GetAkcssMemberNames(
        AkburaSyntax syntax,
        SyntaxList<AkcssTopLevelMemberSyntax> members)
    {
        return GetOrComputeMemberNames(
            syntax,
            static (builder, members) =>
            {
                foreach (var member in members)
                {
                    if (member.Kind == AkburaSyntaxKind.AkcssStyleRuleSyntax)
                    {
                        AddMemberName(
                            builder,
                            ((AkcssStyleRuleSyntax)member).Selector.ToFullString().Trim());
                    }
                    else if (member.Kind == AkburaSyntaxKind.AkcssUtilitiesSectionSyntax)
                    {
                        foreach (var utility in ((AkcssUtilitiesSectionSyntax)member).Utilities)
                        {
                            AddMemberName(
                                builder,
                                utility.Selector.ToFullString().Trim());
                        }
                    }
                }
            },
            members);
    }

    private BoxedMemberNames GetOrComputeMemberNames<TData>(
        AkburaSyntax syntax,
        Action<ImmutableSegmentedHashSet<string>.Builder, TData> addMemberNames,
        TData data)
    {
        var typeIndex = _currentTypeIndex++;
        if (s_nodeToMemberNames.TryGetValue(syntax.Green, out var memberNames))
        {
            return memberNames;
        }

        var builder = ImmutableSegmentedHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        addMemberNames(builder, data);
        var value = builder.ToImmutable();

        if (value.Count == 0)
        {
            return s_emptyMemberNames;
        }

        if (TryGetPreviousMemberNames(typeIndex, value, out var previousMemberNames))
        {
            return s_nodeToMemberNames.GetValue(
                syntax.Green,
                _ => previousMemberNames);
        }

        var computedMemberNames = new BoxedMemberNames(value);
        return s_nodeToMemberNames.GetValue(
            syntax.Green,
            _ => computedMemberNames);
    }

    private bool TryGetPreviousMemberNames(
        int index,
        ImmutableSegmentedHashSet<string> value,
        out BoxedMemberNames memberNames)
    {
        if (index < _previousMemberNames.Count &&
            _previousMemberNames[index].TryGetTarget(out var candidate) &&
            MemberNamesEqual(candidate.Value, value))
        {
            memberNames = candidate;
            return true;
        }

        memberNames = null!;
        return false;
    }

    private static bool MemberNamesEqual(
        ImmutableSegmentedHashSet<string> left,
        ImmutableSegmentedHashSet<string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var name in right)
        {
            if (!left.Contains(name))
            {
                return false;
            }
        }

        return true;
    }

    private static void AddMemberName(
        ImmutableSegmentedHashSet<string>.Builder builder,
        string name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            builder.Add(name);
        }
    }

    private static ImmutableArray<AkburaDiagnostic> GetDiagnostics(AkburaSyntax syntax)
    {
        return syntax.GetDiagnostics();
    }
}
