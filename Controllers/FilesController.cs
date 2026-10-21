using System.Text;
using Africanideasfaceapi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace Africanideasfaceapi.Controllers;

/// <summary>
/// Serves stored biometric and document files after session authorization (not public static files).
/// </summary>
[Route("files")]
[ApiController]
public class FilesController : ControllerBase
{
    private readonly FileStorageService _fileStorage;
    private readonly AuthService _authService;

    public FilesController(FileStorageService fileStorage, AuthService authService)
    {
        _fileStorage = fileStorage;
        _authService = authService;
    }

    [HttpGet("read")]
    public async Task<IActionResult> Read([FromQuery] string? key, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return BadRequest();
        }

        string storageKey;
        try
        {
            storageKey = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(key));
        }
        catch (FormatException)
        {
            return BadRequest();
        }

        if (!_fileStorage.CanAccessStorageKey(storageKey, _authService.CurrentUser, _authService.IsAdmin))
        {
            return Forbid();
        }

        var result = await _fileStorage.OpenReadAsync(storageKey, cancellationToken);
        if (result == null)
        {
            return NotFound();
        }

        return File(result.Stream, result.ContentType, enableRangeProcessing: true);
    }
}
