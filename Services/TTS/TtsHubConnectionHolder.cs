using Microsoft.AspNetCore.SignalR.Client;

namespace MARS.AudioController.Services.TTS;

public interface ITtsHubConnectionHolder
{
    HubConnection? Connection { get; set; }
}

public class TtsHubConnectionHolder : ITtsHubConnectionHolder
{
    public HubConnection? Connection { get; set; }
}
