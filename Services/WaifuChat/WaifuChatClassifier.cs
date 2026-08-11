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
        Определи, является ли сообщение обращением к жене/подруге в Twitch чате.
        Обращение к жене включает: прямое имя персонажа, слова 'жена', 'wife', 'моя дорогая',
        вопросы к жене, просьбы рассказать что-то, комплименты, ревность.
        Общее общение: обсуждение игр, приветствия не к жене, вопросы к чату, мемы, команды бота.
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
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Classification failed, defaulting to general_chat");
            return new ClassificationResult
            {
                Category = "general_chat",
                IsWaifuChat = false,
            };
        }
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
}
