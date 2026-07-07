// This file is ported and adapted from the Roslyn (dotnet/roslyn)

#nullable disable

namespace Akbura.Language;

internal enum DeclarationKind : byte
{
    Namespace,
    Class,
    Interface,
    Struct,
    Enum,
    Delegate,
    Script,
    Submission,
    ImplicitClass,
    Record,
    RecordStruct,
    Extension,
    Union,

    Component,
    AkcssModule,
    AkcssStyle,
    AkcssUtility,
}
