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
