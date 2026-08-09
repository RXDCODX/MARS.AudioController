namespace MARS.AudioController.Services.WaifuChat;

public class WaifuChatMessage
{
    public required string CorrelationId { get; set; }

    public required string TwitchId { get; set; }

    public required string DisplayName { get; set; }

    public string? WaifuName { get; set; }

    public required string Message { get; set; }
}

public class WaifuChatResponse
{
    public required string CorrelationId { get; set; }

    public required string TwitchId { get; set; }

    public required string Response { get; set; }
}

public class StoreEmbeddingRequest
{
    public required string TwitchId { get; set; }

    public required string Text { get; set; }

    public required string Role { get; set; }

    public required float[] Embedding { get; set; }
}

public class SearchRequest
{
    public required string TwitchId { get; set; }

    public required float[] QueryEmbedding { get; set; }

    public int TopK { get; set; } = 5;
}

public class SearchResult
{
    public required string Text { get; set; }

    public required string Role { get; set; }

    public double Score { get; set; }
}

public class StoreFactRequest
{
    public required string TwitchId { get; set; }

    public required string Fact { get; set; }

    public int Importance { get; set; } = 1;
}

public class ChatMessage
{
    public required string Role { get; set; }

    public required string Content { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
