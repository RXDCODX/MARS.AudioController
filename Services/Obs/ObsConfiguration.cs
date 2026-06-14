namespace MARS.AudioController.Services.Obs;

public class ObsConfiguration
{
    public const string SectionName = "Obs";

    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 4455;

    public string Password { get; set; } = string.Empty;

    public string PauseSceneName { get; set; } = "PauseScreenScene";

    public string PauseImageSourceName { get; set; } = "__pause_image__";

    public int ScreenshotQuality { get; set; } = 100;
}
