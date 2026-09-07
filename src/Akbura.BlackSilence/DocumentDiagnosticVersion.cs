using Akbura.Language.CodeGeneration;
using System;
using System.Collections.Immutable;

namespace Akbura.BlackSilence;

/// <summary>
/// Diagnostic invalidation is independent of the existence or contents of emitted C#.
/// Dependency surfaces are conservative; equality never relies on a hash alone.
/// </summary>
internal sealed class DocumentDiagnosticVersion : IEquatable<DocumentDiagnosticVersion>
{
    private const int DiagnosticSchemaVersion = 1;
    private readonly int _schemaVersion = DiagnosticSchemaVersion;
    private readonly object _environment;
    private readonly object _csharpEnvironment;
    private readonly GeneratorProjectOptions _options;
    private readonly ImmutableArray<DependencyGenerationVersion> _dependencies;

    public DocumentDiagnosticVersion(
        DocumentSyntaxVersion document,
        object environment,
        object csharpEnvironment,
        GeneratorProjectOptions options,
        ImmutableArray<DependencyGenerationVersion> dependencies)
    {
        Document = document;
        _environment = environment;
        _csharpEnvironment = csharpEnvironment;
        _options = options;
        _dependencies = dependencies;
    }

    public DocumentSyntaxVersion Document { get; }

    public bool Equals(DocumentDiagnosticVersion? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other == null || _schemaVersion != other._schemaVersion ||
            !ReferenceEquals(_environment, other._environment) ||
            !ReferenceEquals(_csharpEnvironment, other._csharpEnvironment) ||
            _options != other._options ||
            !(ReferenceEquals(Document, other.Document) || Document.HasSameGenerationShape(other.Document)) ||
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

    public override bool Equals(object? obj) => obj is DocumentDiagnosticVersion other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_schemaVersion, Document.FilePath);
}
