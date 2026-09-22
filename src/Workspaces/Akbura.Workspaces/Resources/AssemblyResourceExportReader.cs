using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace Akbura.Workspaces.Resources;

internal static class AssemblyResourceExportReader
{
    internal const string AttributeMetadataName =
        "Akbura.CompilerAnotations." +
        "ExportResourceForAkburaCompletionAttribute";

    private const string AttributeAssemblyName = "Akbura";

    public static ImmutableArray<AssemblyResourceExport> Read(Compilation compilation, CancellationToken cancellationToken = default)
    {
        if (compilation == null)
        {
            throw new ArgumentNullException(nameof(compilation));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var attributeType = FindAttributeType(
            compilation,
            cancellationToken);
        if (attributeType == null)
        {
            return ImmutableArray<AssemblyResourceExport>.Empty;
        }

        using var exports =
            ImmutableArrayBuilder<AssemblyResourceExport>.Rent();

        ReadAssembly(
            compilation.Assembly,
            attributeType,
            exports,
            cancellationToken);

        foreach (var reference in compilation.References)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryGetAssembly(
                    compilation,
                    reference,
                    out var assembly))
            {
                continue;
            }

            ReadAssembly(
                assembly,
                attributeType,
                exports,
                cancellationToken);
        }

        return exports.ToImmutable();
    }

    private static INamedTypeSymbol? FindAttributeType(Compilation compilation, CancellationToken cancellationToken)
    {
        if (string.Equals(
                compilation.Assembly.Identity.Name,
                AttributeAssemblyName,
                StringComparison.Ordinal))
        {
            var sourceType = compilation.Assembly.GetTypeByMetadataName(
                AttributeMetadataName);
            if (sourceType != null)
            {
                return sourceType;
            }
        }

        foreach (var reference in compilation.References)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryGetAssembly(
                    compilation,
                    reference,
                    out var assembly) ||
                !string.Equals(
                    assembly.Identity.Name,
                    AttributeAssemblyName,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var attributeType = assembly.GetTypeByMetadataName(
                AttributeMetadataName);
            if (attributeType != null)
            {
                return attributeType;
            }
        }

        return null;
    }

    private static bool TryGetAssembly(Compilation compilation, MetadataReference reference, out IAssemblySymbol assembly)
    {
        try
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is
                IAssemblySymbol referencedAssembly)
            {
                assembly = referencedAssembly;
                return true;
            }
        }
        catch (BadImageFormatException)
        {
        }
        catch (InvalidCastException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (NotSupportedException)
        {
        }

        assembly = null!;
        return false;
    }

    private static void ReadAssembly(IAssemblySymbol assembly, INamedTypeSymbol attributeType, ImmutableArrayBuilder<AssemblyResourceExport> exports, CancellationToken cancellationToken)
    {
        var assemblyIdentity = ResourceAssemblyIdentity.Create(assembly);

        foreach (var attribute in assembly.GetAttributes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!SymbolEqualityComparer.Default.Equals(
                    attribute.AttributeClass,
                    attributeType) ||
                !TryRead(
                    attribute,
                    assemblyIdentity,
                    out var export))
            {
                continue;
            }

            exports.Add(export);
        }
    }

    private static bool TryRead(AttributeData attribute, ResourceAssemblyIdentity assembly, out AssemblyResourceExport export)
    {
        export = default;
        var arguments = attribute.ConstructorArguments;
        if (arguments.Length != 3 ||
            arguments[0].Value is not string dictionaryPath ||
            arguments[1].Value is not string key ||
            string.IsNullOrWhiteSpace(key) ||
            !ResourcePath.TryCreateExportPath(
                dictionaryPath,
                out var resourcePath))
        {
            return false;
        }

        var typeArgument = arguments[2];
        if (typeArgument.Kind != TypedConstantKind.Type &&
            typeArgument.Kind != TypedConstantKind.Error)
        {
            return false;
        }

        var resourceType = typeArgument.Value as ITypeSymbol;
        if (resourceType?.TypeKind == TypeKind.Error)
        {
            resourceType = null;
        }

        export = new AssemblyResourceExport(
            new ResourceDictionaryIdentity(assembly, resourcePath),
            key,
            resourceType);
        return true;
    }
}
