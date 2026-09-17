using System.Text.Json;

namespace chatrealtime.Configuration;

public class OpenAISettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-live-1";
    public string LiveSessionsUrl { get; set; } = "https://api.openai.com/v1/live/sessions";
    public string LiveSidebandUrl { get; set; } = "wss://api.openai.com/v1/live";
    public string DelegationModel { get; set; } = "gpt-5.6-luna";
    public string DelegationInstructions { get; set; } =
        "You are the reasoning and tool backend for a GPT-Live conversation. Handle the delegated task " +
        "or typed message using the conversation context and the user's latest corrections. Use a tool " +
        "when the answer depends on external or current data, or when an action must be performed; otherwise " +
        "reason directly. Treat tool results as authoritative and never claim an action succeeded before a tool " +
        "confirms it. Ask for missing required details instead of guessing. Return only useful facts, the actual " +
        "task status, and any next step, concisely. Do not imitate the voice persona or mention internal delegation; " +
        "GPT-Live handles personality and speech.";
    public string Voice { get; set; } = "alloy";
    public string SystemPromptFile { get; set; } = "Prompts/Marvin.md";
    public int MaxResponseOutputTokens { get; set; } = 4096;
    public string Instructions { get; set; } = string.Empty;
    public List<ToolConfig> Tools { get; set; } = new();
    public List<McpServerConfig> McpServers { get; set; } = new();
    public ResilienceSettings Resilience { get; set; } = new();
}

public class ToolConfig
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = "builtin"; // builtin, http, or custom
    public JsonElement Parameters { get; set; }
    
    // For HTTP tools
    public HttpToolConfig? Http { get; set; }
}

public class HttpToolConfig
{
    public string Url { get; set; } = string.Empty;
    public string Method { get; set; } = "POST"; // GET, POST, PUT, DELETE
    public Dictionary<string, string> Headers { get; set; } = new();
    public string BodyTemplate { get; set; } = "{{arguments}}"; // Template for request body
}

public class ResilienceSettings
{
    public RetryPolicySettings Retry { get; set; } = new();
    public CircuitBreakerSettings CircuitBreaker { get; set; } = new();
    public TimeoutSettings Timeout { get; set; } = new();
}

public class RetryPolicySettings
{
    public bool Enabled { get; set; } = true;
    public int MaxRetryAttempts { get; set; } = 3;
    public int InitialDelayMs { get; set; } = 100;
    public int MaxDelayMs { get; set; } = 5000;
}

public class CircuitBreakerSettings
{
    public bool Enabled { get; set; } = true;
    public int FailureThreshold { get; set; } = 5;
    public int BreakDurationSeconds { get; set; } = 30;
    public int SamplingDurationSeconds { get; set; } = 60;
}

public class TimeoutSettings
{
    public bool Enabled { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 30;
}

public class McpServerConfig
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
