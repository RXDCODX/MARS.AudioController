using System.Runtime.Versioning;
using MARS.AudioController.Services;
using MARS.AudioController.Services.AudioControllerHub;
using MARS.AudioController.Services.Obs;
using MARS.AudioController.Services.TTS;
using MARS.AudioController.Services.WaifuChat;
using OBSWebsocketDotNet;

namespace MARS.AudioController;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddSingleton<AudioControllerService>();
        builder.Services.AddSingleton<TtsPlaybackStateService>();
        builder.Services.AddSingleton<TtsPlaybackService>();
        builder.Services.AddSingleton<SystemSpeechTtsPlaybackService>();
        builder.Services.AddSingleton<TtsHubConnectionHolder>();
        builder.Services.AddSingleton<ITtsHubConnectionHolder>(sp =>
            sp.GetRequiredService<TtsHubConnectionHolder>()
        );
        builder.Services.AddSingleton<SyntheziaQueueManager>();
        builder.Services.AddSingleton<ISyntheziaQueueManager>(sp =>
            sp.GetRequiredService<SyntheziaQueueManager>()
        );
        builder.Services.AddHostedService(sp => sp.GetRequiredService<SyntheziaQueueManager>());
        builder.Services.AddSingleton<AudioControllerHubClientHostedService>();
        builder.Services.AddHostedService(sp =>
            sp.GetRequiredService<AudioControllerHubClientHostedService>()
        );
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

        // WaifuChat LLM service
        builder.Services.Configure<WaifuChatOptions>(
            builder.Configuration.GetSection(WaifuChatOptions.SectionName));
        builder.Services.AddSingleton<WaifuLlmService>();
        builder.Services.AddSingleton<IWaifuLlmService>(sp =>
            sp.GetRequiredService<WaifuLlmService>());
        builder.Services.AddSingleton<IStreamStateProvider, DefaultStreamStateProvider>();
        builder.Services.AddHostedService<WaifuChatCleanupService>();

        builder.Services.AddControllers();
        builder.Logging.AddConsole();

        var app = builder.Build();

        app.MapControllers();

        app.MapGet("/", () => "Audio Controller REST Server is running!");

        await app.RunAsync();
    }
}
