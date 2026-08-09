namespace MARS.AudioController.Services.WaifuChat;

public class WaifuChatOptions
{
    public const string SectionName = "WaifuChat";

    public string ChatModelId { get; set; } = "qwen2.5:0.5b";

    public string EmbedModelId { get; set; } = "embeddinggemma-300m";

    public string DataPath { get; set; } = "./waifu-chat-data";

    public int MaxTokens { get; set; } = 256;

    public float Temperature { get; set; } = 0.8f;

    public bool Enabled { get; set; } = true;

    public int ResponseCooldownSeconds { get; set; } = 30;

    public int MaxRememberedFacts { get; set; } = 20;

    public int MaxViewerSessions { get; set; } = 50;

    public TimeSpan SessionEvictionTimeout { get; set; } = TimeSpan.FromHours(2);
}
