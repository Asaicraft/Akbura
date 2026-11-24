using Akbura.Collections;
using Akbura.Pools;
using Akbura.Workspaces.Projects;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Microsoft.CodeAnalysis;
using System.Reflection;

namespace Akbura.AvaloniaEdit;

/// <summary>
/// Configures real Avalonia and Akbura compiler references without adding UI dependencies to Workspaces.
/// </summary>
public static class AkburaProjectExtensions
{
    private static readonly string[] FrameworkAssemblyNames =
    [
        "System.Runtime",
        "System.Collections",
        "System.Linq",
        "System.ObjectModel",
        "System.ComponentModel.Primitives",
    ];

    private static readonly ArrayElement<Assembly>[] Roots =
    [
        new() { Value = typeof(Control).Assembly },
        new() { Value = typeof(AvaloniaObject).Assembly },
        new() { Value = typeof(AvaloniaXamlLoader).Assembly },
        new() { Value = typeof(AkburaControl).Assembly },
    ];

    /// <summary>
    /// Configures the project's resolver, or prepares a catalog from the desktop .NET host's
    /// trusted platform assembly list and the explicit Avalonia/Akbura assembly locations.
    /// </summary>
    /// <remarks>
    /// Preparation is synchronous. Hosts should schedule it before binding the editor.
    /// Browser hosts without a configured resolver must use <see cref="UseAvalonia(AkburaProject, IAkburaReferenceResolver)"/>.
    /// No assemblies are loaded and no machine directories or loaded-assembly lists are searched.
    /// </remarks>
    public static void UseAvalonia(this AkburaProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        UseAvalonia(project, project.ReferenceResolver ?? DesktopReferences());
    }

    /// <summary>
    /// Publishes one coherent configuration using real metadata prepared by the host.
    /// </summary>
    /// <remarks>
    /// Supply compile-time assets for one target framework, including their dependencies.
    /// References expose types to language services and do not install a preview runtime.
    /// </remarks>
    public static void UseAvalonia(this AkburaProject project, IAkburaReferenceResolver preparedReferenceSet)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(preparedReferenceSet);
        project.Update(update =>
        {
            update.ReferenceResolver = preparedReferenceSet;

            foreach (var name in FrameworkAssemblyNames)
            {
                var reference = preparedReferenceSet.Resolve(new AssemblyIdentity(name)) ??
                    throw new InvalidOperationException($"UseAvalonia requires real compiler metadata for '{name}'. Supply a complete prepared reference set from the host's resolved dependencies.");
                update.AddReference(reference);
            }

            foreach (var assembly in Roots)
            {
                update.AddAssembly(assembly.Value);
            }
        });
    }

    private static AkburaReferenceSet DesktopReferences()
    {
        if (OperatingSystem.IsBrowser() || AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is not string paths)
        {
            throw new InvalidOperationException("UseAvalonia requires prepared compiler metadata on this host. Call UseAvalonia(preparedReferenceSet), or configure project.ReferenceResolver first.");
        }

        using var explicitPaths = ImmutableArrayBuilder<string>.Rent();
        var seen = PooledHashSet<string>.GetInstance();
        try
        {
            foreach (var path in paths.Split(Path.PathSeparator))
            {
                if (seen.Add(path))
                {
                    explicitPaths.Add(path);
                }
            }
            foreach (var root in Roots)
            {
                var assembly = root.Value;
                var location = assembly.Location;
                if (string.IsNullOrEmpty(location))
                {
                    throw new InvalidOperationException($"Metadata for '{assembly.GetName().Name}' has no file location. Call UseAvalonia(preparedReferenceSet).");
                }
                if (seen.Add(location))
                {
                    explicitPaths.Add(location);
                }
            }
            return AkburaReferenceSet.FromFiles(explicitPaths.AsEnumerable());
        }
        finally
        {
            seen.Free();
        }
    }
}
