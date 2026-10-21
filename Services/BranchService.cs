using Africanideasfaceapi.Data;
using Africanideasfaceapi.Models;
using Microsoft.EntityFrameworkCore;

namespace Africanideasfaceapi.Services;

public class BranchService
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly ILogger<BranchService> _logger;

    public BranchService(IDbContextFactory<AppDbContext> factory, ILogger<BranchService> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<List<Branch>> GetAllBranchesAsync()
    {
        using var context = await _factory.CreateDbContextAsync();
        return await context.Branches.ToListAsync();
    }

    public async Task<Branch?> GetBranchByIdAsync(string id)
    {
        using var context = await _factory.CreateDbContextAsync();
        return await context.Branches.FindAsync(id);
    }

    public async Task<Branch> CreateBranchAsync(Branch branch)
    {
        using var context = await _factory.CreateDbContextAsync();
        context.Branches.Add(branch);
        await context.SaveChangesAsync();
        _logger.LogInformation("Created new branch: {BranchName} ({BranchId})", branch.Name, branch.Id);
        return branch;
    }

    public async Task<bool> UpdateBranchAsync(Branch branch)
    {
        using var context = await _factory.CreateDbContextAsync();
        var existing = await context.Branches.FindAsync(branch.Id);
        if (existing == null) return false;

        existing.Name = branch.Name;
        existing.Latitude = branch.Latitude;
        existing.Longitude = branch.Longitude;
        existing.GeofenceRadius = branch.GeofenceRadius;

        await context.SaveChangesAsync();
        _logger.LogInformation("Updated branch: {BranchName}", branch.Name);
        return true;
    }

    public async Task<bool> DeleteBranchAsync(string id)
    {
        using var context = await _factory.CreateDbContextAsync();
        var branch = await context.Branches.FindAsync(id);
        if (branch == null) return false;

        context.Branches.Remove(branch);
        await context.SaveChangesAsync();
        _logger.LogInformation("Deleted branch: {BranchId}", id);
        return true;
    }
}
