using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoslynSymbol = Microsoft.CodeAnalysis.ISymbol;
using AkburaPropertySymbol = Akbura.Language.Symbols.IPropertySymbol;

namespace Akbura.Language;

internal abstract partial class AkburaSemanticModel
{
    /// <summary>Orders attributes, property elements and implicit content in one object-local graph.</summary>
    internal ImmutableArray<AkburaSyntax> GetMarkupAssignmentOrder(
        MarkupElementSyntax element, out ImmutableArray<AkburaSemanticDiagnostic> diagnostics)
    {
        var assignments = new List<MarkupAssignmentNode>();
        if (element.StartTag == null)
        {
            diagnostics = [];
            return [];
        }

        foreach (var attribute in element.StartTag.Attributes)
        {
            var property = GetSymbolInfo(attribute).Symbol as AkburaPropertySymbol;
            assignments.Add(new(attribute, GetAssignmentMember(property), attribute.Span.Start));
        }

        var firstContent = -1;
        foreach (var content in element.Body)
        {
            if (content is MarkupElementContentSyntax child &&
                GetSymbolInfo(child.Element).Symbol is AkburaPropertySymbol property)
            {
                assignments.Add(new(child.Element, GetAssignmentMember(property), child.Span.Start));
            }
            else if (firstContent < 0 && content.ToFullString().Trim().Length != 0)
            {
                firstContent = content.Span.Start;
            }
        }

        if (firstContent >= 0 && GetSymbolInfo(element).Symbol is IMarkupComponentSymbol component)
        {
            assignments.Add(new(element, component.ContentModel.ContentProperty.Symbol, firstContent));
        }

        assignments.Sort(static (left, right) => left.SourceStart.CompareTo(right.SourceStart));
        var edges = new HashSet<int>[assignments.Count];
        var incoming = new int[assignments.Count];
        for (var i = 0; i < assignments.Count; i++)
        {
            edges[i] = [];
        }

        for (var dependent = 0; dependent < assignments.Count; dependent++)
        {
            var member = assignments[dependent].Member;
            if (member == null)
            {
                continue;
            }

            foreach (var dependency in _markupPropertyMetadata.GetMetadata(member).Dependencies)
            {
                for (var prerequisite = 0; prerequisite < assignments.Count; prerequisite++)
                {
                    if (SymbolEqualityComparer.Default.Equals(
                            CanonicalAssignmentMember(assignments[prerequisite].Member),
                            CanonicalAssignmentMember(dependency)) &&
                        edges[prerequisite].Add(dependent))
                    {
                        incoming[dependent]++;
                    }
                }
            }
        }

        using var ordered = ImmutableArrayBuilder<AkburaSyntax>.Rent(assignments.Count);
        var emitted = new bool[assignments.Count];
        while (ordered.Count < assignments.Count)
        {
            var next = -1;
            for (var i = 0; i < assignments.Count; i++)
            {
                if (!emitted[i] && incoming[i] == 0)
                {
                    next = i;
                    break;
                }
            }

            if (next < 0)
            {
                var cycle = FindMarkupAssignmentCycle(edges);
                var path = string.Join(" -> ", cycle.Select(index => assignments[index].Member?.Name ?? "content"));
                diagnostics = [new AkburaSemanticDiagnostic(
                    assignments[cycle[0]].Syntax,
                    ErrorCodes.AKBURA_SEMANTIC_MarkupPropertyDependencyCycle, [path])];
                // Keep the errorful tree inspectable. A cycle is not an arbitrary emission order.
                return assignments.Select(static assignment => assignment.Syntax).ToImmutableArray();
            }

            emitted[next] = true;
            ordered.Add(assignments[next].Syntax);
            foreach (var dependent in edges[next])
            {
                incoming[dependent]--;
            }
        }

        diagnostics = [];
        return ordered.ToImmutable();
    }

    private static RoslynSymbol? GetAssignmentMember(AkburaPropertySymbol? property) =>
        property?.ClrPropertyDefinition.Symbol ?? property?.WriteDefinition.Symbol;

    private static RoslynSymbol? CanonicalAssignmentMember(RoslynSymbol? member)
    {
        while (member is Microsoft.CodeAnalysis.IPropertySymbol { OverriddenProperty: { } overridden })
        {
            member = overridden;
        }

        return member?.OriginalDefinition;
    }

    private static List<int> FindMarkupAssignmentCycle(HashSet<int>[] edges)
    {
        var state = new byte[edges.Length];
        var stack = new List<int>();
        var cycle = new List<int>();
        for (var node = 0; node < edges.Length; node++)
        {
            if (state[node] == 0 && Visit(node))
            {
                return cycle;
            }
        }

        throw new InvalidOperationException("An unresolved assignment graph must contain a cycle.");

        bool Visit(int node)
        {
            state[node] = 1;
            stack.Add(node);
            foreach (var child in edges[node].OrderBy(static value => value))
            {
                if (state[child] == 1)
                {
                    cycle.AddRange(stack.Skip(stack.IndexOf(child)));
                    cycle.Add(child);
                    return true;
                }

                if (state[child] == 0 && Visit(child))
                {
                    return true;
                }
            }

            stack.RemoveAt(stack.Count - 1);
            state[node] = 2;
            return false;
        }
    }

    private readonly struct MarkupAssignmentNode(AkburaSyntax syntax, RoslynSymbol? member, int sourceStart)
    {
        public AkburaSyntax Syntax { get; } = syntax;
        public RoslynSymbol? Member { get; } = member;
        public int SourceStart { get; } = sourceStart;
    }
}
