namespace MARS.AudioController.Models;

public class TtsPlaybackResponse
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    public int SampleRate { get; set; }

    public TimeSpan Duration { get; set; }

    public string Text { get; set; } = string.Empty;

    public string Language { get; set; } = string.Empty;

    public string OnnxDir { get; set; } = string.Empty;

    public string VoiceStylePath { get; set; } = string.Empty;
}