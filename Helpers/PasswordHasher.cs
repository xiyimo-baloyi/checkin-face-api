namespace Africanideasfaceapi.Helpers;

public static class PasswordHasher
{
    public static string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);

    public static bool Verify(string password, string? storedHash)
    {
        if (string.IsNullOrEmpty(storedHash))
        {
            return false;
        }

        if (IsBcryptHash(storedHash))
        {
            return BCrypt.Net.BCrypt.Verify(password, storedHash);
        }

        return VerifyLegacySha256(password, storedHash);
    }

    public static bool NeedsRehash(string? storedHash) =>
        string.IsNullOrEmpty(storedHash) || !IsBcryptHash(storedHash);

    public static bool IsBcryptHash(string hash) =>
        hash.StartsWith("$2", StringComparison.Ordinal);

    private static bool VerifyLegacySha256(string password, string hash)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashedBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
        var hashString = BitConverter.ToString(hashedBytes).Replace("-", "").ToLowerInvariant();
        return hashString == hash.Trim().ToLowerInvariant();
    }
}
