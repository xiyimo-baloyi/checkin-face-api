using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Azure.CognitiveServices.Vision.Face.Models;
using Africanideasfaceapi.Services;

namespace Africanideasfaceapi.Middleware;

public class GlobalExceptionHandlerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;
    private readonly IWebHostEnvironment _environment;
    private readonly TelemetryService _telemetryService;

    public GlobalExceptionHandlerMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionHandlerMiddleware> logger,
        IWebHostEnvironment environment,
        TelemetryService telemetryService)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
        _telemetryService = telemetryService;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unhandled exception occurred during request {Path}", context.Request.Path);
            
            // Track the exception in Application Insights via TelemetryService
            _telemetryService.TrackException(ex, new Dictionary<string, string>
            {
                { "Path", context.Request.Path },
                { "Method", context.Request.Method },
                { "TraceId", context.TraceIdentifier }
            });

            await HandleExceptionAsync(context, ex, _environment.IsDevelopment());
        }
    }

    private static Task HandleExceptionAsync(HttpContext context, Exception exception, bool isDevelopment)
    {
        var code = HttpStatusCode.InternalServerError;
        var errorResponse = new ErrorResponse
        {
            StatusCode = (int)code,
            Error = "Internal Server Error",
            Message = "An unexpected error occurred while processing your request.",
            TraceId = context.TraceIdentifier
        };

        // Categorize exceptions
        switch (exception)
        {
            case ArgumentException argEx:
                code = HttpStatusCode.BadRequest;
                errorResponse.Error = "Invalid Request";
                errorResponse.Message = argEx.Message;
                break;
                
            case UnauthorizedAccessException:
                code = HttpStatusCode.Unauthorized;
                errorResponse.Error = "Unauthorized";
                errorResponse.Message = "You are not authorized to perform this action.";
                break;
                
            case InvalidOperationException opEx:
                code = HttpStatusCode.BadRequest;
                errorResponse.Error = "Invalid Operation";
                errorResponse.Message = opEx.Message;
                break;

            case APIErrorException apiEx:
                // Handle Azure Cognitive Services specific errors
                code = apiEx.Response?.StatusCode ?? HttpStatusCode.InternalServerError;
                errorResponse.Error = "Face API Error";
                errorResponse.Message = apiEx.Body?.Error?.Message ?? apiEx.Message;
                break;
        }

        if (isDevelopment)
        {
            errorResponse.Details = exception.ToString(); // Include full stack trace in dev
            errorResponse.Message = exception.Message;
        }

        errorResponse.StatusCode = (int)code;
        
        var result = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true 
        });

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)code;
        return context.Response.WriteAsync(result);
    }
}

public class ErrorResponse
{
    public string Error { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public string? Details { get; set; }
    public string? TraceId { get; set; }
}

