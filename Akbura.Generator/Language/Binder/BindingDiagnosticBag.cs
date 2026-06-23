using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Akbura.Language.Binder;

internal sealed class BindingDiagnosticBag
{
    private List<AkburaSemanticDiagnostic>? _semanticDiagnostics;
    private List<Diagnostic>? _csharpDiagnostics;
    private HashSet<string>? _seen;

    public bool IsEmpty =>
        (_semanticDiagnostics == null || _semanticDiagnostics.Count == 0) &&
        (_csharpDiagnostics == null || _csharpDiagnostics.Count == 0);

    public void Add(AkburaSemanticDiagnostic diagnostic)
    {
        if (diagnostic == null)
        {
            throw new ArgumentNullException(nameof(diagnostic));
        }

        if (!MarkSeen(CreateSemanticKey(diagnostic)))
        {
            return;
        }

        (_semanticDiagnostics ??= new List<AkburaSemanticDiagnostic>()).Add(diagnostic);
    }

    public void AddRange(IEnumerable<AkburaSemanticDiagnostic> diagnostics)
    {
        if (diagnostics == null)
        {
            throw new ArgumentNullException(nameof(diagnostics));
        }

        foreach (var diagnostic in diagnostics)
        {
            Add(diagnostic);
        }
    }

    public void AddCSharp(Diagnostic diagnostic)
    {
        if (diagnostic == null)
        {
            throw new ArgumentNullException(nameof(diagnostic));
        }

        if (!MarkSeen(CreateCSharpKey(diagnostic)))
        {
            return;
        }

        (_csharpDiagnostics ??= new List<Diagnostic>()).Add(diagnostic);
    }

    public void AddCSharpRange(IEnumerable<Diagnostic> diagnostics)
    {
        if (diagnostics == null)
        {
            throw new ArgumentNullException(nameof(diagnostics));
        }

        foreach (var diagnostic in diagnostics)
        {
            AddCSharp(diagnostic);
        }
    }

    public ImmutableArray<AkburaSemanticDiagnostic> ToSemanticDiagnostics()
    {
        return _semanticDiagnostics == null || _semanticDiagnostics.Count == 0
            ? ImmutableArray<AkburaSemanticDiagnostic>.Empty
            : _semanticDiagnostics.ToImmutableArray();
    }

    public ImmutableArray<Diagnostic> ToCSharpDiagnostics()
    {
        return _csharpDiagnostics == null || _csharpDiagnostics.Count == 0
            ? ImmutableArray<Diagnostic>.Empty
            : _csharpDiagnostics.ToImmutableArray();
    }

    private bool MarkSeen(string key)
    {
        _seen ??= new HashSet<string>(StringComparer.Ordinal);
        return _seen.Add(key);
    }

    private static string CreateSemanticKey(AkburaSemanticDiagnostic diagnostic)
    {
        return string.Join(
            "|",
            "A",
            diagnostic.Code,
            diagnostic.Severity.ToString(),
            diagnostic.Syntax.FullSpan.ToString(),
            string.Join(",", diagnostic.Parameters.Select(parameter => parameter?.ToString() ?? "<null>")));
    }

    private static string CreateCSharpKey(Diagnostic diagnostic)
    {
        return string.Join(
            "|",
            "C",
            diagnostic.Id,
            diagnostic.Severity.ToString(),
            diagnostic.Location.SourceSpan.ToString(),
            diagnostic.GetMessage());
    }
}
