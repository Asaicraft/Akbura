namespace Akbura.Workspaces.Resources;

internal readonly struct ResourceDictionaryIdentity :
    IEquatable<ResourceDictionaryIdentity>
{
    public ResourceDictionaryIdentity(ResourceAssemblyIdentity assembly, ResourcePath path)
    {
        if (assembly.IsDefault)
        {
            throw new ArgumentException(
                "An assembly identity is required.",
                nameof(assembly));
        }

        if (path.IsDefault)
        {
            throw new ArgumentException(
                "A resource path is required.",
                nameof(path));
        }

        Assembly = assembly;
        Path = path;
    }

    public ResourceAssemblyIdentity Assembly { get; }

    public ResourcePath Path { get; }

    public bool Equals(ResourceDictionaryIdentity other)
    {
        return Assembly.Equals(other.Assembly) &&
            Path.Equals(other.Path);
    }

    public override bool Equals(object? obj)
    {
        return obj is ResourceDictionaryIdentity other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return (Assembly.GetHashCode() * 397) ^ Path.GetHashCode();
        }
    }

    public override string ToString()
    {
        return Assembly.Name + "/" + Path.Value;
    }

    public static bool operator ==(ResourceDictionaryIdentity left, ResourceDictionaryIdentity right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ResourceDictionaryIdentity left, ResourceDictionaryIdentity right)
    {
        return !left.Equals(right);
    }
}
