using Africanideasfaceapi.Services.Interfaces;
using Africanideasfaceapi.Helpers;
using Africanideasfaceapi.Models;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Microsoft.JSInterop;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace Africanideasfaceapi.Services;

public class AuthService : IAuthService
{
    private User? _currentUser;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AuthService> _logger;
    private readonly IConfiguration _configuration;
    private readonly AuditService? _auditService;
    private readonly Microsoft.JSInterop.IJSRuntime _jsRuntime;
    private ISession? _session => _httpContextAccessor.HttpContext?.Session;
    private const string UserSessionKey = "CurrentUser";
    private const string UserIdSessionKey = "UserId";
    private const string UserRoleSessionKey = "UserRole";
    private readonly SettingsService? _settingsService;
    private readonly ServerAuthenticationStateProvider? _authStateProvider;
    private readonly AuthenticationSessionState _sessionState;
    private readonly IWebHostEnvironment _environment;

    /// <summary>Creates a copy of the user without ImageBase64 for session storage to avoid size limits and ensure image is loaded from Cosmos when needed.</summary>
    private static User UserForSession(User user)
    {
        var copy = JsonConvert.DeserializeObject<User>(JsonConvert.SerializeObject(user));
        if (copy != null) copy.ImageBase64 = string.Empty;
        return copy ?? user;
    }

    private void SaveUserToSession(User? user)
    {
        if (_session == null || user == null) return;
        try
        {
            var sessionUser = UserForSession(user);
            var userJson = JsonConvert.SerializeObject(sessionUser);
            
            // Critical check for Blazor Server: Cannot set session after response headers sent
            if (_httpContextAccessor.HttpContext?.Response.HasStarted == true)
            {
                _logger.LogDebug("Skipping session save for {Email}: Response already started.", user.Email);
                return;
            }
            
            _session.Set(UserSessionKey, System.Text.Encoding.UTF8.GetBytes(userJson));
            _session.SetString(UserIdSessionKey, user.Id);
            _session.SetString(UserRoleSessionKey, user.Role ?? "User");
            _authStateProvider?.NotifyAuthenticationChanged();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save user to session for {Email}", user.Email);
        }
    }

    public AuthService(
        IHttpContextAccessor httpContextAccessor,
        ILogger<AuthService> logger,
        IConfiguration configuration,
        Microsoft.JSInterop.IJSRuntime jsRuntime,
        AuthenticationSessionState sessionState,
        AuditService? auditService = null,
        SettingsService? settingsService = null,
        ServerAuthenticationStateProvider? authStateProvider = null,
        IWebHostEnvironment? environment = null)
    {
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
        _configuration = configuration;
        _jsRuntime = jsRuntime;
        _auditService = auditService;
        _settingsService = settingsService;
        _authStateProvider = authStateProvider;
        _sessionState = sessionState;
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        LoadUserFromSession();
    }

    public User? CurrentUser => _currentUser;
    public bool IsAuthenticated => _currentUser != null;
    public bool IsAdmin => _currentUser?.Role == "Admin";

