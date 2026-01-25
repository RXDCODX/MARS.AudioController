using NAudio.CoreAudioApi;

namespace AudioController.Services;

public class MicrophoneVolumeMonitorService(ILogger<MicrophoneVolumeMonitorService> logger)
    : BackgroundService
{
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Microphone Volume Monitor Service started. Checking every 5 minutes."
        );

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                CheckMicrophoneVolume();
                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Service is stopping
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred while checking microphone volume");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); // Wait 1 minute before retrying
            }
        }

        logger.LogInformation("Microphone Volume Monitor Service stopped.");
    }

    private void CheckMicrophoneVolume()
    {
        try
        {
            var deviceEnumerator = new MMDeviceEnumerator();
            var defaultMicrophone = deviceEnumerator.GetDefaultAudioEndpoint(
                DataFlow.Capture,
                Role.Communications
            );

            if (defaultMicrophone == null)
            {
                logger.LogWarning("No default microphone found");
                return;
            }

            var volume = defaultMicrophone.AudioEndpointVolume;
            var currentVolume = volume.MasterVolumeLevelScalar;

            logger.LogInformation("Current microphone volume: {Volume:P}", currentVolume);

            if (currentVolume < 1.0f)
            {
                logger.LogWarning(
                    "Microphone volume is not at maximum! Current: {Volume:P}, Expected: 100%",
                    currentVolume
                );

                volume.MasterVolumeLevelScalar = 1.0f;
                logger.LogInformation("Microphone volume set to maximum");
            }
            else
            {
                logger.LogInformation("Microphone volume is at maximum level ✓");
            }

            // Clean up
            defaultMicrophone.Dispose();
            deviceEnumerator.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to check microphone volume");
        }
    }

    public override void Dispose()
    {
        logger.LogInformation("Disposing Microphone Volume Monitor Service");
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
