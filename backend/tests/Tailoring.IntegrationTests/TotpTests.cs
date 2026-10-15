using System.Text;
using Tailoring.Infrastructure.Services;
using Xunit;

namespace Tailoring.IntegrationTests;

/// <summary>Pure maths, no database. Vectors are from RFC 6238 appendix B (SHA-1, secret "12345678901234567890"), last six digits.</summary>
public sealed class TotpTests
{
    private static readonly byte[] Rfc = Encoding.ASCII.GetBytes("12345678901234567890");

    [Theory]
    [InlineData(59L, "287082")]          // 8-digit vector 94287082
    [InlineData(1111111109L, "081804")]  // 07081804
    [InlineData(1234567890L, "005924")]  // 89005924
    public void Codes_match_the_rfc_vectors(long unixSeconds, string expected)
        => Assert.Equal(expected, Totp.Compute(Rfc, unixSeconds / Totp.StepSeconds));

    [Fact]
    public void FindStep_accepts_two_steps_either_side_and_reports_which_step_matched()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1234567890);
        var step = Totp.StepAt(now);
        foreach (var offset in new long[] { -2, -1, 0, 1, 2 })
            Assert.Equal(step + offset, Totp.FindStep(Rfc, Totp.Compute(Rfc, step + offset), now));
        Assert.Null(Totp.FindStep(Rfc, Totp.Compute(Rfc, step + 3), now));
        Assert.Null(Totp.FindStep(Rfc, Totp.Compute(Rfc, step - 3), now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12 456")]
    [InlineData("abcdef")]
    public void Malformed_codes_never_match(string code)
        => Assert.Null(Totp.FindStep(Rfc, code, DateTimeOffset.FromUnixTimeSeconds(1234567890)));

    [Fact]
    public void Base32_decodes_identity_style_keys_and_rejects_garbage()
    {
        // "12345678901234567890" in RFC 4648 base32
        Assert.Equal(Rfc, Totp.DecodeBase32("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ"));
        Assert.Equal(Rfc, Totp.DecodeBase32("gezd gnbv gy3t qojq gezd gnbv gy3t qojq"));
        Assert.Null(Totp.DecodeBase32("not*base32!"));
        Assert.Null(Totp.DecodeBase32(""));
        Assert.Null(Totp.DecodeBase32(null));
    }
}
