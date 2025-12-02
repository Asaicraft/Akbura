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
}
