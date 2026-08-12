using LMKit.Model;
using LMKit.TextAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MARS.AudioController.Services.WaifuChat;

public class WaifuChatClassifier : IDisposable
{
    private readonly LM _model;
    private readonly Categorization _classifier;
    private readonly ILogger<WaifuChatClassifier> _logger;

    private static readonly string[] Categories = ["waifu_chat", "general_chat"];

    private const string ClassificationGuidance =
        """
        Определи, является ли сообщение обращением к партнёру/супругу в Twitch чате.

        Триггеры (waifu_chat):
        - Женский род: жена, wife, моя дорогая, милая, котик, солнце, зайка, дорогая, любимая
        - Мужской род: муж, husband, мой дорогой, милый, дорогой, любимый
        - Нейтральный: супруг, супруга, spouse, партнёр, partner, половинка
        - Прямое имя персонажа (например: Куруми, Рин, Рем)
        - Вопросы к партнёру, просьбы рассказать, комплименты, ревность
        - Реплай на сообщение партнёра

        general_chat: обсуждение игр, мемы, команды бота (!roll, !coinflip), вопросы к чату.
        """;

    public WaifuChatClassifier(
        IOptions<WaifuChatOptions> options,
        ILogger<WaifuChatClassifier> logger)
    {
        _logger = logger;
        var modelId = options.Value.ClassifierModelId;

        _logger.LogInformation("Loading classifier model: {ModelId}", modelId);
        _model = LM.LoadFromModelID(modelId);
        _classifier = new Categorization(_model);
        _logger.LogInformation("Classifier model loaded successfully");
    }

    private static readonly string[] WaifuKeywords =
    [
        "жена", "муж", "супруг", "супруга", "партнёр", "партнер", "половинка",
        "wife", "husband", "spouse", "partner"
    ];

    public ClassificationResult Classify(string message)
    {
        try
        {
            var lower = message.ToLowerInvariant();
            var hasKeyword = WaifuKeywords.Any(kw => lower.Contains(kw));

            string category;
            if (hasKeyword)
            {
                category = "waifu_chat";
                _logger.LogDebug("Classified by keyword match: {Category}", category);
            }
            else
            {
                _classifier.Guidance = ClassificationGuidance;
                var categoryIndex = _classifier.GetBestCategory(Categories, message);
                category = Categories[categoryIndex];

                _logger.LogDebug(
                    "Classified by model: {Category} (index={Index})",
                    category, categoryIndex);
            }

            return new ClassificationResult
            {
                Category = category,
                IsWaifuChat = category == "waifu_chat",
                DetectedGender = category == "waifu_chat" ? DetectGender(message) : null,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Classification failed, defaulting to general_chat");
            return new ClassificationResult
            {
                Category = "general_chat",
                IsWaifuChat = false,
                DetectedGender = null,
            };
        }
    }

    private static string? DetectGender(string message)
    {
        var lower = message.ToLowerInvariant();

        if (lower.Contains("жена") || lower.Contains("wife") || lower.Contains("милая")
            || lower.Contains("котик") || lower.Contains("солнце") || lower.Contains("зайка")
            || lower.Contains("дорогая") || lower.Contains("любимая"))
        {
            return "female";
        }

        if (lower.Contains("муж") || lower.Contains("husband") || lower.Contains("милый")
            || lower.Contains("дорогой") || lower.Contains("любимый"))
        {
            return "male";
        }

        if (lower.Contains("супруг") || lower.Contains("spouse") || lower.Contains("партнёр")
            || lower.Contains("partner") || lower.Contains("половинка"))
        {
            return "neutral";
        }

        return null;
    }

    public void Dispose()
    {
        _model?.Dispose();
    }
}

public class ClassificationResult
{
    public required string Category { get; set; }
    public bool IsWaifuChat { get; set; }
    public string? DetectedGender { get; set; }
}
