using System.Text.Json;
using System.Text.Json.Serialization;

namespace chatrealtime.Models;

public sealed class LiveCreateRequest
{
    [JsonPropertyName("session")]
    public required LiveSessionConfig Session { get; init; }

    [JsonPropertyName("transport")]
    public required LiveTransport Transport { get; init; }
}

public sealed class LiveSessionConfig
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("audio")]
    public required LiveAudioConfig Audio { get; init; }

    [JsonPropertyName("instructions")]
    public string? Instructions { get; init; }

    [JsonPropertyName("delegation")]
    public required LiveDelegationConfig Delegation { get; init; }

    [JsonPropertyName("client")]
    public required LiveClientConfig Client { get; init; }
}

public sealed class LiveAudioConfig
{
    [JsonPropertyName("output")]
    public required LiveAudioOutputConfig Output { get; init; }
}

public sealed class LiveAudioOutputConfig
{
    [JsonPropertyName("voice")]
    public required string Voice { get; init; }
}

public sealed class LiveDelegationConfig
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "responses";

    [JsonPropertyName("responses")]
    public required LiveResponsesConfig Responses { get; init; }
}

public sealed class LiveResponsesConfig
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("instructions")]
    public string? Instructions { get; init; }

    [JsonPropertyName("max_output_tokens")]
    public int MaxOutputTokens { get; init; }

    [JsonPropertyName("tools")]
    public IReadOnlyList<LiveFunctionTool> Tools { get; init; } = [];

    [JsonPropertyName("tool_choice")]
    public string ToolChoice { get; init; } = "auto";
}

public sealed class LiveFunctionTool
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "function";

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("parameters")]
    public JsonElement Parameters { get; init; }
}

public sealed class LiveClientConfig
{
    [JsonPropertyName("data_channel")]
    public required LiveDataChannelConfig DataChannel { get; init; }
}

public sealed class LiveDataChannelConfig
{
    [JsonPropertyName("allowed_client_events")]
    public IReadOnlyList<string> AllowedClientEvents { get; init; } = [];

    [JsonPropertyName("allowed_server_events")]
    public IReadOnlyList<LiveServerEventSelector> AllowedServerEvents { get; init; } = [];
}

public sealed class LiveServerEventSelector
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("response_event")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResponseEvent { get; init; }
}

public sealed class LiveTransport
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "webrtc";

    [JsonPropertyName("sdp")]
    public required string Sdp { get; init; }
}

public sealed class LiveCreateResponse
{
    [JsonPropertyName("session")]
    public required LiveSessionResource Session { get; init; }

    [JsonPropertyName("transport")]
    public required LiveTransport Transport { get; init; }
}

public sealed class LiveSessionResource
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }
}

public sealed record BrowserLiveSessionResponse(string SessionId, string Sdp, string Type = "webrtc");

public sealed class BrowserLiveSessionRequest
{
    public string Sdp { get; init; } = string.Empty;
}
