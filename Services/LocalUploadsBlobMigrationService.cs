using System.Text;
using Africanideasfaceapi.Data;
using Africanideasfaceapi.Helpers;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Africanideasfaceapi.Services;

public sealed class BlobMigrationReport
{
    public int FilesUploaded { get; set; }
    public int FilesAlreadyInBlob { get; set; }
    public int DbRecordsUpdated { get; set; }
    public int LocalFilesDeleted { get; set; }
    public List<string> Errors { get; set; } = new();
}

public class LocalUploadsBlobMigrationService
{
    private readonly FileStorageService _fileStorage;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<LocalUploadsBlobMigrationService> _logger;

    public LocalUploadsBlobMigrationService(
        FileStorageService fileStorage,
        IDbContextFactory<AppDbContext> dbFactory,
        ILogger<LocalUploadsBlobMigrationService> logger)
    {
        _fileStorage = fileStorage;
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<BlobMigrationReport> MigrateAsync(
        string localUploadsDirectory,
        bool deleteLocalAfterUpload = false,
        CancellationToken cancellationToken = default)
    {
        var report = new BlobMigrationReport();

        if (!_fileStorage.UsesBlobStorage)
        {
            report.Errors.Add("Azure Blob Storage is not configured (AzureStorage:ConnectionString).");
            return report;
        }

        if (!Directory.Exists(localUploadsDirectory))
        {
            report.Errors.Add($"Uploads directory not found: {localUploadsDirectory}");
            return report;
        }

        var pathAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var fullPath in Directory.EnumerateFiles(localUploadsDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativeToUploads = Path.GetRelativePath(localUploadsDirectory, fullPath).Replace("\\", "/");
            var blobRelativePath = relativeToUploads;
            var blobKey = _fileStorage.ToBlobStorageKey(blobRelativePath);

            try
            {
                var existed = await _fileStorage.BlobObjectExistsAsync(blobRelativePath, cancellationToken);
                await _fileStorage.UploadBlobFromFileIfNotExistsAsync(blobRelativePath, fullPath, cancellationToken);
                if (existed)
                {
                    report.FilesAlreadyInBlob++;
                }
                else
                {
                    report.FilesUploaded++;
                }

                RegisterAliases(pathAliases, blobKey, relativeToUploads);

                if (deleteLocalAfterUpload)
                {
                    File.Delete(fullPath);
                    report.LocalFilesDeleted++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to migrate file {Path}", fullPath);
                report.Errors.Add($"{relativeToUploads}: {ex.Message}");
            }
        }

        report.DbRecordsUpdated += await UpdateDatabasePathsAsync(pathAliases, cancellationToken);

        if (deleteLocalAfterUpload)
        {
            TryDeleteEmptyDirectories(localUploadsDirectory);
        }

        return report;
    }

    private static void RegisterAliases(Dictionary<string, string> map, string blobKey, string relativeToUploads)
    {
        var fileName = Path.GetFileName(relativeToUploads.Replace("/", Path.DirectorySeparatorChar.ToString()));

        void Add(string alias)
        {
            if (!string.IsNullOrWhiteSpace(alias))
            {
                map[alias] = blobKey;
            }
        }

        Add(blobKey);
        Add($"local:uploads/{relativeToUploads}");
        Add($"/uploads/{relativeToUploads}");
        Add($"uploads/{relativeToUploads}");
        Add(relativeToUploads);

        if (relativeToUploads.StartsWith("attempts/", StringComparison.OrdinalIgnoreCase))
        {
            Add(fileName);
            Add($"/uploads/attempts/{fileName}");
            Add(PathHelper.ToSecureFileUrl($"local:uploads/attempts/{fileName}"));
            Add(PathHelper.ToSecureFileUrl($"/uploads/attempts/{fileName}"));
        }
    }

    private async Task<int> UpdateDatabasePathsAsync(
        Dictionary<string, string> pathAliases,
        CancellationToken cancellationToken)
    {
        var updated = 0;
        await using var context = await _dbFactory.CreateDbContextAsync(cancellationToken);

        foreach (var user in await context.Users.ToListAsync(cancellationToken))
        {
            if (TryRemap(user.DocumentImagePath, pathAliases, out var mapped))
            {
                user.DocumentImagePath = mapped;
                updated++;
            }
        }

        foreach (var worker in await context.Workers.ToListAsync(cancellationToken))
        {
            if (TryRemap(worker.DocumentImagePath, pathAliases, out var mapped))
            {
                worker.DocumentImagePath = mapped;
                updated++;
            }
        }

        foreach (var log in await context.FaceLogs.ToListAsync(cancellationToken))
        {
            if (TryRemap(log.FaceImagePath, pathAliases, out var mapped))
            {
                log.FaceImagePath = mapped;
                updated++;
            }
        }

        foreach (var leave in await context.LeaveRequests.ToListAsync(cancellationToken))
        {
            if (TryRemap(leave.AttachmentPath, pathAliases, out var mapped))
            {
                leave.AttachmentPath = mapped;
                updated++;
            }
        }

        foreach (var verification in await context.IdCardVerifications.ToListAsync(cancellationToken))
        {
            if (TryRemap(verification.IdCardImagePath, pathAliases, out var mapped))
            {
                verification.IdCardImagePath = mapped;
                updated++;
            }

            if (TryRemap(verification.FaceImagePath, pathAliases, out var mappedFace))
            {
                verification.FaceImagePath = mappedFace;
                updated++;
            }
        }

        if (updated > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        return updated;
    }

    private bool TryRemap(string? storedPath, Dictionary<string, string> pathAliases, out string newPath)
    {
        newPath = storedPath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(storedPath))
        {
            return false;
        }

        if (storedPath.StartsWith("blob:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (TryResolveAlias(storedPath, pathAliases, out newPath))
        {
            return !string.Equals(storedPath, newPath, StringComparison.Ordinal);
        }

        var blobFromNormalized = _fileStorage.TryConvertStoredPathToBlobKey(storedPath);
        if (blobFromNormalized != null)
        {
            newPath = blobFromNormalized;
            return !string.Equals(storedPath, newPath, StringComparison.Ordinal);
        }

        return false;
    }

    private static bool TryResolveAlias(string storedPath, Dictionary<string, string> pathAliases, out string newPath)
    {
        if (pathAliases.TryGetValue(storedPath.Trim(), out newPath!))
        {
            return true;
        }

        if (storedPath.StartsWith("/files/read", StringComparison.OrdinalIgnoreCase))
        {
            var queryIndex = storedPath.IndexOf('?');
            if (queryIndex > 0)
            {
                var query = QueryHelpers.ParseQuery(storedPath[queryIndex..]);
                if (query.TryGetValue("key", out var encoded))
                {
                    try
                    {
                        var decoded = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encoded!));
                        if (pathAliases.TryGetValue(decoded, out newPath!))
                        {
                            return true;
                        }

                        if (pathAliases.TryGetValue(decoded.Trim(), out newPath!))
                        {
                            return true;
                        }
                    }
                    catch (FormatException)
                    {
                        // ignore
                    }
                }
            }
        }

        newPath = storedPath;
        return false;
    }

    private static void TryDeleteEmptyDirectories(string root)
    {
        foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
        {
            if (!Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir);
            }
        }
    }
}
