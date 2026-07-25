namespace MARS.AudioController.Services.TTS;

public interface IVoiceRecognitionHub
{
    Task PlayTts(TwitchUser user, string message);

    Task UpdateTtsState(TtsState state);

    Task ReassignVoice(string userId);

    Task SubmitAudioForRelay(byte[] pcmAudio, int sampleRate, int channels, string text);
}

public class TtsState
{
    public bool IsStopped { get; set; }

    public double Volume { get; set; }

    public bool RelayToDiscord { get; set; }
}

public class TwitchUser
{
    public required string TwitchId { get; set; }

    public required string UserLogin { get; set; }

    public required string DisplayName { get; set; }

    public string? ProfileImageUrl { get; set; }

    public string? ChatColor { get; set; }

    public bool IsModerator { get; set; }

    public bool IsVip { get; set; }

    public DateTime? FollowedAt { get; set; }

    public DateTime LastUpdated { get; set; }

    public DateTime CreatedAt { get; set; }
}