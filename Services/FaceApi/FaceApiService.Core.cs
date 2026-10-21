using Africanideasfaceapi.Helpers;
using Microsoft.Azure.CognitiveServices.Vision.Face;
using Microsoft.Azure.CognitiveServices.Vision.Face.Models;
using Africanideasfaceapi.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using Africanideasfaceapi.Data;
using Microsoft.EntityFrameworkCore;

using Africanideasfaceapi.Hubs;
using Africanideasfaceapi.Services.Interfaces;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Africanideasfaceapi.Models.Configuration;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using Africanideasfaceapi.Services.FaceApi;

[assembly: InternalsVisibleTo("Africanideasfaceapi.Tests")]

namespace Africanideasfaceapi.Services;

public partial class FaceApiService
{
    private readonly IFaceClientWrapper _faceClient;
    private readonly string _personGroupId;
    private readonly ILogger<FaceApiService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHubContext<AnalyticsHub>? _hubContext;


    private Task? _personGroupInitTask;
    private readonly SemaphoreSlim _personGroupInitLock = new(1, 1);
    private readonly SemaphoreSlim _settingsCacheLock = new(1, 1);
    private double? _cachedConfidenceThreshold;
    private DateTime _confidenceThresholdExpiresAt = DateTime.MinValue;

    public FaceApiService(
        IFaceClientWrapper faceClient, 
        ILogger<FaceApiService> logger, 
        IServiceProvider serviceProvider, 
        IOptions<AzureFaceApiSettings> faceApiOptions,
        IHubContext<AnalyticsHub>? hubContext = null)
    {
        _faceClient = faceClient;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _hubContext = hubContext;
        _personGroupId = faceApiOptions.Value.PersonGroupId;

        _logger.LogInformation("FaceApiService initialized with IFaceClientWrapper");

        // Start PersonGroup init in background
        _personGroupInitTask = Task.Run(async () =>
        {
            try
            {
                await InitializePersonGroup();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PersonGroup initialization failed. Will retry on first use.");
            }
        });
    }

    /// <summary>
    /// Ensures PersonGroup exists and is ready.
    /// </summary>
    public async Task EnsurePersonGroupReadyAsync(CancellationToken cancellationToken = default)
    {
        await _personGroupInitLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_personGroupInitTask != null)
            {
                await _personGroupInitTask.ConfigureAwait(false);
                _personGroupInitTask = null;
            }
            await InitializePersonGroup().ConfigureAwait(false);
        }
        finally
        {
            _personGroupInitLock.Release();
        }
    }

    private async Task InitializePersonGroup()
    {
        try
        {
            await _faceClient.GetPersonGroupAsync(_personGroupId);
        }
        catch
        {
            try
            {
                // PersonGroup doesn't exist, create it
                await _faceClient.CreatePersonGroupAsync(_personGroupId, "African Ideas Users", RecognitionModel.Recognition04);
                // Start training
                await _faceClient.TrainPersonGroupAsync(_personGroupId);
            }
            catch (Exception createEx)
            {
                _logger.LogWarning(createEx, "PersonGroup initialization error. Will retry on first use.");
            }
        }
    }
}
