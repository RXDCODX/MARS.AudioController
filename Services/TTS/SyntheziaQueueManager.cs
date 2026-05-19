using System.Collections.Concurrent;
using MARS.AudioController.Models;
using MARS.Server.Services.Twitch.Entitys;

namespace MARS.AudioController.Services.TTS;

public interface ISyntheziaQueueManager
{
    Task EnqueueAsync(TwitchUser user, string message);
}

public class SyntheziaQueueManager : BackgroundService, ISyntheziaQueueManager
{
    private readonly ConcurrentQueue<(TwitchUser User, string Message)> _queue = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly Dictionary<string, string> _linkedVoices = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _availableVoiceStyles;
    private readonly TtsPlaybackService _ttsPlaybackService;
    private readonly ILogger<SyntheziaQueueManager> _logger;

    public bool IsServiceActive { get; set; } = true;

    public SyntheziaQueueManager(
        TtsPlaybackService ttsPlaybackService,
        IConfiguration configuration,
        ILogger<SyntheziaQueueManager> logger
    )
    {
        _ttsPlaybackService = ttsPlaybackService;
        _logger = logger;
        _availableVoiceStyles = configuration.GetSection("Tts:VoiceStyles").Get<List<string>>() ?? new List<string>();
    }

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
                    var voiceStyle = ResolveOrAssignVoice(queued.User.TwitchId);

                    // If this is a newly assigned voice, say greeting first
                    var isNew = !_linkedVoices.ContainsKey(queued.User.TwitchId);
                    if (isNew)
                    {
                        _linkedVoices[queued.User.TwitchId] = voiceStyle;
                        var greeting = $"Привет, {queued.User.DisplayName}! Для тебя был выбран голос {voiceStyle}";
                        await _ttsPlaybackService.PlayAsync(
                            new TtsPlaybackRequest
                            {
                                Text = greeting,
                                VoiceStylePath = voiceStyle,
                                Language = "ru"
                            },
                            stoppingToken
                        );
                    }

                    // Play the actual message
                    await _ttsPlaybackService.PlayAsync(
                        new TtsPlaybackRequest
                        {
                            Text = queued.Message,
                            VoiceStylePath = voiceStyle,
                            Language = "ru"
                        },
                        stoppingToken
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to play queued TTS message for {User}",
                        queued.User.DisplayName
                    );
                }
            }
        }
    }

    private string ResolveOrAssignVoice(string userKey)
    {
        if (_linkedVoices.TryGetValue(userKey, out var existing))
        {
            return existing;
        }

        if (_availableVoiceStyles.Count == 0)
        {
            return "assets/voice_styles/M1.json";
        }

        var index = Random.Shared.Next(_availableVoiceStyles.Count);
        var choice = _availableVoiceStyles[index];
        _linkedVoices[userKey] = choice;
        return choice;
    }
}
