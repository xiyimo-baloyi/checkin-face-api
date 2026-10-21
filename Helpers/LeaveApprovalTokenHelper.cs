using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Africanideasfaceapi.Helpers;

public static class LeaveApprovalTokenHelper
{
    public static string CreateToken(string leaveRequestId, IConfiguration configuration)
    {
        var secret = GetSigningSecret(configuration);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(leaveRequestId));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool Validate(string leaveRequestId, string? token, IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var expected = CreateToken(leaveRequestId, configuration);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(token.Trim().ToLowerInvariant()));
    }

    private static string GetSigningSecret(IConfiguration configuration)
    {
        var secret = configuration["LeaveSettings:ApprovalSigningKey"]
                     ?? configuration["JwtSettings:SecretKey"];
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException("Leave approval signing key is not configured.");
        }

        return secret;
    }
}
