using System.Collections.Concurrent;

namespace MARS.AudioController.Services.WaifuChat;

public class MessageProcessingQueue
{
    private readonly ConcurrentQueue<ChatRequest> _queue = new();
    private readonly ConcurrentDictionary<string, int> _sessionCounts = new();

    public int PendingCount => _queue.Count;

    public int ActiveSessionCount => _sessionCounts.Count;

    public ChatRequest Enqueue(ChatRequest request)
    {
        _queue.Enqueue(request);
        _sessionCounts.AddOrUpdate(request.TwitchId, 1, (_, count) => count + 1);
        return request;
    }

    public bool TryDequeue(out ChatRequest? request)
    {
        if (_queue.TryDequeue(out request!))
        {
            return true;
        }

        request = null;
        return false;
    }

    public void CompleteMessage(ChatRequest request, string? response)
    {
        request.TaskCompletionSource.TrySetResult(response);

        DecrementSession(request.TwitchId);
    }

    public void CompleteWithError(ChatRequest request, string error)
    {
        request.TaskCompletionSource.TrySetException(new Exception(error));

        DecrementSession(request.TwitchId);
    }

    public bool IsProcessingOrQueued(string twitchId)
    {
        return _sessionCounts.ContainsKey(twitchId);
    }

    private void DecrementSession(string twitchId)
    {
        if (_sessionCounts.TryGetValue(twitchId, out var currentCount))
        {
            if (currentCount <= 1)
            {
                _sessionCounts.TryRemove(twitchId, out _);
            }
            else
            {
                _sessionCounts[twitchId] = currentCount - 1;
            }
        }
    }
}
