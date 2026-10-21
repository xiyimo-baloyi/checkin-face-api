namespace Africanideasfaceapi.Services.FaceApi;

internal static class FaceApiConstants
{
    internal const int MaxPersistedFacesPerPerson = 100;
    internal const int MaxEnrichmentFramesPerLogin = 5;
    internal const double EnrichmentConfidenceMargin = 0.05;
    internal static readonly TimeSpan ConfidenceThresholdCacheTtl = TimeSpan.FromSeconds(45);
}
