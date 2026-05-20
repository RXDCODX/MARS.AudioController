using MARS.AudioController.Services;
using MARS.AudioController.Services.TTS;

namespace MARS.AudioController;

internal class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddSingleton<AudioControllerService>();
        builder.Services.AddSingleton<TtsPlaybackStateService>();
        builder.Services.AddSingleton<TtsPlaybackService>();
        builder.Services.AddSingleton<SyntheziaQueueManager>();
        builder.Services.AddSingleton<ISyntheziaQueueManager>(sp => sp.GetRequiredService<SyntheziaQueueManager>());
        builder.Services.AddHostedService(sp => sp.GetRequiredService<SyntheziaQueueManager>());
        builder.Services.AddHostedService<TtsHubClientHostedService>();
        builder.Services.AddHostedService<MicrophoneVolumeMonitorService>();

        // Register audio playback queue service
        builder.Services.AddHttpClient<IAudioPlaybackQueueService, AudioPlaybackQueueService>();
        builder.Services.AddControllers();
        builder.Logging.AddConsole();

        var app = builder.Build();

        app.MapControllers();

        app.MapGet("/", () => "Audio Controller REST Server is running!");

        app.Run();
    }
}
