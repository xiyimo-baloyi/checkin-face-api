using Africanideasfaceapi.Helpers;
using Africanideasfaceapi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Africanideasfaceapi.Data;

/// <summary>
/// Centralizes Cosmos DB initialization and admin user seeding.
/// Extracted from Program.cs to keep the entry point focused on pipeline configuration.
/// </summary>
public static class DbInitializer
{
    public static async Task InitializeAsync(
        IServiceProvider services,
        bool isDevelopment,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var factory = scope.ServiceProvider.GetService<IDbContextFactory<AppDbContext>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        if (factory == null)
        {
             logger.LogWarning("Skipping database initialization: IDbContextFactory<AppDbContext> is not registered.");
             return;
        }

        await using var context = factory.CreateDbContext();

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            logger.LogInformation("Initializing Cosmos DB database and containers with resilience policies...");
            
            // Add retry logic to EnsureCreatedAsync to handle cold-start issues in Azure
            await ResilienceHelper.ExecuteWithRetryAsync(
                () => context.Database.EnsureCreatedAsync(cancellationToken), 
                logger, 
                "Database.EnsureCreated");

            sw.Stop();
            logger.LogInformation("Cosmos DB database and containers initialized successfully in {ElapsedMs} ms.", sw.ElapsedMilliseconds);

            var adminEmail = configuration["AdminSettings:AdminEmail"]
                             ?? "Xiyimo.baloyi@dev.africanideas.co.za";
            var adminSeedPassword = configuration["AdminSettings:DefaultPassword"];
            if (string.IsNullOrWhiteSpace(adminSeedPassword))
            {
                adminSeedPassword = isDevelopment ? "Ideas@12" : null;
            }

            if (string.IsNullOrWhiteSpace(adminSeedPassword))
            {
                logger.LogInformation("Skipping admin user seed: AdminSettings:DefaultPassword is not configured.");
                return;
            }

            var hashedPassword = PasswordHasher.Hash(adminSeedPassword);

            // Case-insensitive check for Admin in Cosmos
            var existingAdmin = await context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    u => u.Email.ToLower() == adminEmail.ToLower(),
                    cancellationToken);

            if (existingAdmin == null)
            {
                var seedSw = System.Diagnostics.Stopwatch.StartNew();
                logger.LogInformation("Seeding admin user: {Email}", adminEmail);

                var adminUser = new User
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "Xiyimo Baloyi",
                    Surname = "Baloyi",
                    Email = adminEmail,
                    IdNumber = "ADMIN001",
                    Role = "Admin",
                    RegistrationMethod = "Password",
                    PasswordHash = hashedPassword,
                    IsActive = true,
                    RegisteredDate = TimeHelper.GetNow(),
                    HasConsented = true,
                    ConsentDate = TimeHelper.GetNow(),
                    Status = "Approved"
                };

                context.Users.Add(adminUser);
                await ResilienceHelper.ExecuteWithRetryAsync(
                    () => context.SaveChangesAsync(cancellationToken), 
                    logger, 
                    "SeedAdminUser");
                    
                seedSw.Stop();
                logger.LogInformation("Admin user seeded successfully in {ElapsedMs} ms.", seedSw.ElapsedMilliseconds);
            }
            else if (isDevelopment)
            {
                // In development, ensure the admin account is perfectly configured
                var modified = false;

                if (PasswordHasher.NeedsRehash(existingAdmin.PasswordHash) ||
                    !PasswordHasher.Verify(adminSeedPassword, existingAdmin.PasswordHash))
                {
                    existingAdmin.PasswordHash = PasswordHasher.Hash(adminSeedPassword);
                    modified = true;
                }

                if (existingAdmin.Role != "Admin")
                {
                    existingAdmin.Role = "Admin";
                    modified = true;
                }

                if (existingAdmin.Status != "Approved")
                {
                    existingAdmin.Status = "Approved";
                    modified = true;
                }

                if (existingAdmin.IsActive != true)
                {
                    existingAdmin.IsActive = true;
                    modified = true;
                }

                if (existingAdmin.Email != adminEmail)
                {
                    existingAdmin.Email = adminEmail; // Fix case matching
                    modified = true;
                }

                if (modified)
                {
                    logger.LogInformation(
                        "Repairing admin user {Email} credentials and status for Development.",
                        adminEmail);
                    
                    context.Users.Update(existingAdmin);
                    await ResilienceHelper.ExecuteWithRetryAsync(
                        () => context.SaveChangesAsync(cancellationToken), 
                        logger, 
                        "RepairAdminUser");
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Cosmos DB initialization failed after retries: {Message}. Monitor /health/ready for diagnostics.",
                ex.Message);
            // Don't rethrow - allow the app to start (it will fail when data is needed, but /health will report why)
        }
    }
}

