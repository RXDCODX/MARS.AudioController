using System.Net;
using MARS.Server.Hubs.Interfaces;
using MARS.Server.Hubs.Models.VoiceRecognition;
using MARS.Server.Services.Twitch.Entitys;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace MARS.AudioController.Services.TTS;

public class TtsHubClientHostedService(
    IConfiguration configuration,
    ISyntheziaQueueManager queueManager,
    ILogger<TtsHubClientHostedService> logger
) : BackgroundService
{
    private const string DefaultHubUrl = "http://localhost:9255/hubs/tts";
    private string _hubUrl = DefaultHubUrl;
    private HubConnection? _connection;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _hubUrl = configuration["TtsHub:Url"] ?? DefaultHubUrl;
        _connection = new HubConnectionBuilder()
            .WithUrl(_hubUrl)
            .WithAutomaticReconnect()
            .Build();

        RegisterHandlers(_connection, stoppingToken);

        _connection.Reconnecting += error =>
        {
            logger.LogWarning(error, "TTS hub connection is reconnecting.");
            return Task.CompletedTask;
        };

        _connection.Reconnected += async _ =>
        {
            logger.LogInformation("TTS hub connection re-established.");
            await RegisterAsConsumerAsync(stoppingToken);
        };

        _connection.Closed += error =>
        {
            logger.LogWarning(error, "TTS hub connection closed.");
            return Task.CompletedTask;
        };

        await StartConnectionAsync(stoppingToken);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }

    private void RegisterHandlers(HubConnection connection, CancellationToken stoppingToken)
    {
        connection.On<TwitchUser, string>(
            nameof(IVoiceRecognitionHub.PlayTts),
            async (user, message) =>
            {
                if (user is null || string.IsNullOrWhiteSpace(message))
                {
                    return;
                }

                await queueManager.EnqueueAsync(user, message);
            }
        );

        connection.On<TtsState>(
            nameof(IVoiceRecognitionHub.UpdateTtsState),
            async state =>
            {
                if (state is null)
                {
                    return;
                }

                await queueManager.ApplyStateAsync(state);
            }
        );
    }

    private async Task StartConnectionAsync(CancellationToken stoppingToken)
    {
        if (_connection is null)
        {
            return;
        }

        if (_connection.State == HubConnectionState.Disconnected)
        {
            for (var i = 0; i <= 10; i++)
            {
                try
                {
                    await _connection.StartAsync(stoppingToken);
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to connect to TTS hub at {HubUrl}", _hubUrl);
                }
            }
            await RegisterAsConsumerAsync(stoppingToken);
            logger.LogInformation("Connected to TTS hub at {HubUrl}", _hubUrl);
        }
    }

    private async Task RegisterAsConsumerAsync(CancellationToken stoppingToken)
    {
        if (_connection is not null && _connection.State == HubConnectionState.Connected)
        {
            await _connection.InvokeAsync(
                "RegisterAsTtsConsumer",
                cancellationToken: stoppingToken
            );
        }
    }

    private async Task ReportPlaybackStartedAsync(string text, CancellationToken stoppingToken)
    {
        if (_connection is not null && _connection.State == HubConnectionState.Connected)
        {
            await _connection.InvokeAsync(
                "ReportTtsPlaybackStarted",
                text,
                cancellationToken: stoppingToken
            );
        }
    }

    private async Task ReportPlaybackCompletedAsync(
        string text,
        TimeSpan duration,
        CancellationToken stoppingToken
    )
    {
        if (_connection is not null && _connection.State == HubConnectionState.Connected)
        {
            await _connection.InvokeAsync(
                "ReportTtsPlaybackCompleted",
                text,
                duration,
                cancellationToken: stoppingToken
            );
        }
    }

    private async Task ReportPlaybackFailedAsync(
        string text,
        string error,
        CancellationToken stoppingToken
    )
    {
        if (_connection is not null && _connection.State == HubConnectionState.Connected)
        {
            await _connection.InvokeAsync(
                "ReportTtsPlaybackFailed",
                text,
                error,
                cancellationToken: stoppingToken
            );
        }
    }
}
