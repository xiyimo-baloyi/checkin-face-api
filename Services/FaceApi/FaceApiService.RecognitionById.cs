using Africanideasfaceapi.Helpers;
using Microsoft.Azure.CognitiveServices.Vision.Face.Models;
using Africanideasfaceapi.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using Africanideasfaceapi.Data;
using Microsoft.EntityFrameworkCore;
using Africanideasfaceapi.Hubs;
using Africanideasfaceapi.Services.Interfaces;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Africanideasfaceapi.Models.Configuration;
using System.Diagnostics;
using Africanideasfaceapi.Services.FaceApi;

namespace Africanideasfaceapi.Services;

public partial class FaceApiService
{

    public async Task<(bool IsMatch, User? User, double Confidence, string? Error)> RecognizeFaceFromIdAsync(Guid faceId, UserService userService)
    {
        try
        {
            _logger.LogDebug("Starting face recognition from FaceId: {FaceId}", faceId);

            // Use Face Identification directly
            var identifyResults = await _faceClient.IdentifyAsync(
                new List<Guid> { faceId },
                _personGroupId
            );

            if (identifyResults == null || !identifyResults.Any() || identifyResults[0].Candidates == null || !identifyResults[0].Candidates.Any())
            {
                return (false, null, 0, "Face not recognized under current user.");
            }

            var candidate = identifyResults[0].Candidates[0];
            var confidence = candidate.Confidence;
            var threshold = await GetConfidenceThresholdAsync();
            if (confidence < threshold)
            {
                _logger.LogWarning("Face recognition confidence too low: {Confidence} (threshold: {Threshold})", confidence, threshold);
                return (false, null, confidence, "Face not recognized with sufficient confidence. Please try again.");
            }

            var personId = candidate.PersonId.ToString();
            _logger.LogInformation("Face recognized with confidence {Confidence}, PersonId: {PersonId}", candidate.Confidence, personId);

            var user = await userService.GetUserByPersonIdAsync(personId);

            if (user == null)
            {
                _logger.LogWarning("User not found for PersonId: {PersonId}", personId);
                return (false, null, confidence, "Face recognized but no account found.");
            }

            return (true, user, confidence, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during face recognition from ID");
            return (false, null, 0, $"Error recognizing face: {ex.Message}");
        }
    }
}
