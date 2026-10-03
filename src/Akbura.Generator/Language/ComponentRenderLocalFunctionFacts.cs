using Akbura.Language.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Akbura.Language;

internal static class ComponentRenderLocalFunctionFacts
{
    public static HashSet<CSharpStatementSyntax> GetCapturedFunctions(
        AkburaDocumentSyntax document)
    {
        var localNames = new HashSet<string>(StringComparer.Ordinal);
        var functions = new Dictionary<string, CSharpStatementSyntax>(StringComparer.Ordinal);

        foreach (var member in document.Members)
        {
            if (member is not CSharpStatementSyntax syntax)
            {
                continue;
            }

            switch (syntax.GetRawCSharpStatement())
            {
                case CSharp.LocalDeclarationStatementSyntax declaration:
                    foreach (var variable in declaration.Declaration.Variables)
                    {
                        localNames.Add(variable.Identifier.ValueText);
                    }
                    break;

                case CSharp.LocalFunctionStatementSyntax function:
                    functions[function.Identifier.ValueText] = syntax;
                    break;
            }
        }

        var captured = new HashSet<CSharpStatementSyntax>();
        foreach (var syntax in functions.Values)
        {
            var function = CSharpSyntaxFactory.ParseStatement(syntax.ToFullString()) as
                CSharp.LocalFunctionStatementSyntax;
            if (function == null)
            {
                continue;
            }

            var shadowedNames = new HashSet<string>(
                function.ParameterList.Parameters.Select(static parameter => parameter.Identifier.ValueText),
                StringComparer.Ordinal);
            foreach (var declaration in function.DescendantNodes().OfType<CSharp.VariableDeclaratorSyntax>())
            {
                shadowedNames.Add(declaration.Identifier.ValueText);
            }

            foreach (var identifier in function.DescendantNodes().OfType<CSharp.IdentifierNameSyntax>())
            {
                if (localNames.Contains(identifier.Identifier.ValueText) &&
                    !shadowedNames.Contains(identifier.Identifier.ValueText))
                {
                    captured.Add(syntax);
                    break;
                }
            }
        }

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var syntax in functions.Values)
            {
                if (captured.Contains(syntax) ||
                    CSharpSyntaxFactory.ParseStatement(syntax.ToFullString()) is not
                        CSharp.LocalFunctionStatementSyntax function)
                {
                    continue;
                }

                foreach (var identifier in function.DescendantNodes().OfType<CSharp.IdentifierNameSyntax>())
                {
                    if (functions.TryGetValue(identifier.Identifier.ValueText, out var callee) &&
                        captured.Contains(callee))
                    {
                        captured.Add(syntax);
                        changed = true;
                        break;
                    }
                }
            }
        }

        return captured;
    }

    public static bool RequiresFreshClosure(
        AkburaDocumentSyntax document,
        string handlerExpression)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in document.Members)
        {
            if (member is CSharpStatementSyntax syntax &&
                syntax.GetRawCSharpStatement() is CSharp.LocalDeclarationStatementSyntax declaration)
            {
                foreach (var variable in declaration.Declaration.Variables)
                {
                    names.Add(variable.Identifier.ValueText);
                }
            }
        }

        if (names.Count == 0)
        {
            return false;
        }

        foreach (var function in GetCapturedFunctions(document))
        {
            if (function.GetRawCSharpStatement() is CSharp.LocalFunctionStatementSyntax declaration)
            {
                names.Add(declaration.Identifier.ValueText);
            }
        }

        var expression = CSharpSyntaxFactory.ParseExpression(handlerExpression);
        return expression.DescendantNodesAndSelf()
            .OfType<CSharp.IdentifierNameSyntax>()
            .Any(identifier => names.Contains(identifier.Identifier.ValueText));
    }
}
