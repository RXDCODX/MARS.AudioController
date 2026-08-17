using MARS.Shared.Models;

namespace MARS.AudioController.Services.TTS;

public class NoOpSyntheziaQueueManager : ISyntheziaQueueManager
{
    public Task EnqueueAsync(TwitchUser user, string message) => Task.CompletedTask;
    public Task ApplyStateAsync(TtsState state) => Task.CompletedTask;
    public Task ReassignUserVoiceAsync(string userId) => Task.CompletedTask;
}
