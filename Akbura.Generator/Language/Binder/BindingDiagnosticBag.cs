// This file is ported and adapted from the Roslyn (dotnet/roslyn)

using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;

namespace Akbura.Language.Binder;

/// <summary>
/// This is base class for a bag used to accumulate information while binding is performed.
/// In Akbura this includes Akbura semantic diagnostics and Roslyn C# diagnostics.
/// </summary>
[DebuggerDisplay("{GetDebuggerDisplay(), nq}")]
internal abstract class BindingDiagnosticBag
{
    private static readonly ObjectPool<BindingDiagnosticBag> s_poolWithDiagnostics =
        new ObjectPool<BindingDiagnosticBag>(
            pool => new PooledBindingDiagnosticBag(pool, new DiagnosticBag(), accumulatesSemanticDiagnostics: true),
            size: 128);

    private static readonly ObjectPool<BindingDiagnosticBag> s_poolWithConcurrentDiagnostics =
        new ObjectPool<BindingDiagnosticBag>(
            pool => new PooledBindingDiagnosticBag(pool, new DiagnosticBag(), accumulatesSemanticDiagnostics: true),
            size: 128);

    public static readonly BindingDiagnosticBag Discarded =
        new PooledBindingDiagnosticBag(pool: null, diagnosticBag: null, accumulatesSemanticDiagnostics: false);

    private readonly bool _accumulatesSemanticDiagnostics;
    private ConcurrentQueue<AkburaSemanticDiagnostic>? _semanticDiagnostics;
    private ConcurrentDictionary<string, byte>? _seen;

    protected BindingDiagnosticBag(
        DiagnosticBag? diagnosticBag,
        bool accumulatesSemanticDiagnostics)
    {
        DiagnosticBag = diagnosticBag;
        _accumulatesSemanticDiagnostics = accumulatesSemanticDiagnostics;
    }

    public readonly DiagnosticBag? DiagnosticBag;

    public bool IsEmpty =>
        IsSemanticEmpty &&
        (DiagnosticBag == null || DiagnosticBag.IsEmptyWithoutResolution);

    public bool IsEmptyWithoutResolution => IsEmpty;

    public bool AccumulatesDiagnostics =>
        _accumulatesSemanticDiagnostics ||
        DiagnosticBag is object;

    public int Count =>
        (_semanticDiagnostics?.Count ?? 0) +
        (DiagnosticBag?.Count ?? 0);

    public void Add(AkburaSemanticDiagnostic diagnostic)
    {
        if (!_accumulatesSemanticDiagnostics)
        {
            return;
        }

        if (diagnostic == null)
        {
            throw new ArgumentNullException(nameof(diagnostic));
        }

        if (!MarkSeen(CreateSemanticKey(diagnostic)))
        {
            return;
        }

        SemanticDiagnostics.Enqueue(diagnostic);
    }

    public void AddRange(IEnumerable<AkburaSemanticDiagnostic> diagnostics)
    {
        if (diagnostics == null)
        {
            throw new ArgumentNullException(nameof(diagnostics));
        }

        foreach (var diagnostic in diagnostics)
        {
            Add(diagnostic);
        }
    }

    public void AddRange(ImmutableArray<AkburaSemanticDiagnostic> diagnostics)
    {
        if (diagnostics.IsDefaultOrEmpty)
        {
            return;
        }

        for (var index = 0; index < diagnostics.Length; index++)
        {
            Add(diagnostics[index]);
        }
    }

    public void AddRange(ReadOnlyBindingDiagnostic diagnostics)
    {
        AddRange(diagnostics.SemanticDiagnostics);
        AddCSharpRange(diagnostics.CSharpDiagnostics);
    }

    public void AddRange(BindingDiagnosticBag? bag)
    {
        if (bag is object)
        {
            AddRange(bag.AsSemanticEnumerable());
            AddRange(bag.DiagnosticBag);
        }
    }

    public void AddRangeAndFree(BindingDiagnosticBag bag)
    {
        AddRange(bag);
        bag.Free();
    }

    public void Add(Diagnostic diagnostic)
    {
        AddCSharp(diagnostic);
    }

    public void AddRange<TDiagnostic>(ImmutableArray<TDiagnostic> diagnostics)
        where TDiagnostic : Diagnostic
    {
        if (diagnostics.IsDefaultOrEmpty)
        {
            return;
        }

        for (var index = 0; index < diagnostics.Length; index++)
        {
            AddCSharp(diagnostics[index]);
        }
    }

    public void AddRange(IEnumerable<Diagnostic> diagnostics)
    {
        AddCSharpRange(diagnostics);
    }

    public void AddRange(DiagnosticBag? bag)
    {
        if (bag is object)
        {
            AddCSharpRange(bag.AsEnumerable());
        }
    }

    public void AddRangeAndFree(DiagnosticBag bag)
    {
        AddRange(bag);
        bag.Free();
    }

