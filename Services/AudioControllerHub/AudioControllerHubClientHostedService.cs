using System.Text.Json;
using MARS.AudioController.Services.Obs;
using MARS.AudioController.Services.TTS;
using MARS.AudioController.Services.WaifuChat;
using MARS.Shared.Hubs;
using MARS.Shared.Models;
using MARS.Shared.Models.WaifuChat;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MARS.AudioController.Services.AudioControllerHub;

/// <summary>
/// Unified SignalR client that connects to MARS.Server's AudioControllerHub.
/// Replaces TtsHubClientHostedService and handles all commands: SoundBar, OBS, TTS, Health.
/// </summary>
public class AudioControllerHubClientHostedService : BackgroundService
{
    private const string DefaultHubUrl = "http://localhost:9255/hubs/audio-controller";

    private readonly IConfiguration _configuration;
    private readonly AudioControllerService _soundBarService;
    private readonly IObsService _obsService;
    private readonly ISyntheziaQueueManager _queueManager;
    private readonly ITtsHubConnectionHolder _hubConnectionHolder;
    private readonly WaifuLlmService? _waifuLlmService;
    private readonly WaifuChatClassifier? _classifier;
    private readonly ILogger<AudioControllerHubClientHostedService> _logger;

    public HubConnection? Connection { get; private set; }

    public AudioControllerHubClientHostedService(
        IConfiguration configuration,
        AudioControllerService soundBarService,
        IObsService obsService,
        ISyntheziaQueueManager queueManager,
        ITtsHubConnectionHolder hubConnectionHolder,
        ILogger<AudioControllerHubClientHostedService> logger,
        WaifuLlmService? waifuLlmService = null,
        WaifuChatClassifier? classifier = null
    )
    {
        _configuration = configuration;
        _soundBarService = soundBarService;
        _obsService = obsService;
        _queueManager = queueManager;
        _hubConnectionHolder = hubConnectionHolder;
        _logger = logger;
        _waifuLlmService = waifuLlmService;
        _classifier = classifier;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var hubUrl = _configuration["AudioControllerHub:Url"] ?? DefaultHubUrl;
        Connection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .Build();

        // Share the connection with SyntheziaQueueManager for SubmitAudioForRelay
        _hubConnectionHolder.Connection = Connection;

        RegisterHandlers(Connection);

        Connection.Reconnecting += error =>
        {
            _logger.LogWarning(error, "AudioControllerHub connection is reconnecting.");
            return Task.CompletedTask;
        };

        Connection.Reconnected += async _ =>
        {
            _logger.LogInformation("AudioControllerHub connection re-established.");
            await RegisterAsync(stoppingToken);
        };

        Connection.Closed += error =>
        {
            _logger.LogWarning(error, "AudioControllerHub connection closed.");
            return Task.CompletedTask;
        };

        await StartConnectionAsync(hubUrl, stoppingToken);
    }

