using AudioController;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<AudioControllerService>();
builder.Services.AddControllers();
builder.Logging.AddConsole();

var app = builder.Build();

app.MapControllers();

app.MapGet("/", () => "Audio Controller REST Server is running!");

app.Run();
