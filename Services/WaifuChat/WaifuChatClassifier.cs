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
        Классифицируй сообщение как обращение к партнёру (waifu_chat) или общее общение (general_chat).

        waifu_chat — если сообщение:
        - Содержит обращение: любимка, котик, солнце, зайка, дорогая, милая, родная, красавица
        - Обращено к имени персонажа: Куруми, Рин, Рем, Асуна
        - Содержит комплимент или ревность
        - Является вопросом/просьбой к партнёру
        - Содержит "жена", "муж", "супруг", "партнёр"

        general_chat — если сообщение:
        - Обсуждает игры, стримы, новости
        - Содержит команды бота: !roll, !coinflip
        - Обращено к чату, а не к партнёру
        - Содержит "привет всем", "как дела" без обращения к партнёру
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
        // Прямые обращения
        "жена", "муж", "супруг", "супруга", "партнёр", "партнер", "половинка",
        "wife", "husband", "spouse", "partner",
        // Ласковые
        "любимая", "любимый", "любимка", "дорогая", "дорогой",
        "милая", "милый", "котик", "солнце", "зайка", "зайчик",
        "родная", "родной", "красавица", "красавец",
        // Английские
        "darling", "babe", "honey", "sweetie", "dear",
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
            || lower.Contains("дорогая") || lower.Contains("любимая") || lower.Contains("любимка")
            || lower.Contains("родная") || lower.Contains("красавица"))
        {
            return "female";
        }

        if (lower.Contains("муж") || lower.Contains("husband") || lower.Contains("милый")
            || lower.Contains("дорогой") || lower.Contains("любимый") || lower.Contains("родной"))
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
