using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Africanideasfaceapi.Helpers;
namespace Africanideasfaceapi.Services;

public class WhatsAppService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<WhatsAppService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    // These would ideally come from configuration
    private const string WhatsAppApiUrl = "https://graph.facebook.com/v18.0"; // Example Meta Cloud API
    
    public WhatsAppService(IConfiguration configuration, ILogger<WhatsAppService> logger, IHttpClientFactory httpClientFactory)
    {
        _configuration = configuration;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public async Task SendMessageAsync(string toPhoneNumber, string messageText)
    {
        var settings = _configuration.GetSection("WhatsAppSettings");
        var phoneNumberId = settings["PhoneNumberId"];
        var accessToken = settings["AccessToken"];
        var enabled = settings.GetValue<bool>("Enabled");

        if (!enabled)
        {
            _logger.LogInformation("WhatsApp integration disabled. Skipping message to {PhoneNumber}: {Message}", toPhoneNumber, messageText);
            return;
        }
        
        // Simulating sending for now if config is missing
        if (string.IsNullOrEmpty(phoneNumberId) || string.IsNullOrEmpty(accessToken))
        {
             _logger.LogWarning("WhatsApp configuration missing. Message would have been sent to {PhoneNumber}: {Message}", toPhoneNumber, messageText);
             return;
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var payload = new
            {
                messaging_product = "whatsapp",
                to = toPhoneNumber,
                type = "text",
                text = new { body = messageText }
            };

            var jsonPayload = JsonConvert.SerializeObject(payload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            var response = await ResilienceHelper.ExecuteWithRetryAsync(
                () => client.PostAsync($"{WhatsAppApiUrl}/{phoneNumberId}/messages", content),
                _logger,
                "SendWhatsAppMessage");
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("WhatsApp message sent successfully to {PhoneNumber}", toPhoneNumber);
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to send WhatsApp message to {PhoneNumber}. Status: {StatusCode}. Error: {Error}", toPhoneNumber, response.StatusCode, errorContent);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception sending WhatsApp message to {PhoneNumber} after retries", toPhoneNumber);
        }
    }

    public async Task SendAttendanceReminderAsync(string phoneNumber, string employeeName)
    {
        var message = $"Hello {employeeName}, this is a reminder to clock in for your shift on the African Ideas Attendance System. Please verify your attendance now.";
        await SendMessageAsync(phoneNumber, message);
    }

    public async Task SendLateAlertAsync(string phoneNumber, string employeeName, TimeSpan lateTime)
    {
         var message = $"Alert: {employeeName}, you have not clocked in yet. You are currently {lateTime.TotalMinutes:F0} minutes late. Please clock in immediately.";
         await SendMessageAsync(phoneNumber, message);
    }
    
    // Future: Add template message support for approved approvals/OTP
}
