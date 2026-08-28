using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using RoslynSymbol = Microsoft.CodeAnalysis.ISymbol;
using NativeSymbol = Akbura.Language.Symbols.ISymbol;
using NativeSymbolKind = Akbura.Language.Symbols.SymbolKind;

namespace Akbura.Workspaces.References;

internal static class AkburaSymbolKeyFactory
{
    public static AkburaSymbolKey Create(
        AkburaDocumentContext context,
        NativeSymbol symbol)
    {
        if (symbol is IMarkupComponentSymbol
            {
                AkburaComponent: { } component,
            })
        {
            symbol = component;
        }
        else if (symbol is Akbura.Language.Symbols.IPropertySymbol
            {
                Parameter: { } parameter,
            })
        {
            symbol = parameter;
        }
        else if (symbol is Akbura.Language.Symbols.IPropertySymbol
            {
                Command: { } command,
            })
        {
            symbol = command;
        }

        if (ShouldUseCSharpIdentity(symbol) &&
            symbol.CSharpDefinition.Symbol is { } csharpSymbol)
        {
            return Create(context, csharpSymbol);
        }

        var projectId = FindOwningProject(
            context,
            GetDeclarationSyntax(symbol));
        var original = symbol.OriginalDefinition;
        var containing = GetContainingIdentity(
            original.ContainingSymbol);

        return new AkburaSymbolKey(
            projectId,
            original.MetadataName,
            MapKind(original.Kind),
            containing);
    }

    public static AkburaSymbolKey Create(
        AkburaDocumentContext context,
        RoslynSymbol symbol)
    {
        var original = symbol.OriginalDefinition;
        var declarationId =
            DocumentationCommentId.CreateDeclarationId(original);
        var assembly = original.ContainingAssembly?
            .Identity.ToString() ?? string.Empty;
        var metadataName = declarationId;

        if (string.IsNullOrWhiteSpace(metadataName))
        {
            var sourceLocation = original.Locations
                .FirstOrDefault(static location =>
                    location.IsInSource);
            var sourceIdentity = sourceLocation == null
                ? string.Empty
                : string.Concat(
                    sourceLocation.SourceTree?.FilePath,
                    ":",
                    sourceLocation.SourceSpan.Start.ToString(),
                    ":",
                    sourceLocation.SourceSpan.Length.ToString());

            metadataName = string.Concat(
                original.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat),
                "|",
                original.MetadataName,
                "|",
                sourceIdentity);

            if (original.Kind is
                    Microsoft.CodeAnalysis.SymbolKind.Local or
                    Microsoft.CodeAnalysis.SymbolKind.Parameter or
                    Microsoft.CodeAnalysis.SymbolKind.RangeVariable)
            {
                metadataName = string.Concat(
                    context.Document.Uri.AbsoluteUri,
                    "|",
                    metadataName);
            }
        }

        return new AkburaSymbolKey(
            FindOwningProject(context, original),
            string.Concat(assembly, "|", metadataName),
            AkburaSymbolKind.CSharpSymbol,
            original.ContainingSymbol?.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat));
    }

    private static bool ShouldUseCSharpIdentity(
        NativeSymbol symbol)
    {
        return symbol.Kind is
                NativeSymbolKind.Property or
                NativeSymbolKind.Event or
                NativeSymbolKind.MarkupComponent &&
            symbol is not IAkburaComponentSymbol &&
            symbol.CSharpDefinition.Symbol != null;
    }

    private static AkburaProjectId FindOwningProject(
        AkburaDocumentContext context,
        RoslynSymbol symbol)
    {
        var assembly = symbol.ContainingAssembly;
        if (assembly != null)
        {
            foreach (var project in context.Solution.Projects.Values)
            {
                if (SymbolEqualityComparer.Default.Equals(
                        project.CSharpCompilation.Assembly,
                        assembly))
                {
                    return project.Id;
                }
            }
        }

        return context.Project.Id;
    }

    private static AkburaProjectId FindOwningProject(
        AkburaDocumentContext context,
        AkburaSyntax? declaration)
    {
        if (declaration == null)
        {
            return context.Project.Id;
        }

        var rootGreen = declaration.Root.Green;
        foreach (var project in context.Solution.Projects.Values)
        {
            foreach (var document in project.Documents.Values)
            {
                if (ReferenceEquals(
                        document.SyntaxTree
                            .GetRootSyntax().Green,
                        rootGreen))
                {
                    return project.Id;
                }
            }
        }

        return context.Project.Id;
    }

    private static AkburaSyntax? GetDeclarationSyntax(
        NativeSymbol symbol)
    {
        return symbol switch
        {
            IAkburaComponentSymbol component =>
                component.DeclarationSyntax,
            IStateSymbol state =>
                state.DeclarationSyntax,
            IParamSymbol parameter =>
                parameter.DeclarationSyntax,
            IInjectSymbol injected =>
                injected.DeclarationSyntax,
            ICommandSymbol command =>
                command.DeclarationSyntax,
            IMarkupItemSymbol item =>
                item.DeclarationSyntax,
            IMarkupNameSymbol name =>
                name.DeclarationSyntax,
            ITailwindUtilityParameterSymbol parameter =>
                parameter.DeclarationSyntax,
            IAkcssSymbol akcss =>
                akcss.DeclarationSyntax,
            IAkcssModuleSymbol module =>
                module.DeclaringSyntax,
            _ => null,
        };
    }

    private static string? GetContainingIdentity(
        NativeSymbol? symbol)
    {
        if (symbol == null)
        {
            return null;
        }

        return symbol.ContainingSymbol == null
            ? symbol.MetadataName
            : string.Concat(
                GetContainingIdentity(symbol.ContainingSymbol),
                ".",
                symbol.MetadataName);
    }

    private static AkburaSymbolKind MapKind(
        NativeSymbolKind kind)
    {
        return kind switch
        {
            NativeSymbolKind.Namespace =>
                AkburaSymbolKind.Namespace,
            NativeSymbolKind.Component or
            NativeSymbolKind.AkburaComponent or
            NativeSymbolKind.MarkupComponent =>
                AkburaSymbolKind.Component,
            NativeSymbolKind.Property =>
                AkburaSymbolKind.Property,
            NativeSymbolKind.Event =>
                AkburaSymbolKind.Event,
            NativeSymbolKind.State =>
                AkburaSymbolKind.State,
            NativeSymbolKind.Parameter =>
                AkburaSymbolKind.Parameter,
            NativeSymbolKind.CommandParameter =>
                AkburaSymbolKind.CommandParameter,
            NativeSymbolKind.TailwindUtilityParameter =>
                AkburaSymbolKind.UtilityParameter,
            NativeSymbolKind.MarkupItem =>
                AkburaSymbolKind.MarkupItem,
            NativeSymbolKind.MarkupName =>
                AkburaSymbolKind.MarkupName,
            NativeSymbolKind.InjectedService =>
                AkburaSymbolKind.InjectedService,
            NativeSymbolKind.Command =>
                AkburaSymbolKind.Command,
            NativeSymbolKind.Function =>
                AkburaSymbolKind.Function,
            NativeSymbolKind.UseHook =>
                AkburaSymbolKind.Hook,
            NativeSymbolKind.AkcssModule =>
                AkburaSymbolKind.AkcssModule,
            NativeSymbolKind.AkcssClass =>
                AkburaSymbolKind.AkcssClass,
            NativeSymbolKind.AkcssUtility =>
                AkburaSymbolKind.AkcssUtility,
            _ => AkburaSymbolKind.None,
        };
    }
}
