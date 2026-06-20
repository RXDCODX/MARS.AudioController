using System.Collections.Concurrent;
using System.IO;
using System.Runtime.Versioning;
using MARS.AudioController.Models;

namespace MARS.AudioController.Services.TTS;

public interface ISyntheziaQueueManager
{
    Task EnqueueAsync(TwitchUser user, string message);

    Task ApplyStateAsync(TtsState state);
}

[SupportedOSPlatform("windows")]
public class SyntheziaQueueManager(
    TtsPlaybackService ttsPlaybackService,
    SystemSpeechTtsPlaybackService systemSpeechTtsPlaybackService,
    TtsPlaybackStateService playbackState,
    IConfiguration configuration,
    ILogger<SyntheziaQueueManager> logger
) : BackgroundService, ISyntheziaQueueManager
{
    private readonly ConcurrentQueue<(TwitchUser User, string Message)> _queue = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly Lock _voicesGate = new();
    private readonly Dictionary<string, VoiceAssignment> _linkedVoices = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly Dictionary<string, string> _voiceDisplayNames = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        { "M1", "Миша" },
        { "M2", "Сергей" },
        { "M3", "Алексей" },
        { "M4", "Дмитрий" },
        { "M5", "Андрей" },
        { "F1", "Елена" },
        { "F2", "Ольга" },
        { "F3", "Наталья" },
        { "F4", "Татьяна" },
        { "F5", "Мария" },
    };
    private readonly List<string> _availableVoiceStyles =
        configuration.GetSection("Tts:VoiceStyles").Get<List<string>>() ?? [];
    private readonly IReadOnlyList<string> _availableSystemVoices =
        systemSpeechTtsPlaybackService.GetInstalledVoices();
    private string? _lastUserTwitchId;

    public Task EnqueueAsync(TwitchUser user, string message)
    {
        if (user is null || string.IsNullOrWhiteSpace(message))
        {
            return Task.CompletedTask;
        }

        if (playbackState.IsStopped)
        {
            logger.LogInformation("TTS message ignored because playback is stopped.");
            return Task.CompletedTask;
        }

        _queue.Enqueue((user, message));
        _signal.Release();
        return Task.CompletedTask;
    }

    public Task ApplyStateAsync(TtsState state)
    {
        playbackState.ApplyState(state);

        if (state.IsStopped)
        {
            while (_queue.TryDequeue(out _)) { }

            logger.LogInformation("TTS queue was cleared because playback was stopped.");
        }

        return Task.CompletedTask;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await _signal.WaitAsync(stoppingToken);

            if (playbackState.IsStopped)
            {
                while (_queue.TryDequeue(out _)) { }

                continue;
            }

            if (_queue.TryDequeue(out var queued))
            {
                try
                {
                    var (assignment, isNew) = ResolveOrAssignVoice(queued.User.TwitchId);

                    if (isNew)
                    {
                        var displayName = GetVoiceDisplayName(assignment);
                        var greeting =
                            $"Привет, {queued.User.DisplayName}! Для тебя был выбран голос {displayName}";
                        await PlayByAssignmentAsync(assignment, greeting, stoppingToken);
                    }

                    var isConsecutive = queued.User.TwitchId == _lastUserTwitchId;
                    _lastUserTwitchId = queued.User.TwitchId;

                    var speechText = BuildSpeechText(queued.User, queued.Message, !isConsecutive);
                    await PlayByAssignmentAsync(assignment, speechText, stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Failed to play queued TTS message for {User}",
                        queued.User.DisplayName
                    );
                }
            }
        }
    }

    private (VoiceAssignment Assignment, bool IsNew) ResolveOrAssignVoice(string userKey)
    {
        lock (_voicesGate)
        {
            if (_linkedVoices.TryGetValue(userKey, out var existing))
            {
                return (existing, false);
            }

            var availableBindings = BuildAvailableBindings();
            var fallback = new VoiceAssignment(VoiceEngine.Onnx, "assets/voice_styles/M1.json");

            var choice =
                availableBindings.Count == 0
                    ? fallback
                    : availableBindings[Random.Shared.Next(availableBindings.Count)];

            _linkedVoices[userKey] = choice;
            return (choice, true);
        }
    }

    private IReadOnlyList<VoiceAssignment> BuildAvailableBindings()
    {
        var result = new List<VoiceAssignment>();

        if (_availableVoiceStyles.Count > 0)
        {
            result.AddRange(
                _availableVoiceStyles
                    .Where(style => !string.IsNullOrWhiteSpace(style))
                    .Select(style => new VoiceAssignment(VoiceEngine.Onnx, style))
            );
        }

        if (_availableSystemVoices.Count > 0)
        {
            result.AddRange(
                _availableSystemVoices
                    .Where(voice => !string.IsNullOrWhiteSpace(voice))
                    .Select(voice => new VoiceAssignment(VoiceEngine.WinApi, voice))
            );
        }

        return result;
    }

    private async Task PlayByAssignmentAsync(
        VoiceAssignment assignment,
        string text,
        CancellationToken cancellationToken
    )
    {
        if (assignment.Engine == VoiceEngine.WinApi)
        {
            await systemSpeechTtsPlaybackService.PlayAsync(
                text,
                assignment.VoiceId,
                playbackState.Volume,
                cancellationToken
            );
        }
        else
        {
            await ttsPlaybackService.PlayAsync(
                new TtsPlaybackRequest
                {
                    Text = text,
                    VoiceStylePath = assignment.VoiceId,
                    Volume = playbackState.Volume,
                    Language = "na",
                },
                cancellationToken
            );
        }
    }

    private string GetVoiceDisplayName(VoiceAssignment assignment)
    {
        if (string.IsNullOrWhiteSpace(assignment.VoiceId))
        {
            return assignment.VoiceId;
        }

        if (assignment.Engine == VoiceEngine.WinApi)
        {
            return assignment.VoiceId;
        }

        var file = Path.GetFileNameWithoutExtension(assignment.VoiceId);
        if (string.IsNullOrWhiteSpace(file))
        {
            return assignment.VoiceId;
        }

        return _voiceDisplayNames.GetValueOrDefault(file, file);
    }

    internal static string BuildSpeechText(TwitchUser user, string message, bool includePrefix)
    {
        var result = message;

        if (includePrefix)
        {
            var userName = string.IsNullOrWhiteSpace(user.DisplayName)
                ? user.UserLogin
                : user.DisplayName;

            if (!string.IsNullOrWhiteSpace(userName))
            {
                result = $"{userName} пишет: {message}";
            }
        }

        result = Helper.NormalizeSpeechText(result);

        return result;
    }

    private enum VoiceEngine
    {
        Onnx,
        WinApi,
    }

    private readonly record struct VoiceAssignment(VoiceEngine Engine, string VoiceId);
}
