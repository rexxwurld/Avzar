using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Tailoring.Application.Media;

namespace Tailoring.Infrastructure.Storage;

/// <summary>
/// Development / single-server storage. Keys are validated and the resolved path must stay inside the root,
/// so even a bug elsewhere cannot turn a key into a path traversal. Writes are atomic (temp file + move).
/// </summary>
public sealed partial class LocalDiskObjectStorage : IObjectStorage
{
    private readonly string _root;

    public LocalDiskObjectStorage(IOptions<StorageOptions> options)
    {
        _root = Path.GetFullPath(options.Value.LocalRoot);
        Directory.CreateDirectory(_root);
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.\-/]*$")]
    private static partial Regex SafeKey();

    private string Resolve(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > 200 || !SafeKey().IsMatch(key)
            || key.Split('/').Any(seg => seg is "" or "." or ".."))
            throw new ArgumentException("Invalid storage key.", nameof(key));

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
}
