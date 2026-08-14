using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MARS.AudioController.Services.WaifuChat;

/// <summary>
/// Доступные LLM модели для генерации ответов, от худшей к лучшей.
/// Поддерживает как enum значения (Gemma4_E2b), так и строковые ID (gemma4:e2b).
/// </summary>
[JsonConverter(typeof(ChatModelJsonConverter))]
public enum ChatModel
{
    Gemma3_270m,
    Gemma3_1b,
    Llama32_1b,
    Gemma4_E2b,
    Llama32_3b,
    Falcon3_3b,
    LmkitTasks_4b,
    Gemma4_E4b,
    Llama31_8b,
    Falcon3_7b,
    Granite4_7b,
    DeepseekR1_8b,
    Gemma4_12b,
    Glm47_Flash,
}

public class ChatModelJsonConverter : JsonConverter<ChatModel>
{
    public override ChatModel Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        var value = reader.GetString();
        return ChatModelExtensions.FromModelId(value ?? "gemma4:e2b");
    }

    public override void Write(
        Utf8JsonWriter writer,
        ChatModel value,
        JsonSerializerOptions options
    )
    {
        writer.WriteStringValue(value.ToModelId());
    }
}

public static class ChatModelExtensions
{
    public static string ToModelId(this ChatModel model) =>
        model switch
        {
            ChatModel.Gemma3_270m => "gemma3:270m",
            ChatModel.Gemma3_1b => "gemma3:1b",
            ChatModel.Llama32_1b => "llama3.2:1b",
            ChatModel.Gemma4_E2b => "gemma4:e2b",
            ChatModel.Llama32_3b => "llama3.2:3b",
            ChatModel.Falcon3_3b => "falcon3:3b",
            ChatModel.LmkitTasks_4b => "lmkit-tasks:4b-preview",
            ChatModel.Gemma4_E4b => "gemma4:e4b",
            ChatModel.Llama31_8b => "llama3.1",
            ChatModel.Falcon3_7b => "falcon3:7b",
            ChatModel.Granite4_7b => "granite4-h:7b",
            ChatModel.DeepseekR1_8b => "deepseek-r1:8b",
            ChatModel.Gemma4_12b => "gemma4:12b",
            ChatModel.Glm47_Flash => "glm4.7-flash",
            _ => "gemma4:e2b",
        };

    public static ChatModel FromModelId(string modelId) =>
        modelId switch
        {
            "gemma3:270m" => ChatModel.Gemma3_270m,
            "gemma3:1b" => ChatModel.Gemma3_1b,
            "llama3.2:1b" => ChatModel.Llama32_1b,
            "gemma4:e2b" => ChatModel.Gemma4_E2b,
            "llama3.2:3b" => ChatModel.Llama32_3b,
            "falcon3:3b" => ChatModel.Falcon3_3b,
            "lmkit-tasks:4b-preview" => ChatModel.LmkitTasks_4b,
            "gemma4:e4b" => ChatModel.Gemma4_E4b,
            "llama3.1" => ChatModel.Llama31_8b,
            "falcon3:7b" => ChatModel.Falcon3_7b,
            "granite4-h:7b" => ChatModel.Granite4_7b,
            "deepseek-r1:8b" => ChatModel.DeepseekR1_8b,
            "gemma4:12b" => ChatModel.Gemma4_12b,
            "glm4.7-flash" => ChatModel.Glm47_Flash,
            _ => ChatModel.Gemma4_E2b,
        };
}