    private void RegisterHandlers(HubConnection connection)
    {
        var hub = typeof(IAudioControllerHub);
        var server = typeof(IAudioControllerHubServer);

        // ── SoundBar ──
        connection.On<string, string[]>(
            nameof(IAudioControllerHub.MuteProcesses),
            async (correlationId, processNames) =>
            {
                try
                {
                    await _soundBarService.MuteAll(processNames);
                    await SendResponse(correlationId, true, null, null);
                }
                catch (Exception ex)
                {
                    await SendResponse(correlationId, false, null, ex.Message);
                }
            }
        );

        connection.On<string>(
            nameof(IAudioControllerHub.UnmuteProcesses),
            async (correlationId) =>
            {
                try
                {
                    await _soundBarService.UnMuteAll();
                    await SendResponse(correlationId, true, null, null);
                }
                catch (Exception ex)
                {
                    await SendResponse(correlationId, false, null, ex.Message);
                }
            }
        );

        connection.On<string>(
            nameof(IAudioControllerHub.GetBagCount),
            async (correlationId) =>
            {
                try
                {
                    var result = _soundBarService.GetBagCount();
                    await SendResponse(correlationId, true, result, null);
                }
                catch (Exception ex)
                {
                    await SendResponse(correlationId, false, null, ex.Message);
                }
            }
        );

        // ── OBS ──
        connection.On<string>(
            nameof(IAudioControllerHub.ConnectObs),
            async (correlationId) =>
            {
                try
                {
                    await _obsService.ConnectAsync();
                    await SendResponse(correlationId, true, null, null);
                }
                catch (Exception ex)
                {
                    await SendResponse(correlationId, false, null, ex.Message);
                }
            }
        );

        connection.On<string>(
            nameof(IAudioControllerHub.DisconnectObs),
            async (correlationId) =>
            {
                try
                {
                    _obsService.DisconnectAsync();
                    await SendResponse(correlationId, true, null, null);
                }
                catch (Exception ex)
                {
                    await SendResponse(correlationId, false, null, ex.Message);
                }
            }
        );

        connection.On<string, string?>(
            nameof(IAudioControllerHub.ScreenshotObs),
            async (correlationId, sourceName) =>
            {
                try
                {
                    var path = await _obsService.ScreenshotAsync(sourceName);
                    await SendResponse(correlationId, true, path, null);
                }
                catch (Exception ex)
                {
                    await SendResponse(correlationId, false, null, ex.Message);
                }
            }
        );

        connection.On<string>(
            nameof(IAudioControllerHub.FreezeObs),
            async (correlationId) =>
            {
                try
                {
                    var result = await _obsService.FreezeAsync();
                    await SendPauseResult(correlationId, result);
                }
                catch (Exception ex)
                {
                    await SendResponse(correlationId, false, null, ex.Message);
                }
            }
        );

        connection.On<string>(
            nameof(IAudioControllerHub.UnfreezeObs),
            async (correlationId) =>
            {
                try
                {
                    var result = await _obsService.UnfreezeAsync();
                    await SendPauseResult(correlationId, result);
                }
                catch (Exception ex)
                {
                    await SendResponse(correlationId, false, null, ex.Message);
                }
            }
        );

        connection.On<string>(
            nameof(IAudioControllerHub.SwitchToPauseScene),
            async (correlationId) =>
            {
                try
                {
                    var result = await _obsService.SwitchToPauseSceneAsync();
                    await SendPauseResult(correlationId, result);
                }
                catch (Exception ex)
                {
                    await SendResponse(correlationId, false, null, ex.Message);
                }
            }
        );

        connection.On<string>(
            nameof(IAudioControllerHub.SwitchFromPauseScene),
            async (correlationId) =>
            {
                try
                {
                    var result = await _obsService.SwitchFromPauseSceneAsync();
                    await SendPauseResult(correlationId, result);
                }
                catch (Exception ex)
                {
                    await SendResponse(correlationId, false, null, ex.Message);
                }
            }
        );

        connection.On<string, int>(
            nameof(IAudioControllerHub.TogglePauseObs),
            async (correlationId, mode) =>
            {
                try
                {
                    var pauseMode =
                        mode == 1 ? ObsPauseMode.PauseScene : ObsPauseMode.FreezeFrame;
                    var result = await _obsService.TogglePauseAsync(pauseMode);
                    await SendPauseResult(correlationId, result);
                }
                catch (Exception ex)
                {
                    await SendResponse(correlationId, false, null, ex.Message);
                }
            }
        );

        connection.On<string>(
            nameof(IAudioControllerHub.GetObsStatus),
            async (correlationId) =>
            {
                try
                {
                    var status = new ObsStatusDto
                    {
                        IsConnected = _obsService.IsConnected,
                        IsPaused = _obsService.IsPaused,
                    };
                    await SendResponse(
                        correlationId,
                        true,
                        JsonSerializer.Serialize(status),
                        null
                    );
                }
                catch (Exception ex)
                {
                    await SendResponse(correlationId, false, null, ex.Message);
                }
            }
        );

        // ── TTS ──
        connection.On<TwitchUser, string>(
            nameof(IAudioControllerHub.PlayTts),
            async (user, message) =>
            {
                if (!string.IsNullOrWhiteSpace(message))
                {
                    await _queueManager.EnqueueAsync(user, message);
                }
            }
        );

        connection.On<TtsState>(
            nameof(IAudioControllerHub.UpdateTtsState),
            async (state) =>
            {
                await _queueManager.ApplyStateAsync(state);
            }
        );

        connection.On<string>(
            nameof(IAudioControllerHub.ReassignVoice),
            async (userId) =>
            {
                if (!string.IsNullOrWhiteSpace(userId))
                {
                    await _queueManager.ReassignUserVoiceAsync(userId);
                }
            }
        );

        // ── WaifuChat ──
        connection.On<WaifuChatMessage>(
            nameof(IAudioControllerHub.WaifuChatMessage),
            async (msg) =>
            {
                _logger.LogInformation(
                    "[WaifuChat] Received from server: correlationId={CorrelationId}, " +
                    "twitchId={TwitchId}, displayName={DisplayName}, message='{Message}'",
                    msg.CorrelationId, msg.TwitchId, msg.DisplayName, msg.Message);

                if (_waifuLlmService is null)
                {
                    _logger.LogWarning("[WaifuChat] WaifuLlmService is NOT initialized — skipping");
                    return;
                }

                try
                {
                    // Если SkipClassifier — сразу в LLM, иначе через классификатор
                    if (msg.SkipClassifier)
                    {
                        _logger.LogInformation(
                            "[WaifuChat] SkipClassifier=true — direct to LLM for {TwitchId}",
                            msg.TwitchId);
                    }
                    else if (_classifier is not null)
                    {
                        var classification = _classifier.Classify(msg.Message);
                        if (!classification.IsWaifuChat)
                        {
                            _logger.LogInformation(
                                "[WaifuChat] Classified as {Category} (gender={Gender}) — skipping",
                                classification.Category, classification.DetectedGender ?? "null");
                            return;
                        }

                        _logger.LogInformation(
                            "[WaifuChat] Classified as waifu_chat (gender={Gender}) — enqueuing",
                            classification.DetectedGender ?? "null");
                    }

                    var request = _waifuLlmService.EnqueueMessage(
                        msg.TwitchId, msg.DisplayName, msg.WaifuName ?? "жена",
                        msg.Message, msg.CharacterDescription, msg.MessageId,
                        msg.LastAutoHelloMessage);

                    _logger.LogInformation(
                        "[WaifuChat] Enqueued for {TwitchId}, awaiting LLM response...",
                        msg.TwitchId);

                    var response = await request.TaskCompletionSource.Task;

                    _logger.LogInformation(
                        "[WaifuChat] LLM response for {TwitchId}: '{Response}' (length={Length})",
                        msg.TwitchId, response ?? "(null)", response?.Length ?? 0);

                    if (!string.IsNullOrWhiteSpace(response) && Connection?.State == HubConnectionState.Connected)
                    {
                        _logger.LogInformation(
                            "[WaifuChat] Sending WaifuChatResponse to server for {TwitchId}",
                            msg.TwitchId);

                        await Connection.InvokeAsync(
                            nameof(IAudioControllerHubServer.WaifuChatResponse),
                            new WaifuChatResponse
                            {
                                CorrelationId = msg.CorrelationId,
                                TwitchId = msg.TwitchId,
                                Response = response,
                                MessageId = msg.MessageId,
                            });

                        _logger.LogInformation(
                            "[WaifuChat] WaifuChatResponse sent successfully for {TwitchId}",
                            msg.TwitchId);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "[WaifuChat] Cannot send response: response={IsNull}, connection={State}",
                            response == null, Connection?.State);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[WaifuChat] Failed to process WaifuChatMessage for {TwitchId}", msg.TwitchId);
                }
            }
        );

        // ── Health ──
        connection.On<string>(
            nameof(IAudioControllerHub.Ping),
            async (correlationId) =>
            {
                await SendResponse(correlationId, true, "pong", null);
            }
        );
    }

