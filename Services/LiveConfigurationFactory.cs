using chatrealtime.Configuration;
using chatrealtime.Models;
using Microsoft.Extensions.Options;

namespace chatrealtime.Services;

public interface ILiveConfigurationFactory
{
    Task<LiveSessionConfig> CreateAsync(CancellationToken cancellationToken);
}

public sealed class LiveConfigurationFactory : ILiveConfigurationFactory
{
    public static readonly string[] AllowedClientEvents =
    [
        "response.item.create",
        "response.create",
        "session.close"
    ];

    public static readonly LiveServerEventSelector[] AllowedServerEvents =
    [
        new() { Type = "session.started" },
        new() { Type = "session.input_transcript.delta" },
        new() { Type = "session.output_transcript.delta" },
        new() { Type = "error" },
        new() { Type = "session.closed" }
    ];

    private readonly OpenAISettings _settings;
    private readonly McpDiscoveryService _mcpDiscovery;
    private readonly ILogger<LiveConfigurationFactory> _logger;
    private readonly SemaphoreSlim _discoveryLock = new(1, 1);
    private IReadOnlyList<ToolConfig>? _tools;

    public LiveConfigurationFactory(
        IOptions<OpenAISettings> settings,
        McpDiscoveryService mcpDiscovery,
        ILogger<LiveConfigurationFactory> logger)
    {
        _settings = settings.Value;
        _mcpDiscovery = mcpDiscovery;
        _logger = logger;
    }

    public async Task<LiveSessionConfig> CreateAsync(CancellationToken cancellationToken)
    {
        var tools = await GetToolsAsync(cancellationToken);
        return new LiveSessionConfig
        {
            Model = _settings.Model,
            Instructions = LoadConversationInstructions(),
            Audio = new LiveAudioConfig
            {
                Output = new LiveAudioOutputConfig { Voice = _settings.Voice }
            },
            Delegation = new LiveDelegationConfig
            {
                Responses = new LiveResponsesConfig
                {
                    Model = _settings.DelegationModel,
                    Instructions = _settings.DelegationInstructions,
                    MaxOutputTokens = _settings.MaxResponseOutputTokens,
                    Tools = tools.Select(tool => new LiveFunctionTool
                    {
                        Name = tool.Name,
                        Description = tool.Description,
                        Parameters = tool.Parameters.ValueKind == System.Text.Json.JsonValueKind.Undefined
                            ? System.Text.Json.JsonSerializer.SerializeToElement(new { type = "object", properties = new { } })
                            : tool.Parameters
                    }).ToArray()
                }
            },
            Client = new LiveClientConfig
            {
                DataChannel = new LiveDataChannelConfig
                {
                    AllowedClientEvents = AllowedClientEvents,
                    AllowedServerEvents = AllowedServerEvents
                }
            }
        };
    }

    private async Task<IReadOnlyList<ToolConfig>> GetToolsAsync(CancellationToken cancellationToken)
    {
        if (_tools is not null)
        {
            return _tools;
        }

        await _discoveryLock.WaitAsync(cancellationToken);
        try
        {
            if (_tools is not null)
            {
                return _tools;
            }

            var discovered = await _mcpDiscovery.DiscoverAllServersAsync(cancellationToken);
            var merged = (_settings.Tools ?? [])
                .Concat(discovered)
                .GroupBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .ToList();

            // ToolExecutorService reads the same options instance.
            _settings.Tools = merged;
            _tools = merged;
            return _tools;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "MCP discovery failed; configured tools will still be available");
            _tools = _settings.Tools ?? [];
            return _tools;
        }
        finally
        {
            _discoveryLock.Release();
        }
    }

    private string LoadConversationInstructions()
    {
        if (string.IsNullOrWhiteSpace(_settings.SystemPromptFile))
        {
            return _settings.Instructions;
        }

        try
        {
            if (File.Exists(_settings.SystemPromptFile))
            {
                return File.ReadAllText(_settings.SystemPromptFile);
            }

            _logger.LogWarning("System prompt file not found: {File}", _settings.SystemPromptFile);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not read system prompt file: {File}", _settings.SystemPromptFile);
        }

        return _settings.Instructions;
    }
}
