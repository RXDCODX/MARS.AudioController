namespace AudioController;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddSingleton<SoundBarService>();
        builder.Services.AddSingleton<SignalRClient>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<SignalRClient>());
        builder.Services.AddSignalR();
        builder.Logging.AddConsole();

        var app = builder.Build();

        app.Map(
            "/*",
            (HttpContext context) =>
            {
                context.Response.StatusCode = 200;
            }
        );

        app.Run();
    }
}
