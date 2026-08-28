using Akbura.Language.Symbols;
using Microsoft.CodeAnalysis;
using RoslynOperationKind = Microsoft.CodeAnalysis.OperationKind;

namespace Akbura.Language.Operations;

internal interface ICSharpOperation : IOperation
{
    RoslynOperationKind RoslynKind { get; }

    SyntaxNode CSharpSyntax { get; }

    CSharpSymbolDefinition CSharpTargetDefinition { get; }

    CSharpSymbolDefinition CSharpTypeDefinition { get; }
}
