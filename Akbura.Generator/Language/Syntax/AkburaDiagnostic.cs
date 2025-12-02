// This file is ported and adopted from KirillOsenkov/XmlParser

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Akbura.Language.Syntax;

/// <summary>
/// Describes how severe a diagnostic is.
/// </summary>
public enum AkburaDiagnosticSeverity
{
    /// <summary>
    /// Something that is an issue, as determined by some authority,
    /// but is not surfaced through normal means.
    /// There may be different mechanisms that act on these issues.
    /// </summary>
    Hidden = 0,

    /// <summary>
    /// Information that does not indicate a problem (i.e. not prescriptive).
    /// </summary>
    Info = 1,

    /// <summary>
    /// Something suspicious but allowed.
    /// </summary>
    Warning = 2,

    /// <summary>
    /// Something not allowed by the rules of the language or other authority.
    /// </summary>
    Error = 3,
}

public class AkburaDiagnostic
{
    public AkburaDiagnostic()
    {

    }

    [SetsRequiredMembers]
    public AkburaDiagnostic(ImmutableArray<object?> parameters, string code, AkburaDiagnosticSeverity severity)
    {
        Parameters = parameters;
        Code = code;
        Severity = severity;
    }

    public required ImmutableArray<object?> Parameters
    {
        get; init;
    }

    public string Message
    {
        get
        {
            var message = AkburaResources.ResourceManager.GetString(Code);

            if(string.IsNullOrWhiteSpace(message))
            {
                return Code;
            }

            return string.Format(message, Parameters.ToArrayUnsafe());
        }
    }

    public required string Code
    {
        get; init;
    }

    public required AkburaDiagnosticSeverity Severity
    {
        get; init;
    }
}
