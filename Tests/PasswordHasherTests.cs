using Africanideasfaceapi.Helpers;
using Xunit;

namespace Africanideasfaceapi.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void Hash_and_verify_roundtrip()
    {
        var hash = PasswordHasher.Hash("TestPassword123!");
        Assert.True(PasswordHasher.Verify("TestPassword123!", hash));
        Assert.False(PasswordHasher.Verify("WrongPassword", hash));
    }

    [Fact]
    public void Legacy_sha256_hashes_still_verify()
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashedBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes("legacy"));
        var legacyHash = Convert.ToHexString(hashedBytes).Replace("-", "").ToLowerInvariant();

        Assert.True(PasswordHasher.Verify("legacy", legacyHash));
        Assert.True(PasswordHasher.NeedsRehash(legacyHash));
    }
}
