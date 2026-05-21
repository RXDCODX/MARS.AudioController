using System.Collections.Concurrent;
using MARS.AudioController.Models;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MARS.AudioController.Services.TTS;

public class TtsPlaybackService(
    IWebHostEnvironment environment,
    ILogger<TtsPlaybackService> logger,
    TtsPlaybackStateService playbackState
)
{
    private readonly ConcurrentDictionary<string, Lazy<TextToSpeech>> _textToSpeechCache = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly ConcurrentDictionary<string, Lazy<Style>> _styleCache = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly SemaphoreSlim _playbackLock = new(1, 1);

    public async Task<TtsPlaybackResponse> PlayAsync(
        TtsPlaybackRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var result = new TtsPlaybackResponse
        {
            Success = false,
            Message = "TTS playback failed",
        };

        if (request == null)
        {
            result.Message = "Request body is required";
        }
        else if (string.IsNullOrWhiteSpace(request.Text))
        {
            result.Message = "Text is required";
        }
        else if (string.IsNullOrWhiteSpace(request.Language))
        {
            result.Message = "Language is required";
        }
        else if (request.TotalStep <= 0)
        {
            result.Message = "TotalStep must be greater than zero";
        }
        else if (request.Speed <= 0)
        {
            result.Message = "Speed must be greater than zero";
        }
        else if (request.SilenceDuration < 0)
        {
            result.Message = "SilenceDuration cannot be negative";
        }
        else
        {
            await _playbackLock.WaitAsync(cancellationToken);
            try
            {
                var resolvedOnnxDir = ResolvePath(request.OnnxDir, "assets/onnx");
                var resolvedVoiceStylePath = ResolvePath(
                    request.VoiceStylePath,
                    "assets/voice_styles/M1.json"
                );

                if (!Directory.Exists(resolvedOnnxDir))
                {
                    result.Message = $"ONNX directory was not found: {resolvedOnnxDir}";
                }
                else if (!File.Exists(resolvedVoiceStylePath))
                {
                    result.Message = $"Voice style file was not found: {resolvedVoiceStylePath}";
                }
                else
                {
                    var textToSpeech = GetTextToSpeech(resolvedOnnxDir, request.UseGpu);
                    var style = GetVoiceStyle(resolvedVoiceStylePath);

                    var (wav, duration) = textToSpeech.Call(
                        request.Text,
                        request.Language,
                        style,
                        request.TotalStep,
                        request.Speed,
                        request.SilenceDuration
                    );

                    using var playbackCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken,
                        playbackState.PlaybackCancellationToken
                    );

                    await PlayAudioAsync(
                        wav,
                        textToSpeech.SampleRate,
                        request.Volume,
                        playbackCancellation.Token
                    );

                    result = new TtsPlaybackResponse
                    {
                        Success = true,
                        Message = "TTS playback completed",
                        SampleRate = textToSpeech.SampleRate,
                        Duration = duration.Length > 0 ? TimeSpan.FromSeconds(duration[0]) : TimeSpan.Zero,
                        Text = request.Text,
                        Language = request.Language,
                        OnnxDir = resolvedOnnxDir,
                        VoiceStylePath = resolvedVoiceStylePath,
                    };
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to synthesize or play TTS audio");
                result.Message = ex.Message;
            }
            finally
            {
                _playbackLock.Release();
            }
        }

        return result;
    }

    private TextToSpeech GetTextToSpeech(string onnxDir, bool useGpu)
    {
        var cacheKey = $"{onnxDir}|{useGpu}";
        var lazy = _textToSpeechCache.GetOrAdd(
            cacheKey,
            _ =>
                new Lazy<TextToSpeech>(
                    () => Helper.LoadTextToSpeech(onnxDir, useGpu),
                    LazyThreadSafetyMode.ExecutionAndPublication
                )
        );

        return lazy.Value;
    }

    private Style GetVoiceStyle(string voiceStylePath)
    {
        var lazy = _styleCache.GetOrAdd(
            voiceStylePath,
            _ =>
                new Lazy<Style>(
                    () => Helper.LoadVoiceStyle([voiceStylePath], verbose: false),
                    LazyThreadSafetyMode.ExecutionAndPublication
                )
        );

        return lazy.Value;
    }

    private static async Task PlayAudioAsync(
        float[] audioData,
        int sampleRate,
        double volume,
        CancellationToken cancellationToken
    )
    {
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        // Convert raw float samples to 16-bit PCM (no volume adjustment here)
        var pcmBytes = ConvertToPcm16(audioData);
        using var memoryStream = new MemoryStream(pcmBytes, writable: false);
        using var sourceStream = new RawSourceWaveStream(
            memoryStream,
            new WaveFormat(sampleRate, 16, 1)
        );

        // Use NAudio sample providers so we can apply a high-quality volume gain.
        var sampleProvider = sourceStream.ToSampleProvider();
        var clampedVolume = (float)Math.Clamp(volume, 0.0, 2.0);
        var volumeProvider = new VolumeSampleProvider(sampleProvider)
        {
            Volume = clampedVolume,
        };

        using var waveOut = new WaveOutEvent();

        EventHandler<StoppedEventArgs>? playbackStoppedHandler = null;
        playbackStoppedHandler = (_, _) =>
        {
            waveOut.PlaybackStopped -= playbackStoppedHandler;
            completion.TrySetResult(true);
        };

        waveOut.PlaybackStopped += playbackStoppedHandler;
        // Initialize playback with the volume-controlled sample provider.
        waveOut.Init(volumeProvider);
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

    private static byte[] ConvertToPcm16(float[] audioData)
    {
        var pcmBytes = new byte[audioData.Length * sizeof(short)];

        for (var i = 0; i < audioData.Length; i++)
        {
            var sample = Math.Clamp(audioData[i], -1.0f, 1.0f);
            var intSample = (short)(sample * short.MaxValue);
            pcmBytes[i * 2] = (byte)(intSample & 0xFF);
            pcmBytes[i * 2 + 1] = (byte)((intSample >> 8) & 0xFF);
        }

        return pcmBytes;
    }

    private string ResolvePath(string path, string defaultPath)
    {
        var result = string.IsNullOrWhiteSpace(path) ? defaultPath : path;

        if (!Path.IsPathRooted(result))
        {
            result = Path.GetFullPath(Path.Combine(environment.ContentRootPath, result));
        }

        return result;
    }
}