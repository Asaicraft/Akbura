using Akbura.Markup;
using Akbura.RuntimePools;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using System.Collections.Immutable;
using System.Runtime.ExceptionServices;

namespace Akbura.Akcss;

internal sealed class AkcssNativeApplications
{
    private static readonly AttachedProperty<object?> s_identity =
        AvaloniaProperty.RegisterAttached<StyledElement, object?>("AkcssNativeIdentity", typeof(AkcssNativeApplications));
    private static readonly IComparer<Application> s_prefixesAbove =
        Comparer<Application>.Create(static (left, right) => CompareApplications(left, right, UnprefixedUtilityPrecedence.Above));
    private static readonly IComparer<Application> s_prefixesBelow =
        Comparer<Application>.Create(static (left, right) => CompareApplications(left, right, UnprefixedUtilityPrecedence.Below));
    private static long s_nextGate;

    private readonly object _identity = new();
    private Styles? _container;
    private Lease[] _installed = [];

    public bool IsEmpty => _installed.Length == 0;

    public static bool HasNativeBody(AkcssStyleActivator activator) =>
        activator is AkcssUtilityCandidateActivator { HasNativeBody: true } or
            AkcssClassActivator { Class: IAkcssNativeStyleProvider };

    public static void Validate(object target, ImmutableArray<AkcssStyleActivator> styles) =>
        Describe(target, styles, validateValues: false, applications: default);

    public void Reconcile(object target, ImmutableArray<AkcssStyleActivator> styles)
    {
        using var applications = ImmutableArrayBuilder<Application>.Rent();
        Describe(target, styles, validateValues: true, applications);
        if (target is not Control control)
        {
            return;
        }

        var previous = _installed;
        Dictionary<Key, Lease>? oldLeases = null;
        var proposed = new Lease[applications.Count];
        var index = 0;
        foreach (var application in applications.WrittenSpan)
        {
            Lease? old = null;
            if (index < previous.Length && previous[index].Application.Key == application.Key)
            {
                old = previous[index];
            }
            else if (previous.Length != 0)
            {
                if (oldLeases is null)
                {
                    oldLeases = new Dictionary<Key, Lease>(previous.Length);
                    foreach (var lease in previous)
                    {
                        oldLeases.Add(lease.Application.Key, lease);
                    }
                }

                oldLeases.TryGetValue(application.Key, out old);
            }

            var candidate = application.Candidate;
            var ready = candidate?.IsReady ?? true;
            var active = ready && (candidate?.IsActive ?? application.Key.Activator.Condition);

            var gate = old?.Gate ?? ":akbura-native-" + Interlocked.Increment(ref s_nextGate)
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
            var root = old?.Root;
            var versions = old?.Versions ?? [];
            if (ready && (root == null || candidate?.HasNativeArgumentVersions(versions) == false))
            {
                var snapshot = candidate?.CaptureNativeArguments() ?? default;
                versions = snapshot.Versions;
                var selector = Selectors.Class(Selectors.PropertyEquals(null, s_identity, _identity), gate);
                root = new Style { Selector = selector };
                try
                {
                    application.Provider.BuildNativeStyle(root, snapshot.Values.IsDefault ? [] : snapshot.Values);
                    if (!ReferenceEquals(root.Selector, selector))
                    {
                        throw new InvalidOperationException("The factory changed its application anchor.");
                    }
                }
                catch (Exception failure)
                {
                    throw new InvalidOperationException(
                        $"Building native AKCSS application '{application.Key.Activator.Style.Name}' on '{control.GetType().Name}' failed.",
                        failure);
                }
            }

            proposed[index++] = new Lease(application, gate, root, versions, active);
        }

        try
        {
            Publish(control, previous, proposed);
        }
        catch (Exception failure)
        {
            try
            {
                Publish(control, proposed, previous, force: true);
            }
            catch (Exception rollback)
            {
                throw new AggregateException("Native AKCSS publication and restoration failed.", failure, rollback);
            }
            throw;
        }
        _installed = proposed;
    }

