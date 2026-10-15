using Akbura.CompilerAnotations;
using Akbura.Markup;

namespace Akbura.Akcss;

internal static partial class AkcssRuntime
{
    internal static (int Keys, int Operations, int PendingKeys, int PendingContributions, bool Transition) GetStorageInfo(object target)
    {
        if (!s_targets.TryGetValue(target, out var runtime))
        {
            return default;
        }
        return runtime.StorageInfo;
    }

    private sealed partial class TargetRuntime
    {
        private Dictionary<string, ConflictIndex> _conflictIndex = new(StringComparer.Ordinal);
        internal (int Keys, int Operations, int PendingKeys, int PendingContributions, bool Transition) StorageInfo =>
            (_conflictIndex.Count, _conflictIndex.Values.Sum(static index => index.OperationCount),
                _pendingConflictKeys.Count, _pendingContributions?.Count ?? 0, _activeTransition != null);

        private ConflictIndex GetConflictIndex(string key)
        {
            if (_conflictIndex.TryGetValue(key, out var result))
            {
                return result;
            }
            IndexedOperation? first = null;
            List<IndexedOperation>? operations = null;
            Dictionary<string, int>? groups = null;
            for (var index = 0; index < _styles.Length; index++)
            {
                if (_styles[index] is not AkcssUtilityCandidateActivator candidate)
                {
                    continue;
                }
                for (var operation = 0; operation < candidate.OperationCount; operation++)
                {
                    if (candidate.GetOperationConflictKey(operation) != key)
                    {
                        continue;
                    }
                    var group = candidate.IsPrefixed ? -1 : -2;
                    if (candidate.IsPrefixed && candidate.ConflictGroup is { } name)
                    {
                        groups ??= new(StringComparer.Ordinal);
                        if (!groups.TryGetValue(name, out group))
                        {
                            group = groups.Count;
                            groups.Add(name, group);
                        }
                    }
                    var entry = new IndexedOperation(new(candidate, operation, _descriptors.IsDefault ? candidate.SourceOrder : index), group);
                    if (first.HasValue)
                    {
                        (operations ??= [first.Value]).Add(entry);
                    }
                    else
                    {
                        first = entry;
                    }
                }
            }
            if (!first.HasValue)
            {
                return ConflictIndex.Empty;
            }
            result = new ConflictIndex(operations?.ToArray() ?? [first.Value], groups?.Count ?? 0);
            _conflictIndex.Add(key, result);
            return result;
        }

        private readonly record struct IndexedOperation(UtilityOperationWinner Winner, int Group);

        // This is a candidate index, not a winner cache. Eligibility and priority
        // are evaluated on every invalidation; scratch winners never survive Resolve.
        private sealed class ConflictIndex(IndexedOperation[] operations, int groupCount)
        {
            internal static readonly ConflictIndex Empty = new([], 0);
            internal int OperationCount => operations.Length;
            private readonly UtilityOperationWinner?[] _groups = groupCount == 0 ? [] : new UtilityOperationWinner?[groupCount];
            private readonly int[] _groupOrder = groupCount == 0 ? [] : new int[groupCount];
            internal bool SupportsPublication { get; } = operations.Length > 0 && operations.All(static entry =>
                entry.Winner.Candidate.CanUpdateContribution(entry.Winner.OperationIndex));

            internal void Reset(object target, UtilityOperationWinner? preserve = null)
            {
                for (var index = operations.Length - 1; index >= 0; index--)
                {
                    var entry = operations[index].Winner;
                    if (preserve?.Is(entry.Candidate, entry.OperationIndex) != true)
                    {
                        entry.Candidate.ResetOperation(entry.OperationIndex, target);
                    }
                }
            }

            internal UtilityOperationWinner? Resolve(object target)
            {
                UtilityOperationWinner? unprefixed = null;
                UtilityOperationWinner? ungrouped = null;
                AkcssOperationPriority? priority = null;
                var activeGroups = 0;
                try
                {
                    foreach (var entry in operations)
                    {
                        var contender = entry.Winner;
                        var candidate = contender.Candidate;
                        if (!candidate.IsOperationActive(contender.OperationIndex, target))
                        {
                            continue;
                        }
                        var currentPriority = candidate.GetOperationPriority(contender.OperationIndex);
                        if (!priority.HasValue || currentPriority > priority.Value)
                        {
                            priority = currentPriority;
                            unprefixed = ungrouped = null;
                            activeGroups = 0;
                            Array.Clear(_groups);
                        }
                        if (currentPriority != priority.Value)
                        {
                            continue;
                        }
                        if (entry.Group == -2)
                        {
                            if (!unprefixed.HasValue || IsLater(contender, unprefixed.Value))
                            {
                                unprefixed = contender;
                            }
                        }
                        else if (entry.Group == -1)
                        {
                            if (!ungrouped.HasValue || IsLater(contender, ungrouped.Value))
                            {
                                ungrouped = contender;
                            }
                        }
                        else
                        {
                            var winner = _groups[entry.Group];
                            if (!winner.HasValue)
                            {
                                _groupOrder[activeGroups++] = entry.Group;
                            }
                            if (!winner.HasValue || candidate.Order > winner.Value.Candidate.Order ||
                                candidate.Order == winner.Value.Candidate.Order && IsLater(contender, winner.Value))
                            {
                                _groups[entry.Group] = contender;
                            }
                        }
                    }
                    UtilityOperationWinner? prefixed = null;
                    for (var index = 0; index < activeGroups; index++)
                    {
                        var winner = _groups[_groupOrder[index]];
                        if (winner.HasValue && (!prefixed.HasValue || IsLater(winner.Value, prefixed.Value)))
                        {
                            prefixed = winner;
                        }
                    }
                    if (ungrouped.HasValue && (!prefixed.HasValue || IsLater(ungrouped.Value, prefixed.Value)))
                    {
                        prefixed = ungrouped;
                    }
                    if (!prefixed.HasValue)
                    {
                        return unprefixed;
                    }
                    if (!unprefixed.HasValue)
                    {
                        return prefixed;
                    }
                    return prefixed.Value.Candidate.UnprefixedPrecedence switch
                    {
                        UnprefixedUtilityPrecedence.Below => unprefixed,
                        UnprefixedUtilityPrecedence.Above => prefixed,
                        _ => IsLater(prefixed.Value, unprefixed.Value) ? prefixed : unprefixed,
                    };
                }
                finally
                {
                    Array.Clear(_groups);
                }
            }
        }
    }
}
