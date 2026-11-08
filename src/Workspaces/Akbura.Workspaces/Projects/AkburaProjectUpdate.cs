using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Reflection;
using System.Text;

namespace Akbura.Workspaces.Projects;

/// <summary>
/// Stages operations for one <see cref="AkburaProject.Update"/> callback.
/// References are validated together after the callback, independently of dependency order.
/// </summary>
public sealed class AkburaProjectUpdate
{
    private readonly List<Action<ProjectConfiguration>> _operations = new();
    private readonly int _threadId = Thread.CurrentThread.ManagedThreadId;
    private bool _sealed;

    internal AkburaProjectUpdate()
    {
    }

    public IAkburaReferenceResolver? ReferenceResolver
    {
        set => Stage(state => state.Resolver = value);
    }

    public void AddAssembly(Assembly assembly)
    {
        if (assembly == null) throw new ArgumentNullException(nameof(assembly));
        if (assembly.IsDynamic) throw new ArgumentException("Dynamic assemblies have no supported compiler metadata. Supply a complete PE image instead.", nameof(assembly));
        Stage(state => state.Assemblies.Add(assembly));
    }

    public void AddAssemblyOf<T>() => AddAssembly(typeof(T).Assembly);

    public void AddAssemblyImage(byte[] assemblyBytes)
    {
        var reference = AssemblyReferenceInfo.FromImage(assemblyBytes);
        AddReference(reference);
    }

    public void AddReference(MetadataReference reference)
    {
        if (reference == null) throw new ArgumentNullException(nameof(reference));
        Stage(state => state.AddReference(reference));
    }

    public void AddSource(string path, string source) => Source(path, source, replace: false);

    public void UpdateSource(string path, string source) => Source(path, source, replace: true);

    public void RemoveSource(string path) => RemoveSource(path, null);

    internal void RemoveSource(string path, Action<bool>? completed)
    {
        var normalized = NormalizePath(path);
        Stage(state =>
        {
            var tree = state.FindSource(normalized);
            if (tree != null) state.Compilation = state.Compilation.RemoveSyntaxTrees(tree);
            completed?.Invoke(tree != null);
        });
    }

    private void Source(string path, string source, bool replace)
    {
        var normalized = NormalizePath(path);
        if (source == null) throw new ArgumentNullException(nameof(source));
        var text = SourceText.From(source, Encoding.UTF8);
        Stage(state =>
        {
            var previous = state.FindSource(normalized);
            if (replace && previous == null) throw new KeyNotFoundException($"C# source '{normalized}' does not exist. Use AddSource first.");
            if (!replace && previous != null) throw new InvalidOperationException($"C# source '{normalized}' already exists. Use UpdateSource to replace it.");
            if (previous != null)
            {
                if (!previous.GetText().ContentEquals(text))
                    state.Compilation = state.Compilation.ReplaceSyntaxTree(previous, previous.WithChangedText(text));
            }
            else
            {
                // Existing trees define project parsing policy. A new, tree-less project
                // uses C# 14, regular source and no preprocessor symbols deterministically.
                var options = state.Compilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions ??
                    CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp14);
                state.Compilation = state.Compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(text, options, normalized));
            }
        });
    }

    private void Stage(Action<ProjectConfiguration> operation)
    {
        if (_sealed || Thread.CurrentThread.ManagedThreadId != _threadId)
            throw new InvalidOperationException("A project update can only be used synchronously inside its configuration callback.");
        _operations.Add(operation);
    }

    internal void Seal() => _sealed = true;

    internal ProjectConfiguration Apply(ProjectContext context, IAkburaReferenceResolver? resolver)
    {
        var state = new ProjectConfiguration(context.CSharpCompilation, resolver);
        foreach (var operation in _operations) operation(state);
        state.CompleteReferences();
        return state;
    }

    internal static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A relative virtual C# source path is required.", nameof(path));
        path = path.Replace('\\', '/');
        if (path.StartsWith("/", StringComparison.Ordinal) || path.IndexOf(':') >= 0 || path.IndexOf('\0') >= 0)
            throw new ArgumentException("Source paths must be relative to the virtual project root.", nameof(path));
        var parts = new List<string>();
        foreach (var part in path.Split(['/'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            if (part == "..")
            {
                if (parts.Count == 0) throw new ArgumentException("The source path escapes the virtual project root.", nameof(path));
                parts.RemoveAt(parts.Count - 1);
            }
            else parts.Add(part);
        }
        var normalized = string.Join("/", parts);
        if (!normalized.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only .cs sources are supported by the project configuration API.", nameof(path));
        return normalized;
    }
}
