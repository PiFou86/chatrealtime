namespace chatrealtime.Configuration;

public class OpenAISettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-realtime-mini-2025-10-06";
    public string RealtimeUrl { get; set; } = "wss://api.openai.com/v1/realtime";
    public string Voice { get; set; } = "alloy";
    public string TranscriptionModel { get; set; } = "gpt-4o-transcribe";
    public string SystemPromptFile { get; set; } = "Prompts/Marvin.md";
    public double Temperature { get; set; } = 0.8;
    public int MaxResponseOutputTokens { get; set; } = 4096;
    public string Instructions { get; set; } = string.Empty;
    public TurnDetectionSettings TurnDetection { get; set; } = new();
}

public class TurnDetectionSettings
{
    public string Type { get; set; } = "server_vad";
    public double Threshold { get; set; } = 0.5;
    public int PrefixPaddingMs { get; set; } = 300;
    public int SilenceDurationMs { get; set; } = 500;
}
