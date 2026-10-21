using Microsoft.Azure.CognitiveServices.Vision.Face.Models;

namespace Africanideasfaceapi.Services.Interfaces;

public interface IFaceClientWrapper
{
    Task<PersonGroup> GetPersonGroupAsync(string personGroupId);
    Task CreatePersonGroupAsync(string personGroupId, string name, string recognitionModel);
    Task TrainPersonGroupAsync(string personGroupId);
    Task<TrainingStatus> GetPersonGroupTrainingStatusAsync(string personGroupId);
    
    Task<Person> CreatePersonAsync(string personGroupId, string name);
    Task<Person> GetFacePersonAsync(string personGroupId, Guid personId);
    Task DeletePersonAsync(string personGroupId, Guid personId);
    
    Task<PersistedFace> AddFaceFromStreamAsync(string personGroupId, Guid personId, Stream image);
    Task DeleteFaceAsync(string personGroupId, Guid personId, Guid persistedFaceId);
    
    Task<IList<DetectedFace>> DetectWithStreamAsync(
        Stream image, 
        bool returnFaceId = true, 
        IList<FaceAttributeType>? returnFaceAttributes = null, 
        string? recognitionModel = null, 
        string? detectionModel = null,
        bool? returnRecognitionModel = null);
        
    Task<IList<IdentifyResult>> IdentifyAsync(IList<Guid> faceIds, string personGroupId);
    Task<VerifyResult> VerifyFaceToFaceAsync(Guid faceId1, Guid faceId2);
}
