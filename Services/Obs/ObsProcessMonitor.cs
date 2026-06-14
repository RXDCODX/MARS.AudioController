using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MARS.AudioController.Services.Obs;

public class ObsProcessMonitor : BackgroundService, IObsProcessMonitor
{
    private static readonly string[] ObsProcessNames = [
        "obs64",
        "obs64debug",
        "obs-browser-page",
    ];

    private readonly IObsService _obsService;
    private readonly ILogger<ObsProcessMonitor> _logger;
    private readonly Func<bool> _processCheck;
    private readonly TimeSpan _pollInterval;

    public bool IsObsProcessRunning => _processCheck();

    public ObsProcessMonitor(IObsService obsService, ILogger<ObsProcessMonitor> logger)
        : this(obsService, logger, null, null) { }

    public ObsProcessMonitor(
        IObsService obsService,
        ILogger<ObsProcessMonitor> logger,
        Func<bool>? processCheck = null,
        TimeSpan? pollInterval = null
    )
    {
        _obsService = obsService;
        _logger = logger;
        _processCheck = processCheck ?? DefaultProcessCheck;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(5);
    }

    private static bool DefaultProcessCheck() =>
        ObsProcessNames.Any(name => Process.GetProcessesByName(name).Length > 0);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OBS process monitor started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processRunning = _processCheck();

                if (processRunning && !_obsService.IsConnected)
                {
                    _logger.LogInformation(
                        "OBS process detected — attempting to connect to WebSocket"
                    );

                    try
                    {
                        await _obsService.ConnectAsync(stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to connect to OBS WebSocket");
                    }
                }
                else if (!processRunning && _obsService.IsConnected)
                {
                    _logger.LogInformation(
                        "No OBS process detected — disconnecting from WebSocket"
                    );

                    _obsService.DisconnectAsync();
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Error in OBS process monitor loop");
            }

            await Task.Delay(_pollInterval, stoppingToken);
        }
    }
}
