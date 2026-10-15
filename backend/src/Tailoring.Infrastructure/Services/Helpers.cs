using Tailoring.Application.Common;

namespace Tailoring.Infrastructure.Services;

internal static class Parse
{
    public static T Value<T>(string raw, string field) where T : struct, Enum
    {
        if (!Enum.TryParse<T>(raw?.Trim(), ignoreCase: true, out var value) || !Enum.IsDefined(value))
            throw AppException.BadRequest($"invalid_{field}", $"'{raw}' is not a valid {field}.");
        return value;
    }

    /// <summary>Escapes LIKE wildcards so user input is matched literally.</summary>
    public static string Like(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    public static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    public static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
