namespace AudioController.Models;

/// <summary>
/// Request to play audio from URL
/// </summary>
public class AudioPlaybackRequest
{
    /// <summary>
    /// URL to MP3 file
    /// </summary>
    public string AudioUrl { get; set; } = string.Empty;

    /// <summary>
    /// Optional display name/label for the audio
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Priority level (0-100, higher = earlier in queue)
    /// </summary>
    public int Priority { get; set; } = 50;
}

/// <summary>
/// Response from audio playback endpoint
/// </summary>
public class AudioPlaybackResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int QueuePosition { get; set; }
    public int TotalInQueue { get; set; }
}

/// <summary>
/// Status of current playback
/// </summary>
public class AudioPlaybackStatus
{
    public bool IsPlaying { get; set; }
    public string? CurrentAudio { get; set; }
    public int QueueLength { get; set; }
    public double PlaybackProgress { get; set; } // 0-1
    public TimeSpan CurrentPosition { get; set; }
    public TimeSpan TotalDuration { get; set; }
}
