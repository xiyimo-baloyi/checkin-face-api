using Microsoft.Azure.CognitiveServices.Vision.Face;
using Microsoft.Azure.CognitiveServices.Vision.Face.Models;
using Africanideasfaceapi.Services.Interfaces;
using Africanideasfaceapi.Helpers;
using Africanideasfaceapi.Models.Configuration;
using Microsoft.Extensions.Options;

namespace Africanideasfaceapi.Services;

public class FaceClientWrapper : IFaceClientWrapper
{
    private readonly IFaceClient _faceClient;
    private readonly ILogger<FaceClientWrapper> _logger;
    private const int InteractiveRetryCount = 2;
    private const int InteractiveInitialDelayMs = 300;

    public FaceClientWrapper(IOptions<AzureFaceApiSettings> settings, ILogger<FaceClientWrapper> logger)
    {
        _logger = logger;
        var options = settings.Value;
        var endpoint = options.Endpoint ?? string.Empty;
        var key = options.Key ?? string.Empty;
        
        endpoint = endpoint.TrimEnd('/');
        
        _faceClient = new FaceClient(new ApiKeyServiceClientCredentials(key))
        {
            Endpoint = endpoint
        };
    }

    public Task<PersonGroup> GetPersonGroupAsync(string personGroupId) => 
        ResilienceHelper.ExecuteWithRetryAsync(() => _faceClient.PersonGroup.GetAsync(personGroupId), _logger, "GetPersonGroup");

    public Task CreatePersonGroupAsync(string personGroupId, string name, string recognitionModel) =>
        ResilienceHelper.ExecuteWithRetryAsync(() => _faceClient.PersonGroup.CreateAsync(personGroupId, name, recognitionModel: recognitionModel), _logger, "CreatePersonGroup");

    public Task TrainPersonGroupAsync(string personGroupId) =>
        ResilienceHelper.ExecuteWithRetryAsync(() => _faceClient.PersonGroup.TrainAsync(personGroupId), _logger, "TrainPersonGroup");

    public Task<TrainingStatus> GetPersonGroupTrainingStatusAsync(string personGroupId) =>
        ResilienceHelper.ExecuteWithRetryAsync(() => _faceClient.PersonGroup.GetTrainingStatusAsync(personGroupId), _logger, "GetTrainingStatus");

    public Task<Person> CreatePersonAsync(string personGroupId, string name) =>
        ResilienceHelper.ExecuteWithRetryAsync(() => _faceClient.PersonGroupPerson.CreateAsync(personGroupId, name), _logger, "CreatePerson");

    public Task<Person> GetFacePersonAsync(string personGroupId, Guid personId) =>
        ResilienceHelper.ExecuteWithRetryAsync(() => _faceClient.PersonGroupPerson.GetAsync(personGroupId, personId), _logger, "GetPerson");

    public Task DeletePersonAsync(string personGroupId, Guid personId) =>
        ResilienceHelper.ExecuteWithRetryAsync(() => _faceClient.PersonGroupPerson.DeleteAsync(personGroupId, personId), _logger, "DeletePerson");

    public Task<PersistedFace> AddFaceFromStreamAsync(string personGroupId, Guid personId, Stream image) =>
        ResilienceHelper.ExecuteWithRetryAsync(() => _faceClient.PersonGroupPerson.AddFaceFromStreamAsync(personGroupId, personId, image), _logger, "AddFace");

    public Task DeleteFaceAsync(string personGroupId, Guid personId, Guid persistedFaceId) =>
        ResilienceHelper.ExecuteWithRetryAsync(() => _faceClient.PersonGroupPerson.DeleteFaceAsync(personGroupId, personId, persistedFaceId), _logger, "DeleteFace");

    public async Task<IList<DetectedFace>> DetectWithStreamAsync(
        Stream image, 
        bool returnFaceId = true, 
        IList<FaceAttributeType>? returnFaceAttributes = null, 
        string? recognitionModel = null, 
        string? detectionModel = null,
        bool? returnRecognitionModel = null)
    {
        return await ResilienceHelper.ExecuteWithRetryAsync(() => _faceClient.Face.DetectWithStreamAsync(
            image, 
            returnFaceId, 
            returnFaceAttributes: returnFaceAttributes, 
            recognitionModel: recognitionModel, 
            detectionModel: detectionModel,
            returnRecognitionModel: returnRecognitionModel), _logger, "DetectFaces", InteractiveRetryCount, InteractiveInitialDelayMs);
    }

    public Task<IList<IdentifyResult>> IdentifyAsync(IList<Guid> faceIds, string personGroupId) =>
        ResilienceHelper.ExecuteWithRetryAsync(() => _faceClient.Face.IdentifyAsync(faceIds, personGroupId), _logger, "IdentifyFaces", InteractiveRetryCount, InteractiveInitialDelayMs);

    public Task<VerifyResult> VerifyFaceToFaceAsync(Guid faceId1, Guid faceId2) =>
        ResilienceHelper.ExecuteWithRetryAsync(() => _faceClient.Face.VerifyFaceToFaceAsync(faceId1, faceId2), _logger, "VerifyFaces");
}

