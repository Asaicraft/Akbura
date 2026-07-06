using Akbura.Language;
using Akbura.Collections;
using Akbura.Language.Binder;
using Akbura.Language.BoundTree;
using Akbura.Language.Declarations;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using AkburaOperation = Akbura.Language.Operations.IOperation;
using AkburaOperationKind = Akbura.Language.Operations.OperationKind;
using AkburaCandidateReason = Akbura.Language.Symbols.CandidateReason;
using AkburaPropertySymbol = Akbura.Language.Symbols.IPropertySymbol;
using AkburaSymbol = Akbura.Language.Symbols.ISymbol;
using AkburaSymbolKind = Akbura.Language.Symbols.SymbolKind;
using AkburaSymbolVisitor = Akbura.Language.Symbols.SymbolVisitor;
using AkburaSyntaxKind = Akbura.Language.Syntax.SyntaxKind;
using BinderType = Akbura.Language.Binder.Binder;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

namespace Akbura.UnitTests;

public abstract class SemanticArchitectureTestBase
{
    private protected static AkburaCompilation CreateCompilation(
        AkburaSyntaxTree tree,
        ImmutableArray<AkcssSyntaxTree> akcssTrees = default)
    {
        return new AkburaCompilation(
            CreateCSharpCompilation(),
            [tree],
            akcssTrees.IsDefault ? ImmutableArray<AkcssSyntaxTree>.Empty : akcssTrees,
            rootNamespace: "Demo",
            projectDirectory: Environment.CurrentDirectory);
    }

    private protected static CSharpCompilation CreateCSharpCompilation()
    {
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Avalonia.Controls.Button).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Avalonia.Media.Color).Assembly.Location),
        };

        return CSharpCompilation.Create(
            "SemanticArchitectureTests",
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private protected static string ReadRepositoryFile(params string[] pathParts)
    {
        return File.ReadAllText(GetRepositoryPath(pathParts));
    }

    private protected static string GetRepositoryPath(params string[] pathParts)
    {
        var parts = new string[pathParts.Length + 5];
        parts[0] = AppContext.BaseDirectory;
        parts[1] = "..";
        parts[2] = "..";
        parts[3] = "..";
        parts[4] = "..";
        Array.Copy(pathParts, 0, parts, 5, pathParts.Length);

        return Path.GetFullPath(Path.Combine(parts));
    }

    private protected static Microsoft.CodeAnalysis.CSharp.Syntax.CompilationUnitSyntax CreateReturnExpressionProbe(
        Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax expression)
    {
        var returnStatement = CSharpSyntaxFactory.ReturnStatement(expression);
        var method = CSharpSyntaxFactory.MethodDeclaration(
                CSharpSyntaxFactory.PredefinedType(
                    CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ObjectKeyword)),
                "__AkburaSemanticProbe")
            .WithBody(CSharpSyntaxFactory.Block(returnStatement));
        var probeClass = CSharpSyntaxFactory.ClassDeclaration("__AkburaSemanticProbe")
            .WithMembers(CSharpSyntaxFactory.SingletonList<Microsoft.CodeAnalysis.CSharp.Syntax.MemberDeclarationSyntax>(method));

        return CSharpSyntaxFactory.CompilationUnit()
            .WithMembers(CSharpSyntaxFactory.SingletonList<Microsoft.CodeAnalysis.CSharp.Syntax.MemberDeclarationSyntax>(probeClass));
    }

}
