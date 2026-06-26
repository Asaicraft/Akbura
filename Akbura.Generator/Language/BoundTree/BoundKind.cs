namespace Akbura.Language.BoundTree;

internal enum BoundKind : byte
{
    None = 0,
    Declaration,
    Block,
    BadStatement,
    LocalDeclarationStatement,
    Expression,
    CSharpExpression,
    ConversionExpression,
    LiteralExpression,
    BinaryExpression,
    CallExpression,
    BadExpression,
    ErrorExpression,
}