    public void AddCSharp(Diagnostic diagnostic)
    {
        if (DiagnosticBag == null)
        {
            return;
        }

        if (diagnostic == null)
        {
            throw new ArgumentNullException(nameof(diagnostic));
        }

        if (!MarkSeen(CreateCSharpKey(diagnostic)))
        {
            return;
        }

        DiagnosticBag.Add(diagnostic);
    }

    public void AddCSharpRange(IEnumerable<Diagnostic> diagnostics)
    {
        if (diagnostics == null)
        {
            throw new ArgumentNullException(nameof(diagnostics));
        }

        foreach (var diagnostic in diagnostics)
        {
            AddCSharp(diagnostic);
        }
    }

    public void AddCSharpRange(ImmutableArray<Diagnostic> diagnostics)
    {
        if (diagnostics.IsDefaultOrEmpty)
        {
            return;
        }

        for (var index = 0; index < diagnostics.Length; index++)
        {
            AddCSharp(diagnostics[index]);
        }
    }

    public bool HasAnyErrors()
    {
        if (IsEmptyWithoutResolution)
        {
            return false;
        }

        foreach (var diagnostic in AsSemanticEnumerable())
        {
            if (diagnostic.Severity == AkburaDiagnosticSeverity.Error)
            {
                return true;
            }
        }

        return DiagnosticBag?.HasAnyErrors() == true;
    }

    public bool HasAnyResolvedErrors()
    {
        if (IsEmptyWithoutResolution)
        {
            return false;
        }

        foreach (var diagnostic in AsSemanticEnumerable())
        {
            if (diagnostic.Severity == AkburaDiagnosticSeverity.Error)
            {
                return true;
            }
        }

        return DiagnosticBag?.HasAnyResolvedErrors() == true;
    }

    public ImmutableArray<AkburaSemanticDiagnostic> ToSemanticDiagnostics()
    {
        return ToReadOnlyCore(_semanticDiagnostics);
    }

    public ImmutableArray<Diagnostic> ToCSharpDiagnostics()
    {
        return DiagnosticBag?.ToReadOnly() ??
            ImmutableArray<Diagnostic>.Empty;
    }

    public ReadOnlyBindingDiagnostic ToReadOnly()
    {
        return new ReadOnlyBindingDiagnostic(
            ToSemanticDiagnostics(),
            ToCSharpDiagnostics());
    }

    public ReadOnlyBindingDiagnostic ToReadOnlyAndFree()
    {
        var diagnostics = ToReadOnly();
        Free();
        return diagnostics;
    }

    public IEnumerable<AkburaSemanticDiagnostic> AsSemanticEnumerable()
    {
        return _semanticDiagnostics ?? [];
    }

    public IEnumerable<Diagnostic> AsCSharpEnumerable()
    {
        return DiagnosticBag?.AsEnumerable() ?? [];
    }

    public void Clear()
    {
        _semanticDiagnostics = null;
        _seen = null;
        DiagnosticBag?.Clear();
    }

    internal virtual void Free()
    {
        _semanticDiagnostics = null;
        _seen = null;
        DiagnosticBag?.Free();
    }

    internal static BindingDiagnosticBag GetInstance()
    {
        return s_poolWithDiagnostics.Allocate();
    }

    internal static BindingDiagnosticBag GetInstance(bool withDiagnostics)
    {
        return withDiagnostics
            ? GetInstance()
            : Discarded;
    }

    internal static BindingDiagnosticBag GetInstance(BindingDiagnosticBag template)
    {
        if (template == null)
        {
            throw new ArgumentNullException(nameof(template));
        }

        return GetInstance(template.AccumulatesDiagnostics);
    }

    /// <summary>
    /// Get an instance suitable for concurrent additions to underlying diagnostic bags.
    /// </summary>
    internal static BindingDiagnosticBag GetConcurrentInstance()
    {
        return s_poolWithConcurrentDiagnostics.Allocate();
    }

    public override string ToString()
    {
        if (IsEmptyWithoutResolution)
        {
            return "<no diagnostics>";
        }

        var builder = new StringBuilder();
        foreach (var diagnostic in AsSemanticEnumerable())
        {
            builder.AppendLine(diagnostic.ToString());
        }

        foreach (var diagnostic in AsCSharpEnumerable())
        {
            builder.AppendLine(diagnostic.ToString());
        }

        return builder.ToString();
    }

    private bool IsSemanticEmpty
    {
        get
        {
            var bag = _semanticDiagnostics;
            return bag == null || bag.IsEmpty;
        }
    }

    private ConcurrentQueue<AkburaSemanticDiagnostic> SemanticDiagnostics
    {
        get
        {
            var bag = _semanticDiagnostics;
            if (bag != null)
            {
                return bag;
            }

            var newBag = new ConcurrentQueue<AkburaSemanticDiagnostic>();
            return Interlocked.CompareExchange(ref _semanticDiagnostics, newBag, null) ?? newBag;
        }
    }

    private ConcurrentDictionary<string, byte> Seen
    {
        get
        {
            var seen = _seen;
            if (seen != null)
            {
                return seen;
            }

            var newSeen = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
            return Interlocked.CompareExchange(ref _seen, newSeen, null) ?? newSeen;
        }
    }

