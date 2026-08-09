using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MARS.AudioController.Services.WaifuChat;

public interface IStreamStateProvider
{
    bool IsOnline { get; }
}

public class WaifuChatCleanupService : BackgroundService
{
    private readonly IWaifuLlmService _llmService;
    private readonly IStreamStateProvider _streamState;
    private readonly ILogger<WaifuChatCleanupService> _logger;
    private readonly TimeSpan _checkInterval;

    public WaifuChatCleanupService(
        IWaifuLlmService llmService,
        IStreamStateProvider streamState,
        ILogger<WaifuChatCleanupService> logger,
        TimeSpan? checkInterval = null)
    {
        _llmService = llmService;
        _streamState = streamState;
        _logger = logger;
        _checkInterval = checkInterval ?? TimeSpan.FromMinutes(10);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_checkInterval, stoppingToken);

                if (!_streamState.IsOnline)
                {
                    _logger.LogInformation("Stream offline — extracting facts and cleaning up sessions");

                    await _llmService.ExtractAndSaveAllFactsAsync(stoppingToken);
                    _llmService.DisposeAllSessions();

                    _logger.LogInformation("Cleanup complete");
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in WaifuChatCleanupService");
            }
        }
    }
}
