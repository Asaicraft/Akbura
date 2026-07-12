namespace Akbura.Language.Symbols;

internal enum PropertyAccessKind : byte
{
    None,
    ClrProperty,
    AvaloniaProperty,
    AttachedAccessor,
    Parameter,
    Command,
}
