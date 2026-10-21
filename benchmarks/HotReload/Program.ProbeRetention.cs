using Akbura.BlackSilence;
using Akbura.Language.Binder;
using Microsoft.CodeAnalysis;
using System.Collections;
using System.Reflection;

internal static partial class Program
{
    // Reflection keeps the same host binary compatible with the frozen controls.
    // This runs after forced GC, outside the GeneratorDriver timing interval.
    private static object ProbeRetention(BlackSilenceProjectState state)
    {
        static object? Read(object? value, string name) => value?.GetType().GetProperty(name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(value);
        static IEnumerable<object> Values(object? value) => Read(value, "Values") is IEnumerable values ? values.Cast<object>() : [];
        var generation = Read(Read(state, "ProbeStore"), "Latest");
        var entries = Values(Read(generation, "Documents")).SelectMany(document => Values(Read(document, "Entries"))).ToArray();
        var operations = new HashSet<IOperation>(ReferenceEqualityComparer.Instance);
        var models = new HashSet<SemanticModel>(ReferenceEqualityComparer.Instance);
        var compilations = new HashSet<Compilation>(ReferenceEqualityComparer.Instance);
        var trees = new HashSet<SyntaxTree>(ReferenceEqualityComparer.Instance);
        var semanticEntries = 0;
        foreach (var entry in entries)
        {
            if (Read(Read(entry, "Binding"), "Result") is CSharpBindingResult result && result.OperationDefinition.Operation is { } operation)
            {
                semanticEntries++;
                operations.Add(operation);
                trees.Add(operation.Syntax.SyntaxTree);
                if (operation.SemanticModel is { } model)
                {
                    models.Add(model);
                    compilations.Add(model.Compilation);
                }
            }
        }
        return new { Entries = entries.Length, SemanticEntries = semanticEntries,
            ProjectionCharacters = entries.Sum(entry => Convert.ToInt64(Read(entry, "Characters"))),
            OperationRoots = operations.Count, ProbeSemanticModels = models.Count, ProbeCompilations = compilations.Count,
            ProbeSyntaxTrees = trees.Count };
    }
}
