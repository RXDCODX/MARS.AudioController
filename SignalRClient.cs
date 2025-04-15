using System;
using MARS.Server.Hubs.Interfaces;
using Microsoft.AspNetCore.SignalR.Client;

namespace AudioController;

public class SignalRClient(SoundBarService soundBarService, IHostEnvironment environment)
    : BackgroundService,
        ISoundBarHub
{
    public Task Mute(params string[] args)
    {
        return soundBarService.MuteAll(args);
    }

    public Task Unmute()
    {
        return soundBarService.UnMuteAll();
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(
                environment.IsDevelopment()
                    ? "http://localhost:9255/soundbar"
                    : "http://localhost:9155/soundbar"
            )
            .WithAutomaticReconnect()
            .ConfigureLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(
                    environment.IsDevelopment() ? LogLevel.Debug : LogLevel.Warning
                );
            })
            .Build();
        connection.On<string[]>(nameof(Mute), Mute);
        connection.On(nameof(Unmute), Unmute);

        return connection.StartAsync(stoppingToken);
    }
}
