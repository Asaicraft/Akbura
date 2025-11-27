using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Akbura.Language.Syntax;

namespace Akbura;
internal static class ThrowHelper
{
    [DoesNotReturn]
    public static void ThrowNotSupportedException()
    {
        throw new NotSupportedException();
    }

    [DoesNotReturn]
    public static T ThrowNotSupportedException<T>()
    {
        throw new NotSupportedException();
    }

    [DoesNotReturn]
    public static void ThrowUnreachableException()
    {
        throw new UnreachableException();
    }

    [DoesNotReturn]
    public static T ThrowUnreachableException<T>()
    {
        throw new UnreachableException();
    }

    [DoesNotReturn]
    public static void ThrowIndexOutOfRangeException()
    {
        throw new IndexOutOfRangeException();
    }

    [DoesNotReturn]
    public static void ThrowIndexOutOfRangeException(string message)
    {
        throw new IndexOutOfRangeException(message);
    }

    [DoesNotReturn]
    public static T ThrowIndexOutOfRangeException<T>()
    {
        throw new IndexOutOfRangeException();
    }

    [DoesNotReturn]
    public static T ThrowIndexOutOfRangeException<T>(string message)
    {
        throw new IndexOutOfRangeException(message);
    }

    [DoesNotReturn]
    public static void UnexpectedValue(object o)
    {
        var output = string.Format("Unexpected value '{0}' of type '{1}'", o, (o != null) ? o.GetType().FullName : "<unknown>");
        Debug.Fail(output);
        throw new InvalidOperationException(output);
    }

    [DoesNotReturn]
    public static T UnexpectedValue<T>(object o)
    {
        var output = string.Format("Unexpected value '{0}' of type '{1}'", o, (o != null) ? o.GetType().FullName : "<unknown>");
        Debug.Fail(output);
        throw new InvalidOperationException(output);
    }

    [DoesNotReturn]
    public static void ArgumentOutRangeStartMustNotBeNegative()
    {
        throw new ArgumentOutOfRangeException("start", "'start' must not be negative");
    }

    [DoesNotReturn]
    public static void ArgumentOutRangeEndMustNotBeLessThanStart(object start, object end)
    {
        var message = string.Format("'end' must not be less than 'start'. start='{0}' end='{1}'.", start, end);

        throw new ArgumentOutOfRangeException(nameof(end), message);
    }

    [DoesNotReturn]
    public static void EndOfStreamException()
    {
        throw new EndOfStreamException("Stream is too long.");
    }

    [DoesNotReturn]
    public static void IOExceptionStreamIsTooLong()
    {
        throw new IOException("Stream is too long.");
    }

    [DoesNotReturn]
    public static T IOExceptionStreamIsTooLong<T>()
    {
        throw new IOException("Stream is too long.");
    }

    [DoesNotReturn]
    public static void InvalidHash(string paramName)
    {
        throw new ArgumentException("Invalid hash.", paramName);
    }

    [DoesNotReturn]
    public static void UnsupportedHashAlgorithm(string paramName)
    {
        throw new ArgumentException("Unsupported hash algorithm.", paramName);
    }

    [DoesNotReturn]
    public static void StreamMustSupportReadAndSeek(string paramName)
    {
        throw new ArgumentException("Stream must support read and seek operations.", paramName);
    }

    [DoesNotReturn]
    public static void FixedSizeCollection()
    {
        throw new NotSupportedException("Collection is fixed size.");
    }

    [DoesNotReturn]
    public static T FixedSizeCollection<T>()
    {
        throw new NotSupportedException("Collection is fixed size.");
    }

    [DoesNotReturn]
    public static void OtherNotArrayOfCorrectLength(string paramName)
    {
        throw new ArgumentException("Object is not a array with the same number of elements as the array to compare it to.", paramName: paramName);
    }

    [DoesNotReturn]
    public static void LongerThanSrcArray(string paramName)
    {
        throw new ArgumentException("Source array was not long enough. Check the source index, length, and the array's lower bounds.", paramName: paramName);
    }

