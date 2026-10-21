namespace Africanideasfaceapi.Models.Configuration;

public class AzureFaceApiSettings
{
    public string Endpoint { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string PersonGroupId { get; set; } = "africanideas-users";
}
