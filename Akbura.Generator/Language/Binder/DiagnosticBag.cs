// This file is ported and adapted from the Roslyn (dotnet/roslyn)

using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace Akbura.Language.Binder;

/// <summary>
/// Represents a mutable bag of Roslyn diagnostics collected during binding.
/// </summary>
/// <remarks>
/// Concurrent Add is supported. Concurrent Add with Clear or Free is not supported.
/// </remarks>
[DebuggerDisplay("{GetDebuggerDisplay(), nq}")]
internal sealed class DiagnosticBag
{
    private ConcurrentQueue<Diagnostic>? _lazyBag;

    public bool IsEmptyWithoutResolution
    {
        get
        {
            var bag = _lazyBag;
            return bag == null || bag.IsEmpty;
        }
    }

    public int Count => _lazyBag?.Count ?? 0;

    public bool HasAnyErrors()
    {
        if (IsEmptyWithoutResolution)
        {
            return false;
        }

        foreach (var diagnostic in Bag)
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

    public void Add(Diagnostic diagnostic)
    {
        if (diagnostic == null)
        {
            throw new ArgumentNullException(nameof(diagnostic));
        }

        Bag.Enqueue(diagnostic);
    }

    public void AddRange<TDiagnostic>(ImmutableArray<TDiagnostic> diagnostics)
        where TDiagnostic : Diagnostic
    {
        if (diagnostics.IsDefaultOrEmpty)
        {
            return;
        }

        var bag = Bag;
        for (var index = 0; index < diagnostics.Length; index++)
        {
            bag.Enqueue(diagnostics[index]);
        }
    }

    public void AddRange(IEnumerable<Diagnostic> diagnostics)
    {
        if (diagnostics == null)
        {
            throw new ArgumentNullException(nameof(diagnostics));
        }

        var bag = Bag;
        foreach (var diagnostic in diagnostics)
        {
            bag.Enqueue(diagnostic);
        }
    }

    public void AddRange(DiagnosticBag bag)
    {
        if (bag == null)
        {
            throw new ArgumentNullException(nameof(bag));
        }

        if (!bag.IsEmptyWithoutResolution)
        {
            AddRange(bag.Bag);
        }
    }

    public void AddRangeAndFree(DiagnosticBag bag)
    {
        AddRange(bag);
        bag.Free();
    }

    public ImmutableArray<TDiagnostic> ToReadOnlyAndFree<TDiagnostic>()
        where TDiagnostic : Diagnostic
    {
        var oldBag = _lazyBag;
        Free();
        return ToReadOnlyCore<TDiagnostic>(oldBag);
    }

    public ImmutableArray<Diagnostic> ToReadOnlyAndFree()
    {
        return ToReadOnlyAndFree<Diagnostic>();
    }

    public ImmutableArray<TDiagnostic> ToReadOnly<TDiagnostic>()
        where TDiagnostic : Diagnostic
    {
        return ToReadOnlyCore<TDiagnostic>(_lazyBag);
    }

    public ImmutableArray<Diagnostic> ToReadOnly()
    {
        return ToReadOnly<Diagnostic>();
    }

    public IEnumerable<Diagnostic> AsEnumerable()
    {
        return _lazyBag ?? [];
    }

    public void Clear()
    {
        _lazyBag = null;
    }

    public void Free()
    {
        Clear();
        s_pool.Free(this);
    }

    public static DiagnosticBag GetInstance()
    {
        return s_pool.Allocate();
    }

    public override string ToString()
    {
        if (IsEmptyWithoutResolution)
        {
            return "<no diagnostics>";
        }

        var builder = new StringBuilder();
        foreach (var diagnostic in Bag)
        {
            builder.AppendLine(diagnostic.ToString());
        }

        return builder.ToString();
    }

    private ConcurrentQueue<Diagnostic> Bag
    {
        get
        {
            var bag = _lazyBag;
            if (bag != null)
            {
                return bag;
            }

            var newBag = new ConcurrentQueue<Diagnostic>();
            return Interlocked.CompareExchange(ref _lazyBag, newBag, null) ?? newBag;
        }
    }

    private static ImmutableArray<TDiagnostic> ToReadOnlyCore<TDiagnostic>(
        ConcurrentQueue<Diagnostic>? oldBag)
        where TDiagnostic : Diagnostic
    {
        if (oldBag == null)
        {
            return ImmutableArray<TDiagnostic>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<TDiagnostic>();
        foreach (var diagnostic in oldBag)
        {
            builder.Add((TDiagnostic)diagnostic);
        }

        return builder.ToImmutable();
    }

    private string GetDebuggerDisplay()
    {
        return "Count = " + Count;
    }

    private static readonly ObjectPool<DiagnosticBag> s_pool = CreatePool(128);

    private static ObjectPool<DiagnosticBag> CreatePool(int size)
    {
        return new ObjectPool<DiagnosticBag>(() => new DiagnosticBag(), size);
    }
}
