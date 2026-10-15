namespace Tailoring.Domain.Common;

public static class Normalize
{
    public static string? Email(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim().ToLowerInvariant();

    /// <summary>Digits only. Limitation: "+234 803 ..." and "0803 ..." normalise differently; revisit with libphonenumber if needed.</summary>
    public static string? Phone(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        var digits = new string(v.Where(char.IsAsciiDigit).ToArray());
        return digits.Length == 0 ? null : digits;
    }
}
