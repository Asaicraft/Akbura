using Akbura.Language.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Akbura;
internal sealed class SyntaxDiagnosticInfo : AkburaDiagnostic
{
    public SyntaxDiagnosticInfo()
    {

    }

    [SetsRequiredMembers]
    public SyntaxDiagnosticInfo(int position, int width, string code)
        : base([], code, AkburaDiagnosticSeverity.Error)
    {
        Position = position;
        Width = width;
    }


    [SetsRequiredMembers]
    public SyntaxDiagnosticInfo(int position, int width, string code, ImmutableArray<object?> parameters, AkburaDiagnosticSeverity severity)
        : base(parameters, code, severity)
    {
        Position = position;
        Width = width;
    }

    [SetsRequiredMembers]
    public SyntaxDiagnosticInfo(int position, int width, string code, ImmutableArray<object?> parameters)
        : base(parameters, code, AkburaDiagnosticSeverity.Error)
    {
        Position = position;
        Width = width;
    }


    public int Position
    {
        get; init;
    }

    public int Width
    {
        get; init;
    }
}
