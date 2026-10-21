using Africanideasfaceapi.Models;

namespace Africanideasfaceapi.Services.Interfaces;

public interface IAuthService
{
    User? CurrentUser { get; }
    bool IsAuthenticated { get; }
    bool IsAdmin { get; }
    void Login(User user, bool isSuccessful = true, string? reason = null, double? latitude = null, double? longitude = null);
    Task<(bool Success, string? Error)> LoginWithPassword(string email, string password, UserService userService, double? latitude = null, double? longitude = null);
    string GenerateJwtToken(User user);
    Task<(bool Success, string? Error)> ValidateDeviceAsync(User user, string? deviceId, UserService userService);
    Task LogoutAsync();
    void UpdateCurrentUser(User? user);
    Task EnsureCurrentUserImageLoadedAsync(UserService userService);
    Task SaveUserToBrowserAsync(User user);
    Task<User?> RefreshCurrentUserAsync(UserService? userService = null);
    Task<User?> LoadUserFromBrowserAsync();
}
