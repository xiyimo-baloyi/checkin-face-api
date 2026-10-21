using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Africanideasfaceapi.Models;
using Africanideasfaceapi.Helpers;

namespace Africanideasfaceapi.Services;

public class EmailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendEmailAsync(string toEmail, string subject, string body, bool isHtml = true)
    {
        var settings = _configuration.GetSection("EmailSettings");
        var host = settings["SmtpServer"];
        var port = int.Parse(settings["SmtpPort"] ?? "465");
        var username = settings["SmtpUsername"];
        var password = settings["SmtpPassword"];
        var senderName = settings["SenderName"] ?? "African Ideas Attendance System";
        var senderEmail = settings["SenderEmail"] ?? username;

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(senderName, senderEmail));
        message.To.Add(new MailboxAddress("", toEmail));
        message.Subject = subject;

        var bodyBuilder = new BodyBuilder { HtmlBody = isHtml ? body : null, TextBody = isHtml ? null : body };
        message.Body = bodyBuilder.ToMessageBody();

        using var client = new SmtpClient();
        try
        {
            _logger.LogInformation("Attempting to send email to {ToEmail} via {Host}", toEmail, host);
            
            // Use SecureSocketOptions.SslOnConnect for port 465
            await client.ConnectAsync(host, port, SecureSocketOptions.SslOnConnect);
            await client.AuthenticateAsync(username, password);
            
            await ResilienceHelper.ExecuteWithRetryAsync(
                () => client.SendAsync(message),
                _logger,
                "SendEmail");
                
            await client.DisconnectAsync(true);
            
            _logger.LogInformation("Email sent successfully to {ToEmail}", toEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {ToEmail} after retries", toEmail);
            throw;
        }
    }

    public async Task SendWelcomeEmailAsync(string toEmail, string userName)
    {
        var subject = "Welcome to African Ideas Attendance System";
        var body = $@"
            <div style='font-family: Arial, sans-serif; padding: 20px; color: #333;'>
                <h2 style='color: #0078d4;'>Welcome, {userName}!</h2>
                <p>Thank you for registering with the African Ideas Attendance System.</p>
                <p>Your account has been successfully created. You can now use our secure face recognition for clocking in and out.</p>
                <hr />
                <p style='font-size: 0.8em; color: #777;'>This is an automated message. Please do not reply.</p>
            </div>";
        
        await SendEmailAsync(toEmail, subject, body);
    }

    public async Task SendVerificationUpdateEmailAsync(string toEmail, string userName, bool isVerified, string? message = null)
    {
        var status = isVerified ? "Verified" : "Verification Failed";
        var color = isVerified ? "#28a745" : "#dc3545";
        
        var subject = $"Identity Verification Update: {status}";
        var body = $@"
            <div style='font-family: Arial, sans-serif; padding: 20px; color: #333;'>
                <h2 style='color: {color};'>Identity Verification Update</h2>
                <p>Hello {userName},</p>
                <p>The verification of your identity document has been completed.</p>
                <p><strong>Status:</strong> <span style='color: {color}; font-weight: bold;'>{status}</span></p>
                {(isVerified ? "<p>Your profile is now fully verified. You can proceed with clocking in and out.</p>" : $"<p>Unfortunately, your verification could not be completed at this time: {message}</p> <p>Please contact HR for assistance.</p>")}
                <hr />
                <p style='font-size: 0.8em; color: #777;'>This is an automated message. Please do not reply.</p>
            </div>";

        await SendEmailAsync(toEmail, subject, body);
    }

    public async Task SendLeaveRequestEmailAsync(string managerEmail, string employeeName, LeaveRequest request, string baseUrl)
    {
        var subject = $"New Leave Request: {employeeName} ({request.LeaveType})";
        var approveUrl = $"{baseUrl}/admin/leave";
        
        var body = $@"
            <div style='font-family: Arial, sans-serif; padding: 20px; color: #333; border: 1px solid #eee; border-radius: 8px;'>
                <h2 style='color: #0078d4;'>Leave Request Submitted</h2>
                <p><strong>Employee:</strong> {employeeName}</p>
                <p><strong>Type:</strong> {request.LeaveType}</p>
                <p><strong>Dates:</strong> {request.StartDate:yyyy-MM-dd} to {request.EndDate:yyyy-MM-dd}</p>
                <p><strong>Reason:</strong> {request.Reason}</p>
                <hr style='border: none; border-top: 1px solid #eee;' />
                <p>Please review this request in the Management Dashboard:</p>
                <div style='margin-top: 20px;'>
                    <a href='{approveUrl}' style='background-color: #0078d4; color: white; padding: 10px 20px; text-decoration: none; border-radius: 5px;'>Review Requests</a>
                </div>
                <p style='margin-top: 20px; font-size: 0.8em; color: #777;'>
                    This is an automated notification from the African Ideas Attendance System.
                </p>
            </div>";

        await SendEmailAsync(managerEmail, subject, body);
    }

    public async Task SendLeaveStatusUpdateEmailAsync(string toEmail, string userName, LeaveRequest request)
    {
        var status = request.Status;
        var color = status == "Approved" ? "#28a745" : "#dc3545";
        var subject = $"Leave Request Update: {status}";
        
        var body = $@"
            <div style='font-family: Arial, sans-serif; padding: 20px; color: #333; border: 1px solid #eee; border-radius: 8px;'>
                <h2 style='color: {color};'>Leave Request {status}</h2>
                <p>Hello {userName},</p>
                <p>Your leave request has been reviewed.</p>
                <div style='background-color: #f9f9f9; padding: 15px; border-radius: 5px; margin: 20px 0;'>
                    <p><strong>Type:</strong> {request.LeaveType}</p>
                    <p><strong>Dates:</strong> {request.StartDate:yyyy-MM-dd} to {request.EndDate:yyyy-MM-dd}</p>
                    <p><strong>Duration:</strong> {request.DurationDays} business days</p>
                    <p><strong>Status:</strong> <span style='color: {color}; font-weight: bold;'>{status}</span></p>
                    {(!string.IsNullOrEmpty(request.AdminComment) ? $"<p><strong>Manager Comment:</strong> {request.AdminComment}</p>" : "")}
                </div>
                <p>You can view your full leave history in your employee profile.</p>
                <hr style='border: none; border-top: 1px solid #eee;' />
                <p style='font-size: 0.8em; color: #777;'>This is an automated message. Please do not reply.</p>
            </div>";

        await SendEmailAsync(toEmail, subject, body);
    }
}
