using Microsoft.CodeAnalysis;
using System;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Akbura.Language;

internal static class PortableExecutableResourceReader
{
    public static bool TryOpenResource(
        PortableExecutableReference reference,
        string resourceName,
        out Stream? stream)
    {
        if (reference == null)
        {
            throw new ArgumentNullException(nameof(reference));
        }

        if (string.IsNullOrEmpty(resourceName))
        {
            throw new ArgumentException("Resource name cannot be empty.", nameof(resourceName));
        }

        stream = null;
        if (string.IsNullOrWhiteSpace(reference.FilePath))
        {
            return false;
        }

        try
        {
            using var file = new FileStream(
                reference.FilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var peReader = new PEReader(file, PEStreamOptions.LeaveOpen);
            if (!peReader.HasMetadata || peReader.PEHeaders.CorHeader == null)
            {
                return false;
            }

            var metadataReader = peReader.GetMetadataReader();
            foreach (var handle in metadataReader.ManifestResources)
            {
                var resource = metadataReader.GetManifestResource(handle);
                if (!resource.Implementation.IsNil ||
                    metadataReader.GetString(resource.Name) != resourceName)
                {
                    continue;
                }

                var resourceDirectory = peReader.PEHeaders.CorHeader.ResourcesDirectory;
                var resourceOffset = checked(
                    resourceDirectory.RelativeVirtualAddress + (int)resource.Offset);
                var reader = peReader.GetSectionData(resourceOffset).GetReader();
                if (reader.RemainingBytes < sizeof(int))
                {
                    throw new InvalidDataException(
                        $"Resource '{resourceName}' in '{reference.FilePath}' is truncated.");
                }

                var length = reader.ReadInt32();
                if (length < 0 || reader.RemainingBytes < length)
                {
                    throw new InvalidDataException(
                        $"Resource '{resourceName}' in '{reference.FilePath}' has an invalid length.");
                }

                stream = new MemoryStream(reader.ReadBytes(length), writable: false);
                return true;
            }
        }
        catch (BadImageFormatException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return false;
    }
}
