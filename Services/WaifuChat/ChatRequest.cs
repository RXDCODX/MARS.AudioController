namespace MARS.AudioController.Services.WaifuChat;

public class ChatRequest
{
    public required string TwitchId { get; set; }

    public required string DisplayName { get; set; }

    public required string WaifuName { get; set; }

    public required string Message { get; set; }

    public string? CharacterDescription { get; set; }

    public string? MessageId { get; set; }

    public TaskCompletionSource<string?> TaskCompletionSource { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