    [DoesNotReturn]
    public static void LongerThanDestArray(string paramName)
    {
        throw new ArgumentException("Destination array was not long enough. Check the destination index, length, and the array's lower bounds.", paramName: paramName);
    }

    [DoesNotReturn]
    public static void NeedNonNegNum(string paramName)
    {
        throw new ArgumentOutOfRangeException(paramName, "Non-negative number required.");
    }

    [DoesNotReturn]
    public static void ArrayLB(string paramName)
    {
        throw new ArgumentOutOfRangeException(paramName, "Number was less than the array's lower bound in the first dimension.");
    }

    [DoesNotReturn]
    public static void RankMustMatch()
    {
        throw new RankException("The specified arrays must have the same number of dimensions.");
    }

    [DoesNotReturn]
    public static void InvalidOffLen(string? paramName = null)
    {
        throw new ArgumentException("Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.", paramName);
    }

    [DoesNotReturn]
    public static void IndexMustBeLessOrEqual()
    {
        throw new ArgumentOutOfRangeException("Index was out of range. Must be non-negative and less than or equal to the size of the collection.");
    }

    [DoesNotReturn]
    public static void CountOutOfRange()
    {
        throw new ArgumentOutOfRangeException("Count must be positive and count must refer to a location within the string/array/collection.");
    }

    [DoesNotReturn]
    public static void IndexMustBeLess(string? paramName = null)
    {
        throw new ArgumentOutOfRangeException(paramName, "Index was out of range. Must be non-negative and less than the size of the collection.");
    }

    [DoesNotReturn]
    public static void WrongValueType(object? value, Type targetType)
    {
        throw new ArgumentException($"The value \"{value}\" is not of type \"{targetType}\" and cannot be used in this generic collection.");
    }

    // Allow nulls for reference types and Nullable<U>, but not for value types.
    // Aggressively inline so the jit evaluates the if in place and either drops the call altogether
    // Or just leaves null test and call to the Non-returning ThrowHelper.ThrowArgumentNullException
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IfNullAndNullsAreIllegalThenThrow<T>(object? value, string argName)
    {
        // Note that default(T) is not equal to null for value types except when T is Nullable<U>.
        if (!(default(T) == null) && value == null)
        {
            throw new ArgumentNullException(argName);
        }
    }

    [DoesNotReturn]
    public static void RankMultiDimNotSupported()
    {
        throw new RankException("Only single dimensional arrays are supported for the requested action.");
    }

    [DoesNotReturn]
    public static void IncompatibleArrayType()
    {
        throw new ArgumentException("Target array type is not compatible with the type of items in the collection.");
    }

    [DoesNotReturn]
    public static void EnumFailedVersion()
    {
        throw new InvalidOperationException("Collection was modified; enumeration operation may not execute.");
    }

    [DoesNotReturn]
    public static void ListInsertOutOfRange(string paramName)
    {
        throw new ArgumentOutOfRangeException(paramName, "Index must be within the bounds of the List.");
    }

    [DoesNotReturn]
    public static void BiggerThanCollection(string paramName)
    {
        throw new ArgumentOutOfRangeException(paramName, "Larger than collection size.");
    }

    [DoesNotReturn]
    public static void EnumOpCantHappen()
    {
        throw new InvalidOperationException("Enumeration has either not started or has already finished.");
    }

    [DoesNotReturn]
    public static void BadComparer(object? comparer)
    {
        throw new ArgumentException($"Unable to sort because the IComparer.Compare() method returns inconsistent results. Either a value does not compare equal to itself, or one value repeatedly compared to another value yields different results. IComparer: '{comparer}'.", nameof(comparer));
    }

    [DoesNotReturn]
    public static void IComparerFailed(Exception e)
    {
        throw new InvalidOperationException("Failed to compare two elements in the array.", e);
    }

    [DoesNotReturn]
    public static void ThrowNullReferenceException()
    {
        throw new NullReferenceException();
    }

