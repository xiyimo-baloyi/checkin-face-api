using Africanideasfaceapi.Helpers;
using Africanideasfaceapi.Models;
using Africanideasfaceapi.Hubs;
using Africanideasfaceapi.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Africanideasfaceapi.Services.Interfaces;

namespace Africanideasfaceapi.Services;

public class UserService : IUserService
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly ILogger<UserService> _logger;
    private readonly AuditService? _auditService;
    private readonly IHubContext<AnalyticsHub>? _analyticsHub;

    public UserService(IDbContextFactory<AppDbContext> factory, ILogger<UserService> logger, AuditService? auditService = null, IHubContext<AnalyticsHub>? analyticsHub = null)
    {
        _factory = factory;
        _logger = logger;
        _auditService = auditService;
        _analyticsHub = analyticsHub;
    }

    /// <summary>Normalizes PersonId to lowercase for consistent storage and lookup (GUID format).</summary>
    private static string? NormalizePersonId(string? personId) =>
        string.IsNullOrWhiteSpace(personId) ? null : personId.Trim().ToLowerInvariant();

    public virtual async Task<User?> GetUserByPersonIdAsync(string personId)
    {
        var normalized = NormalizePersonId(personId);
        if (normalized == null) return null;
        using var context = await _factory.CreateDbContextAsync();
        return await context.Users
            .AsNoTracking()
            .Where(u => u.PersonId == normalized && u.IsActive == true)
            .OrderByDescending(u => u.RegisteredDate)
            .FirstOrDefaultAsync();
    }

    public async Task<User?> GetUserByIdAsync(string id)
    {
        using var context = await _factory.CreateDbContextAsync();
        return await context.Users.FindAsync(id);
    }

    public async Task<List<User>> GetAllUsersAsync()
    {
        using var context = await _factory.CreateDbContextAsync();
        return await context.Users.AsNoTracking().Where(u => u.IsActive == true).ToListAsync();
    }

    public async Task<User> CreateUserAsync(User user)
    {
        using var context = await _factory.CreateDbContextAsync();
        // Check email uniqueness (Cosmos DB doesn't enforce unique constraints)
        // Only check for ACTIVE users. Inactive (deleted) users are ignored.
        var existingUser = (await context.Users
            .Where(u => u.Email == user.Email && u.IsActive == true)
            .ToListAsync())
            .OrderByDescending(u => u.RegisteredDate)
            .FirstOrDefault();
        if (existingUser != null)
        {
            _logger.LogWarning("Attempt to create user with existing email: {Email}", user.Email);
            throw new InvalidOperationException($"A user with email {user.Email} already exists.");
        }

        // Ensure ID is set (required for Cosmos DB partition key)
        if (string.IsNullOrEmpty(user.Id))
        {
            user.Id = Guid.NewGuid().ToString();
        }

        // Ensure RegisteredDate is set
        if (user.RegisteredDate == default)
        {
            user.RegisteredDate = TimeHelper.GetNow();
        }

        _logger.LogInformation("Creating new user: {Email}, RegistrationMethod: {RegistrationMethod}, Id: {UserId}", user.Email, user.RegistrationMethod, user.Id);
        
        try
        {
            context.Users.Add(user);
            await context.SaveChangesAsync();
            _logger.LogInformation("User created successfully: {UserId}, Email: {Email}", user.Id, user.Email);
            _auditService?.LogUserRegistration(user.Id, user.Email, user.RegistrationMethod);
            
            // Broadcast real-time analytics update
            if (_analyticsHub != null)
            {
                await _analyticsHub.Clients.All.SendAsync("AnalyticsUpdated", "UserRegistration", $"New user registered: {user.Email}");
            }
            
            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating user: {Email}, Id: {UserId}. Inner exception: {InnerException}", 
                user.Email, user.Id, ex.InnerException?.Message);
            throw new InvalidOperationException($"Failed to create user: {ex.Message}. Inner exception: {ex.InnerException?.Message}", ex);
        }
    }

    public async Task<bool> DeleteUserAsync(string id, FaceApiService? faceApiService = null)
    {
        using var context = await _factory.CreateDbContextAsync();
        var user = await context.Users.FindAsync(id);
        if (user != null)
        {
            _logger.LogInformation("Deleting user: {UserId}, Email: {Email}", id, user.Email);
            // Delete person from Azure Face API PersonGroup if PersonId exists
            if (!string.IsNullOrEmpty(user.PersonId) && faceApiService != null)
            {
                _logger.LogDebug("Deleting face API person for user: {PersonId}", user.PersonId);
                await faceApiService.DeletePersonAsync(user.PersonId);
            }

            user.IsActive = false;
            await context.SaveChangesAsync();
            _logger.LogInformation("User deleted successfully: {UserId}", id);
            return true;
        }
        _logger.LogWarning("Attempt to delete non-existent user: {UserId}", id);
        return false;
    }

    public async Task<User?> UpdateUserAsync(User user)
    {
        using var context = await _factory.CreateDbContextAsync();
        var existingUser = await context.Users.FindAsync(user.Id);
        if (existingUser != null)
        {
            existingUser.Name = user.Name;
            existingUser.Surname = user.Surname;
            existingUser.Email = user.Email;
            existingUser.IdNumber = user.IdNumber;
            existingUser.Role = user.Role;
            existingUser.Status = user.Status;
            existingUser.IsActive = user.IsActive;
            existingUser.WorkStartTime = user.WorkStartTime;
            existingUser.WorkEndTime = user.WorkEndTime;
            existingUser.BranchId = user.BranchId;
            existingUser.PhoneNumber = user.PhoneNumber;
            
            // Critical: Ensure registration data is preserved or updated
            if (!string.IsNullOrEmpty(user.PersonId)) existingUser.PersonId = NormalizePersonId(user.PersonId);
            if (!string.IsNullOrEmpty(user.ImageBase64)) existingUser.ImageBase64 = user.ImageBase64;
            if (!string.IsNullOrEmpty(user.DocumentImagePath)) existingUser.DocumentImagePath = user.DocumentImagePath;
            if (user.DateOfBirth.HasValue) existingUser.DateOfBirth = user.DateOfBirth;
            
            // Consent Data
            if (user.HasConsented.HasValue) existingUser.HasConsented = user.HasConsented;
            if (user.ConsentDate.HasValue) existingUser.ConsentDate = user.ConsentDate;

            // Flexible Work & Security Settings
            existingUser.EnforceGeofence = user.EnforceGeofence;
            existingUser.EnforceDeviceLock = user.EnforceDeviceLock;
            existingUser.WorkType = user.WorkType;
            existingUser.HybridWorkDays = user.HybridWorkDays ?? new List<string>();
            existingUser.AllowAnyDevice = user.AllowAnyDevice;
            existingUser.DeviceId = user.DeviceId;
            
            await context.SaveChangesAsync();
            return existingUser;
        }
        return null;
    }

    /// <summary>
    /// Gets a user by email address (for document-registered users during first login)
    /// </summary>
    public async Task<User?> GetUserByEmailAsync(string email)
    {
        if (string.IsNullOrEmpty(email)) return null;
        using var context = await _factory.CreateDbContextAsync();
        var normalizedEmail = email.Trim().ToLower();
        return (await context.Users
            .AsNoTracking()
            .Where(u => u.Email.ToLower() == normalizedEmail && u.IsActive == true)
            .ToListAsync())
            .OrderByDescending(u => u.RegisteredDate)
            .FirstOrDefault();
    }

    /// <summary>
    /// Updates a user's PersonId after face enrollment (for document-registered users)
    /// </summary>
    public async Task<bool> UpdateUserPersonIdAsync(string userId, string personId)
    {
        using var context = await _factory.CreateDbContextAsync();
        var user = await context.Users.FindAsync(userId);
        if (user != null)
        {
            user.PersonId = NormalizePersonId(personId);
            await context.SaveChangesAsync();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Checks if a user needs face enrollment (document-registered user without PersonId)
    /// </summary>
    public async Task<bool> UserNeedsFaceEnrollmentAsync(string userId)
    {
        using var context = await _factory.CreateDbContextAsync();
        var user = await context.Users.FindAsync(userId);
        return user != null && user.RegistrationMethod == "Document" && string.IsNullOrEmpty(user.PersonId);
    }

    /// <summary>
    /// Gets a branch by ID
    /// </summary>
    public async Task<Branch?> GetBranchAsync(string branchId)
    {
        using var context = await _factory.CreateDbContextAsync();
        return await context.Branches.FindAsync(branchId);
    }

    public async Task UpdatePasswordHashAsync(string userId, string passwordHash)
    {
        using var context = await _factory.CreateDbContextAsync();
        var user = await context.Users.FindAsync(userId);
        if (user == null)
        {
            return;
        }

        user.PasswordHash = passwordHash;
        await context.SaveChangesAsync();
    }
}


