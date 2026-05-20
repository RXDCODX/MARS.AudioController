using MARS.Server.Hubs.Models.VoiceRecognition;

namespace MARS.AudioController.Services.TTS;

public class TtsPlaybackStateService
{
    private readonly object _gate = new();
    private CancellationTokenSource _stopCancellation = new();

    public bool IsStopped { get; private set; }

    public double Volume { get; private set; } = 1.0;

    public CancellationToken PlaybackCancellationToken
    {
        get
        {
            lock (_gate)
            {
                return _stopCancellation.Token;
            }
        }
    }

    public void ApplyState(TtsState state)
    {
        if (state is null)
        {
            return;
        }

        lock (_gate)
        {
            Volume = Math.Clamp(state.Volume, 0.0, 1.0);

            if (state.IsStopped)
            {
                IsStopped = true;
                _stopCancellation.Cancel();
            }
            else if (IsStopped)
            {
                IsStopped = false;
                _stopCancellation.Dispose();
                _stopCancellation = new CancellationTokenSource();
            }
        }
    }

    public void ResetStop()
    {
        lock (_gate)
        {
            if (IsStopped)
            {
                IsStopped = false;
                _stopCancellation.Dispose();
                _stopCancellation = new CancellationTokenSource();
            }
        }
    }
}
