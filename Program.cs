using MARS.AudioController.Services;
using MARS.AudioController.Services.TTS;
using System.Runtime.Versioning;

namespace MARS.AudioController;

internal class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddSingleton<AudioControllerService>();
        builder.Services.AddSingleton<TtsPlaybackStateService>();
        builder.Services.AddSingleton<TtsPlaybackService>();
        if (OperatingSystem.IsWindows())
        {
            RegisterWindowsTtsServices(builder.Services);
        }
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

    [SupportedOSPlatform("windows")]
    private static void RegisterWindowsTtsServices(IServiceCollection services)
    {
        services.AddSingleton<SystemSpeechTtsPlaybackService>();
        services.AddSingleton<SyntheziaQueueManager>();
        services.AddSingleton<ISyntheziaQueueManager>(sp =>
            sp.GetRequiredService<SyntheziaQueueManager>()
        );
        services.AddHostedService(sp => sp.GetRequiredService<SyntheziaQueueManager>());
    }
}
