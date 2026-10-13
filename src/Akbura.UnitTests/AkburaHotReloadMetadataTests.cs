using Akbura.HotReload;
using System.Collections.Immutable;

namespace Akbura.UnitTests;

public sealed class AkburaHotReloadMetadataTests
{
    [Fact]
    public void RegisterComponentMetadata_CapturesInitialSnapshotAndKeepsItOnRepeatedRegistration()
    {
        var properties = ImmutableArray.Create(
            new AkburaHotReloadPropertyRegistration("padding", AkburaControl.PaddingProperty));
        var metadata = AkburaHotReloadRuntime.RegisterComponentMetadata(
            typeof(RegisteredComponent), "descriptors:old", "states:old", properties);

        var repeated = AkburaHotReloadRuntime.RegisterComponentMetadata(
            typeof(RegisteredComponent), "descriptors:new", "states:new", []);

        Assert.Same(metadata, repeated);
        Assert.Same(metadata, AkburaHotReloadRuntime.GetComponentMetadata(typeof(RegisteredComponent)));
        Assert.Equal("descriptors:old", metadata.DescriptorShape);
        Assert.Equal("states:old", metadata.StateShape);
        Assert.True(properties == metadata.Properties);
    }

    [Fact]
    public void UpdateSnapshots_ReplacesDescriptorsAndStatesIndependently()
    {
        var metadata = AkburaHotReloadRuntime.RegisterComponentMetadata(
            typeof(UpdatedComponent), "old", "old states", []);
        var properties = ImmutableArray.Create(
            new AkburaHotReloadPropertyRegistration("padding", AkburaControl.PaddingProperty));

        metadata.UpdateDescriptors("new", properties);
        Assert.Equal("new", metadata.DescriptorShape);
        Assert.True(properties == metadata.Properties);
        Assert.Equal("old states", metadata.StateShape);

        metadata.UpdateStateShape("new states");
        Assert.Equal("new states", metadata.StateShape);
        Assert.True(properties == metadata.Properties);
        Assert.Equal("new", metadata.DescriptorShape);
    }

    [Fact]
    public void GetComponentMetadata_RunsStaticInitializationBeforeReadingSnapshot()
    {
        var metadata = AkburaHotReloadRuntime.GetComponentMetadata(typeof(InitializedComponent));

        Assert.Equal("initial descriptors", metadata.DescriptorShape);
        Assert.Equal("initial states", metadata.StateShape);
        Assert.Empty(metadata.Properties);
    }

    [Fact]
    public void GetComponentMetadata_RejectsUnregisteredTypes()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AkburaHotReloadRuntime.GetComponentMetadata(typeof(UnregisteredComponent)));
    }

    [Fact]
    public void InvalidSnapshots_DoNotOverwriteCommittedMetadata()
    {
        var metadata = AkburaHotReloadRuntime.RegisterComponentMetadata(
            typeof(InvalidSnapshotComponent), "old", "old states", []);

        Assert.Throws<ArgumentNullException>(() => metadata.UpdateDescriptors(null!, []));
        Assert.Throws<ArgumentException>(() => metadata.UpdateDescriptors("new", default));
        Assert.Throws<ArgumentNullException>(() => metadata.UpdateStateShape(null!));

        Assert.Equal("old", metadata.DescriptorShape);
        Assert.Equal("old states", metadata.StateShape);
        Assert.False(metadata.Properties.IsDefault);
        Assert.Empty(metadata.Properties);
    }

    [Fact]
    public void RegisterComponentMetadata_ValidatesInputsAndIsolatesComponentTypes()
    {
        Assert.Throws<ArgumentNullException>(() =>
            AkburaHotReloadRuntime.RegisterComponentMetadata(null!, "", "", []));
        Assert.Throws<ArgumentNullException>(() =>
            AkburaHotReloadRuntime.RegisterComponentMetadata(typeof(FirstComponent), null!, "", []));
        Assert.Throws<ArgumentNullException>(() =>
            AkburaHotReloadRuntime.RegisterComponentMetadata(typeof(FirstComponent), "", null!, []));
        Assert.Throws<ArgumentException>(() =>
            AkburaHotReloadRuntime.RegisterComponentMetadata(typeof(FirstComponent), "", "", default));
        var first = AkburaHotReloadRuntime.RegisterComponentMetadata(typeof(FirstComponent), "first", "", []);
        var second = AkburaHotReloadRuntime.RegisterComponentMetadata(typeof(SecondComponent), "second", "", []);

        Assert.NotSame(first, second);
        Assert.Equal("first", first.DescriptorShape);
        Assert.Equal("second", second.DescriptorShape);
    }

    private abstract class RegisteredComponent : AkburaControl;
    private abstract class UpdatedComponent : AkburaControl;
    private abstract class UnregisteredComponent : AkburaControl;
    private abstract class InvalidSnapshotComponent : AkburaControl;
    private abstract class FirstComponent : AkburaControl;
    private abstract class SecondComponent : AkburaControl;

    private abstract class InitializedComponent : AkburaControl
    {
        static InitializedComponent()
        {
            AkburaHotReloadRuntime.RegisterComponentMetadata(
                typeof(InitializedComponent), "initial descriptors", "initial states", []);
        }
    }
}
