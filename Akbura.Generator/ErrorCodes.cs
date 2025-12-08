using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura;
internal static class ErrorCodes
{
    public const string ERR_IllegalEscape = nameof(ERR_IllegalEscape);
    public const string ERR_OpenEndedComment = nameof(ERR_OpenEndedComment);
    public const string ERR_UnexpectedCharacter = nameof(ERR_UnexpectedCharacter);
    public const string ERR_InvalidReal = nameof(ERR_InvalidReal);
    public const string ERR_InvalidNumber = nameof(ERR_InvalidNumber);
    public const string ERR_InvalidNumber_WithoutPosition = nameof(ERR_InvalidNumber_WithoutPosition);
    public const string ERR_FloatOverflow = nameof(ERR_FloatOverflow);
    public const string ERR_IntOverflow = nameof(ERR_IntOverflow);
    public const string ERR_IdentifierExpected = nameof(ERR_IdentifierExpected);
    public const string ERR_IdentifierExpectedKW = nameof(ERR_IdentifierExpectedKW);
    public const string ERR_SemicolonExpected = nameof(ERR_SemicolonExpected);
    public const string ERR_CloseParenExpected = nameof(ERR_CloseParenExpected);
    public const string ERR_LbraceExpected = nameof(ERR_LbraceExpected);
    public const string ERR_RbraceExpected = nameof(ERR_RbraceExpected);
    public const string ERR_SyntaxError = nameof(ERR_SyntaxError);
}
