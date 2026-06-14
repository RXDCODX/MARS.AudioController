using System.Runtime.Versioning;
using MARS.AudioController.Services;
using MARS.AudioController.Services.Obs;
using MARS.AudioController.Services.TTS;
using OBSWebsocketDotNet;

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

        // OBS Websocket service
        builder.Services.AddSingleton<IOBSWebsocket>(sp => new OBSWebsocket());
        builder.Services.AddSingleton<IObsService, ObsService>();
        builder.Services.AddSingleton<ObsProcessMonitor>();
        builder.Services.AddSingleton<IObsProcessMonitor>(sp =>
            sp.GetRequiredService<ObsProcessMonitor>()
        );
        builder.Services.AddHostedService(sp => sp.GetRequiredService<ObsProcessMonitor>());

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
