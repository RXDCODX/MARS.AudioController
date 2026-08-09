namespace MARS.AudioController.Services.WaifuChat;

public interface IWaifuLlmService
{
    Task<string?> GenerateResponseAsync(
        string twitchId,
        string displayName,
        string waifuName,
        string userMessage,
        CancellationToken ct = default);

    Task ExtractAndSaveAllFactsAsync(CancellationToken ct);
    void DisposeAllSessions();
}
