namespace MARS.AudioController.Services.WaifuChat;

public class WaifuChatOptions
{
    public const string SectionName = "WaifuChat";

    /// <summary>
    /// ID модели. Поддерживает:
    /// - Enum строку: "Gemma4_E2b", "Llama31_8b"
    /// - LM-Kit ID: "gemma4:e2b", "llama3.1"
    /// - Любой валидный LM-Kit model ID
    /// </summary>
    public string ChatModelId { get; set; } = "gemma4:e2b";

    public string ClassifierModelId { get; set; } = "lmkit-tasks:4b-preview";

    public string EmbedModelId { get; set; } = "embeddinggemma-300m";

    public string ConnectionString { get; set; } = string.Empty;

    public string Schema { get; set; } = "waifu_chat";

    public int MaxTokens { get; set; } = 256;

    public float Temperature { get; set; } = 0.8f;

    public bool Enabled { get; set; } = true;

    public int ResponseCooldownSeconds { get; set; } = 30;

    public int MaxRememberedFacts { get; set; } = 20;

    public int MaxViewerSessions { get; set; } = 50;

    public TimeSpan SessionEvictionTimeout { get; set; } = TimeSpan.FromHours(2);

    public string GetChatModelId()
    {
        if (Enum.TryParse<ChatModel>(ChatModelId, ignoreCase: true, out var model))
        {
            return model.ToModelId();
        }
        return ChatModelId;
    }
}
