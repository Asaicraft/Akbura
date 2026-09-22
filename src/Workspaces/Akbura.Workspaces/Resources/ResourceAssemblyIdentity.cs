using Microsoft.CodeAnalysis;

namespace Akbura.Workspaces.Resources;

internal readonly struct ResourceAssemblyIdentity :
    IEquatable<ResourceAssemblyIdentity>
{
    private readonly AssemblyIdentity? _identity;

    private ResourceAssemblyIdentity(AssemblyIdentity identity)
    {
        _identity = identity;
    }

    public string Name => _identity?.Name ?? string.Empty;

    public AssemblyIdentity RoslynIdentity =>
        _identity ?? throw new InvalidOperationException(
            "The resource assembly identity is uninitialized.");

    public bool IsDefault => _identity == null;

    public static ResourceAssemblyIdentity Create(IAssemblySymbol assembly)
    {
        if (assembly == null)
        {
            throw new ArgumentNullException(nameof(assembly));
        }

        return new ResourceAssemblyIdentity(assembly.Identity);
    }

    public static ResourceAssemblyIdentity Create(AssemblyIdentity identity)
    {
        if (identity == null)
        {
            throw new ArgumentNullException(nameof(identity));
        }

        return new ResourceAssemblyIdentity(identity);
    }

    public bool HasSimpleName(string assemblyName)
    {
        return !string.IsNullOrWhiteSpace(assemblyName) &&
            string.Equals(
                Name,
                assemblyName,
                StringComparison.OrdinalIgnoreCase);
    }

    public bool Equals(ResourceAssemblyIdentity other)
    {
        if (_identity == null || other._identity == null)
        {
            return _identity == other._identity;
        }

        return _identity.Equals(other._identity);
    }

    public override bool Equals(object? obj)
    {
        return obj is ResourceAssemblyIdentity other && Equals(other);
    }

    public override int GetHashCode()
    {
        return _identity?.GetHashCode() ?? 0;
    }

    public override string ToString()
    {
        return _identity?.ToString() ?? string.Empty;
    }

    public static bool operator ==(ResourceAssemblyIdentity left, ResourceAssemblyIdentity right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ResourceAssemblyIdentity left, ResourceAssemblyIdentity right)
    {
        return !left.Equals(right);
    }
}
