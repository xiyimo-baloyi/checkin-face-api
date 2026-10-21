namespace Africanideasfaceapi.Services.FaceApi;

internal static class FaceApiStreamHelper
{
    internal static async Task<byte[]> ToByteArrayAsync(Stream imageStream)
    {
        if (imageStream is MemoryStream memStream && memStream.CanRead && memStream.CanSeek)
        {
            return memStream.ToArray();
        }

        imageStream.Position = 0;
        using var copyStream = new MemoryStream();
        await imageStream.CopyToAsync(copyStream);
        return copyStream.ToArray();
    }
}
