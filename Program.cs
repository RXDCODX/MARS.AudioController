namespace AudioController;

internal class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddSingleton<AudioControllerService>();
        builder.Services.AddHostedService<MicrophoneVolumeMonitorService>();
        builder.Services.AddControllers();
        builder.Logging.AddConsole();

        var app = builder.Build();

        app.MapControllers();

        app.MapGet("/", () => "Audio Controller REST Server is running!");

        app.Run();
    }
}
