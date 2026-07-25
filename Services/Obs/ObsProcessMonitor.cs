using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MARS.AudioController.Services.Obs;

public class ObsProcessMonitor(
    IObsService obsService,
    ILogger<ObsProcessMonitor> logger,
    Func<bool>? processCheck = null,
    TimeSpan? pollInterval = null
) : BackgroundService, IObsProcessMonitor
{
    private static readonly string[] ObsProcessNames = ["obs64", "obs64debug", "obs-browser-page"];

    private readonly Func<bool> _processCheck = processCheck ?? DefaultProcessCheck;
    private readonly TimeSpan _pollInterval = pollInterval ?? TimeSpan.FromSeconds(5);

    public bool IsObsProcessRunning => _processCheck();

    public ObsProcessMonitor(IObsService obsService, ILogger<ObsProcessMonitor> logger)
        : this(obsService, logger, null, null) { }

    private static bool DefaultProcessCheck() =>
        ObsProcessNames.Any(name => Process.GetProcessesByName(name).Length > 0);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Factory.StartNew(
            async () =>
            {
                logger.LogInformation("OBS process monitor started");

                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        var processRunning = _processCheck();

                        if (processRunning && !obsService.IsConnected)
                        {
                            logger.LogInformation(
                                "OBS process detected — attempting to connect to WebSocket"
                            );

                            try
                            {
                                await obsService.ConnectAsync(stoppingToken);
                            }
                            catch (Exception ex)
                            {
                                logger.LogWarning(ex, "Failed to connect to OBS WebSocket");
                            }
                        }
                        else if (!processRunning && obsService.IsConnected)
                        {
                            logger.LogInformation(
                                "No OBS process detected — disconnecting from WebSocket"
                            );

                            obsService.DisconnectAsync();
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogWarning(ex, "Error in OBS process monitor loop");
                    }

                    await Task.Delay(_pollInterval, stoppingToken);
                }
            },
            stoppingToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default
        );
    }
}
