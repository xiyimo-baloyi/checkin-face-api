using System.IO;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Africanideasfaceapi.Models;
using Microsoft.Extensions.Configuration;

namespace Africanideasfaceapi.Services;

public sealed class StoredFileReadResult : IDisposable
{
    public Stream Stream { get; init; } = Stream.Null;
    public string ContentType { get; init; } = "application/octet-stream";

    public void Dispose() => Stream.Dispose();
}

public class FileStorageService
{
    private const string BlobPrefix = "blob:";
    private const string LocalPrefix = "local:";

    private readonly string _webRootPath;
    private readonly string _uploadsBasePath;
    private readonly string _attemptsBasePath;
    private readonly BlobServiceClient? _blobServiceClient;
    private readonly string _containerName;
    private readonly ILogger<FileStorageService> _logger;

    public bool UsesBlobStorage => _blobServiceClient != null;

    public FileStorageService(IWebHostEnvironment environment, IConfiguration configuration, ILogger<FileStorageService> logger)
    {
        _logger = logger;
        _webRootPath = ResolveWebRootPath(environment);
        _uploadsBasePath = Path.Combine(_webRootPath, "uploads", "documents");
        _attemptsBasePath = Path.Combine(_webRootPath, "uploads", "attempts");

        var connectionString = configuration["AzureStorage:ConnectionString"];
        _containerName = configuration["AzureStorage:ContainerName"] ?? "faces";

        if (!string.IsNullOrEmpty(connectionString))
        {
            try
            {
                _blobServiceClient = new BlobServiceClient(connectionString);
                _logger.LogInformation("Azure Blob Storage enabled with private container: {ContainerName}", _containerName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Azure Blob Storage client");
            }
        }

        if (!Directory.Exists(_uploadsBasePath))
        {
            Directory.CreateDirectory(_uploadsBasePath);
        }

        if (!Directory.Exists(_attemptsBasePath))
        {
            Directory.CreateDirectory(_attemptsBasePath);
        }
    }

    private static string ResolveWebRootPath(IWebHostEnvironment environment)
    {
        var webRoot = environment.WebRootPath;
        if (string.IsNullOrWhiteSpace(webRoot))
        {
            webRoot = Path.Combine(environment.ContentRootPath, "wwwroot");
        }

        if (!Directory.Exists(webRoot))
        {
            Directory.CreateDirectory(webRoot);
        }

        return webRoot;
    }

    public async Task<string> SaveDocumentImageAsync(Stream imageStream, string userId, string documentType)
    {
        try
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var fileName = $"{documentType}_{timestamp}.jpg";
            var blobPath = $"documents/{userId}/{fileName}";

            if (_blobServiceClient != null)
            {
                await UploadToBlobAsync(imageStream, blobPath);
                return BlobPrefix + blobPath;
            }

            var userDirectory = Path.Combine(_uploadsBasePath, userId);
            Directory.CreateDirectory(userDirectory);

            var filePath = Path.Combine(userDirectory, fileName);
            await using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                if (imageStream.CanSeek) imageStream.Position = 0;
                await imageStream.CopyToAsync(fileStream);
            }

            return LocalPrefix + $"uploads/documents/{userId}/{fileName}";
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to save document image: {ex.Message}", ex);
        }
    }

    public string GetDocumentImageFullPath(string relativePath)
    {
        var key = NormalizeStorageKey(relativePath);
        if (key == null || key.StartsWith(BlobPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Document is stored in Azure Blob Storage; open via OpenReadAsync.");
        }

        var relative = key.StartsWith(LocalPrefix, StringComparison.OrdinalIgnoreCase)
            ? key[LocalPrefix.Length..]
            : key.TrimStart('/').Replace("\\", "/");

        return Path.Combine(_webRootPath, relative.Replace("/", Path.DirectorySeparatorChar.ToString()));
    }

    public async Task<bool> DeleteDocumentImageAsync(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path)) return false;

            var key = NormalizeStorageKey(path);
            if (key == null) return false;

            if (key.StartsWith(BlobPrefix, StringComparison.OrdinalIgnoreCase))
            {
                if (_blobServiceClient == null) return false;
                var blobName = key[BlobPrefix.Length..];
                var blobClient = _blobServiceClient.GetBlobContainerClient(_containerName).GetBlobClient(blobName);
                return await blobClient.DeleteIfExistsAsync();
            }

            var fullPath = GetDocumentImageFullPath(path);
            if (!File.Exists(fullPath)) return false;

            File.Delete(fullPath);
            var directory = Path.GetDirectoryName(fullPath);
            if (directory != null && Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> DocumentImageExistsAsync(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;

        var key = NormalizeStorageKey(path);
        if (key == null) return false;

        if (key.StartsWith(BlobPrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (_blobServiceClient == null) return false;
            var blobName = key[BlobPrefix.Length..];
            var blobClient = _blobServiceClient.GetBlobContainerClient(_containerName).GetBlobClient(blobName);
            return await blobClient.ExistsAsync();
        }

        var fullPath = GetDocumentImageFullPath(path);
        return File.Exists(fullPath);
    }

    public bool DocumentImageExists(string path) => DocumentImageExistsAsync(path).GetAwaiter().GetResult();
    public bool DeleteDocumentImage(string path) => DeleteDocumentImageAsync(path).GetAwaiter().GetResult();

    public async Task<string> SaveAttemptImageAsync(Stream imageStream, string action)
    {
        try
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            var fileName = $"attempt_{action}_{timestamp}.jpg";
            var blobPath = $"attempts/{fileName}";

            if (_blobServiceClient != null)
            {
                await UploadToBlobAsync(imageStream, blobPath);
                return BlobPrefix + blobPath;
            }

            var filePath = Path.Combine(_attemptsBasePath, fileName);
            await using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                if (imageStream.CanSeek) imageStream.Position = 0;
                await imageStream.CopyToAsync(fileStream);
            }

            return LocalPrefix + $"uploads/attempts/{fileName}";
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to save attempt image: {ex.Message}", ex);
        }
    }

    public async Task<string> SaveLeaveAttachmentAsync(Stream fileStream, string userId, string fileName)
    {
        try
        {
            var extension = Path.GetExtension(fileName);
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var uniqueFileName = $"leave_{timestamp}{extension}";
            var blobPath = $"leave/{userId}/{uniqueFileName}";

            if (_blobServiceClient != null)
            {
                await UploadToBlobAsync(fileStream, blobPath);
                return BlobPrefix + blobPath;
            }

            var leaveDirectory = Path.Combine(_webRootPath, "uploads", "leave", userId);
            Directory.CreateDirectory(leaveDirectory);

            var filePath = Path.Combine(leaveDirectory, uniqueFileName);
            await using (var destinationStream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                if (fileStream.CanSeek) fileStream.Position = 0;
                await fileStream.CopyToAsync(destinationStream);
            }

            return LocalPrefix + $"uploads/leave/{userId}/{uniqueFileName}";
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to save leave attachment: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Normalizes DB paths (legacy /uploads, https blob URLs, blob:/local: prefixes) to a storage key.
    /// </summary>
    public string? NormalizeStorageKey(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        path = path.Trim();

        if (path.StartsWith(BlobPrefix, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(LocalPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (!path.Contains(".blob.core.windows.net", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var uri = new Uri(path);
            var segments = uri.AbsolutePath.Trim('/').Split('/', 2, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2)
            {
                return null;
            }

            return BlobPrefix + segments[1];
        }

        path = path.Replace("\\", "/");
        if (path.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
        {
            return LocalPrefix + path.TrimStart('/');
        }

        if (path.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase))
        {
            return LocalPrefix + path;
        }

        if (path.Contains('/'))
        {
            return LocalPrefix + "uploads/" + path.TrimStart('/');
        }

        return LocalPrefix + "uploads/attempts/" + path;
    }

    public bool CanAccessStorageKey(string storageKey, User? currentUser, bool isAdmin)
    {
        if (isAdmin)
        {
            return true;
        }

        if (currentUser == null)
        {
            return false;
        }

        var normalized = NormalizeStorageKey(storageKey) ?? storageKey;

        if (normalized.StartsWith(BlobPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var blobPath = normalized[BlobPrefix.Length..];
            return CanAccessBlobPath(blobPath, currentUser.Id);
        }

        if (normalized.StartsWith(LocalPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var localPath = normalized[LocalPrefix.Length..].Replace("\\", "/");
            return CanAccessLocalPath(localPath, currentUser.Id);
        }

        return false;
    }

    private static bool CanAccessBlobPath(string blobPath, string userId)
    {
        if (blobPath.StartsWith("attempts/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (blobPath.StartsWith("documents/", StringComparison.OrdinalIgnoreCase))
        {
            var parts = blobPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 && string.Equals(parts[1], userId, StringComparison.OrdinalIgnoreCase);
        }

        if (blobPath.StartsWith("leave/", StringComparison.OrdinalIgnoreCase))
        {
            var parts = blobPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 && string.Equals(parts[1], userId, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool CanAccessLocalPath(string relativePath, string userId)
    {
        if (relativePath.StartsWith("uploads/attempts/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (relativePath.StartsWith("uploads/documents/", StringComparison.OrdinalIgnoreCase))
        {
            var parts = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 3 && string.Equals(parts[2], userId, StringComparison.OrdinalIgnoreCase);
        }

        if (relativePath.StartsWith("uploads/leave/", StringComparison.OrdinalIgnoreCase))
        {
            var parts = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 3 && string.Equals(parts[2], userId, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    public async Task<StoredFileReadResult?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeStorageKey(storageKey) ?? storageKey;

        if (normalized.StartsWith(BlobPrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (_blobServiceClient == null) return null;
            var blobName = normalized[BlobPrefix.Length..];
            var blobClient = _blobServiceClient.GetBlobContainerClient(_containerName).GetBlobClient(blobName);
            if (!await blobClient.ExistsAsync(cancellationToken))
            {
                return null;
            }

            var download = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
            var contentType = download.Value.Details.ContentType;
            if (string.IsNullOrWhiteSpace(contentType))
            {
                contentType = GuessContentType(blobName);
            }

            return new StoredFileReadResult
            {
                Stream = download.Value.Content,
                ContentType = contentType
            };
        }

        if (normalized.StartsWith(LocalPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var relative = normalized[LocalPrefix.Length..].Replace("\\", "/");
            var fullPath = Path.Combine(_webRootPath, relative.Replace("/", Path.DirectorySeparatorChar.ToString()));
            if (!File.Exists(fullPath))
            {
                return null;
            }

            var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return new StoredFileReadResult
            {
                Stream = stream,
                ContentType = GuessContentType(fullPath)
            };
        }

        return null;
    }

    public string ToBlobStorageKey(string blobRelativePath) =>
        BlobPrefix + blobRelativePath.Replace("\\", "/").TrimStart('/');

    /// <summary>
    /// Maps a stored path (local, legacy /uploads, or azure URL) to a blob storage key, if applicable.
    /// </summary>
    public string? TryConvertStoredPathToBlobKey(string? storedPath)
    {
        var normalized = NormalizeStorageKey(storedPath);
        if (normalized == null)
        {
            return null;
        }

        if (normalized.StartsWith(BlobPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        if (!normalized.StartsWith(LocalPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var localRelative = normalized[LocalPrefix.Length..].Replace("\\", "/");
        if (!localRelative.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return ToBlobStorageKey(localRelative["uploads/".Length..]);
    }

    public async Task<bool> BlobObjectExistsAsync(string blobRelativePath, CancellationToken cancellationToken = default)
    {
        if (_blobServiceClient == null)
        {
            return false;
        }

        blobRelativePath = blobRelativePath.Replace("\\", "/").TrimStart('/');
        var blobClient = _blobServiceClient.GetBlobContainerClient(_containerName).GetBlobClient(blobRelativePath);
        return await blobClient.ExistsAsync(cancellationToken);
    }

    public async Task<bool> UploadBlobFromFileIfNotExistsAsync(
        string blobRelativePath,
        string fullLocalFilePath,
        CancellationToken cancellationToken = default)
    {
        if (_blobServiceClient == null)
        {
            throw new InvalidOperationException("Azure Blob Storage is not configured.");
        }

        if (!File.Exists(fullLocalFilePath))
        {
            return false;
        }

        blobRelativePath = blobRelativePath.Replace("\\", "/").TrimStart('/');
        var containerClient = _blobServiceClient.GetBlobContainerClient(_containerName);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

        var blobClient = containerClient.GetBlobClient(blobRelativePath);
        if (await blobClient.ExistsAsync(cancellationToken))
        {
            return true;
        }

        await using var stream = new FileStream(fullLocalFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var headers = new BlobHttpHeaders { ContentType = GuessContentType(blobRelativePath) };
        await blobClient.UploadAsync(
            stream,
            new BlobUploadOptions { HttpHeaders = headers },
            cancellationToken);

        return true;
    }

    private async Task UploadToBlobAsync(Stream stream, string blobPath)
    {
        var containerClient = _blobServiceClient!.GetBlobContainerClient(_containerName);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.None);

        var blobClient = containerClient.GetBlobClient(blobPath);
        if (stream.CanSeek) stream.Position = 0;

        var headers = new BlobHttpHeaders { ContentType = GuessContentType(blobPath) };
        await blobClient.UploadAsync(stream, new BlobUploadOptions { HttpHeaders = headers }, cancellationToken: default);
    }

    private static string GuessContentType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".pdf" => "application/pdf",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
    }
}
