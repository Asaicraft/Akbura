using Avalonia.Controls;
using System;
using System.Collections.Immutable;

namespace Akbura.Akcss;

public abstract class TailwindUtilityActivator : AkcssStyleActivator
{
    protected TailwindUtilityActivator(
        AkcssUtility utility,
        bool isConditional,
        ImmutableArray<object?> arguments)
        : base(utility)
    {
        Utility = utility;
        IsConditional = isConditional;
        Arguments = arguments.IsDefault ? ImmutableArray<object?>.Empty : arguments;
        ValidateArguments();
    }

    public AkcssUtility Utility { get; }

    public override bool IsConditional { get; }

    public abstract override bool Condition { get; }

    public ImmutableArray<object?> Arguments { get; }

    public abstract void Execute(Control control);

    public sealed override void Execute(object target)
    {
        Execute(GetControl(target));
    }

    public virtual void Reset(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        Utility.Reset(control);
    }

    public sealed override void Reset(object target)
    {
        Reset(GetControl(target));
    }

    public virtual IObservable<object?> Watch(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        return Utility.Watch(control);
    }

    public sealed override IObservable<object?> Watch(object target)
    {
        return Watch(GetControl(target));
    }

    private void ValidateArguments()
    {
        var parameterTypes = Utility.Parameters;
        if (parameterTypes.Length != Arguments.Length)
        {
            throw new ArgumentException(
                $"AKCSS utility '{Utility.Name}' expects {parameterTypes.Length} arguments, " +
                $"but {Arguments.Length} were provided.",
                nameof(Arguments));
        }

        for (var index = 0; index < parameterTypes.Length; index++)
        {
            var parameterType = parameterTypes[index];
            var argument = Arguments[index];
            if (argument == null)
            {
                if (parameterType.IsValueType && Nullable.GetUnderlyingType(parameterType) == null)
                {
                    throw CreateArgumentTypeException(index, parameterType, null);
                }
            }
            else if (!parameterType.IsInstanceOfType(argument))
            {
                throw CreateArgumentTypeException(index, parameterType, argument.GetType());
            }
        }
    }

    private ArgumentException CreateArgumentTypeException(
        int index,
        Type expectedType,
        Type? actualType)
    {
        return new ArgumentException(
            $"Argument {index} of AKCSS utility '{Utility.Name}' must be " +
            $"'{expectedType}', not '{actualType?.ToString() ?? "null"}'.",
            nameof(Arguments));
    }

    private static Control GetControl(object target)
    {
        return target as Control ?? throw new ArgumentException(
            $"An AKCSS utility target must derive from '{typeof(Control)}'.",
            nameof(target));
    }
}