    private async Task SendResponse(
        string correlationId,
        bool success,
        string? data,
        string? error
    )
    {
        if (Connection?.State == HubConnectionState.Connected)
        {
            await Connection.InvokeAsync(nameof(IAudioControllerHubServer.CommandResponse), correlationId, success, data, error);
        }
    }

    private async Task SendPauseResult(string correlationId, ObsPauseResult result)
    {
        var dto = new ObsPauseResultDto
        {
            Success = result.Success,
            IsPaused = result is { Success: true } ? _obsService.IsPaused : false,
            Error = result.Error,
            ScreenshotPath = result.ScreenshotPath,
        };
        await SendResponse(
            correlationId,
            result.Success,
            JsonSerializer.Serialize(dto),
            result.Error
        );
    }

    private async Task StartConnectionAsync(string hubUrl, CancellationToken ct)
    {
        if (Connection is null)
        {
            return;
        }

        for (var i = 0; i <= 10; i++)
        {
            try
            {
                await Connection.StartAsync(ct);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to connect to AudioControllerHub at {Url}",
                    hubUrl
                );
                await Task.Delay(1000 * (i + 1), ct);
            }
        }
        await RegisterAsync(ct);
        _logger.LogInformation("Connected to AudioControllerHub at {Url}", hubUrl);
    }

    private async Task RegisterAsync(CancellationToken ct)
    {
        if (Connection?.State == HubConnectionState.Connected)
        {
            await Connection.InvokeAsync(nameof(IAudioControllerHubServer.RegisterAsAudioController), cancellationToken: ct);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Connection is not null)
        {
            await Connection.DisposeAsync();
        }
        await base.StopAsync(cancellationToken);
    }
}
