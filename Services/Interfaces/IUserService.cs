using Africanideasfaceapi.Models;

namespace Africanideasfaceapi.Services.Interfaces;

public interface IUserService
{
    Task<User?> GetUserByPersonIdAsync(string personId);
    Task<User?> GetUserByIdAsync(string id);
    Task<List<User>> GetAllUsersAsync();
    Task<User> CreateUserAsync(User user);
    Task<bool> DeleteUserAsync(string id, FaceApiService? faceApiService = null);
    Task<User?> UpdateUserAsync(User user);
    Task<User?> GetUserByEmailAsync(string email);
    Task<bool> UpdateUserPersonIdAsync(string userId, string personId);
    Task<bool> UserNeedsFaceEnrollmentAsync(string userId);
    Task<Branch?> GetBranchAsync(string branchId);
}
