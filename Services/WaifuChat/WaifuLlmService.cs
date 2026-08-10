using System.Collections.Concurrent;
using System.Text;
using LMKit.Data;
using LMKit.Data.Storage;
using LMKit.Model;
using LMKit.TextGeneration;
using LMKit.TextGeneration.Chat;
using LMKit.TextGeneration.Sampling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MARS.AudioController.Services.WaifuChat;

public class WaifuLlmService : IWaifuLlmService, IDisposable
{
    private readonly LM _chatModel;
    private readonly LM _embedModel;
    private readonly WaifuChatOptions _options;
    private readonly ILogger<WaifuLlmService> _logger;
    private readonly ConcurrentDictionary<string, MultiTurnConversation> _viewerChats = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastActivity = new();
    private readonly CooldownTracker _cooldownTracker;
    private readonly SemaphoreSlim _inferenceLock = new(1, 1);
    private readonly HttpClient _httpClient = new();

    private const string SystemPromptTemplate =
        """
        Ты — {waifuName}, жена {displayName}. Ты общаешься с ним в Twitch чате.
        Ты любящая, заботливая и немного ревнивая жена. Говори коротко (до 2-3 предложений).
        Помни что обсуждала ранее с мужем. Упоминай детали из прошлых разговоров.
        Отвечай на русском языке. Будь игривой и ласковой.
        Не используй эмодзи.
        """;

    public WaifuLlmService(
        IOptions<WaifuChatOptions> options,
        ILogger<WaifuLlmService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _cooldownTracker = new CooldownTracker(_options.ResponseCooldownSeconds);

        _logger.LogInformation("Loading chat model: {ModelId}", _options.ChatModelId);
        _chatModel = LM.LoadFromModelID(_options.ChatModelId);

        _logger.LogInformation("Loading embedding model: {ModelId}", _options.EmbedModelId);
        _embedModel = LM.LoadFromModelID(_options.EmbedModelId);

        _logger.LogInformation("LLM models loaded successfully");
    }

    public async Task<string?> GenerateResponseAsync(
        string twitchId,
        string displayName,
        string waifuName,
        string userMessage,
        CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return null;
        }

        if (_cooldownTracker.IsOnCooldown(twitchId))
        {
            _logger.LogDebug("Viewer {TwitchId} is on cooldown", twitchId);
            return null;
        }

        await _inferenceLock.WaitAsync(ct);
        try
        {
            EvictStaleSessions();

            var systemPrompt = BuildSystemPrompt(waifuName, displayName);

            var chat = _viewerChats.GetOrAdd(twitchId, _ =>
            {
                return new MultiTurnConversation(_chatModel)
                {
                    MaximumCompletionTokens = _options.MaxTokens,
                    SamplingMode = new RandomSampling
                    {
                        Temperature = _options.Temperature,
                    },
                    SystemPrompt = systemPrompt,
                };
            });

            _lastActivity[twitchId] = DateTime.UtcNow;

            _logger.LogInformation(
                "Generating response for {TwitchId} ({DisplayName}) as {WaifuName}",
                twitchId, displayName, waifuName);

            var result = await Task.Factory.StartNew(
                () => chat.Submit(userMessage, ct),
                ct,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

            var responseText = result.Completion.Trim();

            _cooldownTracker.SetCooldown(twitchId);

            return responseText;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate LLM response for {TwitchId}", twitchId);
            return null;
        }
        finally
        {
            _inferenceLock.Release();
        }
    }

    public virtual async Task ExtractAndSaveAllFactsAsync(CancellationToken ct)
    {
        // Fact extraction will be implemented when RAG persistence is added
        await Task.CompletedTask;
    }

    public virtual void DisposeAllSessions()
    {
        foreach (var chat in _viewerChats.Values)
        {
            chat.Dispose();
        }

        _viewerChats.Clear();
        _lastActivity.Clear();
    }

    private void EvictStaleSessions()
    {
        if (_viewerChats.Count < _options.MaxViewerSessions)
        {
            return;
        }

        var cutoff = DateTime.UtcNow - _options.SessionEvictionTimeout;
        var staleKeys = _lastActivity
            .Where(kv => kv.Value < cutoff)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in staleKeys)
        {
            if (_viewerChats.TryRemove(key, out var chat))
            {
                chat.Dispose();
                _lastActivity.TryRemove(key, out _);
            }
        }

        if (_viewerChats.Count >= _options.MaxViewerSessions)
        {
            var oldest = _lastActivity
                .OrderBy(kv => kv.Value)
                .Take(_viewerChats.Count - _options.MaxViewerSessions + 1)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in oldest)
            {
                if (_viewerChats.TryRemove(key, out var chat))
                {
                    chat.Dispose();
                    _lastActivity.TryRemove(key, out _);
                }
            }
        }
    }

    private string BuildSystemPrompt(string waifuName, string displayName)
    {
        return SystemPromptTemplate
            .Replace("{waifuName}", waifuName)
            .Replace("{displayName}", displayName);
    }

    public void Dispose()
    {
        _inferenceLock.Dispose();
        DisposeAllSessions();
        _chatModel?.Dispose();
        _embedModel?.Dispose();
        _httpClient?.Dispose();
    }
}
