using Akbura.Language.CodeGeneration;
using System;
using System.Collections.Immutable;

namespace Akbura.BlackSilence;

/// <summary>
/// Exact generation inputs, compared before any semantic binding or emission.
/// Hashes are deliberately not used as evidence that two inputs are equivalent.
/// </summary>
internal sealed class DocumentGenerationVersion : IEquatable<DocumentGenerationVersion>
{
    private const int GeneratorSchemaVersion = 1;
    private readonly int _schemaVersion = GeneratorSchemaVersion;
    private readonly object _environment;
    private readonly GeneratorProjectOptions _options;
    private readonly ImmutableArray<DependencyGenerationVersion> _dependencies;

    public DocumentGenerationVersion(
        DocumentSyntaxVersion document,
        object environment,
        GeneratorProjectOptions options,
        ImmutableArray<DependencyGenerationVersion> dependencies)
    {
        Document = document;
        _environment = environment;
        _options = options;
        _dependencies = dependencies;
    }

    public DocumentSyntaxVersion Document { get; }

    public bool Equals(DocumentGenerationVersion? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other == null || _schemaVersion != other._schemaVersion ||
            !ReferenceEquals(_environment, other._environment) ||
            _options != other._options || !Document.HasSameGenerationShape(other.Document) ||
            _dependencies.Length != other._dependencies.Length)
        {
            return false;
        }

        for (var i = 0; i < _dependencies.Length; i++)
        {
            if (!_dependencies[i].Equals(other._dependencies[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is DocumentGenerationVersion other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_schemaVersion, Document.FilePath);
}

internal readonly struct DependencyGenerationVersion : IEquatable<DependencyGenerationVersion>
{
    public DependencyGenerationVersion(string identity, DocumentSyntaxVersion document, bool includesBody)
    {
        Identity = identity;
        Document = document;
        IncludesBody = includesBody;
    }

    public string Identity { get; }
    public DocumentSyntaxVersion Document { get; }
    public bool IncludesBody { get; }

    public bool Equals(DependencyGenerationVersion other)
    {
        return string.Equals(Identity, other.Identity, StringComparison.Ordinal) &&
            IncludesBody == other.IncludesBody &&
            (IncludesBody
                ? Document.HasSameGenerationShape(other.Document)
                : Document.HasSameSurface(other.Document));
    }

    public override bool Equals(object? obj) => obj is DependencyGenerationVersion other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Identity);
}
