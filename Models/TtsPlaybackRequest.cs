namespace MARS.AudioController.Models;

public class TtsPlaybackRequest
{
    public string Text { get; set; } = string.Empty;

    public string Language { get; set; } = "na";

    public string OnnxDir { get; set; } = "assets/onnx";

    public string VoiceStylePath { get; set; } = "assets/voice_styles/M1.json";

    public double Volume { get; set; } = 1.0;

    public bool UseGpu { get; set; } = false;

    public int TotalStep { get; set; } = 8;

    public float Speed { get; set; } = 1.05f;

    public float SilenceDuration { get; set; } = 0.3f;
}
