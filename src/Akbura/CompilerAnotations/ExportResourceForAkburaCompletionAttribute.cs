using System;

namespace Akbura.CompilerAnotations;

/// <summary>
/// Exports an Avalonia resource key for Akbura editor completion.
/// </summary>
/// <remarks>
/// This attribute only describes a resource. It does not load the resource or
/// make it available at runtime. Consumers must import the corresponding
/// resource dictionary.
/// </remarks>
[AttributeUsage(
    AttributeTargets.Assembly,
    AllowMultiple = true,
    Inherited = false)]
public sealed class ExportResourceForAkburaCompletionAttribute : Attribute
{
    /// <summary>
    /// Initializes a new resource export.
    /// </summary>
    /// <param name="dictionaryPath">
    /// The published dictionary path relative to the resource root of the
    /// assembly carrying this attribute.
    /// </param>
    /// <param name="key">The exact resource key.</param>
    /// <param name="resourceType">
    /// The declared type of the resource value or a compatible contract type.
    /// </param>
    public ExportResourceForAkburaCompletionAttribute(string dictionaryPath, string key, Type resourceType)
    {
        DictionaryPath = dictionaryPath ??
            throw new ArgumentNullException(nameof(dictionaryPath));
        Key = key ??
            throw new ArgumentNullException(nameof(key));
        ResourceType = resourceType ??
            throw new ArgumentNullException(nameof(resourceType));
    }

    /// <summary>
    /// Gets the published dictionary path relative to this assembly's resource
    /// root.
    /// </summary>
    public string DictionaryPath { get; }

    /// <summary>
    /// Gets the exact resource key.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Gets the declared resource value type or compatible contract type.
    /// </summary>
    public Type ResourceType { get; }
}
