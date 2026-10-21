using System.Security.Claims;
using System.Text;
using Africanideasfaceapi.Models;
using Microsoft.AspNetCore.Components.Authorization;
using Newtonsoft.Json;

namespace Africanideasfaceapi.Services;

public class ServerAuthenticationStateProvider : AuthenticationStateProvider
{
    private const string UserSessionKey = "CurrentUser";
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly AuthenticationSessionState _sessionState;

    public ServerAuthenticationStateProvider(
        IHttpContextAccessor httpContextAccessor,
        AuthenticationSessionState sessionState)
    {
        _httpContextAccessor = httpContextAccessor;
        _sessionState = sessionState;
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var user = TryGetUserFromHttpSession();
        user ??= _sessionState.CurrentUser;

        if (user != null)
        {
            return Task.FromResult(new AuthenticationState(CreatePrincipal(user)));
        }

        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    private User? TryGetUserFromHttpSession()
    {
        var session = _httpContextAccessor.HttpContext?.Session;
        if (session == null || !session.TryGetValue(UserSessionKey, out byte[]? userBytes) || userBytes == null)
        {
            return null;
        }

        try
        {
            var userJson = Encoding.UTF8.GetString(userBytes);
            return JsonConvert.DeserializeObject<User>(userJson);
        }
        catch
        {
            return null;
        }
    }

    private static ClaimsPrincipal CreatePrincipal(User user)
    {
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role ?? "User"),
                new Claim(ClaimTypes.Name, user.Name)
            },
            authenticationType: "session");

        return new ClaimsPrincipal(identity);
    }

    public void NotifyAuthenticationChanged() =>
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
}