    private bool MarkSeen(string key)
    {
        return Seen.TryAdd(key, 0);
    }

    private static ImmutableArray<AkburaSemanticDiagnostic> ToReadOnlyCore(
        ConcurrentQueue<AkburaSemanticDiagnostic>? oldBag)
    {
        return oldBag == null || oldBag.IsEmpty
            ? ImmutableArray<AkburaSemanticDiagnostic>.Empty
            : oldBag.ToImmutableArray();
    }

    private static string CreateSemanticKey(AkburaSemanticDiagnostic diagnostic)
    {
        return string.Join(
            "|",
            "A",
            diagnostic.Code,
            diagnostic.Severity.ToString(),
            diagnostic.Syntax.FullSpan.ToString(),
            string.Join(",", diagnostic.Parameters.Select(parameter => parameter?.ToString() ?? "<null>")));
    }

    private static string CreateCSharpKey(Diagnostic diagnostic)
    {
        return string.Join(
            "|",
            "C",
            diagnostic.Id,
            diagnostic.Severity.ToString(),
            diagnostic.Location.SourceSpan.ToString(),
            diagnostic.GetMessage());
    }

    private string GetDebuggerDisplay()
    {
        return "Count = " + Count;
    }

    private sealed class PooledBindingDiagnosticBag : BindingDiagnosticBag
    {
        private readonly ObjectPool<BindingDiagnosticBag>? _pool;

        internal PooledBindingDiagnosticBag(
            ObjectPool<BindingDiagnosticBag>? pool,
            DiagnosticBag? diagnosticBag,
            bool accumulatesSemanticDiagnostics)
            : base(diagnosticBag, accumulatesSemanticDiagnostics)
        {
            _pool = pool;
        }

        internal override void Free()
        {
            if (_pool is { } pool)
            {
                Clear();
                pool.Free(this);
            }
            else
            {
                base.Free();
            }
        }
    }
}

internal readonly struct ReadOnlyBindingDiagnostic : IEquatable<ReadOnlyBindingDiagnostic>
{
    private readonly ImmutableArray<AkburaSemanticDiagnostic> _semanticDiagnostics;
    private readonly ImmutableArray<Diagnostic> _csharpDiagnostics;

    public static readonly ReadOnlyBindingDiagnostic Empty = new(
        ImmutableArray<AkburaSemanticDiagnostic>.Empty,
        ImmutableArray<Diagnostic>.Empty);

    public ReadOnlyBindingDiagnostic(
        ImmutableArray<AkburaSemanticDiagnostic> semanticDiagnostics,
        ImmutableArray<Diagnostic> csharpDiagnostics)
    {
        _semanticDiagnostics = semanticDiagnostics.IsDefault
            ? ImmutableArray<AkburaSemanticDiagnostic>.Empty
            : semanticDiagnostics;
        _csharpDiagnostics = csharpDiagnostics.IsDefault
            ? ImmutableArray<Diagnostic>.Empty
            : csharpDiagnostics;
    }

    public ImmutableArray<AkburaSemanticDiagnostic> SemanticDiagnostics =>
        _semanticDiagnostics.IsDefault
            ? ImmutableArray<AkburaSemanticDiagnostic>.Empty
            : _semanticDiagnostics;

    public ImmutableArray<Diagnostic> CSharpDiagnostics =>
        _csharpDiagnostics.IsDefault
            ? ImmutableArray<Diagnostic>.Empty
            : _csharpDiagnostics;

    public bool IsEmpty =>
        SemanticDiagnostics.IsEmpty &&
        CSharpDiagnostics.IsEmpty;

    public ReadOnlyBindingDiagnostic NullToEmpty()
    {
        return new ReadOnlyBindingDiagnostic(
            SemanticDiagnostics,
            CSharpDiagnostics);
    }

    public static bool operator ==(
        ReadOnlyBindingDiagnostic first,
        ReadOnlyBindingDiagnostic second)
    {
        return first.SemanticDiagnostics == second.SemanticDiagnostics &&
               first.CSharpDiagnostics == second.CSharpDiagnostics;
    }

    public static bool operator !=(
        ReadOnlyBindingDiagnostic first,
        ReadOnlyBindingDiagnostic second)
    {
        return !(first == second);
    }

    public override bool Equals(object? obj)
    {
        return obj is ReadOnlyBindingDiagnostic other &&
               Equals(other);
    }

    public bool Equals(ReadOnlyBindingDiagnostic other)
    {
        return this == other;
    }

    public override int GetHashCode()
    {
        return SemanticDiagnostics.GetHashCode();
    }

    public bool HasAnyErrors()
    {
        foreach (var diagnostic in SemanticDiagnostics)
        {
            if (diagnostic.Severity == AkburaDiagnosticSeverity.Error)
            {
                return true;
            }
        }

        foreach (var diagnostic in CSharpDiagnostics)
        {
            if (diagnostic.DefaultSeverity == DiagnosticSeverity.Error)
            {
                return true;
            }
        }

        return false;
    }

    public bool HasAnyResolvedErrors()
    {
        return HasAnyErrors();
    }
}
