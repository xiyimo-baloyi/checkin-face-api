using Africanideasfaceapi.DTOs;
using Africanideasfaceapi.Models;
using Africanideasfaceapi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Africanideasfaceapi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AuthService _authService;
    private readonly UserService _userService;
    private readonly FaceApiService _faceApiService;
    private readonly LivenessSessionService _livenessSessions;
    private readonly IConfiguration _configuration;

    public AuthController(
        AuthService authService,
        UserService userService,
        FaceApiService faceApiService,
        LivenessSessionService livenessSessions,
        IConfiguration configuration)
    {
        _authService = authService;
        _userService = userService;
        _faceApiService = faceApiService;
        _livenessSessions = livenessSessions;
        _configuration = configuration;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var (success, error) = await _authService.LoginWithPassword(request.Email, request.Password, _userService);
        
        if (!success)
        {
            return Unauthorized(new { message = error });
        }

        var user = _authService.CurrentUser;
        if (user == null)
        {
             return StatusCode(500, "User state invalid after login");
        }

        var token = _authService.GenerateJwtToken(user);

        return Ok(new
        {
            token,
            user = new
            {
                user.Id,
                user.Name,
                user.Email,
                user.Role
            }
        });
    }

    public class LoginRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    [AllowAnonymous]
    [HttpPost("liveness/session")]
    public IActionResult StartLivenessSession()
    {
        var token = _livenessSessions.CreateSession();
        return Ok(new { token, expiresInSeconds = 300 });
    }

    [AllowAnonymous]
    [HttpPost("liveness/session/{token}/complete")]
    public IActionResult CompleteLivenessSession(string token)
    {
        _livenessSessions.MarkCompleted(token);
        return Ok(new { completed = true });
    }

    [AllowAnonymous]
    [HttpPost("face-login-multi")]
    public async Task<IActionResult> FaceLoginMulti([FromBody] MultiFrameLoginRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var requireLiveness = _configuration.GetValue("LoginSettings:RequireLivenessCheck", true);
        if (requireLiveness && !_livenessSessions.ValidateAndConsume(request.LivenessToken))
        {
            return BadRequest(new { message = "Valid liveness verification is required before face login." });
        }

        if (request.FramesBase64 == null || request.FramesBase64.Count == 0)
        {
            return BadRequest(new { message = "At least one frame is required." });
        }

        var frameStreams = new List<Stream>();

        try
        {
            foreach (var frame in request.FramesBase64)
            {
                if (string.IsNullOrWhiteSpace(frame))
                {
                    continue;
                }

                var base64 = frame;
                var commaIndex = frame.IndexOf(',');
                if (commaIndex >= 0 && commaIndex < frame.Length - 1)
                {
                    base64 = frame[(commaIndex + 1)..];
                }

                try
                {
                    var bytes = Convert.FromBase64String(base64);
                    frameStreams.Add(new MemoryStream(bytes));
                }
                catch (FormatException)
                {
                    // Skip invalid frames but keep processing others
                }
            }

            if (frameStreams.Count == 0)
            {
                return BadRequest(new { message = "No valid image frames were provided." });
            }

            var (success, user, confidence, error) =
                await _faceApiService.RecognizeFromMultipleFramesAsync(frameStreams, _userService);

            if (!success || user == null)
            {
                return Unauthorized(new { message = error ?? "Face not recognized." });
            }

            _authService.Login(user, true);
            var token = _authService.GenerateJwtToken(user);

            return Ok(new
            {
                token,
                confidence,
                user = new
                {
                    user.Id,
                    user.Name,
                    user.Email,
                    user.Role
                }
            });
        }
        finally
        {
            foreach (var s in frameStreams)
            {
                await s.DisposeAsync();
            }
        }
    }
}
