#if STATS
using Akbura.Language.CodeGeneration;
using System;
using System.Collections.Immutable;
using System.Linq;

namespace Akbura.BlackSilence;

internal sealed partial class CSharpEnvironmentSnapshot
{
    internal void TraceComparison(CSharpEnvironmentSnapshot other, bool equal)
    {
        using var measurement = GenerationStatistics.Measure(GenerationStatisticStage.CSharpEnvironmentTraceComparison);
        var reasons = ImmutableArray.CreateBuilder<string>();
        if (!string.Equals(Compilation.AssemblyName, other.Compilation.AssemblyName, StringComparison.Ordinal))
        {
            reasons.Add("AssemblyNameChanged");
        }
        if (!Compilation.Options.Equals(other.Compilation.Options))
        {
            reasons.Add("CompilationOptionsChanged");
        }
        if (!ReferenceEquals(Compilation.ScriptCompilationInfo, other.Compilation.ScriptCompilationInfo))
        {
            reasons.Add("ScriptCompilationIdentityChanged");
        }
        if (_references.Length != other._references.Length)
        {
            reasons.Add("ReferenceCountChanged");
        }
        if (_references.Zip(other._references, static (left, right) => ReferenceEquals(left, right)).Any(static same => !same))
        {
            reasons.Add("ReferenceIdentityChanged");
        }
        if (_parseOptions.Length != other._parseOptions.Length || _parseOptions.Any(option => !other._parseOptions.Contains(option)))
        {
            reasons.Add("ParseOptionsChanged");
        }
        if (!_namespaces.SequenceEqual(other._namespaces, StringComparer.Ordinal))
        {
            reasons.Add("NamespacesChanged");
        }
        if (RetainedSyntaxTrees.Length != other.RetainedSyntaxTrees.Length)
        {
            reasons.Add("RetainedTreeCountChanged");
        }

        var identityEqual = 0;
        var textComparisons = 0;
        for (var index = 0; index < Math.Max(RetainedSyntaxTrees.Length, other.RetainedSyntaxTrees.Length); index++)
        {
            var treeReasons = ImmutableArray.CreateBuilder<string>();
            var left = index < RetainedSyntaxTrees.Length ? RetainedSyntaxTrees[index] : null;
            var right = index < other.RetainedSyntaxTrees.Length ? other.RetainedSyntaxTrees[index] : null;
            if (left == null || right == null)
            {
                treeReasons.Add(left == null ? "TreeAddedAtPosition" : "TreeRemovedAtPosition");
            }
            else if (ReferenceEquals(left, right))
            {
                identityEqual++;
                continue;
            }
            else
            {
                if (!string.Equals(left.FilePath, right.FilePath, StringComparison.Ordinal))
                {
                    treeReasons.Add("TreePathOrOrderChanged");
                }
                if (!left.Options.Equals(right.Options))
                {
                    treeReasons.Add("TreeParseOptionsChanged");
                }
                textComparisons++;
                if (!left.GetText().ContentEquals(right.GetText()))
                {
                    treeReasons.Add("TreeTextChanged");
                }
            }
            reasons.AddRange(treeReasons);
            BlackSilenceInvalidationTrace.Record("CSharpTreeComparison", right?.FilePath ?? left!.FilePath,
                treeReasons.Count == 0 ? "EquivalentReplacement" : "Different", treeReasons.ToImmutable(), ("Position", index));
        }

        BlackSilenceInvalidationTrace.Record("CSharpEnvironmentComparison", string.Empty, equal ? "Equal" : "Different",
            reasons.Distinct(StringComparer.Ordinal).ToImmutableArray(),
            ("OldCompilation", BlackSilenceInvalidationTrace.Id(Compilation)),
            ("NewCompilation", BlackSilenceInvalidationTrace.Id(other.Compilation)),
            ("TreeIdentityEqual", identityEqual), ("TraceTextComparisons", textComparisons));
    }
}
#endif