    public void Clear(object target)
    {
        if (target is not Control control)
        {
            return;
        }

        var previous = _installed;
        _installed = [];
        var container = _container;
        _container = null;
        List<Exception>? failures = null;
        foreach (var lease in previous)
        {
            Attempt(() => ((IPseudoClasses)control.Classes).Remove(lease.Gate), ref failures);
        }
        if (container != null)
        {
            var removed = container.ToArray();
            Attempt(() => control.Styles.Remove(container), ref failures);
            Attempt(container.Clear, ref failures);
            Attempt(() => RetireVisualFrames(control, removed), ref failures);
            Attempt(() => control.ClearValue(s_identity), ref failures);
        }
        ThrowFailures(failures);
    }

    private void Publish(Control target, Lease[] before, Lease[] after, bool force = false)
    {
        var current = _container;
        var rootCount = 0;
        var reorder = force;
        foreach (var lease in after)
        {
            if (lease.Root is not { } root)
            {
                continue;
            }

            reorder |= current is null || rootCount >= current.Count ||
                !EqualityComparer<IStyle>.Default.Equals(current[rootCount], root);
            rootCount++;
        }

        reorder |= rootCount != (current?.Count ?? 0);
        var gates = (IPseudoClasses)target.Classes;
        foreach (var lease in before)
        {
            if (reorder || !IsGateActive(after, lease.Gate))
            {
                gates.Remove(lease.Gate);
            }
        }

        if (reorder)
        {
            if (current != null)
            {
                var removed = current.ToArray();
                current.Clear();
                RetireVisualFrames(target, removed);
            }
            if (rootCount == 0)
            {
                if (current != null)
                {
                    target.Styles.Remove(current);
                }
                _container = null;
                target.ClearValue(s_identity);
            }
            else
            {
                var roots = new IStyle[rootCount];
                var index = 0;
                foreach (var lease in after)
                {
                    if (lease.Root is { } root)
                    {
                        roots[index++] = root;
                    }
                }

                if (current == null)
                {
                    var next = new Styles();
                    next.AddRange(roots);
                    _container = next;
                    target.SetValue(s_identity, _identity);
                    target.Styles.Add(next);
                }
                else
                {
                    if (!target.Styles.Contains(current))
                    {
                        target.Styles.Add(current);
                    }

                    current.AddRange(roots);
                }
            }
        }

        foreach (var lease in after)
        {
            if (lease.Active)
            {
                gates.Add(lease.Gate);
            }
        }
    }

    private static bool IsGateActive(Lease[] leases, string gate)
    {
        foreach (var lease in leases)
        {
            if (lease.Active && lease.Gate == gate)
            {
                return true;
            }
        }

        return false;
    }

    private static void RetireVisualFrames(Control control, IReadOnlyList<IStyle> removed)
    {
        if (!removed.Any(static style => style.Children.Count != 0))
        {
            return;
        }

        // Avalonia's removal notification follows logical children. Apply the same
        // public notification to visual branches that traversal did not visit.
        var visited = new HashSet<StyledElement>(ReferenceEqualityComparer.Instance);
        MarkLogicalBranch(control, visited);
        List<Exception>? failures = null;
        foreach (var visual in control.GetVisualDescendants())
        {
            if (visited.Contains(visual))
            {
                continue;
            }
            Attempt(() => ((IStyleHost)visual).StylesRemoved(removed), ref failures);
            MarkLogicalBranch(visual, visited);
        }
        ThrowFailures(failures);
    }

    private static void MarkLogicalBranch(StyledElement root, HashSet<StyledElement> visited)
    {
        if (!visited.Add(root))
        {
            return;
        }

        foreach (var child in ((Avalonia.LogicalTree.ILogical)root).LogicalChildren)
        {
            if (child is StyledElement element)
            {
                MarkLogicalBranch(element, visited);
            }
        }
    }

