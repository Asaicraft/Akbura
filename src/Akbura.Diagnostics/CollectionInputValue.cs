using System.Collections;
using System.Reflection;

namespace Akbura.Diagnostics;

internal static class CollectionInputValue
{
    public static bool CanEdit(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return type != typeof(string) &&
            !IsDictionary(type) &&
            !IsSet(type) &&
            TryGetElementType(type, out _);
    }

    public static IReadOnlyList<CollectionInputItem> CreateItems(
        object? collection,
        InputRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (collection is not IEnumerable values ||
            !TryGetElementType(request.EditorType, out var elementType))
        {
            return [];
        }

        var items = new List<CollectionInputItem>();
        foreach (var value in values)
        {
            var index = items.Count;
            items.Add(new CollectionInputItem
            {
                Index = index,
                Value = value,
                Request = request with
                {
                    RequestedType = elementType,
                    ExistingValue = value,
                    MemberName = $"{request.MemberName ?? "Collection"}[{index}]",
                },
            });
        }

        return items;
    }

    public static object ReplaceItem(
        object? collection,
        Type collectionType,
        int index,
        object? replacement)
    {
        ArgumentNullException.ThrowIfNull(collectionType);

        if (collection is not IEnumerable values)
        {
            throw new InvalidOperationException(
                $"Collection '{collectionType}' has no value to edit.");
        }

        if (!TryGetElementType(collectionType, out var elementType))
        {
            throw new InvalidOperationException(
                $"Collection element type for '{collectionType}' could not be determined.");
        }

        var items = values.Cast<object?>().ToArray();
        if ((uint)index >= (uint)items.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                "Collection item no longer exists.");
        }

        items[index] = replacement;
        return MaterializeCollection(
            collectionType,
            elementType,
            items);
    }

    private static object MaterializeCollection(
        Type collectionType,
        Type elementType,
        object?[] items)
    {
        if (collectionType.IsArray)
        {
            var array = Array.CreateInstance(elementType, items.Length);
            for (var index = 0; index < items.Length; index++)
            {
                array.SetValue(items[index], index);
            }

            return array;
        }

        var listType = typeof(List<>).MakeGenericType(elementType);
        var typedList = (IList)(Activator.CreateInstance(listType)
            ?? throw new InvalidOperationException(
                $"Could not create '{listType}'."));
        foreach (var item in items)
        {
            typedList.Add(item);
        }

        if (collectionType.IsAssignableFrom(listType))
        {
            return typedList;
        }

        foreach (var constructor in collectionType.GetConstructors())
        {
            var parameters = constructor.GetParameters();
            if (parameters.Length == 1 &&
                parameters[0].ParameterType.IsAssignableFrom(listType))
            {
                return constructor.Invoke([typedList]);
            }
        }

        if (!collectionType.IsAbstract &&
            !collectionType.IsInterface &&
            collectionType.GetConstructor(Type.EmptyTypes) is ConstructorInfo constructorInfo &&
            constructorInfo.Invoke([]) is IList targetList)
        {
            foreach (var item in items)
            {
                targetList.Add(item);
            }

            return targetList;
        }

        var serialized = StateValueConverter.FormatForEditor(
            typedList,
            listType);
        if (StateValueConverter.TryParse(
                serialized,
                collectionType,
                out var converted,
                out var error) &&
            converted is not null)
        {
            return converted;
        }

        throw new InvalidOperationException(
            $"Collection '{collectionType}' cannot be reconstructed: {error}");
    }

    private static bool TryGetElementType(
        Type type,
        out Type elementType)
    {
        if (type.IsArray)
        {
            elementType = type.GetElementType()!;
            return true;
        }

        if (type == typeof(IEnumerable) ||
            type == typeof(ICollection) ||
            type == typeof(IList))
        {
            elementType = typeof(object);
            return true;
        }

        foreach (var candidate in EnumerateTypeAndInterfaces(type))
        {
            if (!candidate.IsGenericType)
            {
                continue;
            }

            var definition = candidate.GetGenericTypeDefinition();
            if (definition == typeof(IList<>) ||
                definition == typeof(IReadOnlyList<>) ||
                definition == typeof(ICollection<>) ||
                definition == typeof(IReadOnlyCollection<>) ||
                definition == typeof(IEnumerable<>))
            {
                elementType = candidate.GetGenericArguments()[0];
                return true;
            }
        }

        elementType = null!;
        return false;
    }

    private static bool IsDictionary(Type type)
    {
        if (typeof(IDictionary).IsAssignableFrom(type))
        {
            return true;
        }

        return EnumerateTypeAndInterfaces(type).Any(
            static candidate =>
                candidate.IsGenericType &&
                (candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>) ||
                 candidate.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>)));
    }

    private static bool IsSet(Type type)
    {
        return EnumerateTypeAndInterfaces(type).Any(
            static candidate =>
                candidate.IsGenericType &&
                (candidate.GetGenericTypeDefinition() == typeof(ISet<>) ||
                 candidate.GetGenericTypeDefinition() == typeof(IReadOnlySet<>)));
    }

    private static IEnumerable<Type> EnumerateTypeAndInterfaces(Type type)
    {
        yield return type;
        foreach (var interfaceType in type.GetInterfaces())
        {
            yield return interfaceType;
        }
    }
}
