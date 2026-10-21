namespace Africanideasfaceapi.Services.FaceApi;

public static class FaceRecognitionErrorMapper
{
    public static string MapErrorToCategory(string? error)
    {
        if (string.IsNullOrEmpty(error))
        {
            return "Unknown";
        }

        var lowerError = error.ToLowerInvariant();
        if (lowerError.Contains("no face"))
        {
            return "NoFace";
        }

        if (lowerError.Contains("multiple face"))
        {
            return "MultipleFaces";
        }

        if (lowerError.Contains("confidence"))
        {
            return "LowConfidence";
        }

        if (lowerError.Contains("angle") || lowerError.Contains("pose") ||
            lowerError.Contains("directly at the camera") || lowerError.Contains("towards the camera") ||
            lowerError.Contains("sideways"))
        {
            return "Pose";
        }

        if (lowerError.Contains("covered") || lowerError.Contains("occlude") || lowerError.Contains("hat") ||
            lowerError.Contains("glasses") || lowerError.Contains("mask"))
        {
            return "Occlusion";
        }

        if (lowerError.Contains("lighting") || lowerError.Contains("dark") || lowerError.Contains("bright") ||
            lowerError.Contains("glare") || lowerError.Contains("exposure") || lowerError.Contains("shadow"))
        {
            return "Lighting";
        }

        if (lowerError.Contains("not recognized") || lowerError.Contains("no account") ||
            lowerError.Contains("no candidates") || lowerError.Contains("identity failed"))
        {
            return "WrongUser";
        }

        if (lowerError.Contains("persongroup not found") || lowerError.Contains("invalid group"))
        {
            return "ConfigurationError";
        }

        if (lowerError.Contains("rate limit") || lowerError.Contains("429") || lowerError.Contains("too many requests"))
        {
            return "RateLimit";
        }

        if (lowerError.Contains("unauthorized") || lowerError.Contains("401") || lowerError.Contains("access key"))
        {
            return "AuthError";
        }

        if (lowerError.Contains("timeout") || lowerError.Contains("timed out") || lowerError.Contains("connection"))
        {
            return "NetworkError";
        }

        return "SystemError";
    }
}
