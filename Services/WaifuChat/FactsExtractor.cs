namespace MARS.AudioController.Services.WaifuChat;

public static class FactsExtractor
{
    private static readonly HashSet<string> NoFactsResponses =
        new(StringComparer.OrdinalIgnoreCase) { "нет", "нет фактов", "no", "none", "пусто" };

    public static List<string> ParseFacts(string llmResponse)
    {
        if (string.IsNullOrWhiteSpace(llmResponse))
        {
            return [];
        }

        var trimmed = llmResponse.Trim();

        if (NoFactsResponses.Contains(trimmed))
        {
            return [];
        }

        var lines = trimmed.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var facts = new List<string>();
        foreach (var line in lines)
        {
            var cleaned = line.Trim().TrimStart('-', '*', '•').Trim();

            if (!string.IsNullOrWhiteSpace(cleaned) && !NoFactsResponses.Contains(cleaned))
            {
                facts.Add(cleaned);
            }
        }

        return facts;
    }
}
