using System.Collections.Concurrent;
using System.Text;
using LMKit.Data;
using LMKit.Data.Storage;
using LMKit.Model;
using LMKit.TextGeneration;
using LMKit.TextGeneration.Chat;
using LMKit.TextGeneration.Events;
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
    private readonly MessageProcessingQueue _messageQueue = new();
    private readonly CancellationTokenSource _processingCts = new();

    private const string SystemPromptTemplate =
        """
        Ты — {waifuName}, жена {displayName}. Ты общаешься с ним в Twitch чате.
        Ты любящая, заботливая и немного ревнивая жена. Говори коротко (до 2-3 предложений).
        Помни что обсуждала ранее с мужем. Упоминай детали из прошлых разговоров.
        Отвечай на русском языке. Будь игривой и ласковой.
        Не используй эмодзи.
        """;

    public WaifuLlmService(IOptions<WaifuChatOptions> options, ILogger<WaifuLlmService> logger)
    {
        _options = options.Value;
        _logger = logger;

        _logger.LogInformation("Loading chat model: {ModelId}", _options.ChatModelId);
        _chatModel = LM.LoadFromModelID(_options.ChatModelId);

        _logger.LogInformation("Loading embedding model: {ModelId}", _options.EmbedModelId);
        _embedModel = LM.LoadFromModelID(_options.EmbedModelId);

        _logger.LogInformation("LLM models loaded successfully");

        _ = Task.Factory.StartNew(
            () => ProcessQueueAsync(_processingCts.Token),
            _processingCts.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    public ChatRequest EnqueueMessage(
        string twitchId,
        string displayName,
        string waifuName,
        string message,
        string? characterDescription,
        string? messageId = null)
    {
        var request = new ChatRequest
        {
            TwitchId = twitchId,
            DisplayName = displayName,
            WaifuName = waifuName,
            Message = message,
            CharacterDescription = characterDescription,
            MessageId = messageId,
        };

        _messageQueue.Enqueue(request);
        _lastActivity[twitchId] = DateTime.UtcNow;

        _logger.LogInformation(
            "Enqueued message for {TwitchId} ({DisplayName}) as {WaifuName}",
            twitchId, displayName, waifuName);

        return request;
    }

    public bool IsProcessingOrQueued(string twitchId)
    {
        return _messageQueue.IsProcessingOrQueued(twitchId);
    }

    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_messageQueue.TryDequeue(out var request))
                {
                    await ProcessMessageAsync(request, ct);
                }
                else
                {
                    await Task.Delay(50, ct);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing queue");
                await Task.Delay(1000, ct);
            }
        }
    }

    private async Task ProcessMessageAsync(ChatRequest request, CancellationToken ct)
    {
        try
        {
            EvictStaleSessions();

            var systemPrompt = BuildSystemPrompt(
                request.WaifuName, request.DisplayName, request.CharacterDescription);

            var chat = _viewerChats.GetOrAdd(
                request.TwitchId,
                _ => new MultiTurnConversation(_chatModel)
                {
                    MaximumCompletionTokens = _options.MaxTokens,
                    SamplingMode = new RandomSampling { Temperature = _options.Temperature },
                    SystemPrompt = systemPrompt,
                    ReasoningLevel = ReasoningLevel.Low,
                }
            );

            _lastActivity[request.TwitchId] = DateTime.UtcNow;

            _logger.LogInformation(
                "Generating response for {TwitchId} ({DisplayName}) as {WaifuName}",
                request.TwitchId, request.DisplayName, request.WaifuName);

            var responseBuilder = new StringBuilder();
            var hasNonReasoningTokens = false;

            chat.AfterTextCompletion += OnAfterTextCompletion;

            TextGenerationResult result;
            try
            {
                result = await Task.Factory.StartNew(
                    () => chat.Submit(request.Message, ct),
                    ct,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
            }
            finally
            {
                chat.AfterTextCompletion -= OnAfterTextCompletion;
            }

            var responseText = hasNonReasoningTokens
                ? responseBuilder.ToString().Trim()
                : result.Completion.Trim();

            responseText = StripThinkingProcess(responseText);

            _messageQueue.CompleteMessage(request, responseText);

            void OnAfterTextCompletion(object? sender, AfterTextCompletionEventArgs e)
            {
                if (e.SegmentType != TextSegmentType.InternalReasoning)
                {
                    responseBuilder.Append(e.Text);
                    hasNonReasoningTokens = true;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate LLM response for {TwitchId}", request.TwitchId);
            _messageQueue.CompleteWithError(request, ex.Message);
        }
    }

    public static string StripThinkingProcess(string completion)
    {
        if (string.IsNullOrWhiteSpace(completion))
        {
            return completion;
        }

        var thinkIndex = completion.IndexOf("Thinking Process:", StringComparison.OrdinalIgnoreCase);
        if (thinkIndex < 0)
        {
            return completion;
        }

        var afterThink = completion[(thinkIndex + "Thinking Process:".Length)..];
        var doubleNewline = afterThink.IndexOf("\n\n", StringComparison.Ordinal);
        if (doubleNewline >= 0)
        {
            return afterThink[(doubleNewline + 2)..].TrimStart();
        }

        return afterThink.TrimStart();
    }

    public virtual async Task ExtractAndSaveAllFactsAsync(CancellationToken ct)
    {
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
        var staleKeys = _lastActivity.Where(kv => kv.Value < cutoff).Select(kv => kv.Key).ToList();

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

    private string BuildSystemPrompt(string waifuName, string displayName, string? characterDescription)
    {
        var prompt = SystemPromptTemplate
            .Replace("{waifuName}", waifuName)
            .Replace("{displayName}", displayName);

        if (!string.IsNullOrWhiteSpace(characterDescription))
        {
            prompt += $"\n\n## Твой характер (из аниме):\n{characterDescription}";
        }

        var now = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time"));

        prompt += $"\n\nТекущая дата и время: {now:dd.MM.yyyy, dddd, HH:mm} (МСК).";

        return prompt;
    }

    public void Dispose()
    {
        _processingCts.Cancel();
        _processingCts.Dispose();
        DisposeAllSessions();
        _chatModel?.Dispose();
        _embedModel?.Dispose();
    }
}
