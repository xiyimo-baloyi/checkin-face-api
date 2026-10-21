using Africanideasfaceapi.Services.FaceApi;
using Xunit;

namespace Africanideasfaceapi.Tests;

public class FaceRecognitionErrorMapperTests
{
    [Theory]
    [InlineData(null, "Unknown")]
    [InlineData("", "Unknown")]
    [InlineData("No face detected", "NoFace")]
    [InlineData("Multiple faces in frame", "MultipleFaces")]
    [InlineData("Confidence below threshold", "LowConfidence")]
    [InlineData("Please look directly at the camera", "Pose")]
    [InlineData("Face is covered by a mask", "Occlusion")]
    [InlineData("Image is too dark", "Lighting")]
    [InlineData("User not recognized", "WrongUser")]
    [InlineData("HTTP 429 rate limit", "RateLimit")]
    public void MapErrorToCategory_maps_known_messages(string? error, string expected)
    {
        Assert.Equal(expected, FaceRecognitionErrorMapper.MapErrorToCategory(error));
    }
}
