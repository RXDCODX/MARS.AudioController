using System.Globalization;
using System.Runtime.Versioning;
using System.Speech.Synthesis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MARS.AudioController.Services.TTS;

[SupportedOSPlatform("windows")]
public class SystemSpeechTtsPlaybackService(ILogger<SystemSpeechTtsPlaybackService> logger)
{
    private readonly SemaphoreSlim _playbackLock = new(1, 1);
    private const float MaxVolume = 6.0f;

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
                var wavBytes = await GenerateSpeechWavAsync(text, voiceName, cancellationToken);

                if (wavBytes.Length > 0)
                {
                    await PlayWavAsync(wavBytes, volume, cancellationToken);
                    result = true;
                }
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
            finally
            {
                _playbackLock.Release();
            }
        }

        return result;
    }

    public async Task<byte[]> GenerateSpeechWavAsync(
        string text,
        string? voiceName,
        CancellationToken cancellationToken = default
    )
    {
        var result = Array.Empty<byte>();

        if (!OperatingSystem.IsWindows())
        {
            logger.LogWarning("System Speech synthesis is only available on Windows.");
        }
        else if (string.IsNullOrWhiteSpace(text))
        {
            logger.LogWarning("System Speech synthesis was skipped because text is empty.");
        }
        else
        {
            using var speech = new SpeechSynthesizer();
            using var waveStream = new MemoryStream();
            var completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            speech.SetOutputToWaveStream(waveStream);
            speech.Volume = 100;

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
                    logger.LogWarning(ex, "Failed to cancel System Speech synthesis.");
                }

                completion.TrySetCanceled(cancellationToken);
            });

            await completion.Task;
            result = waveStream.ToArray();
        }

        return result;
    }

    private static async Task PlayWavAsync(
        byte[] wavBytes,
        double volume,
        CancellationToken cancellationToken
    )
    {
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        using var memoryStream = new MemoryStream(wavBytes, writable: false);
        using var reader = new WaveFileReader(memoryStream);
        var sampleProvider = reader.ToSampleProvider();
        var normalizedVolume = (float)Math.Clamp(volume, 0.0, 2.0) / 2.0f;
        var gain = normalizedVolume * MaxVolume;
        var volumeProvider = new VolumeSampleProvider(sampleProvider) { Volume = gain };
        using var waveOut = new WaveOutEvent();

        EventHandler<StoppedEventArgs>? playbackStoppedHandler = null;
        playbackStoppedHandler = (_, _) =>
        {
            waveOut.PlaybackStopped -= playbackStoppedHandler;
            completion.TrySetResult(true);
        };

        waveOut.PlaybackStopped += playbackStoppedHandler;
        waveOut.Init(volumeProvider.ToWaveProvider16());
        waveOut.Play();

        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            try
            {
                waveOut.Stop();
            }
            catch
            {
                // Ignore shutdown errors when the request is cancelled.
            }

            completion.TrySetCanceled(cancellationToken);
        });

        await completion.Task;
    }
}
