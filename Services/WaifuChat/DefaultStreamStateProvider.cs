namespace MARS.AudioController.Services.WaifuChat;

public class DefaultStreamStateProvider : IStreamStateProvider
{
    // TODO: Интеграция с реальным StreamState из MARS.Server через SignalR
    public bool IsOnline { get; set; } = true;
}
