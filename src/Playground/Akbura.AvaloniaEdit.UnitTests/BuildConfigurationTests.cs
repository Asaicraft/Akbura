using Akbura.Collections;
using Akbura.Pools;
using Akbura.AvaloniaEdit.Highlighting;
using Akbura.AvaloniaEdit.Infrastructure;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Akbura.AvaloniaEdit.UnitTests;

public sealed class BuildConfigurationTests
{
    [Fact]
    public void MeasurementsAreCompiledOnlyWithDebug()
    {
        var assembly = typeof(AkburaCodeEditor).Assembly;
        using var measurements = ImmutableArrayBuilder<ArrayElement<Type>>.Rent();
        foreach (var type in assembly.GetTypes())
        {
            if (type.Name is "EditorPerformance" or "Measurement")
            {
                measurements.Add(new() { Value = type });
            }
        }
        using var owners = ImmutableArrayBuilder<ArrayElement<Type>>.Rent(4);
        owners.Add(new() { Value = typeof(AkburaEditorOptions) });
        owners.Add(new() { Value = typeof(EditorServiceQueue) });
        owners.Add(new() { Value = typeof(EditorSyntaxCache) });
        owners.Add(new() { Value = typeof(WorkspaceColorizer) });
#if DEBUG
        Assert.Equal(2, measurements.Count);
        foreach (var owner in owners.WrittenSpan)
        {
            Assert.NotNull(owner.Value.GetProperty("Performance", BindingFlags.Instance | BindingFlags.NonPublic));
        }
#else
        Assert.Equal(0, measurements.Count);
        foreach (var owner in owners.WrittenSpan)
        {
            Assert.Null(owner.Value.GetProperty("Performance", BindingFlags.Instance | BindingFlags.NonPublic));
        }
        using var stream = File.OpenRead(assembly.Location);
        using var reader = new PEReader(stream);
        var metadata = reader.GetMetadataReader();
        Assert.DoesNotContain(metadata.TypeReferences, handle => metadata.GetString(metadata.GetTypeReference(handle).Name) == "Stopwatch");
#endif
    }
}