    [DoesNotReturn]
    public static void SpanDoesNotIncludeEndOfLine(string paramName)
    {
        throw new ArgumentOutOfRangeException(paramName, "The span does not include the end of a line.");
    }

    [DoesNotReturn]
    public static void LineCannotBeGreaterThanEnd(string paramName, int line, int count)
    {
        throw new ArgumentOutOfRangeException(paramName, $"The requested line number {line} must be less than the number of lines {count}.");
    }

    [DoesNotReturn]
    public static void SourceTextCannotBeEmbedded(string paramName)
    {
        throw new ArgumentException("SourceText cannot be embedded. Provide encoding or canBeEmbedded=true at construction.", paramName);
    }

    [DoesNotReturn]
    public static void ArgumentCannotBeEmpty(string paramName)
    {
        throw new ArgumentException("Argument cannot be empty.", paramName);
    }

    [DoesNotReturn]
    public static void ChangesMustBeWithinBoundsOfSourceText(string paramName)
    {
        throw new ArgumentOutOfRangeException(paramName, "Changes must be within the bounds of the source text.");
    }

    [DoesNotReturn]
    public static void ChangesMustNotOverlap(string paramName)
    {
        throw new ArgumentException("The changes must not overlap.", paramName);
    }

    [DoesNotReturn]
    public static void ReferenceResolverShouldReturnReadableNonNullStream()
    {
        throw new InvalidOperationException("Reference resolver should return readable non-null stream.");
    }

    [DoesNotReturn]
    public static void SeparatorIsExpected()
    {
        throw new InvalidOperationException("Separator is expected.");
    }

    [DoesNotReturn]
    public static void ElementIsExpected()
    {
        throw new InvalidOperationException("Element is expected.");
    }

    [DoesNotReturn]
    public static void ThisMethodCanOnlyBeUsedToCreateTokens(object kind, string paramName)
    {
        throw new ArgumentException($"This method can only be used to create tokens - {kind} is not a token kind.", paramName);
    }

    [DoesNotReturn]
    public static void MissingListItem()
    {
        throw new InvalidOperationException("The item specified is not the element of a list.");
    }

    [DoesNotReturn]
    public static T ThrowArgumentException<T>(string message)
    {
        throw new ArgumentException(message);
    }

    [DoesNotReturn]
    public static T ThrowArgumentOutOfRangeException<T>(string paramName)
    {
        throw new ArgumentOutOfRangeException(paramName);
    }

    [DoesNotReturn]
    public static void ThrowArgumentOutOfRangeException(string? message, string? paramName)
    {
        throw new ArgumentOutOfRangeException(paramName, message);
    }

    [DoesNotReturn]
    public static void ThrowArgumentOutOfRangeException(string paramName)
    {
        throw new ArgumentOutOfRangeException(paramName);
    }

    [DoesNotReturn]
    public static void ThrowArgumentException(string paramName, string message)
    {
        throw new ArgumentException(message, paramName);
    }

    [DoesNotReturn]
    public static void ThrowArgumentNullException(string paramName)
    {
        throw new ArgumentNullException(paramName);
    }

    public static void ThrowIfNull([NotNull] object? value, [CallerArgumentExpression(nameof(value))] string? paramName = null!)
    {
        if (value is null)
        {
            throw new ArgumentNullException(paramName, nameof(value));
        }
    }

    public static void ThrowIfNegative(int value, [CallerArgumentExpression(nameof(value))] string? paramName = null!)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(paramName, "Value must not be negative.");
        }
    }

    public static void ThrowIfGreaterThanOrEqual(uint value, uint max, [CallerArgumentExpression(nameof(value))] string? paramName = null!)
    {
        if (value >= max)
        {
            throw new ArgumentOutOfRangeException(paramName, $"Value must be less than {max}.");
        }
    }

    public static void ThrowIfLessThan(int value, int size, [CallerArgumentExpression(nameof(value))] string? paramName = null!)
    {
        if (value < size)
        {
            throw new ArgumentOutOfRangeException(paramName, $"Value must be greater than or equal to {size}.");
        }
    }
}