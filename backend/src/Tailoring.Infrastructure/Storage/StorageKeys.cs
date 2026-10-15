using System.Text.RegularExpressions;

namespace Tailoring.Infrastructure.Storage;

/// <summary>One definition of what a storage key may look like, shared by every provider.</summary>
public static partial class StorageKeys
{
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.\-/]*$")]
    private static partial Regex Safe();

    public static void EnsureValid(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > 200 || !Safe().IsMatch(key) || key.Split('/').Any(seg => seg is "" or "." or ".."))
            throw new ArgumentException("Invalid storage key.", nameof(key));
    }

    /// <summary>A listing prefix: empty, or key-safe segments (a trailing slash is allowed).</summary>
    public static void EnsureValidPrefix(string prefix)
    {
        if (prefix.Length == 0) return;
        EnsureValid(prefix.TrimEnd('/'));
    }
}