    private static void Describe(object target, ImmutableArray<AkcssStyleActivator> styles, bool validateValues,
        ImmutableArrayBuilder<Application> applications)
    {
        var occurrences = applications.IsDefault ? null : new Dictionary<AkcssStyleActivator, int>(ReferenceEqualityComparer.Instance);
        string? group = null;
        var precedence = UnprefixedUtilityPrecedence.SourceOrder;
        var flat = false;
        for (var index = 0; index < styles.Length; index++)
        {
            var activator = styles[index];
            if (!HasNativeBody(activator))
            {
                continue;
            }

            var occurrence = 0;
            if (occurrences is not null)
            {
                occurrence = occurrences.GetValueOrDefault(activator);
                occurrences[activator] = occurrence + 1;
            }

            if (activator is AkcssUtilityCandidateActivator candidate)
            {
                var eligible = false;
                foreach (var definition in candidate.NativeDefinitions)
                {
                    if (definition.Provider.NativeTargetType.IsInstanceOfType(target))
                    {
                        Add(applications, definition.Provider, candidate, definition.Index, index, occurrence);
                        eligible = true;
                    }
                }
                if (!eligible)
                {
                    continue;
                }

                candidate.ValidateNativePriority(validateValues);
                if (!candidate.IsPrefixed)
                {
                    continue;
                }

                if (candidate.ConflictGroup is not { } name)
                {
                    if (group != null || candidate.UnprefixedPrecedence != UnprefixedUtilityPrecedence.SourceOrder)
                    {
                        throw Unsupported(candidate, "ungrouped prefixes require a common SourceOrder policy");
                    }
                    flat = true;
                }
                else
                {
                    var mode = candidate.UnprefixedPrecedence;
                    if (flat || !double.IsFinite(candidate.Order) ||
                        mode is not (UnprefixedUtilityPrecedence.Above or UnprefixedUtilityPrecedence.Below) ||
                        group != null && (group != name || precedence != mode))
                    {
                        throw Unsupported(candidate, "prefixes require one group with a uniform Above or Below policy and finite order");
                    }
                    group = name;
                    precedence = mode;
                }
            }
            else if (activator.Style is IAkcssNativeStyleProvider provider && provider.NativeTargetType.IsInstanceOfType(target))
            {
                Add(applications, provider, activator, 0, index, occurrence);
            }
        }

        if (group != null && !applications.IsDefault)
        {
            applications.Sort(precedence == UnprefixedUtilityPrecedence.Above ? s_prefixesAbove : s_prefixesBelow);
        }

        void Add(ImmutableArrayBuilder<Application> result, IAkcssNativeStyleProvider provider, AkcssStyleActivator style,
            int implementation, int position, int occurrence)
        {
            if (target is not Control)
            {
                throw Unsupported(style, "the target must be an Avalonia Control");
            }

            if (!result.IsDefault)
            {
                result.Add(new Application(new Key(style, occurrence, implementation), provider, position));
            }
        }

        InvalidOperationException Unsupported(AkcssStyleActivator style, string reason) => new(
            $"Native AKCSS application '{style.Style.Name}' on '{target.GetType().Name}' is unsupported: {reason}.");
    }

    private static int CompareApplications(Application left, Application right, UnprefixedUtilityPrecedence precedence)
    {
        var leftPrefixed = left.Candidate?.IsPrefixed == true;
        var rightPrefixed = right.Candidate?.IsPrefixed == true;
        if (leftPrefixed != rightPrefixed)
        {
            return leftPrefixed == (precedence == UnprefixedUtilityPrecedence.Above) ? 1 : -1;
        }

        if (leftPrefixed)
        {
            var order = left.Candidate!.Order.CompareTo(right.Candidate!.Order);
            if (order != 0)
            {
                return order;
            }
        }

        var position = left.Position.CompareTo(right.Position);
        return position != 0 ? position : left.Key.Implementation.CompareTo(right.Key.Implementation);
    }

    private static void Attempt(Action action, ref List<Exception>? failures)
    {
        try { action(); }
        catch (Exception error) { (failures ??= []).Add(error); }
    }

    private static void ThrowFailures(List<Exception>? failures)
    {
        if (failures?.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }
        if (failures != null)
        {
            throw new AggregateException("Native AKCSS cleanup failed.", failures);
        }
    }

    private readonly record struct Key(AkcssStyleActivator Activator, int Occurrence, int Implementation);
    private readonly record struct Application(Key Key, IAkcssNativeStyleProvider Provider, int Position)
    {
        public AkcssUtilityCandidateActivator? Candidate => Key.Activator as AkcssUtilityCandidateActivator;
    }
    private sealed record Lease(Application Application, string Gate, Style? Root, ImmutableArray<long> Versions, bool Active);
}
