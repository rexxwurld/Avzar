using Microsoft.Extensions.Options;
using Tailoring.Application.Media;

namespace Tailoring.Infrastructure.Storage;

/// <summary>
/// Development / single-server storage. Keys are validated and the resolved path must stay inside the root,
/// so even a bug elsewhere cannot turn a key into a path traversal. Writes are atomic (temp file + move).
/// </summary>
public sealed class LocalDiskObjectStorage : IObjectStorage
{
    private readonly string _root;

    public LocalDiskObjectStorage(IOptions<StorageOptions> options)
    {
        _root = Path.GetFullPath(options.Value.LocalRoot);
        Directory.CreateDirectory(_root);
    }

    private string Resolve(string key)
    {
        StorageKeys.EnsureValid(key);

        var full = Path.GetFullPath(Path.Combine(_root, key));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Storage key escapes the storage root.", nameof(key));
        return full;
    }

    public async Task PutAsync(string key, ReadOnlyMemory<byte> data, string contentType, CancellationToken ct = default)
    {
        var path = Resolve(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temp, data.ToArray(), ct);
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default)
    {
        var path = Resolve(key);
        Stream? stream = File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true)
            : null;
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var path = Resolve(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<StoredObject> ListAsync(string prefix, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        StorageKeys.EnsureValidPrefix(prefix);
        await Task.Yield();
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var key = Path.GetRelativePath(_root, file).Replace(Path.DirectorySeparatorChar, '/');
            if (!key.StartsWith(prefix, StringComparison.Ordinal)) continue;
            FileInfo info;
            try { info = new FileInfo(file); if (!info.Exists) continue; } catch (IOException) { continue; }
            yield return new StoredObject(key, info.Length, info.LastWriteTimeUtc);
        }
    }
}
