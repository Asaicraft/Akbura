using Akbura.Language;
using Microsoft.Build.Framework;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Akbura.Build;

internal sealed class AkburaManifestCache
{
    private const int SchemaVersion = 1;
    private readonly AkburaManifestTask _task;
    private readonly string _outputPath;
    private readonly string _owner;

    public AkburaManifestCache(AkburaManifestTask task)
    {
        _task = task;
        _outputPath = NormalizePath(task.OutputPath);
        var intermediate = NormalizePath(string.IsNullOrEmpty(task.IntermediatePath) ? "obj" : task.IntermediatePath);
        _owner = string.Join('\n', NormalizePath(string.IsNullOrEmpty(task.ProjectPath) ? task.ProjectDirectory : task.ProjectPath),
            intermediate, task.Configuration, task.TargetFramework, task.RuntimeIdentifier, _outputPath);
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_owner)));
        StatePath = Path.Combine(intermediate, $"Akbura.module.{identity[..16]}.cache.json");
        LockPath = _outputPath + ".lock";
    }

    public string StatePath { get; }
    public string LockPath { get; }
    public long HashedBytes { get; private set; }

    public FileStream AcquireOutputLock()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_outputPath)!);
        FileStream stream;
        try
        {
            stream = new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException exception)
        {
            throw new IOException($"Akbura manifest '{_outputPath}' is in use by another build. Use distinct manifest/intermediate paths for concurrent builds.", exception);
        }

        try
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            var owner = reader.ReadToEnd();
            var checksumSeparator = owner.LastIndexOf('\n');
            var hasValidOwner = checksumSeparator >= 0 &&
                owner[(checksumSeparator + 1)..] == HashText(owner[..checksumSeparator]);
            var expectedOwner = _owner + '\n' + HashText(_owner);
            if (hasValidOwner && owner != expectedOwner)
            {
                throw new IOException($"Akbura manifest '{_outputPath}' belongs to a different project/configuration/framework/intermediate path. Set a distinct AkburaModuleManifestPath. To transfer ownership, remove its .lock file only after all builds have finished.");
            }

            if (owner != expectedOwner)
            {
                stream.Position = 0;
                stream.SetLength(0);
                stream.Write(Encoding.UTF8.GetBytes(expectedOwner));
                stream.Flush(flushToDisk: true);
            }

            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public string ComputeFingerprint()
    {
        HashedBytes = 0;
        var fileHashes = new Dictionary<string, string>(PathComparer);
        using var data = new MemoryStream();
        using var writer = new BinaryWriter(data, Encoding.UTF8, leaveOpen: true);
        writer.Write(SchemaVersion);
        writer.Write(AkburaModuleManifest.CurrentFormatVersion);
        writer.Write(_owner);
        writer.Write(_task.AssemblyName);
        writer.Write(_task.RootNamespace);
        writer.Write(NormalizePath(_task.ProjectDirectory));
        writer.Write(_task.DefineConstants);
        writer.Write(_task.LanguageVersion);
        writer.Write(_task.Nullable);
        writer.Write(_task.AllowUnsafeBlocks);
        WriteItems(_task.Sources, includeLogicalName: true);
        WriteItems(_task.CSharpSources, includeLogicalName: false);
        WriteItems(_task.References, includeLogicalName: false);

        // Hash the deployment, not assembly versions: local rebuilds often keep the same version.
        // This also invalidates on Roslyn/dependency or manifest implementation updates.
        var toolFiles = Directory.EnumerateFiles(Path.GetDirectoryName(typeof(AkburaManifestCache).Assembly.Location)!)
            .Where(path => Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal).ToArray();
        writer.Write(toolFiles.Length);
        foreach (var file in toolFiles)
        {
            _task.CancellationToken.ThrowIfCancellationRequested();
            writer.Write(NormalizePath(file));
            writer.Write(HashFile(file));
        }

        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(data.GetBuffer().AsSpan(0, checked((int)data.Length))));

        void WriteItems(ITaskItem[] items, bool includeLogicalName)
        {
            writer.Write(items.Length);
            foreach (var item in items)
            {
                _task.CancellationToken.ThrowIfCancellationRequested();
                var path = NormalizePath(_task.GetFullPath(item));
                writer.Write(path);
                if (includeLogicalName)
                {
                    writer.Write(item.GetMetadata("LogicalName").Replace('\\', '/'));
                }

                writer.Write(HashFile(path));
            }
        }

        string HashFile(string path)
        {
            if (!fileHashes.TryGetValue(path, out var result))
            {
                using var stream = File.OpenRead(path);
                HashedBytes += stream.Length;
                result = Convert.ToHexString(SHA256.HashData(stream));
                fileHashes.Add(path, result);
            }

            return result;
        }
    }

    public bool IsCurrent(string fingerprint)
    {
        try
        {
            var state = JsonSerializer.Deserialize<SuccessfulState>(File.ReadAllBytes(StatePath));
            return state is { SchemaVersion: SchemaVersion } && state.InputFingerprint == fingerprint &&
                state.OutputFingerprint == HashOutput();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Invalidate()
    {
        File.Delete(StatePath);
    }

    public void Publish(string fingerprint)
    {
        _task.CancellationToken.ThrowIfCancellationRequested();
        var state = new SuccessfulState(SchemaVersion, fingerprint, HashOutput());
        WriteAtomically(StatePath, JsonSerializer.SerializeToUtf8Bytes(state));
        if (_task.CancellationToken.IsCancellationRequested)
        {
            Invalidate();
            _task.CancellationToken.ThrowIfCancellationRequested();
        }
    }

    public static void WriteAtomically(string path, byte[] content)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private string HashOutput()
    {
        using var stream = File.OpenRead(_outputPath);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private string NormalizePath(string path)
    {
        var fullPath = Path.GetFullPath(path, _task.ProjectDirectory);
        return OperatingSystem.IsWindows() ? fullPath.ToUpperInvariant() : fullPath;
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static string HashText(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private sealed record SuccessfulState(int SchemaVersion, string InputFingerprint, string OutputFingerprint);
}
