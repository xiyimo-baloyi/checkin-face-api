namespace Africanideasfaceapi.Configuration;

public static class ProductionConfigurationValidator
{
    private const string DefaultJwtFallback = "DefaultSecretKeyForDevelopmentOnly_MustBeLongerThan32Chars";

    public static void Validate(IConfiguration configuration, IWebHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            return;
        }

        var errors = new List<string>();

        var jwtSecret = configuration["JwtSettings:SecretKey"];
        if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret == DefaultJwtFallback)
        {
            errors.Add("JwtSettings:SecretKey must be set to a strong secret in Production.");
        }

        var apiKey = configuration["ApiSettings:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.StartsWith("dev-key", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("ApiSettings:ApiKey must be set to a strong value in Production.");
        }

        if (string.IsNullOrWhiteSpace(configuration["CosmosDb:AccountEndpoint"]) ||
            string.IsNullOrWhiteSpace(configuration["CosmosDb:AccountKey"]))
        {
            errors.Add("CosmosDb credentials must be configured in Production.");
        }

        if (string.IsNullOrWhiteSpace(configuration["AzureFaceApi:Endpoint"]) ||
            string.IsNullOrWhiteSpace(configuration["AzureFaceApi:Key"]))
        {
            errors.Add("AzureFaceApi credentials must be configured in Production.");
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Production configuration validation failed:\n- " + string.Join("\n- ", errors));
        }
    }
}
