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
    public const string AKBURA_SEMANTIC_InvalidMarkupChild = nameof(AKBURA_SEMANTIC_InvalidMarkupChild);
    public const string AKBURA_SEMANTIC_AkcssInvalidColor = nameof(AKBURA_SEMANTIC_AkcssInvalidColor);
    public const string AKBURA_SEMANTIC_AkcssInvalidThickness = nameof(AKBURA_SEMANTIC_AkcssInvalidThickness);
    public const string AKBURA_SEMANTIC_AkcssPropertyNotFound = nameof(AKBURA_SEMANTIC_AkcssPropertyNotFound);
    public const string AKBURA_SEMANTIC_MarkupPropertyNotFound = nameof(AKBURA_SEMANTIC_MarkupPropertyNotFound);
    public const string AKBURA_SEMANTIC_MarkupAttributeValueCannotConvert = nameof(AKBURA_SEMANTIC_MarkupAttributeValueCannotConvert);
    public const string AKBURA_SEMANTIC_MarkupAttributeBindingNotAllowed = nameof(AKBURA_SEMANTIC_MarkupAttributeBindingNotAllowed);
    public const string AKBURA_SEMANTIC_MarkupDuplicatePropertySetter = nameof(AKBURA_SEMANTIC_MarkupDuplicatePropertySetter);
    public const string AKBURA_SEMANTIC_MarkupEventBindingNotAllowed = nameof(AKBURA_SEMANTIC_MarkupEventBindingNotAllowed);
    public const string AKBURA_SEMANTIC_MarkupEventHandlerSignatureMismatch = nameof(AKBURA_SEMANTIC_MarkupEventHandlerSignatureMismatch);
    public const string AKBURA_SEMANTIC_TailwindUtilityNotFound = nameof(AKBURA_SEMANTIC_TailwindUtilityNotFound);
    public const string AKBURA_SEMANTIC_TailwindUtilityAmbiguous = nameof(AKBURA_SEMANTIC_TailwindUtilityAmbiguous);
    public const string AKBURA_SEMANTIC_TailwindUtilityArgumentMismatch = nameof(AKBURA_SEMANTIC_TailwindUtilityArgumentMismatch);
    public const string AKBURA_SEMANTIC_AkcssImportNotFound = nameof(AKBURA_SEMANTIC_AkcssImportNotFound);
    public const string AKBURA_SEMANTIC_AkcssApplyItemNotFound = nameof(AKBURA_SEMANTIC_AkcssApplyItemNotFound);
    public const string AKBURA_SEMANTIC_AkcssApplyItemAmbiguous = nameof(AKBURA_SEMANTIC_AkcssApplyItemAmbiguous);
    public const string AKBURA_SEMANTIC_AkcssInterceptTypeNotFound = nameof(AKBURA_SEMANTIC_AkcssInterceptTypeNotFound);
    public const string AKBURA_SEMANTIC_AkcssInterceptTypeInvalid = nameof(AKBURA_SEMANTIC_AkcssInterceptTypeInvalid);
    public const string AKBURA_SEMANTIC_AkcssSelectorTargetNotFound = nameof(AKBURA_SEMANTIC_AkcssSelectorTargetNotFound);
    public const string AKBURA_SEMANTIC_StateBindingExpressionExpected = nameof(AKBURA_SEMANTIC_StateBindingExpressionExpected);
    public const string AKBURA_SEMANTIC_StateBindingSourceNotObservable = nameof(AKBURA_SEMANTIC_StateBindingSourceNotObservable);
    public const string AKBURA_SEMANTIC_StateBindingTargetNotWritable = nameof(AKBURA_SEMANTIC_StateBindingTargetNotWritable);
    public const string AKBURA_SEMANTIC_UserHookMustBeTopLevel = nameof(AKBURA_SEMANTIC_UserHookMustBeTopLevel);
    public const string WRN_ErrorOverride = nameof(WRN_ErrorOverride);
}
