using System.Collections.Concurrent;
using System.IO;
using MARS.AudioController.Models;
using MARS.Server.Services.Twitch.Entitys;

namespace MARS.AudioController.Services.TTS;

public interface ISyntheziaQueueManager
{
    Task EnqueueAsync(TwitchUser user, string message);
}

public class SyntheziaQueueManager(
    TtsPlaybackService ttsPlaybackService,
    IConfiguration configuration,
    ILogger<SyntheziaQueueManager> logger
) : BackgroundService, ISyntheziaQueueManager
{
    private readonly ConcurrentQueue<(TwitchUser User, string Message)> _queue = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly Dictionary<string, string> _linkedVoices = new(
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

    public Task EnqueueAsync(TwitchUser user, string message)
    {
        if (user is null || string.IsNullOrWhiteSpace(message))
        {
            return Task.CompletedTask;
        }

        _queue.Enqueue((user, message));
        _signal.Release();
        return Task.CompletedTask;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await _signal.WaitAsync(stoppingToken);

            if (_queue.TryDequeue(out var queued))
            {
                try
                {
                    var (voiceStyle, isNew) = ResolveOrAssignVoice(queued.User.TwitchId);

                    if (isNew)
                    {
                        var displayName = GetVoiceDisplayName(voiceStyle);
                        var greeting =
                            $"Привет, {queued.User.DisplayName}! Для тебя был выбран голос {displayName}";
                        await ttsPlaybackService.PlayAsync(
                            new TtsPlaybackRequest
                            {
                                Text = greeting,
                                VoiceStylePath = voiceStyle,
                                Language = "na",
                            },
                            stoppingToken
                        );
                    }

                    // Play the actual message
                    await ttsPlaybackService.PlayAsync(
                        new TtsPlaybackRequest
                        {
                            Text = queued.Message,
                            VoiceStylePath = voiceStyle,
                            Language = "na",
                        },
                        stoppingToken
                    );
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

    private (string VoiceStyle, bool IsNew) ResolveOrAssignVoice(string userKey)
    {
        if (_linkedVoices.TryGetValue(userKey, out var existing))
        {
            return (existing, false);
        }

        if (_availableVoiceStyles.Count == 0)
        {
            var fallback = "assets/voice_styles/M1.json";
            _linkedVoices[userKey] = fallback;
            return (fallback, true);
        }

        var index = Random.Shared.Next(_availableVoiceStyles.Count);
        var choice = _availableVoiceStyles[index];
        _linkedVoices[userKey] = choice;
        return (choice, true);
    }

    private string GetVoiceDisplayName(string voiceStylePath)
    {
        if (string.IsNullOrWhiteSpace(voiceStylePath))
        {
            return voiceStylePath;
        }

        var file = Path.GetFileNameWithoutExtension(voiceStylePath);
        if (string.IsNullOrWhiteSpace(file))
        {
            return voiceStylePath;
        }

        // Try mapping known short codes first (F1, M1 etc.)
        if (_voiceDisplayNames.TryGetValue(file, out var mapped))
        {
            return mapped;
        }

        // Fallback: return the filename/code as-is
        return file;
    }
}
