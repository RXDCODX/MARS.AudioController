using System.Globalization;
using System.Runtime.Versioning;
using System.Speech.Synthesis;

namespace MARS.AudioController.Services.TTS;

[SupportedOSPlatform("windows")]
public class SystemSpeechTtsPlaybackService(ILogger<SystemSpeechTtsPlaybackService> logger)
{
    private readonly SemaphoreSlim _playbackLock = new(1, 1);

    public List<string> GetInstalledVoices()
    {
        var result = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            using var speech = new SpeechSynthesizer();
            result = speech
                .GetInstalledVoices(new CultureInfo("ru-RU"))
                .Select(voice => voice.VoiceInfo.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();
        }

        return result;
    }

    public async Task<bool> PlayAsync(
        string text,
        string? voiceName,
        double volume,
        CancellationToken cancellationToken = default
    )
    {
        var result = false;

        if (!OperatingSystem.IsWindows())
        {
            logger.LogWarning("System Speech playback is only available on Windows.");
        }
        else if (string.IsNullOrWhiteSpace(text))
        {
            logger.LogWarning("System Speech playback was skipped because text is empty.");
        }
        else
        {
            await _playbackLock.WaitAsync(cancellationToken);
            try
            {
                using var speech = new SpeechSynthesizer();
                var completion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );

                speech.SetOutputToDefaultAudioDevice();
                speech.Volume = Math.Clamp((int)Math.Round(volume * 100.0), 0, 100);

                if (!string.IsNullOrWhiteSpace(voiceName))
                {
                    var installedVoice = speech
                        .GetInstalledVoices(new CultureInfo("ru-RU"))
                        .FirstOrDefault(v =>
                            string.Equals(
                                v.VoiceInfo.Name,
                                voiceName,
                                StringComparison.OrdinalIgnoreCase
                            )
                        );

                    if (installedVoice is not null)
                    {
                        speech.SelectVoice(installedVoice.VoiceInfo.Name);
                    }
                }

                EventHandler<SpeakCompletedEventArgs>? speakCompletedHandler = null;
                speakCompletedHandler = (_, eventArgs) =>
                {
                    speech.SpeakCompleted -= speakCompletedHandler;

                    if (eventArgs.Error is not null)
                    {
                        completion.TrySetException(eventArgs.Error);
                    }
                    else if (eventArgs.Cancelled)
                    {
                        completion.TrySetCanceled();
                    }
                    else
                    {
                        completion.TrySetResult(true);
                    }
                };

                speech.SpeakCompleted += speakCompletedHandler;
                speech.SpeakAsync(text);

                using var cancellationRegistration = cancellationToken.Register(() =>
                {
                    try
                    {
                        speech.SpeakAsyncCancelAll();
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to cancel System Speech playback.");
                    }

                    completion.TrySetCanceled(cancellationToken);
                });

                try
                {
                    await completion.Task;
                    result = true;
                }
                catch (OperationCanceledException)
                {
                    result = false;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "System Speech playback failed.");
                    result = false;
                }
            }
            finally
            {
                _playbackLock.Release();
            }
        }

        return result;
    }
}