    private void LoadUserFromSession()
    {
        try
        {
            if (_session != null && _session.TryGetValue(UserSessionKey, out byte[]? userBytes))
            {
                var userJson = System.Text.Encoding.UTF8.GetString(userBytes);
                _currentUser = JsonConvert.DeserializeObject<User>(userJson);
                _sessionState.CurrentUser = _currentUser;
                _logger.LogInformation("Successfully loaded user {Email} from session.", _currentUser?.Email);
            }
            else
            {
                _logger.LogDebug("No user found in session or session is null (Session null: {IsNull})", _session == null);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading user from session.");
        }
    }

    public void Login(User user, bool isSuccessful = true, string? reason = null, double? latitude = null, double? longitude = null)
    {
        _currentUser = user;
        _sessionState.CurrentUser = user;
        _authStateProvider?.NotifyAuthenticationChanged();
        if (_session != null)
        {
            try
            {
                SaveUserToSession(user);
                _logger.LogInformation("User session set: {Email}, Role: {Role}", user.Email, user.Role);

                try
                {
                    _auditService?.LogLoginAttempt(user.Id, user.Email, isSuccessful, reason ?? "Session login", latitude, longitude);
                }
                catch (Exception auditEx)
                {
                    _logger.LogWarning("Failed to log audit for login: {Message}", auditEx.Message);
                }
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning("Session set failed (response already started): {Message}. User logged in for current circuit only.", ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to store user session for {Email}", user.Email);
            }
        }
        else
        {
            _logger.LogDebug("Session is null during login for {Email}. User logged in for current circuit only.", user.Email);
            _auditService?.LogLoginAttempt(user.Id, user.Email, false, "Session unavailable", latitude, longitude);
        }
    }

    public async Task<(bool Success, string? Error)> LoginWithPassword(string email, string password, UserService userService, double? latitude = null, double? longitude = null)
    {
        if (string.IsNullOrEmpty(email)) return (false, "Email is required.");
        
        var normalizedEmail = email.Trim().ToLower();
        _logger.LogInformation("Login attempt with email: {Email} (Normalized: {NormalizedEmail})", email, normalizedEmail);
        
        var user = await userService.GetUserByEmailAsync(normalizedEmail);
        if (user == null)
        {
            _logger.LogWarning("Login failed: User not found for email {NormalizedEmail}", normalizedEmail);
            return (false, "Invalid email or password.");
        }
        
        if (string.IsNullOrEmpty(user.PasswordHash))
        {
            _logger.LogWarning("Login failed: User {Email} has no password hash set.", user.Email);
            return (false, "This account does not have a password set. Please use face recognition.");
        }
        
        var isPasswordValid = PasswordHasher.Verify(password, user.PasswordHash);
        if (isPasswordValid)
        {
            if (PasswordHasher.NeedsRehash(user.PasswordHash))
            {
                var upgradedHash = PasswordHasher.Hash(password);
                await userService.UpdatePasswordHashAsync(user.Id, upgradedHash);
                user.PasswordHash = upgradedHash;
            }

            _logger.LogInformation("Login successful for user: {Email}", user.Email);
            Login(user, true, "Session login", latitude, longitude);
            return (true, null);
        }
        
        _logger.LogWarning("Login failed: Incorrect password for user {Email}", user.Email);
        return (false, "Invalid email or password.");
    }

    public string GenerateJwtToken(User user)
    {
        var jwtSettings = _configuration.GetSection("JwtSettings");
        var secretKey = jwtSettings["SecretKey"];
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            if (_environment.IsDevelopment())
            {
                secretKey = "DefaultSecretKeyForDevelopmentOnly_MustBeLongerThan32Chars";
            }
            else
            {
                throw new InvalidOperationException("JwtSettings:SecretKey is not configured.");
            }
        }
        var issuer = jwtSettings["Issuer"] ?? "AfricanIdeasIdentity";
        var audience = jwtSettings["Audience"] ?? "AfricanIdeasIdentityClient";
        if (!int.TryParse(jwtSettings["ExpiryMinutes"], out int expiryMinutes))
        {
            expiryMinutes = 1440; // Default to 24 hours
        }

        var securityKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(secretKey));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim("Name", user.Name)
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: TimeHelper.GetNow().AddMinutes(expiryMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task<(bool Success, string? Error)> ValidateDeviceAsync(User user, string? deviceId, UserService userService)
    {
        if (string.IsNullOrEmpty(deviceId))
            return (false, "Device ID is required for security verification.");

        // Check Global Settings
        var settings = _settingsService != null ? await _settingsService.GetSettingsAsync() : new SystemSettings();
        
        // Enforcement is active if EITHER global or user-specific lock is on, UNLESS user has AllowAnyDevice bypass
        bool enforceDevice = (settings.GlobalDeviceLockEnforcement.GetValueOrDefault(false) || user.EnforceDeviceLock.GetValueOrDefault(false)) 
                            && !user.AllowAnyDevice.GetValueOrDefault(false);

        _logger.LogDebug("Device validation for {Email}. DeviceId: {DeviceId}, EnforceDevice: {EnforceDevice}, Registered: {RegisteredDeviceId}", 
            user.Email, deviceId, enforceDevice, user.DeviceId);

        if (!enforceDevice)
        {
            // Enforcement OFF:
            // Allow login from any device.
            // If user has NO device registered, auto-bind it for tracking purposes
            if (string.IsNullOrEmpty(user.DeviceId))
            {
                user.DeviceId = deviceId;
                await userService.UpdateUserAsync(user);
                _logger.LogInformation("Device auto-linked for tracking for user {Email}: {DeviceId}", user.Email, deviceId);
            }
            return (true, null);
        }
        
        // Enforcement ON:
        if (string.IsNullOrEmpty(user.DeviceId))
        {
            // First time login - bind the device
            user.DeviceId = deviceId;
            await userService.UpdateUserAsync(user);
            _logger.LogInformation("Device bound for user {Email}: {DeviceId}", user.Email, deviceId);
            return (true, null);
        }
        
        if (user.DeviceId != deviceId)
        {
            _logger.LogWarning("Login attempt from unauthorized device for user {Email}: {DeviceId}. Registered: {RegisteredDeviceId}", 
                user.Email, deviceId, user.DeviceId);
            return (false, "This account is linked to a different device. Please use your registered device or contact an admin.");
        }

        return (true, null);
    }


    public async Task LogoutAsync()
    {
        var email = _currentUser?.Email ?? "Unknown";
        _currentUser = null;
        _sessionState.CurrentUser = null;
        
        if (_session != null)
        {
            _session.Remove(UserSessionKey);
            _session.Remove(UserIdSessionKey);
            _session.Remove(UserRoleSessionKey);
        }

        _authStateProvider?.NotifyAuthenticationChanged();
        _logger.LogInformation("User logged out: {Email}", email);
    }

    public void UpdateCurrentUser(User? user)
    {
        _currentUser = user;
        _sessionState.CurrentUser = user;
        try
        {
            if (user != null) SaveUserToSession(user);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogDebug("Could not update session (response started): {Message}", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update user session.");
        }
    }

    /// <summary>Loads the current user's profile image from Cosmos DB if not already in memory. Call from layout/pages that display the avatar.</summary>
    public async Task EnsureCurrentUserImageLoadedAsync(UserService userService)
    {
        if (_currentUser == null || userService == null) return;
        if (!string.IsNullOrEmpty(_currentUser.ImageBase64)) return;
        try
        {
            var fullUser = await userService.GetUserByIdAsync(_currentUser.Id);
            if (fullUser != null && !string.IsNullOrEmpty(fullUser.ImageBase64))
            {
                _currentUser.ImageBase64 = fullUser.ImageBase64;
                _logger.LogDebug("Loaded profile image from Cosmos for user {Email}", _currentUser.Email);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load profile image from Cosmos for user {UserId}", _currentUser.Id);
        }
    }

    public async Task SaveUserToBrowserAsync(User user)
    {
        // Persistence disabled as per user request
        await Task.CompletedTask;
    }

    public async Task<User?> RefreshCurrentUserAsync(UserService? userService = null)
    {
        if (_currentUser == null) return null;
        try
        {
            if (userService != null)
            {
                var freshUser = await userService.GetUserByIdAsync(_currentUser.Id);
                if (freshUser != null)
                {
                    UpdateCurrentUser(freshUser);
                    return _currentUser;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh current user {UserId}", _currentUser.Id);
        }
        return _currentUser;
    }

    public async Task<User?> LoadUserFromBrowserAsync()
    {
        // Persistence disabled as per user request
        return await Task.FromResult<User?>(null);
    }
}
