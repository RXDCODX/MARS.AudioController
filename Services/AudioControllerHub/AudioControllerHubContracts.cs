namespace MARS.AudioController.Services.AudioControllerHub;

/// <summary>
/// Mirror of server's IAudioControllerHub — method names must match exactly.
/// This is the client-side contract for methods the server invokes on us.
/// </summary>
public interface IAudioControllerHubClient
{
    // ── SoundBar ──

    Task MuteProcesses(string correlationId, string[] processNames);

    Task UnmuteProcesses(string correlationId);

    Task GetBagCount(string correlationId);

    // ── OBS ──

    Task ConnectObs(string correlationId);

    Task DisconnectObs(string correlationId);

    Task ScreenshotObs(string correlationId, string? sourceName);

    Task FreezeObs(string correlationId);

    Task UnfreezeObs(string correlationId);

    Task SwitchToPauseScene(string correlationId);

    Task SwitchFromPauseScene(string correlationId);

    Task TogglePauseObs(string correlationId, int mode);

    Task GetObsStatus(string correlationId);

    // ── TTS ──

    Task PlayTts(TTS.TwitchUser user, string message);

    Task UpdateTtsState(TTS.TtsState state);

    Task ReassignVoice(string userId);

    // ── WaifuChat ──

    Task WaifuChatMessage(
        string correlationId, string twitchId, string displayName,
        string? waifuName, string message);

    // ── Health ──

    Task Ping(string correlationId);
}

/// <summary>
/// Methods the AudioController can invoke on the server.
/// </summary>
public interface IAudioControllerHubServer
{
    Task CommandResponse(string correlationId, bool success, string? data, string? error);

    Task RegisterAsAudioController();

    Task SubmitAudioForRelay(byte[] pcmAudio, int sampleRate, int channels, string text);
}

public class ObsPauseResultDto
{
    public bool Success { get; set; }

    public bool IsPaused { get; set; }

    public string? Error { get; set; }

    public string? ScreenshotPath { get; set; }
}

public class ObsStatusDto
{
    public bool IsConnected { get; set; }

    public bool IsPaused { get; set; }
}
