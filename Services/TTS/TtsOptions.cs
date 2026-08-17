namespace MARS.AudioController.Services.TTS;

public class TtsOptions
{
    public const string SectionName = "Tts";

    /// <summary>Master toggle. Если false — весь TTS отключён.</summary>
    public bool Enabled { get; set; } = true;

    public List<string> VoiceStyles { get; set; } = [];

    public WindowsTtsSettings WindowsTts { get; set; } = new();
    public OnnxTtsSettings OnnxTts { get; set; } = new();

    public class WindowsTtsSettings
    {
        public bool Enabled { get; set; } = true;
    }

    public class OnnxTtsSettings
    {
        public bool Enabled { get; set; } = true;
    }
}
