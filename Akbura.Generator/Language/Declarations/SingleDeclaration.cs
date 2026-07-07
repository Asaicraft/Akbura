using Akbura.Language.Syntax;
using System;
using System.Collections.Immutable;

namespace Akbura.Language;

internal abstract class SingleDeclaration : Declaration
{
    private readonly AkburaSyntax _syntax;
    private readonly SourceLocation _nameLocation;

    /// <summary>
    /// Any diagnostics reported while converting syntax into the declaration instance.
    /// </summary>
    public readonly ImmutableArray<AkburaDiagnostic> Diagnostics;

    protected SingleDeclaration(
        string name,
        AkburaSyntax syntax,
        SourceLocation nameLocation,
        ImmutableArray<AkburaDiagnostic> diagnostics)
        : base(name ?? string.Empty)
    {
        _syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
        _nameLocation = nameLocation ?? throw new ArgumentNullException(nameof(nameLocation));
        Diagnostics = diagnostics.IsDefault
            ? ImmutableArray<AkburaDiagnostic>.Empty
            : diagnostics;
    }

    public SourceLocation Location
    {
        get
        {
            return new SourceLocation(Syntax);
        }
    }

    public AkburaSyntax Syntax => _syntax;

    public SourceLocation NameLocation
    {
        get
        {
            return _nameLocation;
        }
    }
}
