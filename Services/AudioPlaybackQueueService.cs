using AudioController.Models;
using NAudio.Wave;

namespace AudioController.Services;

/// <summary>
/// Audio playback queue item
/// </summary>
public class AudioQueueItem
{
    public string AudioUrl { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public int Priority { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Service for managing audio playback queue and playback
/// </summary>
public interface IAudioPlaybackQueueService
{
    /// <summary>
    /// Adds audio to playback queue
    /// </summary>
    Task<AudioPlaybackResponse> QueueAudioAsync(AudioPlaybackRequest request);

    /// <summary>
    /// Gets current playback status
    /// </summary>
    AudioPlaybackStatus GetStatus();

    /// <summary>
    /// Skips current audio and plays next
    /// </summary>
    Task SkipCurrentAsync();

    /// <summary>
    /// Stops playback and clears queue
    /// </summary>
    Task StopAndClearAsync();

    /// <summary>
    /// Pauses current playback
    /// </summary>
    void Pause();

    /// <summary>
    /// Resumes playback
    /// </summary>
    void Resume();

    /// <summary>
    /// Gets queue items
    /// </summary>
    List<AudioQueueItem> GetQueueItems();

    /// <summary>
    /// Gets current volume (0-100)
    /// </summary>
    int GetVolume();

    /// <summary>
    /// Sets volume (0-100)
    /// </summary>
    void SetVolume(int volume);
}

public class AudioPlaybackQueueService : IAudioPlaybackQueueService, IAsyncDisposable
{
    private readonly ILogger<AudioPlaybackQueueService> _logger;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _queueLock = new(1);
    private readonly PriorityQueue<AudioQueueItem, int> _queue = new();

    private IWavePlayer? _wavePlayer;
    private AudioFileReader? _audioFileReader;
    private string? _currentAudioPath;
    private CancellationTokenSource? _playCancellationTokenSource;

    private bool _isPaused;
    private bool _isDisposed;
    private int _volume = 100; // Default volume 100%

    public AudioPlaybackQueueService(
        ILogger<AudioPlaybackQueueService> logger,
        HttpClient httpClient
    )
    {
        _logger = logger;
        _httpClient = httpClient;
        InitializeAudioPlayer();
    }

    public async Task<AudioPlaybackResponse> QueueAudioAsync(AudioPlaybackRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.AudioUrl))
        {
            return new AudioPlaybackResponse { Success = false, Message = "Audio URL is required" };
        }

        try
        {
            await _queueLock.WaitAsync();

            var queueItem = new AudioQueueItem
            {
                AudioUrl = request.AudioUrl,
                DisplayName = request.DisplayName,
                Priority = request.Priority,
            };

            _queue.Enqueue(queueItem, -request.Priority); // Negative priority for max-heap behavior

            var queuePosition = _queue.Count;
            var isCurrentlyPlaying = _wavePlayer?.PlaybackState == PlaybackState.Playing;

            _logger.LogInformation(
                $"Audio queued: {request.DisplayName ?? request.AudioUrl} at position {queuePosition}"
            );

            // Start playback if nothing is playing
            if (!isCurrentlyPlaying)
            {
                _ = ProcessQueueAsync();
            }

            return new AudioPlaybackResponse
            {
                Success = true,
                Message = $"Audio queued successfully",
                QueuePosition = queuePosition,
                TotalInQueue = _queue.Count,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error queuing audio");
            return new AudioPlaybackResponse
            {
                Success = false,
                Message = $"Error queuing audio: {ex.Message}",
            };
        }
        finally
        {
            _queueLock.Release();
        }
    }

    public AudioPlaybackStatus GetStatus()
    {
        var status = new AudioPlaybackStatus
        {
            IsPlaying = _wavePlayer?.PlaybackState == PlaybackState.Playing,
            CurrentAudio = Path.GetFileName(_currentAudioPath),
            QueueLength = _queue.Count,
        };

        if (_audioFileReader != null && _wavePlayer != null)
        {
            status.CurrentPosition = _audioFileReader.CurrentTime;
            status.TotalDuration = _audioFileReader.TotalTime;
            status.PlaybackProgress =
                _audioFileReader.TotalTime.TotalMilliseconds > 0
                    ? _audioFileReader.CurrentTime.TotalMilliseconds
                        / _audioFileReader.TotalTime.TotalMilliseconds
                    : 0;
        }

        return status;
    }

    public async Task SkipCurrentAsync()
    {
        try
        {
            await _queueLock.WaitAsync();
            StopPlayback();
            _playCancellationTokenSource?.Cancel();
            _playCancellationTokenSource = new CancellationTokenSource();
        }
        finally
        {
            _queueLock.Release();
        }

        await ProcessQueueAsync();
    }

    public async Task StopAndClearAsync()
    {
        try
        {
            await _queueLock.WaitAsync();
            StopPlayback();
            _playCancellationTokenSource?.Cancel();
            _playCancellationTokenSource = new CancellationTokenSource();

            // Clear queue
            while (_queue.Count > 0)
            {
                _queue.Dequeue();
            }

            _logger.LogInformation("Playback stopped and queue cleared");
        }
        finally
        {
            _queueLock.Release();
        }
    }

    public void Pause()
    {
        if (_wavePlayer?.PlaybackState == PlaybackState.Playing)
        {
            _wavePlayer.Pause();
            _isPaused = true;
            _logger.LogInformation("Playback paused");
        }
    }

    public void Resume()
    {
        if (_wavePlayer?.PlaybackState == PlaybackState.Paused)
        {
            _wavePlayer.Play();
            _isPaused = false;
            _logger.LogInformation("Playback resumed");
        }
    }

    public List<AudioQueueItem> GetQueueItems()
    {
        var items = new List<AudioQueueItem>();
        var tempQueue = new PriorityQueue<AudioQueueItem, int>();

        // Extract items from queue
        while (_queue.Count > 0)
        {
            var item = _queue.Dequeue();
            items.Add(item);
            // Store in temp for restoration
            var priority = -item.Priority; // Inverse to get original
            tempQueue.Enqueue(item, priority);
        }

        // Restore queue
        while (tempQueue.Count > 0)
        {
            var item = tempQueue.Dequeue();
            _queue.Enqueue(item, -item.Priority);
        }

        return items;
    }

    public int GetVolume()
    {
        return _volume;
    }

    public void SetVolume(int volume)
    {
        // Clamp volume between 0 and 100
        _volume = Math.Max(0, Math.Min(100, volume));

        // Apply volume to current playback if active
        if (_wavePlayer != null && _audioFileReader != null)
        {
            _audioFileReader.Volume = _volume / 100f; // Convert to 0-1 range
            _logger.LogInformation($"Volume set to {_volume}%");
        }
        else
        {
            _logger.LogInformation($"Volume set to {_volume}% (will apply to next playback)");
        }
    }

    private void InitializeAudioPlayer()
    {
        try
        {
            _wavePlayer = new WaveOutEvent();
            _wavePlayer.PlaybackStopped += OnPlaybackStopped;
            _logger.LogInformation("Audio player initialized");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize audio player");
        }
    }

    private async Task ProcessQueueAsync()
    {
        await _queueLock.WaitAsync();
        try
        {
            while (_queue.Count > 0 && !_isDisposed)
            {
                var queueItem = _queue.Dequeue();
                _logger.LogInformation(
                    $"Starting playback: {queueItem.DisplayName ?? queueItem.AudioUrl}"
                );

                await PlayAudioAsync(queueItem);

                if (_playCancellationTokenSource?.Token.IsCancellationRequested == true)
                {
                    _logger.LogInformation("Playback cancelled");
                    break;
                }
            }

            _logger.LogInformation("Queue processing completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing queue");
        }
        finally
        {
            _queueLock.Release();
        }
    }

    private async Task PlayAudioAsync(AudioQueueItem queueItem)
    {
        try
        {
            // Download audio file
            var audioPath = await DownloadAudioFileAsync(queueItem.AudioUrl);
            _currentAudioPath = audioPath;

            // Create audio file reader
            _audioFileReader = new AudioFileReader(audioPath);

            if (_wavePlayer == null)
            {
                InitializeAudioPlayer();
            }

            _wavePlayer!.Init(_audioFileReader);

            // Wait for playback to complete
            _playCancellationTokenSource = new CancellationTokenSource();
            var playbackCompletedSource = new TaskCompletionSource<bool>();

            EventHandler<StoppedEventArgs>? handler = null;
            handler = (sender, e) =>
            {
                if (handler != null)
                {
                    _wavePlayer.PlaybackStopped -= handler;
                }
                playbackCompletedSource.TrySetResult(true);
            };

            _wavePlayer.PlaybackStopped += handler;
            _wavePlayer.Play();

            // Wait for playback to complete or cancellation
            await Task.WhenAny(
                playbackCompletedSource.Task,
                Task.Delay(Timeout.Infinite, _playCancellationTokenSource.Token)
            );

            StopPlayback();

            // Clean up temp file
            try
            {
                if (File.Exists(audioPath))
                {
                    File.Delete(audioPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete temporary audio file");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error playing audio from URL: {AudioUrl}", queueItem.AudioUrl);
        }
    }

    private async Task<string> DownloadAudioFileAsync(string audioUrl)
    {
        try
        {
            _logger.LogInformation("Downloading audio from: {AudioUrl}", audioUrl);

            var response = await _httpClient.GetAsync(audioUrl);
            response.EnsureSuccessStatusCode();

            var tempPath = Path.Combine(Path.GetTempPath(), $"audio_{Guid.NewGuid()}.mp3");
            await using (var contentStream = await response.Content.ReadAsStreamAsync())
            await using (var fileStream = File.Create(tempPath))
            {
                await contentStream.CopyToAsync(fileStream);
            }

            _logger.LogInformation("Audio downloaded to: {TempPath}", tempPath);
            return tempPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download audio from: {AudioUrl}", audioUrl);
            throw;
        }
    }

    private void StopPlayback()
    {
        try
        {
            if (_wavePlayer != null)
            {
                _wavePlayer.Stop();
            }

            _audioFileReader?.Dispose();
            _audioFileReader = null;
            _currentAudioPath = null;
            _isPaused = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping playback");
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            _logger.LogError(e.Exception, "Playback error occurred");
        }
        else
        {
            _logger.LogInformation("Playback stopped naturally");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        await StopAndClearAsync();
        _playCancellationTokenSource?.Dispose();
        _audioFileReader?.Dispose();
        _wavePlayer?.Dispose();
        _queueLock.Dispose();

        _logger.LogInformation("AudioPlaybackQueueService disposed");
    }
}
