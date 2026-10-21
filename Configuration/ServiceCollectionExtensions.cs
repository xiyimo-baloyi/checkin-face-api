using System.IO.Compression;
using System.Text;
using Africanideasfaceapi.Data;
using Africanideasfaceapi.Hubs;
using Africanideasfaceapi.Helpers;
using Africanideasfaceapi.Models;
using Africanideasfaceapi.Services;
using Africanideasfaceapi.Services.Interfaces;
using AspNetCoreRateLimit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Hangfire;
using Hangfire.Storage.SQLite;
using Africanideasfaceapi.Services.Monitoring;
using Africanideasfaceapi.Models.Configuration;
using Microsoft.AspNetCore.Components.Authorization;


namespace Africanideasfaceapi.Configuration;

/// <summary>
/// Extension methods for registering application and infrastructure services.
/// Groups together service registrations that were previously in Program.cs
/// to keep the entry point focused on pipeline configuration.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        services.AddAuthorizationCore();
        services.AddCascadingAuthenticationState();
        services.AddScoped<AuthenticationSessionState>();
        services.AddScoped<ServerAuthenticationStateProvider>();
        services.AddScoped<AuthenticationStateProvider>(sp =>
            sp.GetRequiredService<ServerAuthenticationStateProvider>());

        // Blazor / Razor Components
        services
            .AddRazorComponents()
            .AddInteractiveServerComponents(options =>
            {
                options.DetailedErrors = environment.IsDevelopment()
                    || configuration.GetValue<bool>("CircuitOptions:DetailedErrors");

                // Keep disconnected circuits so brief idle (e.g. overnight tab) can reconnect without reload
                // Increased retention to 30 minutes to tolerate longer sleep periods in Azure/Mobile
                options.DisconnectedCircuitMaxRetained = configuration.GetValue("CircuitOptions:DisconnectedCircuitMaxRetained", 1500);
                var retentionMinutes = configuration.GetValue("CircuitOptions:DisconnectedCircuitRetentionPeriodMinutes", 30);
                options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(Math.Clamp(retentionMinutes, 1, 120));
            });

        // Register Circuit Handler for monitoring
        services.AddScoped<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler, Africanideasfaceapi.Services.Monitoring.CircuitLoggingHandler>();

        // SignalR for Blazor circuits and analytics hub
        var signalRBuilder = services.AddSignalR(options =>
            {
                // Increased to 10MB to handle high-res biometric frames and complex render updates
                options.MaximumReceiveMessageSize = 10 * 1024 * 1024; 
                options.ClientTimeoutInterval = TimeSpan.FromSeconds(120); 
                options.HandshakeTimeout = TimeSpan.FromSeconds(60); // Double for slow mobile handshakes
                options.KeepAliveInterval = TimeSpan.FromSeconds(15);
                options.EnableDetailedErrors = environment.IsDevelopment()
                    || configuration.GetValue<bool>("CircuitOptions:DetailedErrors");
                options.StreamBufferCapacity = 20; // Limit per-connection stream buffers
            })
            .AddHubOptions<AnalyticsHub>(options =>
            {
                options.MaximumReceiveMessageSize = 10 * 1024 * 1024; // 10MB for analytics
            });

        // Check for Azure SignalR Connection String
        var azureSignalRConnectionString = configuration["Azure:SignalR:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(azureSignalRConnectionString))
        {
            signalRBuilder.AddAzureSignalR(azureSignalRConnectionString);
            Console.WriteLine("Azure SignalR Service configured.");
        }
        else
        {
            Console.WriteLine("Azure SignalR Service NOT configured - using default SignalR.");
        }

        // Cosmos DB
        ConfigureCosmosDb(services, configuration);

        // Session support for authentication persistence
        services.AddDistributedMemoryCache();
        services.AddSession(options =>
        {
            options.IdleTimeout = TimeSpan.FromMinutes(30);
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
            options.Cookie.SecurePolicy = environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
        });

        // Health Checks: liveness = process up only; readiness = Cosmos + Face API (see /health/live vs /health/ready).
        services
            .AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy("Application is healthy"), tags: new[] { "liveness", "ready" })
            .AddCheck<CosmosDbHealthCheck>("cosmosdb", tags: new[] { "ready" })
            .AddCheck<FaceApiHealthCheck>("faceapi", tags: new[] { "ready" });

        // Rate Limiting
        services.Configure<IpRateLimitOptions>(configuration.GetSection("IpRateLimiting"));
        services.AddInMemoryRateLimiting();
        services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();

        // Whitelist Blazor endpoints to prevent false-positive 429s
        services.Configure<IpRateLimitOptions>(options =>
        {
            options.EndpointWhitelist ??= new List<string>();

            // Use wildcard for various transport types
            options.EndpointWhitelist.Add("*:/_blazor*");
            options.EndpointWhitelist.Add("*:/_framework/*");
            options.EndpointWhitelist.Add("get:/_blazor");
            options.EndpointWhitelist.Add("post:/_blazor");
            options.EndpointWhitelist.Add("post:/_blazor/initializers");
        });

        // JWT Authentication
        ConfigureAuthentication(services, configuration, environment);

        // Hangfire for background jobs
        ConfigureHangfire(services, configuration);

        // Response Compression for SignalR/WebSockets
        services.AddResponseCompression(opts =>
        {
            opts.MimeTypes = ResponseCompressionDefaults.MimeTypes;
        });

        services.Configure<GzipCompressionProviderOptions>(options =>
        {
            options.Level = CompressionLevel.Fastest;
        });

        // Options Pattern
        services.Configure<AzureFaceApiSettings>(configuration.GetSection("AzureFaceApi"));
        services.Configure<CosmosDbSettings>(configuration.GetSection("CosmosDb"));

        // Register Infrastructure and Domain services
        services.RegisterInfrastructureServices();
        services.RegisterDomainServices();

        // API controllers and Swagger
        services.AddControllers();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc(
                "v1",
                new Microsoft.OpenApi.Models.OpenApiInfo
                {
                    Title = "African Ideas Attendance System API",
                    Version = "v1",
                    Description = "API for worker attendance tracking and identity verification"
                });

            c.AddSecurityDefinition(
                "ApiKey",
                new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                {
                    Description = "API Key authentication",
                    Name = "X-API-Key",
                    In = Microsoft.OpenApi.Models.ParameterLocation.Header,
                    Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey
                });

            c.AddSecurityRequirement(
                new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
                {
                    {
                        new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                        {
                            Reference = new Microsoft.OpenApi.Models.OpenApiReference
                            {
                                Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                                Id = "ApiKey"
                            }
                        },
                        Array.Empty<string>()
                    }
                });
        });

        return services;
    }

    private static void ConfigureCosmosDb(IServiceCollection services, IConfiguration configuration)
    {
        try
        {
            var cosmosConfig = configuration.GetSection("CosmosDb");
            var cosmosEndpoint = cosmosConfig["AccountEndpoint"];
            var cosmosKey = cosmosConfig["AccountKey"];
            var cosmosDbName = cosmosConfig["DatabaseName"];

            if (string.IsNullOrEmpty(cosmosEndpoint) || string.IsNullOrEmpty(cosmosKey))
            {
                Console.WriteLine("WARNING: Cosmos DB credentials are empty in configuration.");
            }

            services.AddDbContextFactory<AppDbContext>(options =>
                options.UseCosmos(
                    cosmosEndpoint ?? throw new InvalidOperationException("CosmosDb:AccountEndpoint is required"),
                    cosmosKey ?? throw new InvalidOperationException("CosmosDb:AccountKey is required"),
                    cosmosDbName ?? throw new InvalidOperationException("CosmosDb:DatabaseName is required")
                ));
        }
        catch (Exception ex)
        {
            // Log warning but don't crash - allows app to start (e.g. for health checks or static files)
            Console.WriteLine($"ERROR: Cosmos DB configuration failed: {ex.Message}. Application features will be limited.");
        }
    }

    private static void ConfigureAuthentication(
        IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        var jwtSettings = configuration.GetSection("JwtSettings");
        var secretKey = jwtSettings["SecretKey"];
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            if (environment.IsDevelopment())
            {
                secretKey = "DefaultSecretKeyForDevelopmentOnly_MustBeLongerThan32Chars";
            }
            else
            {
                throw new InvalidOperationException("JwtSettings:SecretKey is required outside Development.");
            }
        }
        var issuer = jwtSettings["Issuer"] ?? "AfricanIdeasAttendance";
        var audience = jwtSettings["Audience"] ?? "AfricanIdeasAttendanceClient";

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = issuer,
                    ValidAudience = audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey))
                };
            });
    }

    private static void RegisterInfrastructureServices(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddMemoryCache();
        services.AddHttpClient();

        // Monitoring & Telemetry
        services.AddSingleton<ITelemetryInitializer, BranchTelemetryInitializer>();
        services.AddSingleton<TelemetryService>(sp =>
        {
            var client = sp.GetService<TelemetryClient>();
            var logger = sp.GetRequiredService<ILogger<TelemetryService>>();
            return new TelemetryService(client, logger);
        });
        services.AddScoped<AuditService>();
        services.AddScoped<Africanideasfaceapi.Services.Monitoring.CircuitLoggingHandler>();
    }

    private static void RegisterDomainServices(this IServiceCollection services)
    {
        // Core Business Services
        services.AddSingleton<HolidayService>();
        
        services.AddScoped<UserService>();
        services.AddScoped<IUserService>(sp => sp.GetRequiredService<UserService>());
        
        services.AddSingleton<IFaceClientWrapper, FaceClientWrapper>();
        services.AddScoped<IFaceLogService, FaceLogService>();
        services.AddScoped<IBiometricIdentityService, BiometricIdentityService>();
        services.AddSingleton<FaceApiService>();
        services.AddScoped<AuthService>();
        services.AddScoped<IAuthService>(sp => sp.GetRequiredService<AuthService>());
        services.AddScoped<FileStorageService>();
        services.AddScoped<LocalUploadsBlobMigrationService>();
        services.AddSingleton<LivenessSessionService>();
        services.AddScoped<IdCardVerificationService>();
        services.AddScoped<ConsentService>();
        services.AddScoped<ExportService>();
        services.AddScoped<EmailService>();
        services.AddScoped<AnalyticsService>();
        services.AddScoped<IAnalyticsService>(sp => sp.GetRequiredService<AnalyticsService>());
        services.AddScoped<LocationService>();
        
        services.AddScoped<AttendanceService>();
        services.AddScoped<IAttendanceService>(sp => sp.GetRequiredService<AttendanceService>());
        
        services.AddScoped<AbsenceService>();
        services.AddScoped<IAbsenceService>(sp => sp.GetRequiredService<AbsenceService>());
        
        services.AddScoped<AppNotificationService>();
        services.AddScoped<CorrectionService>();
        services.AddScoped<BranchService>();
        services.AddScoped<LeaveService>();
        services.AddScoped<SettingsService>();
        
        services.AddScoped<ReportService>();
        services.AddScoped<IReportService>(sp => sp.GetRequiredService<ReportService>());
        
        services.AddScoped<WhatsAppService>();
        services.AddScoped<RemoteWorkService>();
        services.AddScoped<AbsenceNotificationService>();
        services.AddScoped<WarmupService>();
        
        // Background Services
        services.AddHostedService<AttendanceBackgroundService>();
    }

    private static void ConfigureHangfire(IServiceCollection services, IConfiguration configuration)
    {
        // Use SQLite storage for Hangfire (lightweight and suitable for single-instance deployments)
        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSQLiteStorage("hangfire.db")
        );

        // Add Hangfire server
        services.AddHangfireServer(options =>
        {
            options.WorkerCount = 1; // Single worker for lightweight operations
        });
    }
}

