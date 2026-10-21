using Africanideasfaceapi.Models;

namespace Africanideasfaceapi.Services;

/// <summary>
/// Per-circuit user snapshot so Blazor authorization matches AuthService when HTTP session commit is deferred.
/// </summary>
public sealed class AuthenticationSessionState
{
    public User? CurrentUser { get; set; }
}
