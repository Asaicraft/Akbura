using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Akbura.Pools;
using Akbura.Collections;

namespace Akbura.Language.Syntax.Green;
internal abstract partial class GreenNode
{
    public const int ListKind = 1;
    public const ushort SlotCountMask = 0b_0000_0000_0000_1111;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal protected static void Adjust(GreenNode slot, ref int width, ref ushort nodeFlagsAndSlotCount)
    {
        width += slot.FullWidth;

        nodeFlagsAndSlotCount |= (ushort)(slot.Flags & slot.InheritMask);

        var add = 1;
        var current = nodeFlagsAndSlotCount & 0b1111;
        var sum = current + add;
        if (sum > 0b1111)
        {
            sum = 0b1111;
        }

        nodeFlagsAndSlotCount = (ushort)((nodeFlagsAndSlotCount & 0b1111_1111_1111_0000) | (sum & 0b1111));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal protected static void Adjust(ReadOnlySpan<GreenNode> slots, ref int width, ref ushort nodeFlagsAndSlotCount)
    {
        var slotCount = slots.Length;
        for (var i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];

            width += slot.FullWidth;
            nodeFlagsAndSlotCount |= (ushort)(slot.Flags | slot.InheritMask);
        }

        if (slotCount > 14)
        {
            slotCount = 15;
        }

        nodeFlagsAndSlotCount = (ushort)((nodeFlagsAndSlotCount & 0b1111_1111_1111_0000) | (slotCount & 0b1111));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Adjust(ArrayElement<GreenNode>[] slots, ref int width, ref ushort nodeFlagsAndSlotCount)
    {
        var slotCount = slots.Length;

        for (var i = 0; i < slots.Length; i++)
        {
            var slot = slots[i].Value;

            width += slot.FullWidth;
            nodeFlagsAndSlotCount |= (ushort)(slot.Flags | slot.InheritMask);
        }

        if (slotCount > 14)
        {
            slotCount = 15;
        }

        nodeFlagsAndSlotCount = (ushort)((nodeFlagsAndSlotCount & SlotCountMask) | (slotCount & 0b1111));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void AdjustWidthAndFlags(GreenNode slot, ref int width, ref ushort flags)
    {
        width += slot.FullWidth;
        flags |= (ushort)(slot.Flags & slot.InheritMask);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void AdjustWidthAndFlags(ReadOnlySpan<GreenNode> slots, ref int width, ref ushort flags)
    {
        for (var i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];

            width += slot.FullWidth;
            flags |= (ushort)(slot.Flags | slot.InheritMask);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void AdjustWidthAndFlags(ArrayElement<GreenNode>[] slots, ref int width, ref ushort flags)
    {
        for (var i = 0; i < slots.Length; i++)
        {
            var slot = slots[i].Value;

            width += slot.FullWidth;
            flags |= (ushort)(slot.Flags | slot.InheritMask);
        }
    }

    private static readonly ConditionalWeakTable<GreenNode, AkburaDiagnostic[]> s_diagnosticsTable = new();
    private static readonly ConditionalWeakTable<GreenNode, AkburaSyntaxAnnotation[]> s_annotationsTable = new();

    private readonly int _fullWidth;
    private readonly ushort _nodeFlagsAndSlotCount;
    private readonly ushort _rawKind;

    protected GreenNode(ushort kind)
    {
        _rawKind = kind;
    }

    protected GreenNode(ushort kind, ImmutableArray<AkburaDiagnostic>? diagnosticInfos, ImmutableArray<AkburaSyntaxAnnotation>? annotations)
    {
        _rawKind = kind;

        ContainsAnnotationsDirectly = SetAnnotations(annotations);
        ContainsDiagnosticsDirectly = SetDiagnostics(diagnosticInfos);
    }


    internal virtual bool IsToken => false;
    internal bool IsList => _rawKind == ListKind;
    internal virtual bool IsTrivia => false;
    internal bool IsSkippedTokensTrivia => Kind == SyntaxKind.SkippedTokensTrivia;

    public int FullWidth
    {
        get => _fullWidth;
        protected init => _fullWidth = value;
    }

    public SyntaxKind Kind => (SyntaxKind)_rawKind;

    public virtual SyntaxKind ContextualKind => Kind;

    public string KindText => Kind.ToString();

    public ushort RawKind => _rawKind;

    public int SmallSlotCount => _nodeFlagsAndSlotCount & 0b_0000_0000_0000_1111;

    public int SlotCount
    {
        get
        {
            if (SmallSlotCount < 15)
            {
                return SmallSlotCount;
            }

            return GetSlotCount();
        }

        internal init
        {
            var slotCount = (ushort)(value & 0b_0000_0000_0000_1111);

            _nodeFlagsAndSlotCount |= slotCount;
        }
    }

    public ushort FlagsAndSlotCount => _nodeFlagsAndSlotCount;

    public virtual int Width => _fullWidth - GetLeadingTriviaWidth() - GetTrailingTriviaWidth();

    public virtual int GetLeadingTriviaWidth()
        => FullWidth != 0 ? GetFirstTerminal()?.GetLeadingTriviaWidth() ?? 0 : 0;

    public virtual int GetTrailingTriviaWidth()
        => FullWidth != 0 ? GetLastTerminal()?.GetTrailingTriviaWidth() ?? 0 : 0;

    public bool HasLeadingTrivia => GetLeadingTriviaWidth() != 0;

    public bool HasTrailingTrivia => GetTrailingTriviaWidth() != 0;

    protected virtual int GetSlotCount()
    {
        return SmallSlotCount;
    }

    public virtual int GetSlotOffset(int index)
    {
        var offset = 0;
        for (var i = 0; i < index; i++)
        {
            var child = GetSlot(i);

            if (child != null)
            {
                offset += child.FullWidth;
            }
        }

        return offset;
    }

    protected ushort Flags
    {
        get => (ushort)(_nodeFlagsAndSlotCount & 0b_1111_1111_1111_0000);
        init
        {
            var existingSlots = (ushort)(_nodeFlagsAndSlotCount & SlotCountMask);
            _nodeFlagsAndSlotCount = (ushort)((value & 0b_1111_1111_1111_0000) | existingSlots);
        }
    }

    internal protected ushort InheritMask => (ushort)(_nodeFlagsAndSlotCount & (ushort)GreenNodeFlags.InheritMask);

    public bool IsNotMissing
    {
        get => (_nodeFlagsAndSlotCount & (int)GreenNodeFlags.IsNotMissing) != 0;
        init => _nodeFlagsAndSlotCount = (ushort)(value ? (_nodeFlagsAndSlotCount | (int)GreenNodeFlags.IsNotMissing) : (_nodeFlagsAndSlotCount & ~(int)GreenNodeFlags.IsNotMissing));
    }

    public bool IsMissing
    {
        get => !IsNotMissing;
        init => IsNotMissing = !value;
    }

    public bool ContainsDiagnosticsDirectly
    {
        get => (_nodeFlagsAndSlotCount & (int)GreenNodeFlags.ContainsDiagnosticsDirectly) != 0;
        init => _nodeFlagsAndSlotCount = (ushort)(value ? (_nodeFlagsAndSlotCount | (int)GreenNodeFlags.ContainsDiagnosticsDirectly) : (_nodeFlagsAndSlotCount & ~(int)GreenNodeFlags.ContainsDiagnosticsDirectly));
    }

    public bool ContainsAnnotationsDirectly
    {
        get => (_nodeFlagsAndSlotCount & (int)GreenNodeFlags.ContainsAnnotationsDirectly) != 0;
        init => _nodeFlagsAndSlotCount = (ushort)(value ? (_nodeFlagsAndSlotCount | (int)GreenNodeFlags.ContainsAnnotationsDirectly) : (_nodeFlagsAndSlotCount & ~(int)GreenNodeFlags.ContainsAnnotationsDirectly));
    }

    public bool ContainsDiagnostics
    {
        get => (_nodeFlagsAndSlotCount & (int)GreenNodeFlags.ContainsDiagnostics) != 0;
        init => _nodeFlagsAndSlotCount = (ushort)(value
            ? (_nodeFlagsAndSlotCount | (int)GreenNodeFlags.ContainsDiagnostics)
            : (_nodeFlagsAndSlotCount & ~(int)GreenNodeFlags.ContainsDiagnostics));
    }

    public bool ContainsAnnotations
    {
        get => (_nodeFlagsAndSlotCount & (int)GreenNodeFlags.ContainsAnnotations) != 0;
        init => _nodeFlagsAndSlotCount = (ushort)(value
            ? (_nodeFlagsAndSlotCount | (int)GreenNodeFlags.ContainsAnnotations)
            : (_nodeFlagsAndSlotCount & ~(int)GreenNodeFlags.ContainsAnnotations));
    }

    public bool IsCSharpSyntax
    {
        get => (_nodeFlagsAndSlotCount & (int)GreenNodeFlags.IsCSharpSyntax) != 0;
        init => _nodeFlagsAndSlotCount = (ushort)(value
            ? (_nodeFlagsAndSlotCount | (int)GreenNodeFlags.IsCSharpSyntax)
            : (_nodeFlagsAndSlotCount & ~(int)GreenNodeFlags.IsCSharpSyntax));
    }

    public bool ContainsAkburaSyntaxInCSharpSyntax
    {
        get => (_nodeFlagsAndSlotCount & (int)GreenNodeFlags.ContainsAkburaSyntaxInCSharpSyntax) != 0;
        init => _nodeFlagsAndSlotCount = (ushort)(value
            ? (_nodeFlagsAndSlotCount | (int)GreenNodeFlags.ContainsAkburaSyntaxInCSharpSyntax)
            : (_nodeFlagsAndSlotCount & ~(int)GreenNodeFlags.ContainsAkburaSyntaxInCSharpSyntax));
    }

    public bool ContainsSkippedText
    {
        get => (_nodeFlagsAndSlotCount & (int)GreenNodeFlags.ContainsSkippedText) != 0;
        init => _nodeFlagsAndSlotCount = (ushort)(value
            ? (_nodeFlagsAndSlotCount | (int)GreenNodeFlags.ContainsSkippedText)
            : (_nodeFlagsAndSlotCount & ~(int)GreenNodeFlags.ContainsSkippedText));
    }

    public GreenNode GetRequiredSlot(int index)
    {
        var slot = GetSlot(index);
        return slot ?? throw new InvalidOperationException($"Slot at index {index} is required but was null.");
    }
    public abstract GreenNode? GetSlot(int index);

    public AkburaSyntax CreateRed() => CreateRed(null, 0);
    public abstract AkburaSyntax CreateRed(AkburaSyntax? parent, int position);

    #region Diagnostics

    public GreenNode AddDiagnostics(params ImmutableArray<AkburaDiagnostic> diagnosticInfos)
    {
        if (diagnosticInfos == null || diagnosticInfos.Length == 0)
        {
            return this;
        }

        var existingDiagnostics = GetDiagnostics();

        if (existingDiagnostics.Length == 0)
        {
            return WithDiagnostics(diagnosticInfos);
        }

        var allDiagnostics = new AkburaDiagnostic[existingDiagnostics.Length + diagnosticInfos.Length];

        existingDiagnostics.CopyTo(allDiagnostics, 0);
        diagnosticInfos.CopyTo(allDiagnostics, existingDiagnostics.Length);

        var array = allDiagnostics.ToImmutableArrayUnsafe();

        return WithDiagnostics(array);
    }
    public GreenNode AddDiagnostics(IEnumerable<AkburaDiagnostic> diagnosticInfos)
    {
        if (diagnosticInfos == null)
        {
            return this;
        }
        var existingDiagnostics = GetDiagnostics();
        if (existingDiagnostics.Length == 0)
        {
            return WithDiagnostics(diagnosticInfos.ToImmutableArray());
        }
        using var builder = ImmutableArrayBuilder<AkburaDiagnostic>.Rent(existingDiagnostics.Length * 2);
        builder.AddRange(existingDiagnostics);
        builder.AddRange(diagnosticInfos);
        return WithDiagnostics(builder.ToImmutable());
    }

    public ImmutableArray<AkburaDiagnostic> GetDiagnostics()
    {
        if (s_diagnosticsTable.TryGetValue(this, out var diagnostics))
        {
            return diagnostics.ToImmutableArrayUnsafe();
        }

        return [];
    }

    private bool SetDiagnostics(ImmutableArray<AkburaDiagnostic>? diagnostics)
    {
        if (diagnostics == null)
        {
            return false;
        }

        if (diagnostics.Value.Length == 0)
        {
            return false;
        }

        var value = diagnostics.Value;
        var array = Unsafe.As<ImmutableArray<AkburaDiagnostic>, AkburaDiagnostic[]>(ref value);

        s_diagnosticsTable.Add(this, array);
        return true;
    }

    public abstract GreenNode WithDiagnostics(ImmutableArray<AkburaDiagnostic>? diagnostics);

    #endregion

    #region Annotations

    public GreenNode AddAnnotations(params ImmutableArray<AkburaSyntaxAnnotation> annotations)
    {
        if (annotations == null || annotations.Length == 0)
        {
            return this;
        }

        var existingAnnotations = GetAnnotations();

        if (existingAnnotations.Length == 0)
        {
            return WithAnnotations(annotations);
        }

        var allAnnotations = new AkburaSyntaxAnnotation[existingAnnotations.Length + annotations.Length];

        existingAnnotations.CopyTo(allAnnotations, 0);
        annotations.CopyTo(allAnnotations, existingAnnotations.Length);

        var array = Unsafe.As<AkburaSyntaxAnnotation[], ImmutableArray<AkburaSyntaxAnnotation>>(ref allAnnotations);

        return WithAnnotations(array);
    }

    public GreenNode AddAnnotations(IEnumerable<AkburaSyntaxAnnotation> annotations)
    {
        if (annotations == null)
        {
            return this;
        }
        var existingAnnotations = GetAnnotations();

        if (existingAnnotations.Length == 0)
        {
            return WithAnnotations(annotations.ToImmutableArray());
        }

        using var builder = ImmutableArrayBuilder<AkburaSyntaxAnnotation>.Rent(existingAnnotations.Length * 2);

        builder.AddRange(existingAnnotations);
        builder.AddRange(annotations);

        return WithAnnotations(builder.ToImmutable());
    }

    public ImmutableArray<AkburaSyntaxAnnotation> GetAnnotations()
    {
        if (s_annotationsTable.TryGetValue(this, out var annotations))
        {
            return Unsafe.As<AkburaSyntaxAnnotation[], ImmutableArray<AkburaSyntaxAnnotation>>(ref annotations);
        }

        return [];
    }

    private bool SetAnnotations(ImmutableArray<AkburaSyntaxAnnotation>? annotations)
    {
        if (annotations == null)
        {
            return false;
        }

        if (annotations.Value.Length == 0)
        {
            return false;
        }

        var value = annotations.Value;
        var array = Unsafe.As<ImmutableArray<AkburaSyntaxAnnotation>, AkburaSyntaxAnnotation[]>(ref value);

        s_annotationsTable.Add(this, array);
        return true;
    }

    public bool HasAnnotations(string annotationKind)
    {
        var annotations = GetAnnotations();
        if (annotations == [])
        {
            return false;
        }

        foreach (var a in annotations)
        {
            if (a.Kind == annotationKind)
            {
                return true;
            }
        }

        return false;
    }

    public bool HasAnnotations(IEnumerable<string> annotationKinds)
    {
        var annotations = GetAnnotations();
        if (annotations == [])
        {
            return false;
        }

        foreach (var a in annotations)
        {
            if (annotationKinds.Contains(a.Kind))
            {
                return true;
            }
        }

        return false;
    }

    public bool HasAnnotation([NotNullWhen(true)] AkburaSyntaxAnnotation? annotation)
    {
        var annotations = GetAnnotations();
        if (annotations == [])
        {
            return false;
        }

        if (annotation is null)
        {
            return false;
        }

        foreach (var a in annotations)
        {
            if (a == annotation)
            {
                return true;
            }
        }

        return false;
    }

    public IEnumerable<AkburaSyntaxAnnotation> GetAnnotations(string annotationKind)
    {
        if (string.IsNullOrWhiteSpace(annotationKind))
        {
            throw new ArgumentNullException(nameof(annotationKind));
        }

        var annotations = GetAnnotations();

        if (annotations == [])
        {
            return [];
        }

        return GetAnnotationsSlow(annotations, annotationKind);
    }

    public IEnumerable<AkburaSyntaxAnnotation> GetAnnotations(IEnumerable<string> annotationKinds)
    {
        if(annotationKinds is null)
        {
            throw new ArgumentNullException(nameof(annotationKinds));
        }

        var annotations = GetAnnotations();

        if (annotations == [])
        {
            return [];
        }

        return GetAnnotationsSlow(annotations, annotationKinds);
    }

    private static IEnumerable<AkburaSyntaxAnnotation> GetAnnotationsSlow(ImmutableArray<AkburaSyntaxAnnotation> annotations, string annotationKind)
    {
        foreach (var annotation in annotations)
        {
            if (annotation.Kind == annotationKind)
            {
                yield return annotation;
            }
        }
    }
    private static IEnumerable<AkburaSyntaxAnnotation> GetAnnotationsSlow(ImmutableArray<AkburaSyntaxAnnotation> annotations, IEnumerable<string> annotationKinds)
    {
        foreach (var annotation in annotations)
        {
            if (annotationKinds.Contains(annotation.Kind))
            {
                yield return annotation;
            }
        }
    }

    public abstract GreenNode WithAnnotations(ImmutableArray<AkburaSyntaxAnnotation>? annotations);

    public GreenNode? WithoutAnnotations(IEnumerable<AkburaSyntaxAnnotation> annotations)
    {
        var existingAnnotations = GetAnnotations();
        if (existingAnnotations == [])
        {
            return this;
        }

        using var toKeep = ImmutableArrayBuilder<AkburaSyntaxAnnotation>.Rent(existingAnnotations.Length > 8
            ? existingAnnotations.Length * 2
            : 8);

        foreach (var annotation in existingAnnotations)
        {
            if (!annotations.Contains(annotation))
            {
                toKeep.Add(annotation);
            }
        }
        
        if (toKeep.Count == existingAnnotations.Length)
        {
            return this;
        }

        return WithAnnotations(toKeep.ToImmutable());
    }

    #endregion

    // TODO devirtualize this method, and use Kind 
    public virtual SyntaxToken CreateSeparator(AkburaSyntax element)
    {
        return SyntaxFactory.TokenWithTrailingSpace(SyntaxKind.CommaToken);
    }

    public virtual bool IsTriviaWithEndOfLine()
    {
        return Kind == SyntaxKind.EndOfLineTrivia;
    }

    public GreenChildSyntaxList ChildNodesAndTokens()
    {
        return new GreenChildSyntaxList(this);
    }

    /// <summary>
    /// Enumerates all green nodes of the tree rooted by this node (including this node).  This includes normal
    /// nodes, list nodes, and tokens.  The nodes will be returned in depth-first order.  This will not descend 
    /// into trivia or structured trivia.
    /// </summary>
    public NodeEnumerable EnumerateNodes() => new(this);

    /// <summary>
    /// Find the slot that contains the given offset.
    /// </summary>
    /// <param name="offset">The target offset. Must be between 0 and <see cref="FullWidth"/>.</param>
    /// <returns>The slot index of the slot containing the given offset.</returns>
    /// <remarks>
    /// The base implementation is a linear search. This should be overridden
    /// if a derived class can implement it more efficiently.
    /// </remarks>
    public virtual int FindSlotIndexContainingOffset(int offset)
    {
        Debug.Assert(0 <= offset && offset < FullWidth);

        int i;
        var accumulatedWidth = 0;

        for (i = 0; ; i++)
        {
            Debug.Assert(i < SlotCount);
            var child = GetSlot(i);
            if (child != null)
            {
                accumulatedWidth += child.FullWidth;
                if (offset < accumulatedWidth)
                {
                    break;
                }
            }
        }

        return i;
    }

    #region Caching

    /// <summary>
    /// Maximum number of child nodes allowed for caching.
    /// Nodes exceeding this number of children are not eligible for caching.
    /// </summary>
    public const int MaxCachedChildNum = 3;

    /// <summary>
    /// Determines if the current node is eligible for caching based on its flags and slot count.
    /// Nodes are cacheable if they are not missing and their slot count does not exceed <see cref="MaxCachedChildNum"/>.
    /// </summary>
    public bool IsCacheable => IsNotMissing && SlotCount <= MaxCachedChildNum;

    /// <summary>
    /// Computes a unique hash code for caching the node, combining node flags, slot count, kind, and identities of child nodes.
    /// <para>
    /// The method uses <see cref="SmallSlotCount"/> directly to avoid virtual method calls, ensuring optimal performance.
    /// Since cacheable nodes always have a slot count within the small limit (<= <see cref="MaxCachedChildNum"/>),
    /// the use of <see cref="SmallSlotCount"/> is guaranteed to be safe here.
    /// </para>
    /// </summary>
    /// <returns>A positive integer representing the computed hash code.</returns>
    public int GetCacheHash()
    {
        Debug.Assert(IsCacheable);

        var code = (_nodeFlagsAndSlotCount << 16) | _rawKind;
        var count = SlotCount;

        for (var i = 0; i < count; i++)
        {
            var child = GetSlot(i);
            if (child != null)
            {
                code = HashCode.Combine(RuntimeHelpers.GetHashCode(child), code);
            }
        }

        return code & int.MaxValue;
    }

    /// <summary>
    /// Checks if the current node is equivalent to another node in the cache,
    /// based on kind, combined flags and slot count, and one child node.
    /// </summary>
    public bool IsCacheEquivalent(int kind, ushort flagsAndSlotCount, GreenNode? child1)
    {
        Debug.Assert(IsCacheable);

        return RawKind == kind &&
            _nodeFlagsAndSlotCount == flagsAndSlotCount &&
            SmallSlotCount == 1 &&
            GetSlot(0) == child1;
    }

    /// <summary>
    /// Checks if the current node is equivalent to another node in the cache,
    /// based on kind, combined flags and slot count, and two child nodes.
    /// </summary>
    public bool IsCacheEquivalent(int kind, ushort flagsAndSlotCount, GreenNode? child1, GreenNode? child2)
    {
        Debug.Assert(IsCacheable);

        return RawKind == kind &&
            _nodeFlagsAndSlotCount == flagsAndSlotCount &&
            SmallSlotCount == 2 &&
            GetSlot(0) == child1 &&
            GetSlot(1) == child2;
    }

    /// <summary>
    /// Checks if the current node is equivalent to another node in the cache,
    /// based on kind, combined flags and slot count, and three child nodes.
    /// </summary>
    public bool IsCacheEquivalent(int kind, ushort flagsAndSlotCount, GreenNode? child1, GreenNode? child2, GreenNode? child3)
    {
        Debug.Assert(IsCacheable);

        return RawKind == kind &&
            _nodeFlagsAndSlotCount == flagsAndSlotCount &&
            SmallSlotCount == 3 &&
            GetSlot(0) == child1 &&
            GetSlot(1) == child2 &&
            GetSlot(2) == child3;
    }

    #endregion

    #region List factories

    public static GreenNode List(params GreenNode[] nodes)
    {
        return List((ReadOnlySpan<GreenNode>)nodes);
    }

    public static GreenNode List(ReadOnlySpan<GreenNode> nodes)
    {
        if (nodes.Length == 1)
        {
            return nodes[0];
        }

        if (nodes.Length == 2)
        {
            return GreenSyntaxList.List(nodes[0], nodes[1]);
        }

        if (nodes.Length == 3)
        {
            return GreenSyntaxList.List(nodes[0], nodes[1], nodes[2]);
        }

        return GreenSyntaxList.List(nodes);
    }

    /*
     * There are 3 overloads of this, because most callers already know what they have is a List<T> and only transform it.
     * In those cases List<TFrom> performs much better.
     * In other cases, the type is unknown / is IEnumerable<T>, where we try to find the best match.
     * There is another overload for IReadOnlyList, since most collections already implement this, so checking for it will
     * perform better then copying to a List<T>, though not as good as List<T> directly.
     */
    public static GreenNode? CreateList<TFrom>(IEnumerable<TFrom>? enumerable, Func<TFrom, GreenNode> select)
        => enumerable switch
        {
            null => null,
            List<TFrom> l => CreateList(l, select),
            IReadOnlyList<TFrom> l => CreateList(l, select),
            _ => CreateList(enumerable.ToList(), select)
        };

    public static GreenNode? CreateList<TFrom>(List<TFrom> list, Func<TFrom, GreenNode> select)
    {
        switch (list.Count)
        {
            case 0:
                return null;
            case 1:
                return select(list[0]);
            case 2:
                return GreenSyntaxList.List(select(list[0]), select(list[1]));
            case 3:
                return GreenSyntaxList.List(select(list[0]), select(list[1]), select(list[2]));
            default:
            {
                var array = new ArrayElement<GreenNode>[list.Count];

                for (var i = 0; i < array.Length; i++)
                {
                    array[i].Value = select(list[i]);
                }

                return GreenSyntaxList.List(array);
            }
        }
    }

    public static GreenNode? CreateList<TFrom>(IReadOnlyList<TFrom> list, Func<TFrom, GreenNode> select)
    {
        switch (list.Count)
        {
            case 0:
                return null;
            case 1:
                return select(list[0]);
            case 2:
                return GreenSyntaxList.List(select(list[0]), select(list[1]));
            case 3:
                return GreenSyntaxList.List(select(list[0]), select(list[1]), select(list[2]));
            default:
            {
                var array = new ArrayElement<GreenNode>[list.Count];
                for (var i = 0; i < array.Length; i++)
                {
                    array[i].Value = select(list[i]);
                }

                return GreenSyntaxList.List(array);
            }
        }
    }

    #endregion

    #region Tokens 
    public virtual object? GetValue() => null;
    public virtual string? GetValueText() => string.Empty;
    public virtual GreenNode? GetLeadingTrivia() => null;
    public virtual GreenNode? GetTrailingTrivia() => null;

    public virtual GreenNode WithLeadingTrivia(GreenNode? trivia) => this;

    public virtual GreenNode WithTrailingTrivia(GreenNode? trivia) => this;

    public GreenNode? GetFirstTerminal()
    {
        var node = this;

        do
        {
            GreenNode? firstChild = null;
            for (int i = 0, n = node.SlotCount; i < n; i++)
            {
                var child = node.GetSlot(i);
                if (child != null)
                {
                    firstChild = child;
                    break;
                }
            }
            node = firstChild;
        }
        while (node?.SmallSlotCount > 0);

        return node;
    }

    public GreenNode? GetLastTerminal()
    {
        var node = this;

        do
        {
            GreenNode? lastChild = null;
            for (var i = node.SlotCount - 1; i >= 0; i--)
            {
                var child = node.GetSlot(i);
                if (child != null)
                {
                    lastChild = child;
                    break;
                }
            }
            node = lastChild;
        }
        // Note: it's ok to examine SmallSlotCount here.  All we're trying to do is make sure we have at least one
        // child.  And SmallSlotCount works both for small counts and large counts.  This avoids an unnecessary
        // virtual call for large list nodes.
        while (node?.SmallSlotCount > 0);

        return node;
    }

    public GreenNode? GetLastNonmissingTerminal()
    {
        var node = this;

        do
        {
            GreenNode? nonmissingChild = null;
            for (var i = node.SlotCount - 1; i >= 0; i--)
            {
                var child = node.GetSlot(i);
                if (child != null && child.IsNotMissing)
                {
                    nonmissingChild = child;
                    break;
                }
            }
            node = nonmissingChild;
        }
        // Note: it's ok to examine SmallSlotCount here.  All we're trying to do is make sure we have at least one
        // child.  And SmallSlotCount works both for small counts and large counts.  This avoids an unnecessary
        // virtual call for large list nodes.
        while (node?.SmallSlotCount > 0);

        return node;
    }
    #endregion

    #region Equivalence 
    public virtual bool IsEquivalentTo([NotNullWhen(true)] GreenNode? other)
    {
        if (this == other)
        {
            return true;
        }

        if (other == null)
        {
            return false;
        }

        return EquivalentToInternal(this, other);
    }

    private static bool EquivalentToInternal(GreenNode node1, GreenNode node2)
    {
        if (node1.Kind != node2.Kind)
        {
            // A single-element list is usually represented as just a single node,
            // but can be represented as a List node with one child. Move to that
            // child if necessary.
            if (node1.IsList && node1.SlotCount == 1)
            {
                node1 = node1.GetRequiredSlot(0);
            }

            if (node2.IsList && node2.SlotCount == 1)
            {
                node2 = node2.GetRequiredSlot(0);
            }

            if (node1.Kind != node2.Kind)
            {
                return false;
            }
        }

        if (node1._fullWidth != node2._fullWidth)
        {
            return false;
        }

        var n = node1.SlotCount;
        if (n != node2.SlotCount)
        {
            return false;
        }

        for (var i = 0; i < n; i++)
        {
            var node1Child = node1.GetSlot(i);
            var node2Child = node2.GetSlot(i);
            if (node1Child != null && node2Child != null && !node1Child.IsEquivalentTo(node2Child))
            {
                return false;
            }
        }

        return true;
    }
    #endregion

    #region Text

    public virtual string ToFullString()
    {
        var sb = PooledStringBuilder.GetInstance();
        var writer = new System.IO.StringWriter(sb.Builder, System.Globalization.CultureInfo.InvariantCulture);
        WriteTo(writer, leading: true, trailing: true);

        return sb.ToStringAndFree();
    }

    public override string ToString()
    {
        var sb = PooledStringBuilder.GetInstance();
        var writer = new System.IO.StringWriter(sb.Builder, System.Globalization.CultureInfo.InvariantCulture);
        WriteTo(writer, leading: false, trailing: false);

        return sb.ToStringAndFree();
    }

    public void WriteTo(TextWriter writer)
    {
        WriteTo(writer, leading: true, trailing: true);
    }

    public void WriteTo(TextWriter writer, bool leading, bool trailing)
    {
        // Use an actual stack so we can write out deeply recursive structures without overflowing.
        var stack = ArrayBuilder<(GreenNode node, bool leading, bool trailing)>.GetInstance();
        stack.Push((this, leading, trailing));

        // Separated out stack processing logic so that it does not unintentionally refer to 
        // "this", "leading" or "trailing".
        processStack(writer, stack);
        stack.Free();
        return;

        static void processStack(
            TextWriter writer,
            ArrayBuilder<(GreenNode node, bool leading, bool trailing)> stack)
        {
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                var currentNode = current.node;
                var currentLeading = current.leading;
                var currentTrailing = current.trailing;

                if (currentNode.IsToken)
                {
                    currentNode.WriteTokenTo(writer, currentLeading, currentTrailing);
                    continue;
                }

                if (currentNode.IsTrivia)
                {
                    currentNode.WriteTriviaTo(writer);
                    continue;
                }

                var firstIndex = GreenNode.GetFirstNonNullChildIndex(currentNode);
                var lastIndex = GreenNode.GetLastNonNullChildIndex(currentNode);

                for (var i = lastIndex; i >= firstIndex; i--)
                {
                    var child = currentNode.GetSlot(i);
                    if (child != null)
                    {
                        var first = i == firstIndex;
                        var last = i == lastIndex;
                        stack.Push((child, currentLeading | !first, currentTrailing | !last));
                    }
                }
            }
        }
    }

    private static int GetFirstNonNullChildIndex(GreenNode node)
    {
        var n = node.SlotCount;
        var firstIndex = 0;

        for (; firstIndex < n; firstIndex++)
        {
            var child = node.GetSlot(firstIndex);
            if (child != null)
            {
                break;
            }
        }

        return firstIndex;
    }

    private static int GetLastNonNullChildIndex(GreenNode node)
    {
        var n = node.SlotCount;
        var lastIndex = n - 1;

        for (; lastIndex >= 0; lastIndex--)
        {
            var child = node.GetSlot(lastIndex);
            if (child != null)
            {
                break;
            }
        }

        return lastIndex;
    }

    protected virtual void WriteTriviaTo(TextWriter writer)
    {
        throw new NotImplementedException();
    }

    protected virtual void WriteTokenTo(TextWriter writer, bool leading, bool trailing)
    {
        throw new NotImplementedException();
    }

    #endregion

    #region Syntax Visitor

    public virtual void Accept(GreenSyntaxVisitor greenSyntaxVisitor)
    {
        greenSyntaxVisitor.DefaultVisit(this);
    }

    public virtual T? Accept<T>(GreenSyntaxVisitor<T> greenSyntaxVisitor)
    {
        return greenSyntaxVisitor.DefaultVisit(this);
    }

    public virtual TResult? Accept<TParameter, TResult>(GreenSyntaxVisitor<TParameter, TResult> greenSyntaxVisitor, TParameter parameter)
    {
        return greenSyntaxVisitor.DefaultVisit(this, parameter);
    }

    #endregion
}