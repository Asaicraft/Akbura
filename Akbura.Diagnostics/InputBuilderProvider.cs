using System.Collections;

namespace Akbura.Diagnostics;

internal sealed class InputBuilderProvider : IInputBuilderProvider
{
    private readonly IReadOnlyList<InputBuilder> _builders;

    public InputBuilderProvider(IEnumerable<InputBuilder> builders)
    {
        ArgumentNullException.ThrowIfNull(builders);

        _builders = builders.ToArray();
        if (_builders.Any(static builder => builder is null))
        {
            throw new ArgumentException(
                "Input builders cannot contain null values.",
                nameof(builders));
        }
    }

    public int Count => _builders.Count;

    public InputBuilder this[int index] => _builders[index];

    public InputBuilder Provide(InputRequest inputRequest)
    {
        return Provides(inputRequest).FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"No input builder can edit '{inputRequest.RequestedType}'.");
    }

    public IEnumerable<InputBuilder> Provides(InputRequest inputRequest)
    {
        ArgumentNullException.ThrowIfNull(inputRequest);

        return _builders.Where(builder => builder.CanProvide(inputRequest));
    }

    public IEnumerator<InputBuilder> GetEnumerator()
    {
        return _builders.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    internal static List<InputBuilder> CreateDefaultBuilders()
    {
        return
        [
            new StringInputBuilder(),
            new NumericInputBuilder<byte>(),
            new NumericInputBuilder<sbyte>(),
            new NumericInputBuilder<short>(),
            new NumericInputBuilder<ushort>(),
            new NumericInputBuilder<int>(),
            new NumericInputBuilder<uint>(),
            new NumericInputBuilder<long>(),
            new NumericInputBuilder<ulong>(),
            new NumericInputBuilder<float>(),
            new NumericInputBuilder<double>(),
            new NumericInputBuilder<decimal>(),
            new NumericInputBuilder<byte>(isNullable: true),
            new NumericInputBuilder<sbyte>(isNullable: true),
            new NumericInputBuilder<short>(isNullable: true),
            new NumericInputBuilder<ushort>(isNullable: true),
            new NumericInputBuilder<int>(isNullable: true),
            new NumericInputBuilder<uint>(isNullable: true),
            new NumericInputBuilder<long>(isNullable: true),
            new NumericInputBuilder<ulong>(isNullable: true),
            new NumericInputBuilder<float>(isNullable: true),
            new NumericInputBuilder<double>(isNullable: true),
            new NumericInputBuilder<decimal>(isNullable: true),
            new UniversalInputBuilder(),
        ];
    }
}
