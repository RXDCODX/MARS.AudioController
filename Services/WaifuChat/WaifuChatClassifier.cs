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

        Обращения включают:
        - Женский род: жена, wife, моя дорогая, милая, котик, солнце, зайка
        - Мужской род: муж, husband, мой дорогой, милый
        - Нейтральный: супруг, супруга, spouse, партнёр, partner, половинка, любимый/любимая
        - Прямое имя персонажа (например: Куруми, Рин, Рем)
        - Вопросы к партнёру, просьбы рассказать, комплименты, ревность

        Общее общение: обсуждение игр, приветствия не к партнёру, вопросы к чату, мемы, команды бота.
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

    public ClassificationResult Classify(string message)
    {
        try
        {
            _classifier.Guidance = ClassificationGuidance;
            var categoryIndex = _classifier.GetBestCategory(Categories, message);
            var category = Categories[categoryIndex];

            _logger.LogDebug(
                "Classified message: {Category} (index={Index})",
                category, categoryIndex);

            return new ClassificationResult
            {
                Category = category,
                IsWaifuChat = categoryIndex == 0,
                DetectedGender = categoryIndex == 0 ? DetectGender(message) : null,
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
