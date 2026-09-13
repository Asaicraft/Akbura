using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System.Linq;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akbura.Language.Binder;

internal sealed partial class CSharpProbeBinder
{
    internal CSharpBindingResult BindMarkupCondition(CSharpExpressionSyntax syntax)
    {
        var tree = CreateSyntaxTree(new CSharpProbeBuilder(this)
            .CreateMarkupConditionProbe(syntax, CSharpProbeBuilder.ParseMarkupCondition(syntax)));
        var semantic = CreateSemanticModel(tree);
        var condition = tree.GetRoot().GetAnnotatedNodes(CSharpProbeBuilder.MarkupConditionAnnotationKind)
            .OfType<CSharp.ExpressionSyntax>().Single();
        return BindExpression(semantic, condition, isBindingPath: false);
    }
}
