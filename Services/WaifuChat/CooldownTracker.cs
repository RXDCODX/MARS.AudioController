using System.Collections.Concurrent;

namespace MARS.AudioController.Services.WaifuChat;

public class CooldownTracker
{
    private readonly int _cooldownSeconds;
    private readonly ConcurrentDictionary<string, DateTime> _cooldowns = new();

    public CooldownTracker(int cooldownSeconds)
    {
        _cooldownSeconds = cooldownSeconds;
    }

    public bool IsOnCooldown(string twitchId)
    {
        if (twitchId == "broadcaster")
        {
            return false;
        }

        if (_cooldowns.TryGetValue(twitchId, out var lastResponse))
        {
            return (DateTime.UtcNow - lastResponse).TotalSeconds < _cooldownSeconds;
        }

        return false;
    }

    public void SetCooldown(string twitchId)
    {
        _cooldowns[twitchId] = DateTime.UtcNow;
    }
}
