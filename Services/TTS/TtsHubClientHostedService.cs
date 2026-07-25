using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MARS.AudioController.Services.TTS;

public class TtsHubClientHostedService(
    IConfiguration configuration,
    ISyntheziaQueueManager queueManager,
    ITtsHubConnectionHolder hubConnectionHolder,
    ILogger<TtsHubClientHostedService> logger
) : BackgroundService
{
    private const string DefaultHubUrl = "http://localhost:9255/hubs/tts";
    private string _hubUrl = DefaultHubUrl;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _hubUrl = configuration["TtsHub:Url"] ?? DefaultHubUrl;
        hubConnectionHolder.Connection = new HubConnectionBuilder().WithUrl(_hubUrl).WithAutomaticReconnect().Build();

        RegisterHandlers(stoppingToken);

        hubConnectionHolder.Connection.Reconnecting += error =>
        {
            logger.LogWarning(error, "TTS hub connection is reconnecting.");
            return Task.CompletedTask;
        };

        hubConnectionHolder.Connection.Reconnected += async _ =>
        {
            logger.LogInformation("TTS hub connection re-established.");
            await RegisterAsConsumerAsync(stoppingToken);
        };

        hubConnectionHolder.Connection.Closed += error =>
        {
            logger.LogWarning(error, "TTS hub connection closed.");
            return Task.CompletedTask;
        };

        await Task.Factory.StartNew(
            async () => await StartConnectionAsync(stoppingToken),
            stoppingToken
        );
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (hubConnectionHolder.Connection is not null)
        {
            await hubConnectionHolder.Connection.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }

    private void RegisterHandlers(CancellationToken stoppingToken)
    {
        hubConnectionHolder.Connection!.On<TwitchUser, string>(
            nameof(IVoiceRecognitionHub.PlayTts),
            async (user, message) =>
            {
                if (string.IsNullOrWhiteSpace(message))
                {
                    return;
                }

                await queueManager.EnqueueAsync(user, message);
            }
        );

        hubConnectionHolder.Connection!.On<TtsState>(
            nameof(IVoiceRecognitionHub.UpdateTtsState),
            async state =>
            {
                await queueManager.ApplyStateAsync(state);
            }
        );

        hubConnectionHolder.Connection!.On<string>(
            nameof(IVoiceRecognitionHub.ReassignVoice),
            async userId =>
            {
                if (string.IsNullOrWhiteSpace(userId))
                {
                    return;
                }

                await queueManager.ReassignUserVoiceAsync(userId);
            }
        );
    }

    private async Task StartConnectionAsync(CancellationToken stoppingToken)
    {
        if (hubConnectionHolder.Connection is null)
        {
            return;
        }

        if (hubConnectionHolder.Connection.State == HubConnectionState.Disconnected)
        {
            for (var i = 0; i <= 10; i++)
            {
                try
                {
                    await hubConnectionHolder.Connection.StartAsync(stoppingToken);
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
        if (hubConnectionHolder.Connection is not null && hubConnectionHolder.Connection.State == HubConnectionState.Connected)
        {
            await hubConnectionHolder.Connection.InvokeAsync("RegisterAsTtsConsumer", cancellationToken: stoppingToken);
        }
    }

    private async Task ReportPlaybackStartedAsync(string text, CancellationToken stoppingToken)
    {
        if (hubConnectionHolder.Connection is not null && hubConnectionHolder.Connection.State == HubConnectionState.Connected)
        {
            await hubConnectionHolder.Connection.InvokeAsync(
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
        if (hubConnectionHolder.Connection is not null && hubConnectionHolder.Connection.State == HubConnectionState.Connected)
        {
            await hubConnectionHolder.Connection.InvokeAsync(
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
        if (hubConnectionHolder.Connection is not null && hubConnectionHolder.Connection.State == HubConnectionState.Connected)
        {
            await hubConnectionHolder.Connection.InvokeAsync(
                "ReportTtsPlaybackFailed",
                text,
                error,
                cancellationToken: stoppingToken
            );
        }
    }
}
