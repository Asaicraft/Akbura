using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Language.Operations;

internal interface IMarkupWhitespaceDirectiveOperation : IMarkupAttributeOperation
{
    new MarkupAttachedPropertyAttributeSyntax Syntax { get; }

    IPropertySymbol? Property { get; }

    string RawValue { get; }

    MarkupWhitespaceMode? DeclaredMode { get; }

    MarkupWhitespaceMode EffectiveMode { get; }
}