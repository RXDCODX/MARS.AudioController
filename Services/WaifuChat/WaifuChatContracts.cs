namespace MARS.AudioController.Services.WaifuChat;

public class StoreFactRequest
{
    public required string TwitchId { get; set; }

    public required string Fact { get; set; }

    public int Importance { get; set; } = 1;
}
