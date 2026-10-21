using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Africanideasfaceapi.Helpers;

public static class PathHelper
{
    /// <summary>
    /// Returns a browser URL for stored uploads. Sensitive files use authenticated /files/read; external URLs pass through.
    /// </summary>
    public static string FixImagePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        path = path.Trim();

        if (path.StartsWith("/files/read", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (path.Contains(".blob.core.windows.net", StringComparison.OrdinalIgnoreCase))
            {
                return ToSecureFileUrl(path);
            }

            return path;
        }

        if (path.StartsWith("blob:", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("local:", StringComparison.OrdinalIgnoreCase))
        {
            return ToSecureFileUrl(path);
        }

        if (path.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase))
        {
            return ToSecureFileUrl(path);
        }

        path = path.Replace("\\", "/");
        if (path.Contains('/'))
        {
            return ToSecureFileUrl(path);
        }

        return ToSecureFileUrl("uploads/attempts/" + path);
    }

    private static bool BlobOrLocalPrefix(string path) =>
        path.StartsWith("blob:", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("local:", StringComparison.OrdinalIgnoreCase);

    public static string ToSecureFileUrl(string? storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath))
        {
            return "";
        }

        var encoded = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(storedPath.Trim()));
        return $"/files/read?key={encoded}";
    }
}
