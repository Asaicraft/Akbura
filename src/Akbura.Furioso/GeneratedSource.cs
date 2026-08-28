using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Furioso;

internal readonly record struct GeneratedSource(
    string HintName,
    SourceText SourceText);