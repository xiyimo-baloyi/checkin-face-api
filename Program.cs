using Africanideasfaceapi.Configuration;
using Africanideasfaceapi.Data;
using Africanideasfaceapi.Middleware;
using Africanideasfaceapi.Hubs;
using Africanideasfaceapi.Components;
using Hangfire;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using AspNetCoreRateLimit;
using System.Text;
using System.Text.Json;
using Africanideasfaceapi.Services;
using Azure.Extensions.AspNetCore.Configuration.Secrets;

var builder = WebApplication.CreateBuilder(args);

// Add local settings file if it exists (for local development secrets)
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

if (builder.Configuration.GetValue<bool>("AzureKeyVault:Enabled"))
{
    var vaultName = builder.Configuration["AzureKeyVault:VaultName"];
    if (!string.IsNullOrWhiteSpace(vaultName))
    {
        builder.Configuration.AddAzureKeyVault(
            new Uri($"https://{vaultName}.vault.azure.net/"),
            new Azure.Identity.DefaultAzureCredential());
    }
}

// Add services to the container using the centralized extension method.
builder.Services.AddApplicationServices(builder.Configuration, builder.Environment);

var app = builder.Build();

ProductionConfigurationValidator.Validate(app.Configuration, app.Environment);

if (TryRunUploadsBlobMigration(args, app))
{
    return;
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "African Ideas Face API v1");
    });
}
else
{
    // Use custom global exception handler in non-dev environments
    app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseResponseCompression();

// Biometric and ID uploads must not be served from wwwroot (use /files/read with auth).
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/uploads", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    await next(context);
});

app.UseStaticFiles();
app.UseAntiforgery();

// Use Session for authentication state persistence
app.UseSession();

// Use Ip Rate Limiting
app.UseIpRateLimiting();

// Authentication & Authorization
app.UseAuthentication();
app.UseAuthorization();

// Custom API Key Middleware for protected endpoints
app.UseMiddleware<ApiKeyMiddleware>();

// Health Check Endpoints
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("liveness")
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("ready")
});

// Hangfire Dashboard
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new Africanideasfaceapi.Configuration.HangfireAuthorizationFilter() }
});

// Map SignalR Hubs
app.MapHub<AnalyticsHub>("/hubs/analytics");

// Map Controllers for API endpoints
app.MapControllers();

// Map Blazor Components
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Initialize the database asynchronously
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        await DbInitializer.InitializeAsync(services, app.Environment.IsDevelopment());
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding the database.");
    }
}

app.Run();

static bool TryRunUploadsBlobMigration(string[] args, WebApplication app)
{
    var migrateIndex = Array.FindIndex(args, a =>
        a.Equals("--migrate-uploads-to-blob", StringComparison.OrdinalIgnoreCase));
    if (migrateIndex < 0)
    {
        return false;
    }

    string? uploadsPath = null;
    if (migrateIndex + 1 < args.Length && !args[migrateIndex + 1].StartsWith('-'))
    {
        uploadsPath = args[migrateIndex + 1];
    }

    uploadsPath ??= Path.Combine(app.Environment.ContentRootPath, "wwwroot", "uploads");
    var deleteLocal = args.Any(a => a.Equals("--delete-local", StringComparison.OrdinalIgnoreCase));

    using var scope = app.Services.CreateScope();
    var migrator = scope.ServiceProvider.GetRequiredService<LocalUploadsBlobMigrationService>();
    var report = migrator.MigrateAsync(uploadsPath, deleteLocal).GetAwaiter().GetResult();

    var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    Console.OutputEncoding = Encoding.UTF8;
    Console.WriteLine(json);

    Environment.ExitCode = report.Errors.Count == 0 ? 0 : 1;
    return true;
}

// Required for Routes.razor and DbInitializer to reference the Program class
public partial class Program { }
