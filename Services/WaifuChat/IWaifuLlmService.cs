namespace MARS.AudioController.Services.WaifuChat;

public interface IWaifuLlmService
{
    ChatRequest EnqueueMessage(
        string twitchId,
        string displayName,
        string waifuName,
        string message,
        string? characterDescription,
        string? messageId = null);

    bool IsProcessingOrQueued(string twitchId);

    Task ExtractAndSaveAllFactsAsync(CancellationToken ct);
    void DisposeAllSessions();
}
