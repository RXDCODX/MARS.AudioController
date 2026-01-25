using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace AudioController.Services;

public class AudioControllerService
{
    private readonly List<AudioSessionControl> _mutedBag = [];
    private readonly SemaphoreSlim semaphoreSlim = new(1);

    public async Task MuteAll(params string[] args)
    {
        await semaphoreSlim.WaitAsync();
        var deviceEnumerator = new MMDeviceEnumerator();
        var defaultDevice = deviceEnumerator.GetDefaultAudioEndpoint(
            DataFlow.Render,
            Role.Multimedia
        );

        // Добавьте эту проверку
        if (defaultDevice.AudioSessionManager == null)
        {
            throw new InvalidOperationException("Cannot access audio session manager");
        }

        for (var index = 0; index < defaultDevice.AudioSessionManager.Sessions.Count; index++)
        {
            var session = defaultDevice.AudioSessionManager.Sessions[index];
            try
            {
                var processName = GetProcessName(session);
                if (args.All(e => !processName.Contains(e, StringComparison.OrdinalIgnoreCase)))
                {
                    session.SimpleAudioVolume.Mute = true;
                    _mutedBag.Add(session);
                }
            }
            catch (Exception)
            {
                // ignored
            }
        }
        semaphoreSlim.Release();
    }

    private static string GetProcessName(AudioSessionControl session)
    {
        try
        {
            return session.GetProcessID == 0
                ? "System"
                : Process.GetProcessById((int)session.GetProcessID).ProcessName;
        }
        catch
        {
            return "Unknown";
        }
    }

    public async Task UnMuteAll()
    {
        await semaphoreSlim.WaitAsync();
        {
            foreach (var control in _mutedBag.ToList())
            {
                control.SimpleAudioVolume.Mute = false;
                _mutedBag.Remove(control);
            }
        }

        semaphoreSlim.Release();
    }

    public string GetBagCount()
    {
        return string.Join(
            Environment.NewLine,
            _mutedBag.ToArray().Select(e => $"({e.GetProcessID}) {e.DisplayName}")
        );
    }
}
