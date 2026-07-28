using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Akbura.Engine;


/// <summary>
/// We reserve the right to change it in the future.
/// </summary>
public sealed class AkburaEngine
{
    public const int DefaultMaxUpdatesPerBatch = 100;

    public static readonly AkburaEngine Empty = new(EmptyServiceProvider.Instance);

    private int _maxUpdatesPerBatch = DefaultMaxUpdatesPerBatch;

    /// <summary>
    /// Gets or initializes the fallback engine instance used when an
    /// <see cref="AkburaEngine"/> is not provided explicitly.
    /// </summary>
    public static AkburaEngine Singletone
    {
        get
        {
            if (field == null)
            {
                throw new InvalidOperationException();
            }

            return field;
        }

        set
        {
            if (field != null)
            {
                throw new InvalidOperationException(
                    "The AkburaEngine singleton instance has already been initialized.");
            }

            field = value;
        }
    }

    private readonly IAkburaServiceProvider _serviceProvider;

    /// <summary>
    /// Gets or sets the maximum number of consecutive <c>Update()</c> passes
    /// that one component can execute in a single synchronous batch.
    /// </summary>
    /// <remarks>
    /// The limit prevents a component that changes its own state or parameters
    /// on every pass from keeping the application in an infinite update loop.
    /// </remarks>
    public int MaxUpdatesPerBatch
    {
        get => _maxUpdatesPerBatch;
        set
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "The maximum update count must be greater than zero.");
            }

            _maxUpdatesPerBatch = value;
        }
    }

    public AkburaEngine(IAkburaServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }


    public object? GetService(AkburaControl control, Type type, bool? optional = null, string? fieldName = null)
    {
        var injectInfo = new InjectionInfo(
                RequestedService: type,
                TargetControl: control,
                NextProvider: null, 
                IsOptional: optional,
                FieldName: fieldName);

        return _serviceProvider.GetService(in injectInfo);
    }
}
