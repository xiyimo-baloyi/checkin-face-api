using Africanideasfaceapi.Helpers;
using Africanideasfaceapi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Africanideasfaceapi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LeaveController : ControllerBase
{
    private readonly LeaveService _leaveService;
    private readonly IConfiguration _configuration;

    public LeaveController(LeaveService leaveService, IConfiguration configuration)
    {
        _leaveService = leaveService;
        _configuration = configuration;
    }

    [AllowAnonymous]
    [HttpGet("approve/{id}")]
    public async Task<IActionResult> ApproveLeave(string id, [FromQuery] string token)
    {
        if (!LeaveApprovalTokenHelper.Validate(id, token, _configuration))
        {
            return Content("<html><body><h2>Invalid or missing approval link.</h2></body></html>", "text/html");
        }

        var result = await _leaveService.ReviewLeaveRequestAsync(id, "Approved", "Approved via email link", null);
        if (result)
        {
            return Content("<html><body><h2 style='color: #28a745; font-family: sans-serif;'>Leave Request Approved!</h2><p style='font-family: sans-serif;'>The employee has been notified and their attendance records will be updated.</p></body></html>", "text/html");
        }
        return Content("<html><body><h2 style='color: #dc3545; font-family: sans-serif;'>Approval Failed</h2><p style='font-family: sans-serif;'>Could not find leave request or it has already been processed.</p></body></html>", "text/html");
    }

    [AllowAnonymous]
    [HttpGet("reject/{id}")]
    public async Task<IActionResult> RejectLeave(string id, [FromQuery] string token)
    {
        if (!LeaveApprovalTokenHelper.Validate(id, token, _configuration))
        {
            return Content("<html><body><h2>Invalid or missing approval link.</h2></body></html>", "text/html");
        }

        var result = await _leaveService.ReviewLeaveRequestAsync(id, "Rejected", "Rejected via email link", null);
        if (result)
        {
            return Content("<html><body><h2 style='color: #dc3545; font-family: sans-serif;'>Leave Request Rejected</h2><p style='font-family: sans-serif;'>The decision has been recorded.</p></body></html>", "text/html");
        }
        return Content("<html><body><h2 style='color: #dc3545; font-family: sans-serif;'>Rejection Failed</h2><p style='font-family: sans-serif;'>Could not find leave request or it has already been processed.</p></body></html>", "text/html");
    }
}
